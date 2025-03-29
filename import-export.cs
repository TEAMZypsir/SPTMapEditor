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
    /// Exporter manager that coordinates with TarkinItemExporter
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
        
        // Logging
        private static BepInEx.Logging.ManualLogSource Logger;
        
        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            
            _instance = this;
            DontDestroyOnLoad(gameObject);
            
            // Get logger
            Logger = BepInEx.Logging.Logger.CreateLogSource("ExporterManager");
            
            Logger.LogInfo("ExporterManager initialized");
        }
        
        /// <summary>
        /// Export the specified objects to GLTF/GLB format
        /// </summary>
        public void Export(IEnumerable<GameObject> objectsToExport, string filename)
        {
            if (_exportInProgress)
            {
                Logger.LogWarning("Export already in progress");
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
            
            // Try to find objects with inspection/preview components
            try
            {
                // Find objects with certain components that indicate they're being viewed
                // This may need to be adapted based on the game
                var viewComponents = FindObjectsOfType<Component>()
                    .Where(c => c.GetType().Name.Contains("Preview") || 
                                c.GetType().Name.Contains("Inspector"))
                    .ToList();
                                
                foreach (var comp in viewComponents)
                {
                    if (comp.gameObject != null)
                    {
                        currentItems.Add(comp.gameObject);
                    }
                }
                
                // Fallback - use TransformCacher selected object if available
                if (currentItems.Count == 0 && TransformCacher.Instance != null)
                {
                    var selectedObject = TransformCacher.Instance.GetCurrentInspectedObject();
                    if (selectedObject != null)
                    {
                        currentItems.Add(selectedObject);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogError($"Error getting open items: {ex.Message}");
            }
            
            return currentItems;
        }
        
        private IEnumerator ExportCoroutine(IEnumerable<GameObject> objectsToExport, string filename)
        {
            _exportInProgress = true;
            _exportProgress = 0f;
            _exportStatus = "Starting export...";
            
            yield return null;
            
            try
            {
                // Convert to hashset to remove duplicates
                var objectsSet = new HashSet<GameObject>(objectsToExport);
                
                // Get export directory
                string exportDir = Path.Combine(Paths.PluginPath, "TransformCacher", "Exports", SceneManager.GetActiveScene().name);
                
                // Ensure directory exists
                Directory.CreateDirectory(exportDir);
                
                // Use specific export file format based on preference
                if (!filename.EndsWith(".glb") && !filename.EndsWith(".gltf"))
                {
                    filename += _useGlbFormat ? ".glb" : ".gltf";
                }
                
                _exportStatus = "Setting up export...";
                _exportProgress = 0.1f;
                yield return null;
                
                // Get full path
                string fullPath = Path.Combine(exportDir, filename);
                
                // Try to find TarkinItemExporter
                _exportStatus = "Looking for export module...";
                _exportProgress = 0.2f;
                yield return null;
                
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
                    yield return null;
                    
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
                    yield return null;
                    
                    // Call the export method
                    var exportMethod = tarkinExporter.GetMethod("Export", 
                        new[] { typeof(HashSet<GameObject>), typeof(string), typeof(string) });
                        
                    if (exportMethod != null)
                    {
                        exportMethod.Invoke(null, new object[] { objectsSet, exportDir, Path.GetFileName(filename) });
                        Logger.LogInfo($"Export initiated to {fullPath}");
                        
                        _exportStatus = "Export in progress...";
                        _exportProgress = 0.5f;
                    }
                    else
                    {
                        Logger.LogError("Export method not found in TarkinItemExporter");
                        _exportStatus = "Export failed: Method not found";
                        _exportInProgress = false;
                    }
                }
                else
                {
                    Logger.LogWarning("TarkinItemExporter not found. Cannot export models.");
                    _exportStatus = "Export failed: Exporter not found";
                    _exportInProgress = false;
                }
            }
            catch (Exception ex)
            {
                Logger.LogError($"Error during export: {ex.Message}\n{ex.StackTrace}");
                _exportStatus = $"Export failed: {ex.Message}";
                _exportInProgress = false;
            }
            
            yield return null;
        }
    }
}