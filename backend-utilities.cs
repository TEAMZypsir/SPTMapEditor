using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using UnityEngine;

namespace TransformCacher
{
    #region Logging

    /// <summary>
    /// BepInEx logger implementation for external libraries
    /// </summary>
    public class BepinexLogger : ILogger
    {
        private ManualLogSource _logger;

        public BepinexLogger(ManualLogSource logger)
        {
            _logger = logger;
        }

        public void Log(LoggerEvent loggerEvent, string message, bool ignoreLevel = false)
        {
            switch (loggerEvent)
            {
                case LoggerEvent.Verbose:
                case LoggerEvent.Debug:
                case LoggerEvent.Info:
                    _logger.LogInfo(message);
                    break;
                case LoggerEvent.Warning:
                    _logger.LogWarning(message);
                    break;
                case LoggerEvent.Error:
                    _logger.LogError(message);
                    break;
            }
        }
    }

    /// <summary>
    /// Progress logger for async operations
    /// </summary>
    public class ProgressLogger : IProgress<int>
    {
        public static event Action<int> OnProgress;
        
        public void Report(int value)
        {
            OnProgress?.Invoke(value);
        }
    }

    #endregion

    #region Unity Extensions

    /// <summary>
    /// Extension methods for Unity transforms
    /// </summary>
    public static class UnityExtensions
    {
        /// <summary>
        /// Get the root transform of a given transform
        /// </summary>
        public static Transform GetRoot(this Transform tr)
        {
            while (tr.parent != null)
            {
                tr = tr.parent;
            }
            return tr;
        }

        /// <summary>
        /// Zero out all transforms up the hierarchy
        /// </summary>
        public static void ZeroTransformAndItsParents(this Transform tr)
        {
            do
            {
                tr.localPosition = Vector3.zero;
                tr.localRotation = Quaternion.identity;
                tr.localScale = Vector3.one;
                tr = tr.parent;
            }
            while (tr.parent != null);
        }

        /// <summary>
        /// Destroy all components of a given type
        /// </summary>
        public static void DestroyAll<T>(this T[] components) where T : Component
        {
            if (components == null) return;
            
            foreach (T t in components)
            {
                if (t != null)
                {
                    UnityEngine.Object.Destroy(t);
                }
            }
        }
    }

    #endregion

    #region LoggerEvent Enum

    /// <summary>
    /// Logger event types for BepInEx logger
    /// </summary>
    public enum LoggerEvent
    {
        Verbose,
        Debug,
        Info,
        Warning,
        Error
    }

    #endregion

    #region ILogger Interface

    /// <summary>
    /// Interface for loggers that can be used by external libraries
    /// </summary>
    public interface ILogger
    {
        void Log(LoggerEvent loggerEvent, string message, bool ignoreLevel = false);
    }

    #endregion

    #region Asset Studio Integration

