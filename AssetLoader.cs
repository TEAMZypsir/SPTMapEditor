using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx;
using UnityEngine;
using UnityEngine.AssetBundleModule;

namespace TransformCacher
{
    /// <summary>
    /// Utility class for loading and managing asset bundles
    /// </summary>
    public class AssetBundleLoader
    {
        private static BepInEx.Logging.ManualLogSource Logger = BepInEx.Logging.Logger.CreateLogSource("AssetBundleLoader");
        
        // Cache for loaded bundles
        private static Dictionary<string, AssetBundle> _loadedBundles = new Dictionary<string, AssetBundle>();
        
        // Path to bundles directory
        private static string _bundleDirectory => Path.Combine(Paths.PluginPath, "TransformCacher", "bundles");
        
        /// <summary>
        /// Get all bundle files in the bundles directory and subdirectories
        /// </summary>
        public static List<string> GetBundleFiles()
        {
            List<string> bundles = new List<string>();
            
            try
            {
                // Create bundles directory if it doesn't exist
                if (!Directory.Exists(_bundleDirectory))
                {
                    Directory.CreateDirectory(_bundleDirectory);
                    Logger.LogInfo($"Created bundle directory: {_bundleDirectory}");
                    return bundles;
                }
                
                // Get all files excluding common non-bundle files
                string[] files = Directory.GetFiles(_bundleDirectory, "*", SearchOption.AllDirectories);
                foreach (string file in files)
                {
                    // Skip meta files, manifests, etc.
                    if (file.EndsWith(".meta") || file.EndsWith(".manifest") || 
                        file.EndsWith(".txt") || file.EndsWith(".md"))
                        continue;
                        
                    bundles.Add(file);
                }
                
                Logger.LogInfo($"Found {bundles.Count} bundle files");
            }
            catch (Exception ex)
            {
                Logger.LogError($"Error getting bundle files: {ex.Message}");
            }
            
            return bundles;
        }
        
        /// <summary>
        /// Get all subdirectories in the bundles directory
        /// </summary>
        public static List<string> GetBundleDirectories()
        {
            List<string> directories = new List<string>();
            
            try
            {
                if (!Directory.Exists(_bundleDirectory))
                {
                    Directory.CreateDirectory(_bundleDirectory);
                    return directories;
                }
                
                string[] subdirs = Directory.GetDirectories(_bundleDirectory);
                directories.AddRange(subdirs);
            }
            catch (Exception ex)
            {
                Logger.LogError($"Error getting bundle directories: {ex.Message}");
            }
            
            return directories;
        }
        
        /// <summary>
        /// Load an asset bundle and cache it
        /// </summary>
        public static AssetBundle LoadBundle(string bundlePath)
        {
            try
            {
                // Check if bundle is already loaded
                if (_loadedBundles.TryGetValue(bundlePath, out AssetBundle cachedBundle))
                {
                    return cachedBundle;
                }
                
                // Load the bundle from disk
                AssetBundle bundle = AssetBundle.LoadFromFile(bundlePath);
                if (bundle == null)
                {
                    Logger.LogError($"Failed to load bundle: {bundlePath}");
                    return null;
                }
                
                // Cache the bundle
                _loadedBundles[bundlePath] = bundle;
                
                Logger.LogInfo($"Loaded bundle: {Path.GetFileName(bundlePath)}");
                return bundle;
            }
            catch (Exception ex)
            {
                Logger.LogError($"Error loading bundle {bundlePath}: {ex.Message}");
                return null;
            }
        }
        
        /// <summary>
        /// Get all GameObject assets from a bundle
        /// </summary>
        public static List<GameObject> GetGameObjectsFromBundle(string bundlePath)
        {
            List<GameObject> objects = new List<GameObject>();
            
            try
            {
                AssetBundle bundle = LoadBundle(bundlePath);
                if (bundle == null)
                {
                    return objects;
                }
                
                // Get all asset names in the bundle
                string[] assetNames = bundle.GetAllAssetNames();
                
                foreach (string assetName in assetNames)
                {
                    try
                    {
                        // Try to load as GameObject
                        GameObject obj = bundle.LoadAsset<GameObject>(assetName);
                        if (obj != null)
                        {
                            objects.Add(obj);
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.LogWarning($"Error loading asset {assetName}: {ex.Message}");
                    }
                }
                
                Logger.LogInfo($"Found {objects.Count} GameObjects in bundle {Path.GetFileName(bundlePath)}");
            }
            catch (Exception ex)
            {
                Logger.LogError($"Error getting objects from bundle {bundlePath}: {ex.Message}");
            }
            
            return objects;
        }
        
        /// <summary>
        /// Unload all loaded bundles
        /// </summary>
        public static void UnloadAllBundles(bool unloadAllAssets = false)
        {
            try
            {
                foreach (var bundle in _loadedBundles.Values)
                {
                    if (bundle != null)
                    {
                        bundle.Unload(unloadAllAssets);
                    }
                }
                
                _loadedBundles.Clear();
                Logger.LogInfo("Unloaded all asset bundles");
            }
            catch (Exception ex)
            {
                Logger.LogError($"Error unloading bundles: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Unload a specific bundle
        /// </summary>
        public static void UnloadBundle(string bundlePath, bool unloadAllAssets = false)
        {
            try
            {
                if (_loadedBundles.TryGetValue(bundlePath, out AssetBundle bundle))
                {
                    if (bundle != null)
                    {
                        bundle.Unload(unloadAllAssets);
                    }
                    
                    _loadedBundles.Remove(bundlePath);
                    Logger.LogInfo($"Unloaded bundle: {Path.GetFileName(bundlePath)}");
                }
            }
            catch (Exception ex)
            {
                Logger.LogError($"Error unloading bundle {bundlePath}: {ex.Message}");
            }
        }
    }
}