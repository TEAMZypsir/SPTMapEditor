using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TransformCacher
{
    public class AssetRedirector : MonoBehaviour
    {
        private static AssetRedirector _instance;
        public static AssetRedirector Instance
        {
            get
            {
                if (_instance == null)
                {
                    GameObject go = new GameObject("AssetRedirector");
                    DontDestroyOnLoad(go);
                    _instance = go.AddComponent<AssetRedirector>();
                }
                return _instance;
            }
        }
        
        // Logger reference
        private BepInEx.Logging.ManualLogSource Logger;
        
        // Asset paths
        private string _modDirectory;
        private string _modifiedAssetsPath;
        
        // Scenes that have been modified
        private HashSet<string> _modifiedScenes = new HashSet<string>();
        
        // Harmony for patching
        private Harmony _harmony;
        
        public void Initialize()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            
            _instance = this;
            
            // Get logger
            Logger = BepInEx.Logging.Logger.CreateLogSource("AssetRedirector");
            
            // Set up asset paths
            string pluginPath = Path.GetDirectoryName(typeof(TransformCacherPlugin).Assembly.Location);
            _modDirectory = Path.Combine(pluginPath, "ModifiedAssets");
            _modifiedAssetsPath = Path.Combine(_modDirectory, "Assets");
            
            // Initialize Harmony for patching
            _harmony = new Harmony("com.transformcacher.assetredirector");
            
            // Apply Harmony patches
            ApplyPatches();
            
            // Add scene loading hook
            SceneManager.sceneLoaded += OnSceneLoaded;
            
            Logger.LogInfo("AssetRedirector initialized");
        }

        /// <summary>
        /// Register modified scenes that should be loaded from our mod directory
        /// </summary>
        public void RegisterModifiedScenes(List<string> sceneNames)
        {
            foreach (string sceneName in sceneNames)
            {
                _modifiedScenes.Add(sceneName);
            }
            
            Logger.LogInfo($"Registered {sceneNames.Count} modified scenes");
        }
        
        /// <summary>
        /// Check if a scene has been modified
        /// </summary>
        public bool IsSceneModified(string sceneName)
        {
            // First check our registered modified scenes list
            if (_modifiedScenes.Contains(sceneName))
            {
                return true;
            }
            
            // Next, check if we have a bundle file for this scene
            string bundlePath = Path.Combine(_modifiedAssetsPath, "Scenes", $"{sceneName}.bundle");
            if (File.Exists(bundlePath))
            {
                // Try to check if the bundle contains our marker
                try {
                    using (FileStream fs = new FileStream(bundlePath, FileMode.Open, FileAccess.Read))
                    {
                        // Read a larger portion to ensure we capture the marker
                        byte[] buffer = new byte[8192]; // Increased buffer size
                        fs.Read(buffer, 0, buffer.Length);
                        
                        // Try UTF8 first
                        string content = System.Text.Encoding.UTF8.GetString(buffer);
                        
                        // If it contains our marker, then it's a properly modified scene
                        if (content.Contains("MODIFIED_BY_TRANSFORM_CACHER"))
                        {
                            // If the file exists but wasn't in our modified scenes list, add it
                            _modifiedScenes.Add(sceneName);
                            Logger.LogInfo($"Found valid bundle file for scene: {sceneName}, marking as modified (UTF8 marker)");
                            return true;
                        }
                        
                        // Try ASCII as fallback
                        content = System.Text.Encoding.ASCII.GetString(buffer);
                        if (content.Contains("MODIFIED_BY_TRANSFORM_CACHER"))
                        {
                            _modifiedScenes.Add(sceneName);
                            Logger.LogInfo($"Found valid bundle file for scene: {sceneName}, marking as modified (ASCII marker)");
                            return true;
                        }
                        
                        // If we have a BundleLoader available, try using that
                        if (BundleLoader.Instance != null)
                        {
                            var transforms = BundleLoader.Instance.LoadTransformsFromBundle(bundlePath);
                            if (transforms != null && transforms.Count > 0)
                            {
                                _modifiedScenes.Add(sceneName);
                                Logger.LogInfo($"Found valid transforms in bundle file for scene: {sceneName}, marking as modified");
                                return true;
                            }
                        }
                        
                        Logger.LogInfo($"Found bundle file for scene: {sceneName}, but it doesn't contain our marker");
                    }
                }
                catch (Exception ex) {
                    Logger.LogWarning($"Error checking bundle file: {ex.Message}");
                    
                    // If we can't read the file, still add it to modified scenes as a precaution
                    _modifiedScenes.Add(sceneName);
                    return true;
                }
            }
            
            // Next, check if we have a unity file for this scene
            string unityFilePath = Path.Combine(_modifiedAssetsPath, "Scenes", $"{sceneName}.unity");
            if (File.Exists(unityFilePath))
            {
                // If the file exists but wasn't in our modified scenes list, add it
                _modifiedScenes.Add(sceneName);
                Logger.LogInfo($"Found unity file for scene: {sceneName}, marking as modified");
                return true;
            }
            
            // Not modified
            return false;
        }
        
        /// <summary>
        /// Apply Harmony patches for asset loading
        /// </summary>
        private void ApplyPatches()
        {
            try
            {
                // Patch SceneManager.LoadScene
                MethodInfo originalLoadScene = typeof(SceneManager).GetMethod("LoadScene", 
                    new Type[] { typeof(string), typeof(LoadSceneMode) });
                    
                MethodInfo patchLoadScene = typeof(AssetRedirector).GetMethod("PrefixLoadScene", 
                    BindingFlags.Static | BindingFlags.Public);
                    
                _harmony.Patch(originalLoadScene, 
                    prefix: new HarmonyMethod(patchLoadScene));
                
                // Patch Resources.Load
                MethodInfo originalResourcesLoad = typeof(Resources).GetMethod("Load", 
                    new Type[] { typeof(string), typeof(Type) });
                    
                MethodInfo patchResourcesLoad = typeof(AssetRedirector).GetMethod("PrefixResourcesLoad", 
                    BindingFlags.Static | BindingFlags.Public);
                    
                _harmony.Patch(originalResourcesLoad, 
                    prefix: new HarmonyMethod(patchResourcesLoad));
                
                Logger.LogInfo("Applied Harmony patches for asset redirection");
            }
            catch (Exception ex)
            {
                Logger.LogError($"Error applying Harmony patches: {ex.Message}");
            }
        }
        
        /// <summary>
        /// OnSceneLoaded event handler
        /// </summary>
        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            Logger.LogInfo($"Scene loaded: {scene.name} (Modified: {IsSceneModified(scene.name)})");
            
            if (IsSceneModified(scene.name))
            {
                // If this is one of our modified scenes, we can skip applying runtime changes
                // since the modifications are already in the loaded scene file
                Logger.LogInfo($"Loaded modified scene {scene.name}, skipping runtime changes");
                
                // Signal to other components that they don't need to apply runtime changes
                EventSystem.TriggerEvent("ModifiedSceneLoaded", scene.name);
            }
        }
        
        /// <summary>
        /// Prefix patch for SceneManager.LoadScene
        /// </summary>
        public static bool PrefixLoadScene(string sceneName, LoadSceneMode mode, ref string __0)
        {
            try
            {
                // Check if this scene has been modified
                if (Instance.IsSceneModified(sceneName))
                {
                    // First check for a bundle file (our preferred format)
                    string modifiedBundlePath = Path.Combine(Instance._modifiedAssetsPath, "Scenes", $"{sceneName}.bundle");
                    
                    if (File.Exists(modifiedBundlePath))
                    {
                        // Redirect to our modified bundle
                        __0 = modifiedBundlePath;
                        Instance.Logger.LogInfo($"Redirected scene load: {sceneName} -> {modifiedBundlePath}");
                        return true;
                    }
                    
                    // Fall back to unity file format if bundle not found
                    string modifiedScenePath = Path.Combine(Instance._modifiedAssetsPath, "Scenes", $"{sceneName}.unity");
                    
                    if (File.Exists(modifiedScenePath))
                    {
                        // Redirect to our modified scene
                        __0 = modifiedScenePath;
                        Instance.Logger.LogInfo($"Redirected scene load: {sceneName} -> {modifiedScenePath}");
                    }
                }
            }
            catch (Exception ex)
            {
                Instance.Logger.LogError($"Error in PrefixLoadScene: {ex.Message}");
            }
            
            // Always continue with the original method
            return true;
        }
        
        /// <summary>
        /// Prefix patch for Resources.Load
        /// </summary>
        public static bool PrefixResourcesLoad(string path, Type type, ref string __0)
        {
            try
            {
                // Check if this resource is in a modified scene's streaming assets
                foreach (string sceneName in Instance._modifiedScenes)
                {
                    if (path.Contains(sceneName))
                    {
                        string modifiedResourcePath = Path.Combine(Instance._modifiedAssetsPath, 
                            "StreamingAssets", path);
                        
                        if (File.Exists(modifiedResourcePath))
                        {
                            // Redirect to our modified resource
                            __0 = modifiedResourcePath;
                            Instance.Logger.LogInfo($"Redirected resource load: {path} -> {modifiedResourcePath}");
                            break;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Instance.Logger.LogError($"Error in PrefixResourcesLoad: {ex.Message}");
            }
            
            // Always continue with the original method
            return true;
        }
    }
}