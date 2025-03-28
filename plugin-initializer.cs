using System;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace TransformCacher
{
    [BepInPlugin("com.username.transformcacher", "Transform Cacher", "1.1.0")]
    public class TransformCacherPlugin : BaseUnityPlugin
    {
        // Main components
        private GameObject _transformCacherObj;
        private DatabaseManager _databaseManager;
        
        private void Awake()
        {
            try
            {
                // Initialize configuration
                TransformCacher.EnablePersistence = Config.Bind("General", 
                    "EnablePersistence", true, 
                    "Apply saved transforms when scenes load");
                    
                TransformCacher.EnableDebugGUI = Config.Bind("General", 
                    "EnableDebugGUI", true, 
                    "Show debug GUI window");
                    
                TransformCacher.EnableObjectHighlight = Config.Bind("General", 
                    "EnableObjectHighlight", true, 
                    "Show blue highlight effect on selected objects");
                
                TransformCacher.TransformDelay = Config.Bind("Advanced", 
                    "TransformDelay", 2.0f, 
                    "Delay in seconds before applying transforms after scene load");
                
                TransformCacher.MaxRetries = Config.Bind("Advanced", 
                    "MaxRetries", 3, 
                    "Maximum number of attempts to apply transforms");
                    
                TransformCacher.SaveHotkey = Config.Bind("Hotkeys", 
                    "SaveHotkey", new KeyboardShortcut(KeyCode.F9), 
                    "Hotkey to save all tagged objects");
                    
                TransformCacher.TagHotkey = Config.Bind("Hotkeys", 
                    "TagHotkey", new KeyboardShortcut(KeyCode.F10), 
                    "Hotkey to tag currently inspected object");
                    
                TransformCacher.DestroyHotkey = Config.Bind("Hotkeys", 
                    "DestroyHotkey", new KeyboardShortcut(KeyCode.Delete), 
                    "Hotkey to destroy currently inspected object");
                    
                TransformCacher.SpawnHotkey = Config.Bind("Hotkeys", 
                    "SpawnHotkey", new KeyboardShortcut(KeyCode.F8), 
                    "Hotkey to open prefab selector for spawning");
                    
                // Add the new mouse toggle hotkey
                TransformCacher.MouseToggleHotkey = Config.Bind("Hotkeys", 
                    "MouseToggleHotkey", new KeyboardShortcut(KeyCode.Tab, KeyCode.LeftAlt), 
                    "Hotkey to toggle between mouse UI control and game control");
                
                // Initialize Harmony for patching
                Harmony harmony = new Harmony("com.username.transformcacher");
                harmony.PatchAll();
                
                // Create directory for database
                string saveDir = System.IO.Path.Combine(Paths.PluginPath, "TransformCacher");
                if (!System.IO.Directory.Exists(saveDir))
                {
                    System.IO.Directory.CreateDirectory(saveDir);
                }
                
                // Initialize DatabaseManager (singleton)
                _databaseManager = DatabaseManager.Instance;
                _databaseManager.Initialize();
                
                // Create GameObject and add components
                _transformCacherObj = new GameObject("TransformCacher");
                _transformCacherObj.AddComponent<TransformCacher>();
                _transformCacherObj.AddComponent<TransformIdBaker>();
                
                // Add the exporter integration component
                _transformCacherObj.AddComponent<ExporterIntegration>();
                
                DontDestroyOnLoad(_transformCacherObj);
                
                Logger.LogInfo("Transform Cacher initialized successfully!");
            }
            catch (Exception ex)
            {
                Logger.LogError($"Failed to initialize Transform Cacher: {ex.Message}\n{ex.StackTrace}");
            }
        }
        
        private void OnDestroy()
        {
            // Clean up
            if (_transformCacherObj != null)
                Destroy(_transformCacherObj);
                
            Logger.LogInfo("Transform Cacher unloaded");
        }
    }
}