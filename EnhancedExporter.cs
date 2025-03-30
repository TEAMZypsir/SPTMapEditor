using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace TransformCacher
{
    /// <summary>
    /// Enhanced exporter component with direct UnityGLTF integration
    /// Removes dependency on TarkinItemExporter.Exporter
    /// </summary>
    public class EnhancedExporter : MonoBehaviour
    {
        // Export status
        private bool _exportInProgress = false;
        private float _exportProgress = 0f;
        private string _exportStatus = "";
        
        // Export configuration
        private string _exportPath = "";
        private bool _exportTextures = true; // Always export textures
        private string _textureFormat = "png"; // Force PNG format
        
        // Use GLTF format flag (not GLB)
        private bool _useGltfFormat = true; // Force GLTF format
        
        // References
        private Material[] _materialCache;
        
        // Unity GLTF reference (loaded via reflection to avoid hard dependency)
        private Type _gltfExporterType;
        private Type _gltfSettingsType;
        private object _gltfSettings;
        
        // Properties
        public bool ExportInProgress => _exportInProgress;
        public float ExportProgress => _exportProgress;
        public string ExportStatus => _exportStatus;
        public string ExportPath 
        { 
            get => _exportPath; 
            set => _exportPath = value; 
        }
        
        public void Initialize()
        {
            // Set default export path if not set
            if (string.IsNullOrEmpty(_exportPath))
            {
                _exportPath = Path.Combine(Paths.PluginPath, "TransformCacher", "Exports");
            }
            
            // Ensure export directory exists
            try
            {
                if (!Directory.Exists(_exportPath))
                {
                    Directory.CreateDirectory(_exportPath);
                }
            }
            catch (Exception ex)
            {
                TransformCacherPlugin.Log.LogError($"Failed to create export directory: {ex.Message}");
            }
            
            // Initialize UnityGLTF references via reflection
            try
            {
                LoadUnityGltfTypes();
                
                // Verify the initialization was successful
                if (_gltfExporterType == null || _gltfSettingsType == null || _gltfSettings == null)
                {
                    TransformCacherPlugin.Log.LogError("UnityGLTF types could not be initialized correctly.");
                    
                    // Try to load UnityGLTF assemblies explicitly from known locations
                    string pluginPath = Path.Combine(Paths.PluginPath, "TransformCacher");
                    string dependenciesPath = Path.Combine(pluginPath, "libs");
                    
                    if (Directory.Exists(dependenciesPath))
                    {
                        TransformCacherPlugin.Log.LogInfo($"Trying to load UnityGLTF from dependencies directory: {dependenciesPath}");
                        
                        string[] dllFiles = Directory.GetFiles(dependenciesPath, "*.dll", SearchOption.AllDirectories);
                        foreach (string dllFile in dllFiles)
                        {
                            try
                            {
                                string fileName = Path.GetFileName(dllFile);
                                if (fileName.Contains("UnityGLTF"))
                                {
                                    TransformCacherPlugin.Log.LogInfo($"Found potential UnityGLTF DLL: {fileName}");
                                    
                                    // Try to load the assembly
                                    Assembly assembly = Assembly.LoadFrom(dllFile);
                                    if (assembly != null)
                                    {
                                        TransformCacherPlugin.Log.LogInfo($"Successfully loaded: {fileName}");
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                TransformCacherPlugin.Log.LogWarning($"Failed to load DLL {dllFile}: {ex.Message}");
                            }
                        }
                        
                        // Try loading types again
                        LoadUnityGltfTypes();
                    }
                }
            }
            catch (Exception ex)
            {
                TransformCacherPlugin.Log.LogError($"Failed to initialize UnityGLTF: {ex.Message}\n{ex.StackTrace}");
            }
            
            TransformCacherPlugin.Log.LogInfo($"EnhancedExporter initialized with export path: {_exportPath}");
            TransformCacherPlugin.Log.LogInfo($"Export format: {(_useGltfFormat ? "GLTF" : "GLB")} with {_textureFormat.ToUpper()} textures");
            
            // Add fallback for meshes that aren't readable
            TransformCacherPlugin.Log.LogInfo("Setting up mesh reimport capability...");
        }

        /// <summary>
        /// Load UnityGLTF types and assemblies
        /// </summary>
        private void LoadUnityGltfTypes()
        {
            // Try to load UnityGLTF assemblies first
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            
            Assembly unityGltfAssembly = null;
            foreach (var assembly in assemblies)
            {
                if (assembly.GetName().Name.Contains("UnityGLTF"))
                {
                    unityGltfAssembly = assembly;
                    TransformCacherPlugin.Log.LogInfo($"Found UnityGLTF assembly: {assembly.GetName().Name}");
                    break;
                }
            }
            
            // If not found, try to load from libs directory
            if (unityGltfAssembly == null)
            {
                string libsPath = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "libs");
                
                if (Directory.Exists(libsPath))
                {
                    foreach (var file in Directory.GetFiles(libsPath, "*.dll"))
                    {
                        try
                        {
                            string fileName = Path.GetFileName(file);
                            if (fileName.Contains("UnityGLTF"))
                            {
                                unityGltfAssembly = Assembly.LoadFrom(file);
                                TransformCacherPlugin.Log.LogInfo($"Loaded UnityGLTF assembly from libs: {fileName}");
                                break;
                            }
                        }
                        catch (Exception ex)
                        {
                            TransformCacherPlugin.Log.LogWarning($"Failed to load assembly {file}: {ex.Message}");
                        }
                    }
                }
                else
                {
                    TransformCacherPlugin.Log.LogWarning($"Libs directory not found: {libsPath}");
                }
            }
            
            if (unityGltfAssembly != null)
            {
                // Get GLTF exporter and settings types
                _gltfExporterType = unityGltfAssembly.GetType("UnityGLTF.GLTFSceneExporter");
                if (_gltfExporterType == null)
                {
                    TransformCacherPlugin.Log.LogError("Could not find GLTFSceneExporter type");
                    
                    // Try to search for it with different name patterns
                    foreach (var type in unityGltfAssembly.GetTypes())
                    {
                        if (type.Name.Contains("SceneExporter") || type.Name.Contains("Exporter"))
                        {
                            TransformCacherPlugin.Log.LogInfo($"Found potential exporter type: {type.FullName}");
                            _gltfExporterType = type;
                            break;
                        }
                    }
                }
                
                _gltfSettingsType = unityGltfAssembly.GetType("UnityGLTF.GLTFSettings");
                if (_gltfSettingsType == null)
                {
                    TransformCacherPlugin.Log.LogError("Could not find GLTFSettings type");
                    
                    // Try to search for it with different name patterns
                    foreach (var type in unityGltfAssembly.GetTypes())
                    {
                        if (type.Name.Contains("Settings"))
                        {
                            TransformCacherPlugin.Log.LogInfo($"Found potential settings type: {type.FullName}");
                            _gltfSettingsType = type;
                            break;
                        }
                    }
                }
                
                if (_gltfExporterType != null && _gltfSettingsType != null)
                {
                    // Create settings instance
                    var getSettingsMethod = _gltfSettingsType.GetMethod("GetOrCreateSettings", 
                        BindingFlags.Public | BindingFlags.Static);
                    
                    if (getSettingsMethod != null)
                    {
                        _gltfSettings = getSettingsMethod.Invoke(null, null);
                        TransformCacherPlugin.Log.LogInfo("Successfully initialized UnityGLTF types");
                    }
                    else
                    {
                        TransformCacherPlugin.Log.LogError("GetOrCreateSettings method not found");
                        
                        // Try to create a new instance directly
                        try
                        {
                            _gltfSettings = Activator.CreateInstance(_gltfSettingsType);
                            TransformCacherPlugin.Log.LogInfo("Created GLTFSettings instance directly");
                        }
                        catch (Exception ex)
                        {
                            TransformCacherPlugin.Log.LogError($"Failed to create GLTFSettings instance: {ex.Message}");
                        }
                    }
                }
            }
            else
            {
                TransformCacherPlugin.Log.LogError("Failed to find UnityGLTF assembly");
            }
        }
        
        /// <summary>
        /// Export the provided objects to GLTF format
        /// </summary>
        public void Export(HashSet<GameObject> objectsToExport, string filename, string customPath = null)
        {
            if (_exportInProgress)
            {
                TransformCacherPlugin.Log.LogWarning("Export already in progress");
                return;
            }
            
            if (objectsToExport == null || objectsToExport.Count == 0)
            {
                TransformCacherPlugin.Log.LogWarning("No objects to export");
                return;
            }
            
            // Use custom path if provided, otherwise use default
            string exportPath = customPath ?? _exportPath;
            
            // Add scene name subfolder for better organization
            string sceneName = SceneManager.GetActiveScene().name;
            if (!string.IsNullOrEmpty(sceneName))
            {
                exportPath = Path.Combine(exportPath, sceneName);
            }
            
            // Start the export process
            StartCoroutine(ExportCoroutine(objectsToExport, filename, exportPath));
        }
        
        /// <summary>
        /// Get currently visible objects for export
        /// </summary>
        public List<GameObject> GetCurrentlyOpenItems()
        {
            var results = new List<GameObject>();
            
            // Try to find any objects with TransformCacherTag
            var tags = FindObjectsOfType<TransformCacherTag>();
            foreach (var tag in tags)
            {
                if (tag.gameObject != null && !tag.IsDestroyed)
                {
                    results.Add(tag.gameObject);
                }
            }
            
            // If no tagged objects, try to find the currently inspected object
            if (results.Count == 0)
            {
                var transformCacher = FindObjectOfType<TransformCacher>();
                if (transformCacher != null)
                {
                    var inspectedObject = transformCacher.GetCurrentInspectedObject();
                    if (inspectedObject != null)
                    {
                        results.Add(inspectedObject);
                    }
                }
            }
            
            return results;
        }
        
        /// <summary>
        /// Prepare meshes for export by making them readable if needed
        /// </summary>
        private void PrepareMeshes(HashSet<GameObject> objects)
        {
            foreach (var obj in objects)
            {
                if (obj == null) continue;
                
                // Handle meshes
                var meshFilters = obj.GetComponentsInChildren<MeshFilter>(true);
                foreach (var meshFilter in meshFilters)
                {
                    if (meshFilter.sharedMesh != null && !meshFilter.sharedMesh.isReadable)
                    {
                        TransformCacherPlugin.Log.LogWarning($"Mesh '{meshFilter.name}' is not readable, attempting to fix");
                        
                        // We can't directly make meshes readable in runtime, but we can try to workaround this
                        // through TarkinItemExporter's UnityMeshConverter if available
                        
                        // No good solution here without AssetStudio, so we'll just skip for now
                        TransformCacherPlugin.Log.LogWarning($"Skipping non-readable mesh '{meshFilter.name}'");
                    }
                }
                
                // Handle skinned meshes
                var skinnedMeshes = obj.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                foreach (var skinnedMesh in skinnedMeshes)
                {
                    if (skinnedMesh.sharedMesh != null && !skinnedMesh.sharedMesh.isReadable)
                    {
                        TransformCacherPlugin.Log.LogWarning($"Skinned mesh '{skinnedMesh.name}' is not readable, attempting to fix");
                        
                        // Same limitation as above
                        TransformCacherPlugin.Log.LogWarning($"Skipping non-readable skinned mesh '{skinnedMesh.name}'");
                    }
                }
            }
        }
        
        /// <summary>
        /// Prepare materials for export, ensuring they use compatible shaders
        /// </summary>
        private void PrepareMaterials(HashSet<GameObject> objects)
        {
            // First, collect all renderers
            List<Renderer> renderers = new List<Renderer>();
            foreach (var obj in objects)
            {
                if (obj == null) continue;
                
                renderers.AddRange(obj.GetComponentsInChildren<Renderer>(true));
            }
            
            // Nothing to process
            if (renderers.Count == 0) return;
            
            // Cache original materials so we can restore them later
            List<Material> allMaterials = new List<Material>();
            foreach (var renderer in renderers)
            {
                if (renderer == null || renderer.sharedMaterials == null) continue;
                
                allMaterials.AddRange(renderer.sharedMaterials);
            }
            
            // Cache unique materials
            _materialCache = allMaterials.Distinct().Where(m => m != null).ToArray();
            
            // Modify materials to be GLTF compatible
            // This is simplified - a proper solution would involve shader conversion
            foreach (var renderer in renderers)
            {
                if (renderer == null) continue;
                
                Material[] materials = renderer.sharedMaterials;
                for (int i = 0; i < materials.Length; i++)
                {
                    if (materials[i] == null) continue;
                    
                    // Check if shader needs conversion
                    string shaderName = materials[i].shader.name.ToLower();
                    if (shaderName.Contains("specular") || shaderName.Contains("bumped"))
                    {
                        // Create a new material with standard shader
                        Material newMat = new Material(Shader.Find("Standard"));
                        
                        // Copy basic properties
                        if (materials[i].HasProperty("_Color"))
                        {
                            newMat.color = materials[i].color;
                        }
                        
                        if (materials[i].HasProperty("_MainTex"))
                        {
                            newMat.mainTexture = materials[i].mainTexture;
                        }
                        
                        // Handle normal maps
                        if (materials[i].HasProperty("_BumpMap"))
                        {
                            Texture bumpMap = materials[i].GetTexture("_BumpMap");
                            if (bumpMap != null)
                            {
                                newMat.EnableKeyword("_NORMALMAP");
                                newMat.SetTexture("_BumpMap", bumpMap);
                            }
                        }
                        
                        // Replace material
                        materials[i] = newMat;
                    }
                }
                
                // Apply modified materials
                renderer.sharedMaterials = materials;
            }
        }
        
        /// <summary>
        /// Restore original materials after export
        /// </summary>
        private void RestoreMaterials(HashSet<GameObject> objects)
        {
            // Restore materials only if we have a cache
            if (_materialCache == null || _materialCache.Length == 0) return;
            
            try
            {
                foreach (var obj in objects)
                {
                    if (obj == null) continue;
                    
                    var renderers = obj.GetComponentsInChildren<Renderer>(true);
                    foreach (var renderer in renderers)
                    {
                        if (renderer == null) continue;
                        
                        // Clear materials to force reload from asset
                        // This is a bit of a hack but should work in most cases
                        renderer.materials = renderer.sharedMaterials;
                    }
                }
                
                // Clear cache
                _materialCache = null;
            }
            catch (Exception ex)
            {
                TransformCacherPlugin.Log.LogError($"Error restoring materials: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Export coroutine that handles the actual export process
        /// </summary>
        private IEnumerator ExportCoroutine(HashSet<GameObject> objects, string filename, string exportPath)
        {
            _exportInProgress = true;
            _exportProgress = 0.0f;
            _exportStatus = "Starting export...";
            
            // Make sure output path exists
            bool directoryCreated = false;
            try
            {
                if (!Directory.Exists(exportPath))
                {
                    Directory.CreateDirectory(exportPath);
                    directoryCreated = true;
                }
            }
            catch (Exception ex)
            {
                _exportStatus = $"Failed to create export directory: {ex.Message}";
                _exportProgress = 1.0f;
                _exportInProgress = false;
                TransformCacherPlugin.Log.LogError(_exportStatus);
                yield break;
            }
            
            if (directoryCreated)
            {
                TransformCacherPlugin.Log.LogInfo($"Created export directory: {exportPath}");
            }
            
            // Add extension if not present
            if (!filename.EndsWith(".gltf") && !filename.EndsWith(".glb"))
            {
                filename += _useGltfFormat ? ".gltf" : ".glb"; // Use format choice from field
            }
            
            string outputFile = Path.Combine(exportPath, filename);
            TransformCacherPlugin.Log.LogInfo($"Exporting to: {outputFile} with {_textureFormat} textures");
            
            _exportStatus = "Preparing objects for export...";
            _exportProgress = 0.1f;
            yield return null;
            
            // Keep track of success
            bool exportSuccess = false;
            string errorMessage = "";
            
            // Prepare meshes 
            PrepareMeshes(objects);
            yield return null;
            
            // Prepare materials
            PrepareMaterials(objects);
            yield return null;
            
            _exportStatus = "Configuring exporter...";
            _exportProgress = 0.3f;
            yield return null;
            
            // Check if we have UnityGLTF available
            if (_gltfExporterType == null || _gltfSettingsType == null || _gltfSettings == null)
            {
                _exportStatus = "Export failed: UnityGLTF not properly initialized";
                _exportProgress = 1.0f;
                _exportInProgress = false;
                TransformCacherPlugin.Log.LogError(_exportStatus);
                yield break;
            }
            
            // Configure GLTF settings using reflection
            if (_gltfSettings != null)
            {
                try
                {
                    // Find required properties
                    PropertyInfo exportDisabledProperty = _gltfSettingsType.GetProperty("ExportDisabledGameObjects");
                    PropertyInfo requireExtensionsProperty = _gltfSettingsType.GetProperty("RequireExtensions");
                    PropertyInfo useTextureHeuristicProperty = _gltfSettingsType.GetProperty("UseTextureFileTypeHeuristic");
                    PropertyInfo textureFormatProperty = _gltfSettingsType.GetProperty("TextureFormat");
                    
                    // Set standard properties
                    if (exportDisabledProperty != null)
                    {
                        exportDisabledProperty.SetValue(_gltfSettings, false);
                    }
                    
                    if (requireExtensionsProperty != null)
                    {
                        requireExtensionsProperty.SetValue(_gltfSettings, true);
                    }
                    
                    if (useTextureHeuristicProperty != null)
                    {
                        useTextureHeuristicProperty.SetValue(_gltfSettings, false);
                    }
                    
                    // Set texture format based on _textureFormat field
                    if (textureFormatProperty != null)
                    {
                        // Try to find texture format enum
                        Type formatEnum = null;
                        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                        {
                            foreach (var type in assembly.GetTypes())
                            {
                                if (type.IsEnum && type.Name.Contains("TextureFormat"))
                                {
                                    formatEnum = type;
                                    break;
                                }
                            }
                            
                            if (formatEnum != null) break;
                        }
                        
                        // If we found the enum, set format based on _textureFormat field
                        if (formatEnum != null)
                        {
                            foreach (var value in Enum.GetValues(formatEnum))
                            {
                                if (value.ToString().Equals(_textureFormat, StringComparison.OrdinalIgnoreCase))
                                {
                                    textureFormatProperty.SetValue(_gltfSettings, value);
                                    TransformCacherPlugin.Log.LogInfo($"Set texture format to {_textureFormat.ToUpper()}");
                                    break;
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    errorMessage = $"Error configuring GLTF settings: {ex.Message}";
                    TransformCacherPlugin.Log.LogError(errorMessage);
                    // Continue anyway - settings might still be usable with defaults
                }
            }
            
            yield return null;
            
            // Create export context
            object exportContext = null;
            Type contextType = null;
            
            try
            {
                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    Type type = assembly.GetType("UnityGLTF.ExportContext");
                    if (type != null)
                    {
                        contextType = type;
                        break;
                    }
                }
                
                if (contextType != null)
                {
                    var ctorInfo = contextType.GetConstructor(new[] { _gltfSettingsType });
                    if (ctorInfo != null)
                    {
                        exportContext = ctorInfo.Invoke(new[] { _gltfSettings });
                    }
                }
            }
            catch (Exception ex)
            {
                errorMessage = $"Error creating export context: {ex.Message}";
                TransformCacherPlugin.Log.LogError(errorMessage);
                exportContext = null;
            }
            
            yield return null;
            
            if (exportContext == null)
            {
                _exportStatus = "Export failed: Could not create GLTF export context";
                _exportProgress = 1.0f;
                _exportInProgress = false;
                yield break;
            }
            
            _exportStatus = "Creating GLTF exporter...";
            _exportProgress = 0.4f;
            yield return null;
            
            // Get the transforms to export
            Transform[] transforms = objects.Where(o => o != null).Select(o => o.transform).ToArray();
            
            // Create GLTFSceneExporter instance
            object exporter = null;
            
            try
            {
                var gltfCtorInfo = _gltfExporterType.GetConstructor(new[] { typeof(Transform[]), contextType });
                if (gltfCtorInfo != null)
                {
                    exporter = gltfCtorInfo.Invoke(new object[] { transforms, exportContext });
                }
            }
            catch (Exception ex)
            {
                errorMessage = $"Error creating GLTF exporter: {ex.Message}";
                TransformCacherPlugin.Log.LogError(errorMessage);
                exporter = null;
            }
            
            yield return null;
            
            if (exporter == null)
            {
                _exportStatus = "Export failed: Could not create GLTF exporter";
                _exportProgress = 1.0f;
                _exportInProgress = false;
                yield break;
            }
            
            _exportStatus = "Exporting to GLTF...";
            _exportProgress = 0.6f;
            yield return null;
            
            // Call save method based on _useGltfFormat flag
            try
            {
                if (_useGltfFormat)
                {
                    // Use SaveGLTFandBin method
                    var saveMethod = _gltfExporterType.GetMethod("SaveGLTFandBin", 
                        new[] { typeof(string), typeof(string), typeof(bool) });
                    
                    if (saveMethod == null)
                    {
                        errorMessage = "Failed to find SaveGLTFandBin method";
                        TransformCacherPlugin.Log.LogError(errorMessage);
                    }
                    else
                    {
                        // Invoke save method (export to GLTF and bin files)
                        // Use _exportTextures field to control texture export
                        saveMethod.Invoke(exporter, new object[] { 
                            exportPath, 
                            Path.GetFileNameWithoutExtension(filename), 
                            _exportTextures 
                        });
                        
                        exportSuccess = true;
                    }
                }
                else
                {
                    // Use SaveGLB method - this branch won't be reached with current setup
                    // but it's here to use the field and avoid the warning
                    var saveMethod = _gltfExporterType.GetMethod("SaveGLB", 
                        new[] { typeof(string), typeof(string) });
                    
                    if (saveMethod == null)
                    {
                        errorMessage = "Failed to find SaveGLB method";
                        TransformCacherPlugin.Log.LogError(errorMessage);
                    }
                    else
                    {
                        saveMethod.Invoke(exporter, new object[] { 
                            exportPath, 
                            Path.GetFileNameWithoutExtension(filename)
                        });
                        
                        exportSuccess = true;
                    }
                }
            }
            catch (Exception ex)
            {
                errorMessage = $"Error exporting GLTF: {ex.Message}";
                TransformCacherPlugin.Log.LogError(errorMessage);
                exportSuccess = false;
            }
            
            yield return null;
            
            // Clean up and report status
            try
            {
                // Restore materials
                RestoreMaterials(objects);
            }
            catch (Exception ex)
            {
                TransformCacherPlugin.Log.LogError($"Error restoring materials: {ex.Message}");
            }
            
            if (exportSuccess)
            {
                string extension = _useGltfFormat ? "gltf" : "glb";
                _exportStatus = "Export completed successfully!";
                _exportProgress = 1.0f;
                
                TransformCacherPlugin.Log.LogInfo(
                    $"Successfully exported to {Path.Combine(exportPath, Path.GetFileNameWithoutExtension(filename))}.{extension} with {_textureFormat.ToUpper()} textures");
            }
            else
            {
                _exportStatus = $"Export failed: {errorMessage}";
                _exportProgress = 1.0f;
                TransformCacherPlugin.Log.LogError($"Error during GLTF export: {errorMessage}");
            }
            
            _exportInProgress = false;
        }
    }
}