    /// <summary>
    /// Static class for helping with AssetStudio integration
    /// </summary>
    public static class AssetStudioHelper
    {
        /// <summary>
        /// Initialize AssetStudio logging with BepInEx logger
        /// </summary>
        public static void InitializeAssetStudioLogging(ManualLogSource logSource)
        {
            try
            {
                // Find AssetStudio Logger class via reflection
                var assetStudioAssembly = AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(a => a.GetName().Name.Contains("AssetStudio"));
                    
                if (assetStudioAssembly != null)
                {
                    var loggerType = assetStudioAssembly.GetType("AssetStudio.Logger");
                    if (loggerType != null)
                    {
                        var defaultField = loggerType.GetField("Default", BindingFlags.Public | BindingFlags.Static);
                        if (defaultField != null)
                        {
                            // Create our logger and assign it to AssetStudio's Logger.Default
                            var bepinexLogger = new BepinexLogger(logSource);
                            defaultField.SetValue(null, bepinexLogger);
                        }
                    }
                    
                    var progressType = assetStudioAssembly.GetType("AssetStudio.Progress");
                    if (progressType != null)
                    {
                        var defaultField = progressType.GetField("Default", BindingFlags.Public | BindingFlags.Static);
                        if (defaultField != null)
                        {
                            // Create progress logger
                            var progressLogger = new ProgressLogger();
                            defaultField.SetValue(null, progressLogger);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                logSource.LogError($"Failed to initialize AssetStudio logging: {ex.Message}");
            }
        }
    }

    #endregion
}
namespace TransformCacher
{
    [BepInPlugin("com.transformcacher.plugin", "TransformCacher", "1.1.0")]
    public class TransformCacherPlugin : BaseUnityPlugin
    {
        // Configuration entries
        public static ConfigEntry<bool> EnablePersistence;
        public static ConfigEntry<bool> EnableDebugGUI;
        public static ConfigEntry<bool> EnableObjectHighlight;
        public static ConfigEntry<KeyboardShortcut> SaveHotkey;
        public static ConfigEntry<KeyboardShortcut> TagHotkey;
        public static ConfigEntry<KeyboardShortcut> DestroyHotkey;
        public static ConfigEntry<KeyboardShortcut> SpawnHotkey;
        public static ConfigEntry<KeyboardShortcut> MouseToggleHotkey;
        public static ConfigEntry<float> TransformDelay;
        public static ConfigEntry<int> MaxRetries;
        
        // New configuration entries for export
        public static ConfigEntry<string> DefaultExportPath;
        public static ConfigEntry<bool> AlwaysExportTextures;
        
        // Logging
        public static ManualLogSource Log;
        
        // Initialized flag
        private bool _initialized = false;

        private void Awake()
        {
            // Set up logging
            Log = Logger;
            Log.LogInfo("TransformCacher is starting...");

            // Initialize configuration
            InitializeConfiguration();
            
            // Initialize dependency loader first
            DependencyLoader.Initialize();
            
            try
            {
                // Initialize components
                GameObject managerObject = new GameObject("TransformCacherManager");
                DontDestroyOnLoad(managerObject);

                // Initialize DatabaseManager first as it's needed by other components
                DatabaseManager databaseManager = DatabaseManager.Instance;
                if (databaseManager == null)
                {
                    Log.LogError("Failed to get database manager instance");
                }
                else
                {
                    databaseManager.Initialize();
                    Log.LogInfo("DatabaseManager initialized");
                }

                // Add main controller components
                var transformCacher = managerObject.AddComponent<TransformCacher>();
                
                // Add TransformIdBaker
                var idBaker = managerObject.AddComponent<TransformIdBaker>();
                if (idBaker != null)
                {
                    idBaker.Initialize();
                    Log.LogInfo("TransformIdBaker initialized");
                }
                
                // Add enhanced exporter
                var exporter = managerObject.AddComponent<EnhancedExporter>();
                if (exporter != null)
                {
                    exporter.Initialize();
                    
                    // Set export path from config
                    if (!string.IsNullOrEmpty(DefaultExportPath.Value))
                    {
                        exporter.ExportPath = DefaultExportPath.Value;
                    }
                    
                    Log.LogInfo("EnhancedExporter initialized");
                }

                // Add GUI last since it depends on other components
                var gui = managerObject.AddComponent<TransformCacherGUI>();
                if (gui != null && transformCacher != null && databaseManager != null)
                {
                    gui.Initialize(transformCacher, databaseManager, idBaker);
                    Log.LogInfo("GUI initialized");
                }
                
                _initialized = true;
                StartCoroutine(InitializeBundles());
                
                Log.LogInfo("TransformCacher initialized successfully");
            }
            catch (Exception ex)
            {
                Log.LogError($"Error during initialization: {ex.Message}\n{ex.StackTrace}");
            }
        }
        
        private IEnumerator InitializeBundles()
        {
            // Wait a bit for the game to fully initialize
            yield return new WaitForSeconds(2f);
            
            try
            {
                // Try to find or load UnityGLTF assemblies
                var unityGltfAssembly = DependencyLoader.LoadAssembly("UnityGLTFScripts");
                if (unityGltfAssembly == null)
                {
                    Log.LogWarning("UnityGLTFScripts assembly not found in libs directory");
                }
                else
                {
                    Log.LogInfo("Successfully loaded UnityGLTFScripts assembly");
                }
                
                // Load any required helper assemblies
                DependencyLoader.LoadAssembly("GLTFSerialization");
                DependencyLoader.LoadAssembly("UnityGLTF.Helpers");
                
                // Try to load AssetStudio if needed (optional)
                var assetStudioAssembly = DependencyLoader.LoadAssembly("AssetStudio");
                if (assetStudioAssembly != null)
                {
                    Log.LogInfo("Successfully loaded AssetStudio assembly");
                    
                    // Initialize AssetStudio logging
                    try
                    {
                        var loggerType = assetStudioAssembly.GetType("AssetStudio.Logger");
                        if (loggerType != null)
                        {
                            var defaultField = loggerType.GetField("Default", 
                                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                                
                            if (defaultField != null)
                            {
                                // Find or create BepinexLogger
                                var bepinexLoggerType = typeof(TransformCacher).Assembly.GetType("TransformCacher.BepinexLogger");
                                if (bepinexLoggerType != null)
                                {
                                    var constructor = bepinexLoggerType.GetConstructor(new[] { typeof(ManualLogSource) });
                                    if (constructor != null)
                                    {
                                        var logger = constructor.Invoke(new object[] { Log });
                                        defaultField.SetValue(null, logger);
                                        Log.LogInfo("Configured AssetStudio logger");
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.LogWarning($"Error configuring AssetStudio logging: {ex.Message}");
                    }
                }
                
                // Look for shader bundles in the libs directory
                string libsPath = Path.Combine(Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location), "libs");
                string bundlePath = Path.Combine(libsPath, "unitygltf");
                
                if (File.Exists(bundlePath))
                {
                    Log.LogInfo($"Found shader bundle: {bundlePath}");
                    
                    try
                    {
                        var bundle = AssetBundle.LoadFromFile(bundlePath);
                        if (bundle != null)
                        {
                            var shaders = bundle.LoadAllAssets<Shader>();
                            if (shaders != null && shaders.Length > 0)
                            {
                                // Add shaders to BundleShaders
                                var bundleShadersType = typeof(TransformCacher).Assembly.GetType("TransformCacher.BundleShaders");
                                if (bundleShadersType != null)
                                {
                                    var addMethod = bundleShadersType.GetMethod("Add", new[] { typeof(Shader[]) });
                                    if (addMethod != null)
                                    {
                                        addMethod.Invoke(null, new object[] { shaders });
                                        Log.LogInfo($"Added {shaders.Length} shaders from bundle");
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.LogError($"Error loading shader bundle: {ex.Message}");
                    }
                }
                else
                {
                    Log.LogWarning($"Shader bundle not found: {bundlePath}");
                }
            }
            catch (Exception ex)
            {
                Log.LogError($"Error during bundle initialization: {ex.Message}\n{ex.StackTrace}");
            }
        }

        private void InitializeConfiguration()
        {
            // Plugin settings
            EnablePersistence = Config.Bind("General", "EnablePersistence", true, 
                "Enable persistence of object transforms across game sessions");
            
            EnableDebugGUI = Config.Bind("General", "EnableDebugGUI", true, 
                "Enable debug GUI for transform management");
            
            EnableObjectHighlight = Config.Bind("General", "EnableObjectHighlight", true, 
                "Enable highlighting of selected objects");

            // Hotkeys
            SaveHotkey = Config.Bind("Hotkeys", "SaveHotkey", new KeyboardShortcut(KeyCode.F9), 
                "Hotkey to save all tagged objects");
            
            TagHotkey = Config.Bind("Hotkeys", "TagHotkey", new KeyboardShortcut(KeyCode.F10), 
                "Hotkey to tag the currently inspected object");
            
            DestroyHotkey = Config.Bind("Hotkeys", "DestroyHotkey", new KeyboardShortcut(KeyCode.Delete), 
                "Hotkey to mark the currently inspected object for destruction");
            
            SpawnHotkey = Config.Bind("Hotkeys", "SpawnHotkey", new KeyboardShortcut(KeyCode.F8), 
                "Hotkey to open the prefab selector");
            
            MouseToggleHotkey = Config.Bind("Hotkeys", "MouseToggleHotkey", new KeyboardShortcut(KeyCode.Tab, KeyCode.LeftAlt), 
                "Hotkey to toggle between mouse UI control and game control");

            // Advanced settings
            TransformDelay = Config.Bind("Advanced", "TransformDelay", 2.0f, 
                "Delay in seconds before applying transforms after scene load");
            
            MaxRetries = Config.Bind("Advanced", "MaxRetries", 3, 
                "Maximum number of retry attempts for applying transforms");
            
            // Export settings
            DefaultExportPath = Config.Bind("Export", "DefaultExportPath", 
                Path.Combine(Paths.PluginPath, "TransformCacher", "Exports"),
                "Default path for exporting models");
                
            AlwaysExportTextures = Config.Bind("Export", "AlwaysExportTextures", true,
                "Always export textures with models");

            Log.LogInfo("Configuration initialized");
        }
    }
}