using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using BepInEx;
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
        private static TransformIdBaker _instance;
        public static TransformIdBaker Instance => _instance;

        // Reference to database manager
        private DatabaseManager _databaseManager;
        
        // UI state for the baker window
        private Rect _bakerWindowRect = new Rect(400, 200, 500, 500); // Increased height for additional options
        private Vector2 _bakerScrollPosition;
        private Vector2 _sceneScrollPosition;
        private bool _showBakerWindow = false;
        
        // Baking status
        private bool _isBaking = false;
        private string _bakingStatus = "";
        private float _bakingProgress = 0f;
        
        // Scene statistics
        private int _totalObjectsInScene = 0;
        private int _objectsBaked = 0;
        private int _objectsIgnored = 0;
        private Dictionary<string, int> _objectCountByType = new Dictionary<string, int>();
        
        // New features
        private string _ignorePrefix = "Weapon spawn";
        private List<string> _scenesToBake = new List<string>();
        private int _currentSceneIndex = 0;
        private bool _bakeMultipleScenes = false;
        private bool _showSettings = false;
        
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
            
            // Get logger from TransformCacher
            Logger = BepInEx.Logging.Logger.CreateLogSource("TransformIdBaker");
            
            // Get reference to database manager
            _databaseManager = DatabaseManager.Instance;
            if (_databaseManager == null)
            {
                Logger.LogError("Failed to get DatabaseManager instance");
            }
            
            Logger.LogInfo("TransformIdBaker initialized successfully");
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
            string objectPath = TransformCacher.GetFullPath(transform);
            string itemId = TransformCacher.GenerateItemID(transform);
            
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
        /// Start the baking process for the current scene or multiple scenes
        /// </summary>
        public void StartBaking()
        {
            if (_isBaking)
            {
                Logger.LogWarning("Already baking, please wait for the current process to finish");
                return;
            }
            
            _isBaking = true;
            _bakingStatus = "Initializing...";
            _bakingProgress = 0f;
            _objectsIgnored = 0;
            
            if (_bakeMultipleScenes && _scenesToBake.Count > 0)
            {
                _currentSceneIndex = 0;
                StartCoroutine(BakeMultipleScenes());
            }
            else
            {
                Scene currentScene = SceneManager.GetActiveScene();
                
                if (IsSceneBaked(currentScene))
                {
                    if (!EditorUtility.DisplayDialog("Scene Already Baked", 
                        $"Scene '{currentScene.name}' already has baked IDs. Do you want to rebake it?", 
                        "Yes", "No"))
                    {
                        _isBaking = false;
                        _bakingStatus = "Baking cancelled - scene already baked";
                        return;
                    }
                }
                
                StartCoroutine(BakeScene(currentScene));
            }
        }
        
        /// <summary>
        /// Bake multiple scenes in sequence
        /// </summary>
        private IEnumerator BakeMultipleScenes()
        {
            _bakingStatus = $"Preparing to bake {_scenesToBake.Count} scenes...";
            yield return null;
            
            for (_currentSceneIndex = 0; _currentSceneIndex < _scenesToBake.Count; _currentSceneIndex++)
            {
                string sceneName = _scenesToBake[_currentSceneIndex];
                _bakingStatus = $"Loading scene {_currentSceneIndex + 1}/{_scenesToBake.Count}: {sceneName}";
                yield return null;
                
                // Try to find an already loaded scene with this name
                Scene targetScene = default;
                bool sceneFound = false;
                
                for (int i = 0; i < SceneManager.sceneCount; i++)
                {
                    Scene scene = SceneManager.GetSceneAt(i);
                    if (scene.name == sceneName)
                    {
                        targetScene = scene;
                        sceneFound = true;
                        break;
                    }
                }
                
                if (!sceneFound)
                {
                    Logger.LogWarning($"Scene '{sceneName}' not found or not loaded. Skipping.");
                    continue;
                }
                
                // Bake the scene
                yield return StartCoroutine(BakeScene(targetScene));
            }
            
            _bakingStatus = $"Completed baking {_scenesToBake.Count} scenes.";
            _isBaking = false;
        }
        
        /// <summary>
        /// Bake all objects in the specified scene
        /// </summary>
        private IEnumerator BakeScene(Scene scene)
        {
            string sceneName = scene.name;
            
            _bakingStatus = $"Collecting objects in {sceneName}...";
            yield return null;
            
            // Get all objects in the scene
            List<GameObject> allObjects = CollectAllGameObjectsInScene(scene);
            _totalObjectsInScene = allObjects.Count;
            _objectsBaked = 0;
            _objectsIgnored = 0;
            _objectCountByType.Clear();
            
            Logger.LogInfo($"Starting to bake {_totalObjectsInScene} objects in scene '{sceneName}'");
            
            // Get baked IDs database from database manager
            var bakedIdsDb = _databaseManager.GetBakedIdsDatabase();
            
            // Create or clear the scene entry in the database
            if (!bakedIdsDb.ContainsKey(sceneName))
            {
                bakedIdsDb[sceneName] = new Dictionary<string, BakedIdData>();
            }
            else
            {
                bakedIdsDb[sceneName].Clear();
            }
            
            _bakingStatus = $"Baking objects in {sceneName}...";
            yield return null;
            
            // Process each object
            for (int i = 0; i < allObjects.Count; i++)
            {
                GameObject obj = allObjects[i];
                if (obj == null) continue;
                
                try
                {
                    // Check if this object should be ignored based on the prefix
                    if (obj.name.StartsWith(_ignorePrefix))
                    {
                        _objectsIgnored++;
                        continue;
                    }
                    
                    // Generate IDs
                    string pathId = TransformCacher.GeneratePathID(obj.transform);
                    string itemId = TransformCacher.GenerateItemID(obj.transform);
                    string uniqueId = pathId + "+" + itemId;
                    
                    // Get object path
                    string objectPath = TransformCacher.GetFullPath(obj.transform);
                    
                    // Create the baked ID data
                    var bakedData = new BakedIdData
                    {
                        PathID = pathId,
                        ItemID = itemId,
                        UniqueId = uniqueId,
                        ItemPath = objectPath,
                        SceneName = sceneName,
                        Position = obj.transform.position,
                        Rotation = obj.transform.eulerAngles,
                        Scale = obj.transform.localScale,
                        ParentPath = obj.transform.parent != null ? TransformCacher.GetFullPath(obj.transform.parent) : "",
                        Children = GetChildrenIds(obj.transform),
                        IsDestroyed = false,
                        IsSpawned = false,
                        PrefabPath = ""
                    };
                    
                    // Store in database using ItemID as the key
                    bakedIdsDb[sceneName][itemId] = bakedData;
                    
                    // Update object type statistics
                    string objectType = GetObjectType(obj);
                    if (!_objectCountByType.ContainsKey(objectType))
                    {
                        _objectCountByType[objectType] = 0;
                    }
                    _objectCountByType[objectType]++;
                    
                    _objectsBaked++;
                    _bakingProgress = (float)_objectsBaked / (_totalObjectsInScene - _objectsIgnored);
                }
                catch (Exception ex)
                {
                    Logger.LogWarning($"Error baking object {obj.name}: {ex.Message}");
                }
                
                // Yield periodically to avoid freezing
                if (i % 100 == 0)
                {
                    _bakingStatus = $"Baking {sceneName}... ({_objectsBaked}/{_totalObjectsInScene - _objectsIgnored})";
                    yield return null;
                }
            }
            
            // Update the database in the manager
            _databaseManager.SetBakedIdsDatabase(bakedIdsDb);
            
            // Save the database
            _bakingStatus = "Saving database...";
            yield return null;
            
            _databaseManager.SaveBakedIdsDatabase();
            
            _bakingStatus = $"Baking completed for {sceneName}! {_objectsBaked} objects baked, {_objectsIgnored} objects ignored.";
            Logger.LogInfo($"Finished baking {_objectsBaked} objects in scene '{sceneName}'. Ignored {_objectsIgnored} objects with prefix '{_ignorePrefix}'.");
            
            if (!_bakeMultipleScenes)
            {
                _isBaking = false;
            }
            
            yield return new WaitForSeconds(1.0f); // Give a moment to see the completion message
        }
        
        /// <summary>
        /// Get a list of children itemIDs
        /// </summary>
        private List<string> GetChildrenIds(Transform parent)
        {
            List<string> childrenIds = new List<string>();
            
            if (parent == null) return childrenIds;
            
            foreach (Transform child in parent)
            {
                string childId = TransformCacher.GenerateItemID(child);
                childrenIds.Add(childId);
            }
            
            return childrenIds;
        }
        
        /// <summary>
        /// Collect all GameObjects in a scene including inactive ones
        /// </summary>
        private List<GameObject> CollectAllGameObjectsInScene(Scene scene)
        {
            List<GameObject> results = new List<GameObject>();
            
            // Get root objects first
            GameObject[] rootObjects = scene.GetRootGameObjects();
            
            foreach (GameObject root in rootObjects)
            {
                results.Add(root);
                CollectChildrenRecursively(root.transform, results);
            }
            
            return results;
        }
        
        /// <summary>
        /// Helper to collect all children recursively
        /// </summary>
        private void CollectChildrenRecursively(Transform parent, List<GameObject> results)
        {
            foreach (Transform child in parent)
            {
                results.Add(child.gameObject);
                CollectChildrenRecursively(child, results);
            }
        }
        
        /// <summary>
        /// Get a category/type for an object based on its name, components, or path
        /// </summary>
        private string GetObjectType(GameObject obj)
        {
            if (obj == null) return "Unknown";
            
            // Check for common component types first
            if (obj.GetComponent<Light>() != null) return "Light";
            if (obj.GetComponent<Camera>() != null) return "Camera";
            if (obj.GetComponent<Collider>() != null) return "Collider";
            if (obj.GetComponent<MeshRenderer>() != null) return "Renderer";
            
            // Check name patterns
            string objName = obj.name.ToLower();
            
            if (objName.Contains("light")) return "Light";
            if (objName.Contains("camera")) return "Camera";
            if (objName.Contains("door")) return "Door";
            if (objName.Contains("prop")) return "Prop";
            if (objName.Contains("weapon")) return "Weapon";
            if (objName.Contains("wall") || objName.Contains("floor") || objName.Contains("ceiling")) return "Structure";
            
            // Default
            return "Other";
        }
        
        /// <summary>
        /// Draw the baker UI
        /// </summary>
        public void OnGUI()
        {
            if (_showBakerWindow)
            {
                _bakerWindowRect = GUI.Window(100, _bakerWindowRect, DrawBakerWindow, "Transform ID Baker");
            }
        }
        
        /// <summary>
        /// Draw the baker window contents
        /// </summary>
        private void DrawBakerWindow(int id)
        {
            GUILayout.BeginVertical();
            
            // Scene information
            Scene currentScene = SceneManager.GetActiveScene();
            GUILayout.Label($"Current Scene: {currentScene.name}");
            
            bool isSceneBaked = IsSceneBaked(currentScene);
            string bakeStatus = isSceneBaked 
                ? "<color=green>Baked</color>"
                : "<color=yellow>Not Baked</color>";
                
            // Get the number of baked objects if available
            if (isSceneBaked)
            {
                var sceneBakedIds = _databaseManager.GetBakedIdsDatabase();
                int count = sceneBakedIds[currentScene.name].Count;
                bakeStatus += $" ({count} objects)";
            }
            
            GUILayout.Label($"Bake Status: {bakeStatus}", new GUIStyle(GUI.skin.label) { richText = true });
            
            GUILayout.Space(10);
            
            // Settings section
            _showSettings = GUILayout.Toggle(_showSettings, "Show Settings");
            
            if (_showSettings)
            {
                GUILayout.BeginVertical(GUI.skin.box);
                
                // Ignore prefix setting
                GUILayout.BeginHorizontal();
                GUILayout.Label("Ignore Prefix:", GUILayout.Width(100));
                _ignorePrefix = GUILayout.TextField(_ignorePrefix, GUILayout.Width(150));
                GUILayout.EndHorizontal();
                
                GUILayout.Label("Objects with names starting with this prefix will be ignored during baking.", 
                    EditorStyles.miniLabel);
                
                // Multiple scenes toggle
                _bakeMultipleScenes = GUILayout.Toggle(_bakeMultipleScenes, "Bake Multiple Scenes");
                
                if (_bakeMultipleScenes)
                {
                    GUILayout.Space(5);
                    GUILayout.Label("Scenes to Bake:");
                    
                    _sceneScrollPosition = GUILayout.BeginScrollView(_sceneScrollPosition, GUILayout.Height(100));
                    
                    // List all loaded scenes
                    for (int i = 0; i < SceneManager.sceneCount; i++)
                    {
                        Scene scene = SceneManager.GetSceneAt(i);
                        bool isSelected = _scenesToBake.Contains(scene.name);
                        
                        bool newIsSelected = GUILayout.Toggle(isSelected, scene.name);
                        
                        if (newIsSelected != isSelected)
                        {
                            if (newIsSelected)
                            {
                                _scenesToBake.Add(scene.name);
                            }
                            else
                            {
                                _scenesToBake.Remove(scene.name);
                            }
                        }
                    }
                    
                    GUILayout.EndScrollView();
                    
                    // Add unloaded scene option
                    GUILayout.BeginHorizontal();
                    GUILayout.Label("Add Scene:", GUILayout.Width(70));
                    string newScene = GUILayout.TextField("", GUILayout.Width(120));
                    
                    if (GUILayout.Button("Add", GUILayout.Width(50)) && !string.IsNullOrEmpty(newScene))
                    {
                        if (!_scenesToBake.Contains(newScene))
                        {
                            _scenesToBake.Add(newScene);
                        }
                    }
                    
                    GUILayout.EndHorizontal();
                    
                    if (_scenesToBake.Count == 0)
                    {
                        GUILayout.Label("<color=yellow>No scenes selected</color>", 
                            new GUIStyle(GUI.skin.label) { richText = true });
                    }
                }
                
                GUILayout.EndVertical();
            }
            
            GUILayout.Space(10);
            
            // Baking controls
            GUI.enabled = !_isBaking;
            if (GUILayout.Button("Bake Scene(s)", GUILayout.Height(40)))
            {
                StartBaking();
            }
            GUI.enabled = true;
            
            // Baking progress
            if (_isBaking)
            {
                GUILayout.Space(10);
                GUILayout.Label(_bakingStatus);
                
                Rect progressRect = GUILayoutUtility.GetRect(100, 20);
                EditorGUI.ProgressBar(progressRect, _bakingProgress, $"{Mathf.RoundToInt(_bakingProgress * 100)}%");
                
                if (_objectsIgnored > 0)
                {
                    GUILayout.Label($"Ignored {_objectsIgnored} objects starting with \"{_ignorePrefix}\"");
                }
            }
            
            GUILayout.Space(10);
            
            // Database statistics
            GUILayout.Label("Database Statistics", GUI.skin.box);
            
            _bakerScrollPosition = GUILayout.BeginScrollView(_bakerScrollPosition);
            
            // Get baked IDs database from database manager
            var currentBakedIdsDb = _databaseManager.GetBakedIdsDatabase();
            
            // Show scenes with baked IDs
            foreach (var scene in currentBakedIdsDb.Keys)
            {
                int count = currentBakedIdsDb[scene].Count;
                GUILayout.Label($"Scene '{scene}': {count} objects");
            }
            
            // Show object type breakdown for current scene if baked
            if (_isBaking || (isSceneBaked && _objectCountByType.Count > 0))
            {
                GUILayout.Space(10);
                GUILayout.Label("Object Types:");
                
                foreach (var typeCount in _objectCountByType.OrderByDescending(kv => kv.Value))
                {
                    GUILayout.Label($"- {typeCount.Key}: {typeCount.Value}");
                }
            }
            
            GUILayout.EndScrollView();
            
            GUILayout.Space(10);
            
            // Close button
            if (GUILayout.Button("Close"))
            {
                _showBakerWindow = false;
            }
            
            GUILayout.EndVertical();
            
            // Allow the window to be dragged
            GUI.DragWindow();
        }
        
        /// <summary>
        /// Toggle visibility of the baker window
        /// </summary>
        public void ToggleBakerWindow()
        {
            _showBakerWindow = !_showBakerWindow;
        }
        
        // Unity Editor compatibility shim - only needed for the baker UI
        private static class EditorUtility
        {
            public static bool DisplayDialog(string title, string message, string ok, string cancel)
            {
                // In a real game build, we would show a custom dialog
                // For now, just log and return true
                Logger.LogInfo($"[Dialog] {title}: {message}");
                return true;
            }
        }
        
        public static class EditorGUI
        {
            public static void ProgressBar(Rect rect, float value, string text)
            {
                // Draw a simple progress bar
                GUI.Box(rect, "");
                
                Rect fillRect = new Rect(rect.x, rect.y, rect.width * value, rect.height);
                GUI.Box(fillRect, "");
                
                // Center the text
                GUIStyle centeredStyle = new GUIStyle(GUI.skin.label);
                centeredStyle.alignment = (TextAnchor)4;
                
                GUI.Label(rect, text, centeredStyle);
            }
        }
        
        // Additional helper for Editor styles
        private static class EditorStyles
        {
            public static GUIStyle boldLabel
            {
                get
                {
                    GUIStyle style = new GUIStyle(GUI.skin.label);
                    style.fontStyle = FontStyle.Bold;
                    return style;
                }
            }
            
            public static GUIStyle miniLabel
            {
                get
                {
                    GUIStyle style = new GUIStyle(GUI.skin.label);
                    style.fontSize = style.fontSize - 2;
                    return style;
                }
            }
        }
    }
}