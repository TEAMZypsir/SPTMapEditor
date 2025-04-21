using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TransformCacher
{
    /// <summary>
    /// Handles object destruction and memory management
    /// </summary>
    public class DestroyHandler : MonoBehaviour
    {
        private static DestroyHandler _instance;
        public static DestroyHandler Instance
        {
            get
            {
                if (_instance == null)
                {
                    GameObject go = new GameObject("DestroyHandler");
                    DontDestroyOnLoad(go);
                    _instance = go.AddComponent<DestroyHandler>();
                }
                return _instance;
            }
        }
        
        // Database manager reference
        private DatabaseManager _databaseManager;
        
        // Logger reference
        private BepInEx.Logging.ManualLogSource Logger;
        
        // Destroyed objects tracking
        private Dictionary<string, HashSet<string>> _destroyedObjectsCache = new Dictionary<string, HashSet<string>>();
        
        /// <summary>
        /// Initialize the DestroyHandler
        /// </summary>
        public void Initialize()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(this);
                return;
            }
            
            _instance = this;
            
            // Get logger from plugin
            Logger = BepInEx.Logging.Logger.CreateLogSource("DestroyHandler");
            
            // Get database manager instance
            _databaseManager = DatabaseManager.Instance;
            
            Logger.LogInfo("DestroyHandler initialized");
        }

        /// <summary>
        /// Mark an object for destruction and destroy it immediately 
        /// </summary>
        public void MarkForDestruction(GameObject obj)
        {
            if (obj == null) return;
            
            try
            {
                string sceneName = SceneManager.GetActiveScene().name;
                
                // Create cache entry for the scene if it doesn't exist
                if (!_destroyedObjectsCache.ContainsKey(sceneName))
                {
                    _destroyedObjectsCache[sceneName] = new HashSet<string>();
                }
                
                // Get or add tag
                TransformCacherTag tag = obj.GetComponent<TransformCacherTag>();
                if (tag == null)
                {
                    tag = obj.AddComponent<TransformCacherTag>();
                    tag.PathID = FixUtility.GeneratePathID(obj.transform);
                    tag.ItemID = FixUtility.GenerateItemID(obj.transform);
                }
                
                // Mark as destroyed
                tag.IsDestroyed = true;
                
                string uniqueId = FixUtility.GenerateUniqueId(obj.transform);
                string objectPath = FixUtility.GetFullPath(obj.transform);
                
                // Cache the object path
                _destroyedObjectsCache[sceneName].Add(objectPath);
                
                // Update transforms database
                var transformsDb = _databaseManager.GetTransformsDatabase();
                
                if (!transformsDb.ContainsKey(sceneName))
                {
                    transformsDb[sceneName] = new Dictionary<string, TransformData>();
                }
                
                // Update or add entry to mark as destroyed
                if (transformsDb[sceneName].ContainsKey(uniqueId))
                {
                    transformsDb[sceneName][uniqueId].IsDestroyed = true;
                }
                else
                {
                    // Create a new entry
                    var data = new TransformData
                    {
                        UniqueId = uniqueId,
                        PathID = tag.PathID,
                        ItemID = tag.ItemID,
                        ObjectPath = objectPath,
                        ObjectName = obj.name,
                        SceneName = sceneName,
                        Position = obj.transform.position,
                        Rotation = obj.transform.eulerAngles,
                        Scale = obj.transform.localScale,
                        ParentPath = obj.transform.parent != null ? FixUtility.GetFullPath(obj.transform.parent) : "",
                        IsDestroyed = true
                    };
                    
                    transformsDb[sceneName][uniqueId] = data;
                }
                
                // Update database
                _databaseManager.SetTransformsDatabase(transformsDb);
                _databaseManager.SaveTransformsDatabase();
                
                // Also mark all children as destroyed
                MarkChildrenAsDestroyed(obj.transform, sceneName);
                
                // Hide the object but don't destroy it yet (will be destroyed on scene reload)
                obj.SetActive(false);
                
                Logger.LogInfo($"Marked object for destruction: {objectPath}");
            }
            catch (Exception ex)
            {
                Logger.LogError($"Error marking object for destruction: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Mark all children as destroyed
        /// </summary>
        public void MarkChildrenAsDestroyed(Transform parent, string sceneName)
        {
            if (parent == null) return;
            
            // Get the transform database
            var transformsDb = _databaseManager.GetTransformsDatabase();
            
            // Process each child
            foreach (Transform child in parent)
            {
                GameObject childObj = child.gameObject;
                
                // Get or add tag
                TransformCacherTag tag = childObj.GetComponent<TransformCacherTag>();
                if (tag == null)
                {
                    tag = childObj.AddComponent<TransformCacherTag>();
                    tag.PathID = FixUtility.GeneratePathID(child);
                    tag.ItemID = FixUtility.GenerateItemID(child);
                }
                
                // Mark as destroyed
                tag.IsDestroyed = true;
                
                string uniqueId = FixUtility.GenerateUniqueId(child);
                string objectPath = FixUtility.GetFullPath(child);
                
                // Cache the object path
                if (!_destroyedObjectsCache.ContainsKey(sceneName))
                {
                    _destroyedObjectsCache[sceneName] = new HashSet<string>();
                }
                _destroyedObjectsCache[sceneName].Add(objectPath);
                
                // Update transforms database
                if (!transformsDb.ContainsKey(sceneName))
                {
                    transformsDb[sceneName] = new Dictionary<string, TransformData>();
                }
                
                // Update or add entry to mark as destroyed
                if (transformsDb[sceneName].ContainsKey(uniqueId))
                {
                    transformsDb[sceneName][uniqueId].IsDestroyed = true;
                }
                else
                {
                    // Create a new entry
                    var data = new TransformData
                    {
                        UniqueId = uniqueId,
                        PathID = tag.PathID,
                        ItemID = tag.ItemID,
                        ObjectPath = objectPath,
                        ObjectName = childObj.name,
                        SceneName = sceneName,
                        Position = child.position,
                        Rotation = child.eulerAngles,
                        Scale = child.localScale,
                        ParentPath = objectPath,
                        IsDestroyed = true
                    };
                    
                    transformsDb[sceneName][uniqueId] = data;
                }
                
                // Hide the object
                childObj.SetActive(false);
                
                // Recursively process children
                MarkChildrenAsDestroyed(child, sceneName);
            }
            
            // Update database at the end
            _databaseManager.SetTransformsDatabase(transformsDb);
        }
        
        /// <summary>
        /// Apply destruction to objects and properly free memory
        /// </summary>
        public IEnumerator ApplyDestroyedObjects(string sceneName)
        {
            Logger.LogInfo($"Applying destroyed objects for scene: {sceneName}");
            
            int destroyedObjects = 0;
            int failedObjects = 0;
            
            // Get the transforms database
            var transformsDb = _databaseManager.GetTransformsDatabase();
            
            // First check the transform database for objects marked as destroyed
            int totalMarkedObjects = 0;
            if (transformsDb.ContainsKey(sceneName))
            {
                totalMarkedObjects = transformsDb[sceneName].Values.Count(entry => entry.IsDestroyed);
                Logger.LogInfo($"Found {totalMarkedObjects} objects marked as destroyed in database");
            }
            
            int processedCount = 0;
            
            if (transformsDb.ContainsKey(sceneName))
            {
                EnhancedNotification.SetLoadingMessage($"Deleting {totalMarkedObjects} objects", 40);
                yield return new WaitForSeconds(0.5f);
                
                // Process database entries first
                foreach (var entry in transformsDb[sceneName].Values.ToArray())
                {
                    if (entry.IsDestroyed)
                    {
                        // Try to find the object
                        GameObject obj = FindObjectByPath(entry.ObjectPath);
                        if (obj != null)
                        {
                            try
                            {
                                // First tag it if needed
                                TransformCacherTag tag = obj.GetComponent<TransformCacherTag>();
                                if (tag == null)
                                {
                                    tag = obj.AddComponent<TransformCacherTag>();
                                    tag.PathID = entry.PathID;
                                    tag.ItemID = entry.ItemID;
                                    tag.IsDestroyed = true;
                                }
                                
                                // Then actually destroy it to free memory
                                GameObject.Destroy(obj);
                                destroyedObjects++;
                                
                                // Add to cache for reference only
                                if (!_destroyedObjectsCache.ContainsKey(sceneName))
                                {
                                    _destroyedObjectsCache[sceneName] = new HashSet<string>();
                                }
                                _destroyedObjectsCache[sceneName].Add(entry.ObjectPath);
                            }
                            catch (Exception ex)
                            {
                                Logger.LogError($"Error destroying object {entry.ObjectPath}: {ex.Message}");
                                failedObjects++;
                            }
                        }
                        else
                        {
                            failedObjects++;
                        }
                        
                        processedCount++;
                        
                        // Update progress every few objects for visibility
                        if (processedCount % 5 == 0 && totalMarkedObjects > 0)
                        {
                            float progressPercent = Mathf.Min((float)processedCount / totalMarkedObjects * 100, 100);
                            EnhancedNotification.TrackProgress("Deleting Items", destroyedObjects, failedObjects, progressPercent);
                            yield return null;  // Allow a frame to update UI
                        }
                    }
                }
            }
            
            // Now check if there are any tagged objects we missed
            var taggedObjects = GameObject.FindObjectsOfType<TransformCacherTag>(true)
                                         .Where(tag => tag.IsDestroyed)
                                         .ToArray();
                                         
            if (taggedObjects.Length > 0)
            {
                EnhancedNotification.SetLoadingMessage($"Deleting {taggedObjects.Length} additional tagged objects", 60);
                yield return new WaitForSeconds(0.5f);
                
                int additionalProcessed = 0;
                foreach (var tag in taggedObjects)
                {
                    if (tag != null && tag.gameObject != null)
                    {
                        try
                        {
                            string objectPath = FixUtility.GetFullPath(tag.transform);
                            
                            // Actually destroy the object to free memory
                            GameObject.Destroy(tag.gameObject);
                            destroyedObjects++;
                            
                            // Add to cache for reference
                            if (!_destroyedObjectsCache.ContainsKey(sceneName))
                            {
                                _destroyedObjectsCache[sceneName] = new HashSet<string>();
                            }
                            _destroyedObjectsCache[sceneName].Add(objectPath);
                        }
                        catch (Exception ex)
                        {
                            Logger.LogError($"Error destroying tagged object: {ex.Message}");
                            failedObjects++;
                        }
                        
                        additionalProcessed++;
                        
                        // Update progress every few objects for visibility
                        if (additionalProcessed % 5 == 0)
                        {
                            float progressPercent = Mathf.Min(60f + (float)additionalProcessed / taggedObjects.Length * 40f, 100);
                            EnhancedNotification.TrackProgress("Deleting Tagged Items", destroyedObjects, failedObjects, progressPercent);
                            yield return null;  // Allow a frame to update UI
                        }
                    }
                }
            }
            
            // Final progress update
            EnhancedNotification.TrackProgress("Deleting Items", destroyedObjects, failedObjects, 100);
            yield return new WaitForSeconds(0.5f);
            
            // Clean up database to permanently remove destroyed objects
            CleanupDestroyedObjectsFromDatabase(sceneName);
            
            // Force garbage collection to reclaim memory
            ForceMemoryCleanup();
            
            Logger.LogInfo($"Finished destroying objects: {destroyedObjects} destroyed, {failedObjects} failed");
        }
        
        /// <summary>
        /// Clean up destroyed objects from the database to reduce memory usage
        /// </summary>
        public void CleanupDestroyedObjectsFromDatabase(string sceneName)
        {
            // Get the transforms database
            var transformsDb = _databaseManager.GetTransformsDatabase();
            
            if (transformsDb != null && transformsDb.ContainsKey(sceneName))
            {
                int removedCount = 0;
                var sceneData = new Dictionary<string, TransformData>(transformsDb[sceneName]);
                var entriesToRemove = sceneData.Where(entry => entry.Value.IsDestroyed).Select(entry => entry.Key).ToArray();
                
                foreach (var key in entriesToRemove)
                {
                    sceneData.Remove(key);
                    removedCount++;
                }
                
                // Update the database with cleaned data
                transformsDb[sceneName] = sceneData;
                _databaseManager.SetTransformsDatabase(transformsDb);
                _databaseManager.SaveTransformsDatabase();
                
                Logger.LogInfo($"Removed {removedCount} destroyed objects from database for scene {sceneName}");
            }
        }
        
        /// <summary>
        /// Force memory cleanup to reclaim memory from destroyed objects
        /// </summary>
        public void ForceMemoryCleanup()
        {
            long memBefore = GC.GetTotalMemory(false) / (1024 * 1024);
            
            // Force garbage collection
            System.GC.Collect();
            System.GC.WaitForPendingFinalizers();
            System.GC.Collect();
            
            // For Unity specifically
            Resources.UnloadUnusedAssets();
            
            long memAfter = GC.GetTotalMemory(false) / (1024 * 1024);
            
            Logger.LogInfo($"Memory cleanup completed - Before: {memBefore}MB, After: {memAfter}MB, Freed: {memBefore - memAfter}MB");
        }
        
        /// <summary>
        /// Find object by path
        /// </summary>
        private GameObject FindObjectByPath(string fullPath)
        {
            if (string.IsNullOrEmpty(fullPath))
                return null;
                
            // Try direct GameObject.Find first
            GameObject directObj = GameObject.Find(fullPath);
            if (directObj != null)
                return directObj;
                
            // Split the path into segments
            string[] segments = fullPath.Split('/');
            
            // Try to find the object by traversing the hierarchy
            foreach (var rootObj in SceneManager.GetActiveScene().GetRootGameObjects())
            {
                if (rootObj.name == segments[0])
                {
                    // Found the root object, now traverse children
                    if (segments.Length == 1)
                        return rootObj;
                        
                    Transform currTrans = rootObj.transform;
                    bool pathValid = true;
                    
                    for (int i = 1; i < segments.Length; i++)
                    {
                        Transform nextTrans = null;
                        bool segmentFound = false;
                        
                        foreach (Transform child in currTrans)
                        {
                            if (child.name == segments[i] || 
                                child.name.Equals(segments[i], StringComparison.OrdinalIgnoreCase))
                            {
                                nextTrans = child;
                                segmentFound = true;
                                break;
                            }
                        }
                        
                        if (!segmentFound)
                        {
                            pathValid = false;
                            break;
                        }
                        
                        currTrans = nextTrans;
                    }
                    
                    if (pathValid)
                        return currTrans.gameObject;
                }
            }
            
            return null;
        }
        
        /// <summary>
        /// Check if an object is marked for destruction
        /// </summary>
        public bool IsMarkedForDestruction(GameObject obj)
        {
            if (obj == null) return false;
            
            // Check tag first
            TransformCacherTag tag = obj.GetComponent<TransformCacherTag>();
            if (tag != null && tag.IsDestroyed)
                return true;
                
            // Check cache
            string sceneName = SceneManager.GetActiveScene().name;
            if (_destroyedObjectsCache.ContainsKey(sceneName))
            {
                string path = FixUtility.GetFullPath(obj.transform);
                return _destroyedObjectsCache[sceneName].Contains(path);
            }
            
            return false;
        }
        
        /// <summary>
        /// Get the destroyed object cache
        /// </summary>
        public Dictionary<string, HashSet<string>> GetDestroyedObjectsCache()
        {
            return _destroyedObjectsCache;
        }
        
        /// <summary>
        /// Check the destroyed objects cache for any missed objects and clean them up
        /// </summary>
        public IEnumerator CheckDestroyedObjectsCache(string sceneName)
        {
            int destroyedObjects = 0;
            
            if (_destroyedObjectsCache.ContainsKey(sceneName))
            {
                foreach (string objectPath in _destroyedObjectsCache[sceneName])
                {
                    // Try to find the object by path
                    GameObject obj = FindObjectByPath(objectPath);
                    if (obj != null)
                    {
                        // Actually destroy it
                        GameObject.Destroy(obj);
                        destroyedObjects++;
                    }
                    
                    // Yield every 10 objects to prevent freezing
                    if (destroyedObjects % 10 == 0)
                    {
                        yield return null;
                    }
                }
                
                Logger.LogInfo($"Destroyed {destroyedObjects} cached objects for {sceneName}");
            }
            
            // Force a final GC to clean up memory
            ForceMemoryCleanup();
            
            yield break;
        }
        
        /// <summary>
        /// Save destroyed objects to asset files
        /// </summary>
        public void SaveDestroyedObjectsToAssets(string sceneName = null)
        {
            if (string.IsNullOrEmpty(sceneName))
            {
                sceneName = SceneManager.GetActiveScene().name;
            }
            
            try
            {
                // Get transforms database
                var transformsDb = _databaseManager.GetTransformsDatabase();
                
                if (!transformsDb.ContainsKey(sceneName))
                {
                    Logger.LogInfo($"No transforms data for scene {sceneName}");
                    return;
                }
                
                // Get all destroyed objects for the scene
                var destroyedObjects = transformsDb[sceneName].Values
                    .Where(data => data.IsDestroyed)
                    .ToList();
                    
                if (destroyedObjects.Count == 0)
                {
                    Logger.LogInfo($"No destroyed objects found for scene {sceneName}");
                    return;
                }
                
                // Get the asset manager and apply changes directly to asset files
                var assetManager = AssetManager.Instance;
                if (assetManager != null)
                {
                    // First copy the scene to mod directory if needed
                    assetManager.CopySceneIfNeeded(sceneName);
                    
                    // Apply the destroyed objects changes
                    assetManager.RemoveDestroyedObjectsFromAsset(sceneName, destroyedObjects);
                    
                    Logger.LogInfo($"Successfully saved {destroyedObjects.Count} destroyed objects to asset files");
                    
                    // Register with asset redirector
                    AssetRedirector.Instance.RegisterModifiedScenes(new List<string> { sceneName });
                }
            }
            catch (Exception ex)
            {
                Logger.LogError($"Error saving destroyed objects to assets: {ex.Message}");
            }
        }
    }
}