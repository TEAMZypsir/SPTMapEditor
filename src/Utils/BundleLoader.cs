using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using AssetRipper.IO.Endian;
using TransformCacher.AssetRipperIntegration;

namespace TransformCacher
{
    public class BundleLoader : MonoBehaviour
    {
        private static BundleLoader _instance;
        public static BundleLoader Instance => _instance;
        
        private BepInEx.Logging.ManualLogSource Logger;
        private SceneParser _sceneParser;
        private string _modifiedAssetsPath;
        
        public void Initialize()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            
            _instance = this;
            DontDestroyOnLoad(gameObject);
            
            // Get logger
            Logger = BepInEx.Logging.Logger.CreateLogSource("BundleLoader");
            
            // Set up paths
            string pluginPath = Path.GetDirectoryName(typeof(TransformCacherPlugin).Assembly.Location);
            _modifiedAssetsPath = Path.Combine(pluginPath, "ModifiedAssets", "Assets");
            
            // Create scene parser
            _sceneParser = new SceneParser(Logger);
            
            Logger.LogInfo("BundleLoader initialized");
        }
        
        /// <summary>
        /// Load transforms from a bundle file for a specific scene
        /// </summary>
        public List<SerializedTransform> LoadTransformsFromBundle(string bundlePath)
        {
            try
            {
                if (!File.Exists(bundlePath))
                {
                    Logger.LogWarning($"Bundle file not found: {bundlePath}");
                    return new List<SerializedTransform>();
                }
                
                // Use SceneParser to extract transforms
                return _sceneParser.ExtractTransforms(bundlePath);
            }
            catch (Exception ex)
            {
                Logger.LogError($"Error loading transforms from bundle: {ex.Message}");
                return new List<SerializedTransform>();
            }
        }
        
