using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TransformCacher
{
    /// <summary>
    /// Class responsible for "baking" unique IDs for objects in scenes to ensure
    /// consistent IDs across different users and game sessions.
    /// </summary>
    public class TransformIdBaker : MonoBehaviour
    {
        // Baking status
        private bool _isBaking = false;
        private string _bakingStatus = "";
        private float _bakingProgress = 0f;
        
        // Scene statistics - now properly used
        private int _totalObjectsInScene = 0;
        private int _objectsBaked = 0;
        private int _objectsIgnored = 0;
        private int _processedCount = 0; // Added class-level counter to avoid ref parameter
        private Dictionary<string, int> _objectCountByType = new Dictionary<string, int>();
        
        // Progress properties for GUI access
        public bool IsBaking() => _isBaking;
        public string GetBakingStatus() => _bakingStatus;
        public float GetBakingProgress() => _bakingProgress;
        
        // Statistics properties for the GUI
        public int GetTotalObjectCount() => _totalObjectsInScene;
        public int GetBakedObjectCount() => _objectsBaked;
        public int GetIgnoredObjectCount() => _objectsIgnored;
        public Dictionary<string, int> GetObjectTypeDistribution() => _objectCountByType;
        
        private static TransformIdBaker _instance;
        public static TransformIdBaker Instance => _instance;

        // Reference to database manager
        private DatabaseManager _databaseManager;
        
        // Logging
        private static BepInEx.Logging.ManualLogSource Logger;

        public void Initialize()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(this);
                return;
            }
            
            _instance = this;
            
            // Get logger
            Logger = BepInEx.Logging.Logger.CreateLogSource("TransformIdBaker");
            
            // Get reference to database manager
            _databaseManager = DatabaseManager.Instance;
            if (_databaseManager == null)
            {
                Logger.LogError("Failed to get DatabaseManager instance");
            }
            
            // Initialize statistics
            ResetStatistics();
            
            Logger.LogInfo("TransformIdBaker initialized successfully");
        }
        
        // Reset statistics counters
        private void ResetStatistics()
        {
            _totalObjectsInScene = 0;
            _objectsBaked = 0;
            _objectsIgnored = 0;
            _processedCount = 0; // Reset process counter
            _objectCountByType.Clear();
        }
        
        /// <summary>
        /// Checks if the current scene has been baked already
        /// </summary>
        public bool IsSceneBaked(Scene scene)
        {
            string sceneName = scene.name;
            return _databaseManager.IsSceneBaked(sceneName);
        }
        
        /// <summary>
        /// Try to get a baked ID for an object
        /// </summary>
        public bool TryGetBakedId(Transform transform, out BakedIdData bakedData)
        {
            bakedData = null;
            
            if (transform == null || _databaseManager == null)
                return false;
                
            string sceneName = transform.gameObject.scene.name;
            string objectPath = FixUtility.GetFullPath(transform);
            string itemId = FixUtility.GenerateItemID(transform);
            
            // First try to get by path
            if (_databaseManager.TryGetBakedIdByPath(sceneName, objectPath, out bakedData))
            {
                return true;
            }
            
            // Then try by itemId
            if (_databaseManager.TryGetBakedDataById(sceneName, itemId, out bakedData))
            {
                return true;
            }
            
            return false;
        }
        
        /// <summary>
        /// Start the baking process for the current scene
        /// </summary>
        public void StartBaking()
        {
            if (_isBaking)
            {
                Logger.LogWarning("Baking already in progress");
                return;
            }
            
            Scene currentScene = SceneManager.GetActiveScene();
            Logger.LogInfo($"Starting ID baking for scene: {currentScene.name}");
            
            ResetStatistics();
            _isBaking = true;
            _bakingStatus = $"Starting ID baking for {currentScene.name}...";
            _bakingProgress = 0f;
            
            StartCoroutine(BakeScene(currentScene));
        }
        
        /// <summary>
        /// Bake unique IDs for objects in a scene
        /// </summary>
        private IEnumerator BakeScene(Scene scene)
        {
            string sceneName = scene.name;
            _bakingStatus = $"Analyzing scene: {sceneName}";
            
            // Step 1: Count all objects in the scene
            GameObject[] rootObjects = scene.GetRootGameObjects();
            _totalObjectsInScene = CountObjectsInScene(rootObjects);
            
            Logger.LogInfo($"Found {_totalObjectsInScene} objects in scene {sceneName}");
            _bakingStatus = $"Found {_totalObjectsInScene} objects in {sceneName}";
            _bakingProgress = 0.1f;
            
            yield return null;
            
            // Step 2: Analyze scene hierarchy
            Dictionary<string, BakedIdData> bakedIds = new Dictionary<string, BakedIdData>();
            _processedCount = 0; // Reset before processing
            
            foreach (var rootObj in rootObjects)
            {
                yield return StartCoroutine(ProcessGameObjectHierarchy(rootObj, sceneName, bakedIds));
            }
            
            // Step 3: Save baked IDs to database
            _bakingStatus = $"Saving {bakedIds.Count} baked IDs for scene {sceneName}";
            _bakingProgress = 0.9f;
            
            // Create scene entry if it doesn't exist
            var bakedIdsDb = _databaseManager.GetBakedIdsDatabase();
            if (!bakedIdsDb.ContainsKey(sceneName))
            {
                bakedIdsDb[sceneName] = new Dictionary<string, BakedIdData>();
            }
            
            // Add or update baked IDs
            foreach (var entry in bakedIds)
            {
                bakedIdsDb[sceneName][entry.Key] = entry.Value;
            }
            
            // Save database
            _databaseManager.SetBakedIdsDatabase(bakedIdsDb);
            _databaseManager.SaveBakedIdsDatabase();
            
            // Finalize
            _bakingStatus = $"Completed baking {_objectsBaked} objects in {sceneName} ({_objectsIgnored} objects ignored)";
            _bakingProgress = 1.0f;
            
            // Log statistics
            Logger.LogInfo($"Baking completed. Total: {_totalObjectsInScene}, Baked: {_objectsBaked}, Ignored: {_objectsIgnored}");
            
            // Log object type distribution
            if (_objectCountByType.Count > 0)
            {
                Logger.LogInfo("Object type distribution:");
                foreach (var type in _objectCountByType.OrderByDescending(t => t.Value))
                {
                    Logger.LogInfo($"- {type.Key}: {type.Value}");
                }
            }
            
            _isBaking = false;
            
            yield break;
        }
        
        /// <summary>
        /// Count all objects in the scene hierarchy
        /// </summary>
        private int CountObjectsInScene(GameObject[] rootObjects)
        {
            int count = 0;
            _objectCountByType.Clear();
            
            foreach (var root in rootObjects)
            {
                CountObjectsRecursive(root, ref count);
            }
            
            return count;
        }
        
        /// <summary>
        /// Count objects recursively and track by component types
        /// </summary>
        private void CountObjectsRecursive(GameObject obj, ref int count)
        {
            count++;
            
            // Track object types based on components
            var components = obj.GetComponents<Component>();
            foreach (var component in components)
            {
                if (component == null) continue;
                
                string typeName = component.GetType().Name;
                
                if (!_objectCountByType.ContainsKey(typeName))
                {
                    _objectCountByType[typeName] = 0;
                }
                
                _objectCountByType[typeName]++;
            }
            
            // Process children
            foreach (Transform child in obj.transform)
            {
                CountObjectsRecursive(child.gameObject, ref count);
            }
        }
        
        /// <summary>
        /// Process a GameObject and its children for baking
        /// </summary>
        private IEnumerator ProcessGameObjectHierarchy(GameObject obj, string sceneName, 
            Dictionary<string, BakedIdData> bakedIds)
        {
            // Process this object
            bool shouldBake = ShouldBakeObject(obj);
            
            if (shouldBake)
            {
                string uniqueId = FixUtility.GenerateUniqueId(obj.transform);
                string pathId = FixUtility.GeneratePathID(obj.transform);
                string itemId = FixUtility.GenerateItemID(obj.transform);
                string objectPath = FixUtility.GetFullPath(obj.transform);
                
                BakedIdData bakedData = new BakedIdData
                {
                    UniqueId = uniqueId,
                    PathID = pathId,
                    ItemID = itemId,
                    ItemPath = objectPath,
                    SceneName = sceneName,
                    Position = obj.transform.position,
                    Rotation = obj.transform.eulerAngles,
                    Scale = obj.transform.localScale,
                    ParentPath = obj.transform.parent != null ? FixUtility.GetFullPath(obj.transform.parent) : "",
                    IsDestroyed = false,
                    IsSpawned = false,
                    PrefabPath = "",
                    Children = GetChildPaths(obj.transform)
                };
                
                bakedIds[uniqueId] = bakedData;
                _objectsBaked++;
            }
            else
            {
                _objectsIgnored++;
            }
            
            // Update progress
            _processedCount++;
            _bakingProgress = 0.1f + (0.8f * _processedCount / _totalObjectsInScene);
            _bakingStatus = $"Processing {_processedCount}/{_totalObjectsInScene} objects in {sceneName}";
            
            // Process children - yield every 50 objects to avoid freezing
            int childrenProcessed = 0;
            foreach (Transform child in obj.transform)
            {
                yield return StartCoroutine(ProcessGameObjectHierarchy(child.gameObject, sceneName, bakedIds));
                
                childrenProcessed++;
                if (childrenProcessed % 50 == 0)
                {
                    yield return null;
                }
            }
        }
        
        /// <summary>
        /// Get a list of child paths for a transform
        /// </summary>
        private List<string> GetChildPaths(Transform parent)
        {
            List<string> childPaths = new List<string>();
            
            foreach (Transform child in parent)
            {
                childPaths.Add(FixUtility.GetFullPath(child));
            }
            
            return childPaths;
        }
        
        /// <summary>
        /// Determine if an object should be baked
        /// </summary>
        private bool ShouldBakeObject(GameObject obj)
        {
            // Skip inactive objects
            if (!obj.activeInHierarchy) 
                return false;
                
            // Skip temporary objects
            if (obj.name.StartsWith("TEMP_") || obj.name.StartsWith("tmp_") || 
                obj.name == "InvisibleHighlighter") // Added this check
                return false;
                
            // Skip UI elements
            if (obj.name.Contains("Canvas") || obj.name.Contains("UI_"))
                return false;
                
            // Skip objects with certain components
            if (obj.GetComponent<Camera>() != null ||
                obj.GetComponent<Light>() != null ||
                obj.GetComponentInChildren<Component>()?.GetType().Name == "ParticleSystem")
                return false;
                
            // Only bake objects with renderers
            bool hasRenderer = obj.GetComponent<Renderer>() != null;
            bool hasCollider = obj.GetComponent<Collider>() != null;
            
            return hasRenderer || hasCollider;
        }
    }
}