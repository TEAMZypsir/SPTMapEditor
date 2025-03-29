using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using BepInEx;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace TransformCacher
{
    /// <summary>
    /// Manages shader loading and lookup from asset bundles
    /// </summary>
    public static class BundleShaders
    {
        private static Dictionary<string, Shader> shaders;

        public static void Add(Shader add)
        {
            if (shaders == null)
            {
                shaders = new Dictionary<string, Shader>();
            }
            
            if (add != null && !string.IsNullOrEmpty(add.name))
            {
                if (!shaders.ContainsKey(add.name))
                {
                    TransformCacherPlugin.Log.LogInfo("Added " + add.name + " shader to BundleShaders.");
                    shaders[add.name] = add;
                }
                else
                {
                    TransformCacherPlugin.Log.LogWarning("Shader with name '" + add.name + "' already exists in BundleShaders.");
                }
            }
            else
            {
                TransformCacherPlugin.Log.LogError("Cannot add null shader or shader with empty name to BundleShaders.");
            }
        }

        public static void Add(Shader[] add)
        {
            if (add == null)
            {
                TransformCacherPlugin.Log.LogError("Cannot add null shader array to BundleShaders.");
            }
            else
            {
                foreach (Shader shader in add)
                {
                    Add(shader);
                }
            }
        }

        public static Shader Find(string name)
        {
            if (shaders == null)
            {
                TransformCacherPlugin.Log.LogWarning("No shaders have been added to BundleShaders yet.");
                return null;
            }
            
            if (string.IsNullOrEmpty(name))
            {
                TransformCacherPlugin.Log.LogError("Cannot find shader with null or empty name.");
                return null;
            }
            
            Shader shader;
            if (shaders.TryGetValue(name, out shader))
            {
                TransformCacherPlugin.Log.LogInfo("Shader '" + name + "' found successfully!");
                return shader;
            }
            else
            {
                TransformCacherPlugin.Log.LogWarning("Shader '" + name + "' not found in BundleShaders.");
                return null;
            }
        }
    }

    /// <summary>
    /// Component that handles model exporting functionality
    /// </summary>
    public class ExporterComponent : MonoBehaviour
    {
        // Export status
        private bool _exportInProgress = false;
        private float _exportProgress = 0f;
        private string _exportStatus = "";
        
        // References to other systems
        private ExporterManager _exporterManager;
        
        // Use GLB format flag
        private bool _useGlbFormat = false;
        
        // Properties
        public bool ExportInProgress => _exportInProgress;
        public float ExportProgress => _exportProgress;
        public string ExportStatus => _exportStatus;
        public bool UseGlbFormat 
        { 
            get => _useGlbFormat; 
            set => _useGlbFormat = value; 
        }
        
        public void Initialize()
        {
            // Get reference to exporter manager
            _exporterManager = FindObjectOfType<ExporterManager>();
            if (_exporterManager == null)
            {
                _exporterManager = gameObject.AddComponent<ExporterManager>();
            }
            
            TransformCacherPlugin.Log.LogInfo("ExporterComponent initialized");
        }
        
        /// <summary>
        /// Export the provided objects
        /// </summary>
        public void Export(HashSet<GameObject> objectsToExport, string filename)
        {
            if (_exportInProgress)
            {
                TransformCacherPlugin.Log.LogWarning("Export already in progress");
                return;
            }
            
            if (_exporterManager != null)
            {
                _exporterManager.UseGlbFormat = _useGlbFormat;
                _exporterManager.Export(objectsToExport, filename);
                
                // Update status
                _exportInProgress = true;
                _exportStatus = "Export started...";
                _exportProgress = 0.1f;
                
                // Start progress tracking
                StartCoroutine(TrackExportProgress());
            }
            else
            {
                TransformCacherPlugin.Log.LogError("ExporterManager not found, cannot export");
                _exportStatus = "Export failed: Exporter not available";
            }
        }
        
        /// <summary>
        /// Get currently open/visible items for export
        /// </summary>
        public List<GameObject> GetCurrentlyOpenItems()
        {
            if (_exporterManager != null)
            {
                return _exporterManager.GetCurrentlyOpenItems();
            }
            
            return new List<GameObject>();
        }
        
        private IEnumerator TrackExportProgress()
        {
            // Wait for export to start
            yield return new WaitForSeconds(0.5f);
            
            // Monitor progress
            float elapsed = 0f;
            while (_exportInProgress && elapsed < 60f) // Timeout after 60 seconds
            {
                if (_exporterManager != null)
                {
                    _exportProgress = _exporterManager.ExportProgress;
                    _exportStatus = _exporterManager.ExportStatus;
                    
                    // Check if export is complete
                    if (_exportProgress >= 1.0f || !_exporterManager.ExportInProgress)
                    {
                        _exportInProgress = false;
                        _exportProgress = 1.0f;
                        _exportStatus = "Export completed";
                        break;
                    }
                }
                
                elapsed += Time.deltaTime;
                yield return null;
            }
            
            // Handle timeout
            if (elapsed >= 60f)
            {
                _exportInProgress = false;
                _exportStatus = "Export timed out";
                TransformCacherPlugin.Log.LogWarning("Export operation timed out");
            }
        }
    }

    /// <summary>
    /// Loads asset bundles required for exporting
    /// </summary>
    public class BundleLoader : MonoBehaviour
    {
        // Paths
        private string _bundlesPath;
        
        // Track loaded bundles
        private Dictionary<string, AssetBundle> _loadedBundles = new Dictionary<string, AssetBundle>();
        
        // Status
        private bool _initialized = false;
        private bool _loadingInProgress = false;
        
        public void Initialize()
        {
            if (_initialized) return;
            
            // Set up bundle path
            _bundlesPath = Path.Combine(Paths.PluginPath, "TransformCacher", "bundles");
            
            if (!Directory.Exists(_bundlesPath))
            {
                Directory.CreateDirectory(_bundlesPath);
            }
            
            _initialized = true;
            TransformCacherPlugin.Log.LogInfo($"BundleLoader initialized with path: {_bundlesPath}");
            
            // Start loading bundles
            StartCoroutine(LoadBundles());
        }
        
        /// <summary>
        /// Load all asset bundles from the bundles directory
        /// </summary>
        private IEnumerator LoadBundles()
        {
            if (_loadingInProgress) yield break;
            
            _loadingInProgress = true;
            TransformCacherPlugin.Log.LogInfo("Starting to load asset bundles...");
            
            // To avoid 'yield' in try-catch, we'll structure this differently
            bool dirExists = false;
            string[] bundleFiles = new string[0];
            
            try
            {
                // Check if bundlesPath directory exists
                dirExists = Directory.Exists(_bundlesPath);
                if (!dirExists)
                {
                    TransformCacherPlugin.Log.LogWarning($"Bundles directory not found: {_bundlesPath}");
                    _loadingInProgress = false;
                    yield break;
                }
                
                // Get all bundle files
                bundleFiles = Directory.GetFiles(_bundlesPath, "*", SearchOption.TopDirectoryOnly)
                    .Where(f => !f.EndsWith(".manifest") && !f.EndsWith(".meta"))
                    .ToArray();
            }
            catch (Exception ex)
            {
                TransformCacherPlugin.Log.LogError($"Error accessing bundle directory: {ex.Message}");
                _loadingInProgress = false;
                yield break;
            }
            
            if (bundleFiles.Length == 0)
            {
                TransformCacherPlugin.Log.LogWarning("No bundle files found in the bundles directory");
                _loadingInProgress = false;
                yield break;
            }
            
            TransformCacherPlugin.Log.LogInfo($"Found {bundleFiles.Length} potential bundle files");
            
            // Load each bundle
            foreach (string bundleFile in bundleFiles)
            {
                yield return StartCoroutine(LoadBundle(bundleFile));
            }
            
            // Process loaded bundles
            try
            {
                TransformCacherPlugin.Log.LogInfo($"Finished loading {_loadedBundles.Count} bundles");
                
                // Initialize shaders from unitygltf bundle if available
                if (_loadedBundles.ContainsKey("unitygltf"))
                {
                    var shaders = _loadedBundles["unitygltf"].LoadAllAssets<Shader>();
                    BundleShaders.Add(shaders);
                    TransformCacherPlugin.Log.LogInfo($"Added {shaders.Length} shaders from unitygltf bundle");
                }
            }
            catch (Exception ex)
            {
                TransformCacherPlugin.Log.LogError($"Error processing loaded bundles: {ex.Message}\n{ex.StackTrace}");
            }
            
            _loadingInProgress = false;
        }
        
        private IEnumerator LoadBundle(string bundlePath)
        {
            string bundleName = Path.GetFileName(bundlePath);
            
            // Check if already loaded
            if (_loadedBundles.ContainsKey(bundleName))
            {
                TransformCacherPlugin.Log.LogInfo($"Bundle {bundleName} already loaded");
                yield break;
            }
            
            TransformCacherPlugin.Log.LogInfo($"Loading bundle: {bundlePath}");
            
            // Separate the try-catch from the yield
            AssetBundleCreateRequest request = null;
            
            try
            {
                request = AssetBundle.LoadFromFileAsync(bundlePath);
            }
            catch (Exception ex)
            {
                TransformCacherPlugin.Log.LogError($"Error creating bundle request {bundlePath}: {ex.Message}");
                yield break;
            }
            
            // Now yield outside the try-catch
            yield return request;
            
            // Process the result
            try
            {
                if (request.assetBundle == null)
                {
                    TransformCacherPlugin.Log.LogError($"Failed to load bundle: {bundlePath}");
                    yield break;
                }
                
                _loadedBundles[bundleName] = request.assetBundle;
                TransformCacherPlugin.Log.LogInfo($"Successfully loaded bundle: {bundleName}");
            }
            catch (Exception ex)
            {
                TransformCacherPlugin.Log.LogError($"Error processing loaded bundle {bundlePath}: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Get an already loaded asset bundle by name
        /// </summary>
        public AssetBundle GetBundle(string bundleName)
        {
            if (_loadedBundles.ContainsKey(bundleName))
            {
                return _loadedBundles[bundleName];
            }
            
            return null;
        }
        
        /// <summary>
        /// Load an asset bundle synchronously
        /// </summary>
        public AssetBundle LoadAssetBundle(string bundleName)
        {
            // Check if already loaded
            if (_loadedBundles.ContainsKey(bundleName))
            {
                return _loadedBundles[bundleName];
            }
            
            // Try to load
            try
            {
                string bundlePath = Path.Combine(_bundlesPath, bundleName);
                if (!File.Exists(bundlePath))
                {
                    TransformCacherPlugin.Log.LogError($"Bundle file not found: {bundlePath}");
                    return null;
                }
                
                AssetBundle bundle = AssetBundle.LoadFromFile(bundlePath);
                if (bundle != null)
                {
                    _loadedBundles[bundleName] = bundle;
                    TransformCacherPlugin.Log.LogInfo($"Successfully loaded bundle: {bundleName}");
                    return bundle;
                }
                else
                {
                    TransformCacherPlugin.Log.LogError($"Failed to load bundle: {bundlePath}");
                    return null;
                }
            }
            catch (Exception ex)
            {
                TransformCacherPlugin.Log.LogError($"Error loading bundle {bundleName}: {ex.Message}");
                return null;
            }
        }
    }

    /// <summary>
    /// Exporter manager that coordinates model export functionality
    /// </summary>
    public class ExporterManager : MonoBehaviour
    {
        private static ExporterManager _instance;
        public static ExporterManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    var go = new GameObject("ExporterManager");
                    _instance = go.AddComponent<ExporterManager>();
                    DontDestroyOnLoad(go);
                }
                return _instance;
            }
        }
        
        // Is export in progress
        private bool _exportInProgress = false;
        public bool ExportInProgress => _exportInProgress;
        
        // Export progress and status
        private float _exportProgress = 0f;
        private string _exportStatus = "";
        public float ExportProgress => _exportProgress;
        public string ExportStatus => _exportStatus;
        
        // Use GLB format
        private bool _useGlbFormat = false;
        public bool UseGlbFormat 
        { 
            get => _useGlbFormat; 
            set => _useGlbFormat = value; 
        }
        
        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            
            _instance = this;
            DontDestroyOnLoad(gameObject);
            
            TransformCacherPlugin.Log.LogInfo("ExporterManager initialized");
        }
        
        /// <summary>
        /// Export the specified objects to GLTF/GLB format
        /// </summary>
        public void Export(IEnumerable<GameObject> objectsToExport, string filename)
        {
            if (_exportInProgress)
            {
                TransformCacherPlugin.Log.LogWarning("Export already in progress");
                return;
            }
            
            StartCoroutine(ExportCoroutine(objectsToExport, filename));
        }
        
        /// <summary>
        /// Get currently open/visible items
        /// </summary>
        public List<GameObject> GetCurrentlyOpenItems()
        {
            var currentItems = new List<GameObject>();
            
            // First try to find any components with a special tag
            try
            {
                // Get any specially tagged objects
                var taggedObjects = FindObjectsOfType<TransformCacherTag>();
                foreach (var tag in taggedObjects)
                {
                    if (tag.gameObject != null && !tag.IsDestroyed)
                    {
                        currentItems.Add(tag.gameObject);
                    }
                }
                
                // If no tagged objects, try to find a selected object
                if (currentItems.Count == 0)
                {
                    var transformCacher = FindObjectOfType<TransformCacher>();
                    if (transformCacher != null)
                    {
                        var selectedObject = transformCacher.GetCurrentInspectedObject();
                        if (selectedObject != null)
                        {
                            currentItems.Add(selectedObject);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                TransformCacherPlugin.Log.LogError($"Error getting open items: {ex.Message}");
            }
            
            return currentItems;
        }
        
        private IEnumerator ExportCoroutine(IEnumerable<GameObject> objectsToExport, string filename)
        {
            _exportInProgress = true;
            _exportProgress = 0f;
            _exportStatus = "Starting export...";
            
            yield return null;
            
            // Instead of wrapping entire coroutine in try-catch, we'll separate setup and processing
            HashSet<GameObject> objectsSet = null;
            string exportDir = "";
            
            try
            {
                // Convert to hashset to remove duplicates
                objectsSet = new HashSet<GameObject>(objectsToExport);
                
                // Get export directory
                exportDir = Path.Combine(Paths.PluginPath, "TransformCacher", "Exports", SceneManager.GetActiveScene().name);
                
                // Ensure directory exists
                Directory.CreateDirectory(exportDir);
                
                // Use specific export file format based on preference
                if (!filename.EndsWith(".glb") && !filename.EndsWith(".gltf"))
                {
                    filename += _useGlbFormat ? ".glb" : ".gltf";
                }
            }
            catch (Exception ex)
            {
                TransformCacherPlugin.Log.LogError($"Error during export setup: {ex.Message}\n{ex.StackTrace}");
                _exportStatus = $"Export failed: {ex.Message}";
                _exportInProgress = false;
                yield break;
            }
            
            _exportStatus = "Setting up export...";
            _exportProgress = 0.1f;
            yield return null;
            
            // Get full path
            string fullPath = Path.Combine(exportDir, filename);
            
            try
            {
                _exportStatus = "Looking for export module...";
                _exportProgress = 0.2f;
                
                // Try to find TarkinItemExporter
                Type tarkinExporter = null;
                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    tarkinExporter = assembly.GetType("TarkinItemExporter.Exporter");
                    if (tarkinExporter != null) break;
                }
                
                if (tarkinExporter != null)
                {
                    _exportStatus = "Found exporter, setting parameters...";
                    _exportProgress = 0.3f;
                    
                    // Set GLB flag
                    var glbField = tarkinExporter.GetField("glb");
                    if (glbField != null)
                    {
                        glbField.SetValue(null, _useGlbFormat);
                    }
                    
                    // Register callback for completion
                    var callbackField = tarkinExporter.GetField("CallbackFinished");
                    if (callbackField != null)
                    {
                        Action onFinished = () =>
                        {
                            _exportInProgress = false;
                            _exportProgress = 1.0f;
                            _exportStatus = $"Export completed to {fullPath}";
                        };
                        
                        callbackField.SetValue(null, onFinished);
                    }
                    
                    _exportStatus = "Starting export process...";
                    _exportProgress = 0.4f;
                    
                    // Call the export method
                    var exportMethod = tarkinExporter.GetMethod("Export", 
                        new[] { typeof(HashSet<GameObject>), typeof(string), typeof(string) });
                        
                    if (exportMethod != null)
                    {
                        exportMethod.Invoke(null, new object[] { objectsSet, exportDir, Path.GetFileName(filename) });
                        TransformCacherPlugin.Log.LogInfo($"Export initiated to {fullPath}");
                        
                        _exportStatus = "Export in progress...";
                        _exportProgress = 0.5f;
                    }
                    else
                    {
                        TransformCacherPlugin.Log.LogError("Export method not found in TarkinItemExporter");
                        _exportStatus = "Export failed: Method not found";
                        _exportInProgress = false;
                    }
                }
                else
                {
                    TransformCacherPlugin.Log.LogWarning("TarkinItemExporter not found. Cannot export models.");
                    _exportStatus = "Export failed: Exporter not found";
                    _exportInProgress = false;
                }
            }
            catch (Exception ex)
            {
                TransformCacherPlugin.Log.LogError($"Error during export: {ex.Message}\n{ex.StackTrace}");
                _exportStatus = $"Export failed: {ex.Message}";
                _exportInProgress = false;
            }
            
            yield return null;
        }
    }
}