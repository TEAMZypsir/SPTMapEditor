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
}
namespace TransformCacher
{
    // Define the UISide options enum
    public enum UISideOption
    {
        Left,
        Right
    }

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
        public static ConfigEntry<float> TransformDelay;
        public static ConfigEntry<int> MaxRetries;
        
        // UI positioning configuration
        public static ConfigEntry<UISideOption> UISide;
        
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

                // Add GUI last since it depends on other components
                var gui = managerObject.AddComponent<TransformCacherGUI>();
                if (gui != null && transformCacher != null && databaseManager != null)
                {
                    gui.Initialize(transformCacher, databaseManager, idBaker);
                    Log.LogInfo("GUI initialized");
                }
                
                _initialized = true;
                
                Log.LogInfo("TransformCacher initialized successfully");
            }
            catch (Exception ex)
            {
                Log.LogError($"Error during initialization: {ex.Message}\n{ex.StackTrace}");
            }
        }
        
        public void OnDisable()
        {
            if (_initialized)
            {
                // Perform cleanup only if initialized
                Log.LogInfo("TransformCacher plugin disabled");
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
            
            // UI positioning configuration
            UISide = Config.Bind("General", "UISide", UISideOption.Left, 
                "Which side of the screen to pin the UI to (Left or Right)");

            // Hotkeys
            SaveHotkey = Config.Bind("Hotkeys", "SaveHotkey", new KeyboardShortcut(KeyCode.F9), 
                "Hotkey to save all tagged objects");
            
            TagHotkey = Config.Bind("Hotkeys", "TagHotkey", new KeyboardShortcut(KeyCode.F10), 
                "Hotkey to tag the currently inspected object");
            
            DestroyHotkey = Config.Bind("Hotkeys", "DestroyHotkey", new KeyboardShortcut(KeyCode.Delete), 
                "Hotkey to mark the currently inspected object for destruction");
            
            SpawnHotkey = Config.Bind("Hotkeys", "SpawnHotkey", new KeyboardShortcut(KeyCode.F8), 
                "Hotkey to open the prefab selector");
            
            // Advanced settings
            TransformDelay = Config.Bind("Advanced", "TransformDelay", 2.0f, 
                "Delay in seconds before applying transforms after scene load");
            
            MaxRetries = Config.Bind("Advanced", "MaxRetries", 3, 
                "Maximum number of retry attempts for applying transforms");

            Log.LogInfo("Configuration initialized");
        }
    }
}