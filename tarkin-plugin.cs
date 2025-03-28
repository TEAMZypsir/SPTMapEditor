using System;
using System.IO;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using UnityEngine;

namespace TarkinItemExporter
{
    [BepInPlugin("com.username.tarkinitemexporter", "Tarkin Item Exporter", "1.0.0")]
    public class Plugin : BaseUnityPlugin
    {
        public static Plugin Instance { get; private set; }
        
        // Configuration options
        public static ConfigEntry<string> OutputDir;
        
        // Logging
        public static ManualLogSource Log;
        
        // Directory for asset bundles
        public string BundlesDirectory { get; private set; }
        
        private void Awake()
        {
            if (Instance != null)
            {
                Destroy(this);
                return;
            }
            
            Instance = this;
            Log = Logger;
            
            // Initialize configuration
            OutputDir = Config.Bind("General", 
                "OutputDirectory", Path.Combine(Paths.PluginPath, "TarkinExports"), 
                "Directory for exported models");
            
            // Ensure output directory exists
            Directory.CreateDirectory(OutputDir.Value);
            
            // Initialize bundles directory
            BundlesDirectory = Path.Combine(Paths.PluginPath, "TarkinItemExporter", "bundles");
            if (!Directory.Exists(BundlesDirectory))
            {
                Directory.CreateDirectory(BundlesDirectory);
                Log.LogInfo("Created bundles directory at: " + BundlesDirectory);
            }
            
            // Initialize components
            try
            {
                // Create UI components
                GameObject uiManagerObj = new GameObject("TarkinUIManager");
                uiManagerObj.AddComponent<UI.SimpleUIManager>();
                DontDestroyOnLoad(uiManagerObj);
                
                // Initialize bundle loader
                GameObject bundleLoaderObj = new GameObject("BundleLoader");
                bundleLoaderObj.AddComponent<SimpleBundleLoader>();
                DontDestroyOnLoad(bundleLoaderObj);
                
                Log.LogInfo("Tarkin Item Exporter initialized successfully!");
            }
            catch (Exception ex)
            {
                Log.LogError($"Failed to initialize Tarkin Item Exporter: {ex.Message}\n{ex.StackTrace}");
            }
        }
    }
}