        /// <summary>
        /// Apply transforms from a bundle file to objects in the scene
        /// </summary>
        public void ApplyTransformsFromBundle(string sceneName, bool forceApply = false)
        {
            try
            {
                string bundlePath = Path.Combine(
                    Path.GetDirectoryName(typeof(TransformCacherPlugin).Assembly.Location),
                    "ModifiedAssets", "Assets", "Scenes", $"{sceneName}.bundle");
                    
                if (!File.Exists(bundlePath))
                {
                    Logger.LogWarning($"No bundle file found for scene: {sceneName}");
                    return;
                }
                
                Logger.LogInfo($"Applying transforms from bundle for scene: {sceneName}");
                
                // Load transforms from the bundle
                List<SerializedTransform> transforms = LoadTransformsFromBundle(bundlePath);
                if (transforms.Count == 0)
                {
                    Logger.LogWarning("Bundle contained no valid transforms");
                    return;
                }
                
                // Apply the transforms to objects in the scene
                int appliedCount = 0;
                
                foreach (var transform in transforms)
                {
                    try
                    {
                        // Find the corresponding object in the scene
                        GameObject targetObj = FindObjectByNameOrPath(transform.Name, transform.HierarchyPath);
                        if (targetObj != null)
                        {
                            // Apply transform values from bundle
                            targetObj.transform.position = transform.LocalPosition;
                            targetObj.transform.rotation = transform.LocalRotation;
                            targetObj.transform.localScale = transform.LocalScale;
                            
                            // Add or update tag component
                            TransformCacherTag tag = targetObj.GetComponent<TransformCacherTag>();
                            if (tag == null)
                            {
                                tag = targetObj.AddComponent<TransformCacherTag>();
                            }
                            
                            // Update tag with PathID
                            tag.PathID = transform.PathID.ToString();
                            
                            // Set active state if it was explicitly set in the bundle
                            targetObj.SetActive(transform.IsActive);
                            
                            appliedCount++;
                            Logger.LogInfo($"Applied bundle transform to: {targetObj.name}");
                        }
                        else
                        {
                            Logger.LogWarning($"Could not find object named '{transform.Name}' in scene");
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.LogError($"Error applying transform for {transform.Name}: {ex.Message}");
                    }
                }
                
                Logger.LogInfo($"Applied {appliedCount}/{transforms.Count} transforms from bundle");
                
                // Also update the database to track these changes
                UpdateDatabaseFromBundle(sceneName, transforms);
            }
            catch (Exception ex)
            {
                Logger.LogError($"Error applying transforms from bundle: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Update the TransformCacher database with the transforms from the bundle
        /// </summary>
        private void UpdateDatabaseFromBundle(string sceneName, List<SerializedTransform> transforms)
        {
            try
            {
                if (DatabaseManager.Instance == null)
                {
                    Logger.LogWarning("DatabaseManager not initialized, cannot update database");
                    return;
                }
                
                var transformsDb = DatabaseManager.Instance.GetTransformsDatabase();
                if (!transformsDb.ContainsKey(sceneName))
                {
                    transformsDb[sceneName] = new Dictionary<string, TransformData>();
                }
                
                foreach (var transform in transforms)
                {
                    string uniqueId = transform.PathID.ToString();
                    
                    TransformData data = new TransformData
                    {
                        UniqueId = uniqueId,
                        PathID = transform.PathID.ToString(),
                        ObjectName = transform.Name,
                        ObjectPath = transform.HierarchyPath ?? transform.Name,
                        SceneName = sceneName,
                        Position = transform.LocalPosition,
                        Rotation = transform.LocalRotation.eulerAngles,
                        Scale = transform.LocalScale,
                        IsDestroyed = !transform.IsActive
                    };
                    
                    transformsDb[sceneName][uniqueId] = data;
                }
                
                // Update the database
                DatabaseManager.Instance.SetTransformsDatabase(transformsDb);
                
                Logger.LogInfo($"Updated database with {transforms.Count} transforms from bundle for scene: {sceneName}");
            }
            catch (Exception ex)
            {
                Logger.LogError($"Error updating database from bundle: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Find an object in the scene by name or path
        /// </summary>
        private GameObject FindObjectByNameOrPath(string name, string path)
        {
            if (string.IsNullOrEmpty(name)) return null;
            
            // First try the exact name
            GameObject obj = GameObject.Find(name);
            if (obj != null) return obj;
            
            // Try finding by full path
            if (!string.IsNullOrEmpty(path))
            {
                obj = GameObject.Find(path);
                if (obj != null) return obj;
            }
            
            // Try checking active scene's root objects for name matches
            Scene currentScene = SceneManager.GetActiveScene();
            GameObject[] rootObjects = currentScene.GetRootGameObjects();
            
            // First check direct children of root objects
            foreach (var rootObj in rootObjects)
            {
                Transform child = FindNamedChild(rootObj.transform, name);
                if (child != null) return child.gameObject;
            }
            
            // If not found, search deeper
            foreach (var rootObj in rootObjects)
            {
                Transform child = FindNamedChildRecursive(rootObj.transform, name);
                if (child != null) return child.gameObject;
            }
            
            return null;
        }
        
        /// <summary>
        /// Find a direct child by name
        /// </summary>
        private Transform FindNamedChild(Transform parent, string name)
        {
            foreach (Transform child in parent)
            {
                if (child.name == name || child.name.Equals(name, StringComparison.OrdinalIgnoreCase))
                {
                    return child;
                }
            }
            return null;
        }
        
        /// <summary>
        /// Recursively search for a child by name
        /// </summary>
        private Transform FindNamedChildRecursive(Transform parent, string name)
        {
            foreach (Transform child in parent)
            {
                if (child.name == name || child.name.Equals(name, StringComparison.OrdinalIgnoreCase))
                {
                    return child;
                }
                
                Transform found = FindNamedChildRecursive(child, name);
                if (found != null) return found;
            }
            return null;
        }
    }
}