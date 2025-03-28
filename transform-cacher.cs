using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TransformCacher
{
    public class TransformCacherTag : MonoBehaviour
    {
        public string UniqueId { get; private set; }
        public string PathID { get; set; }
        public string ItemID { get; set; }

        public bool IsSpawned { get; set; } = false;
        public bool IsDestroyed { get; set; } = false;
        
        public void Awake()
        {
            // Generate a unique ID based on hierarchical path and other properties
            UniqueId = TransformCacher.GenerateUniqueId(transform);
            
            // Generate PathID and ItemID if they don't already exist
            if (string.IsNullOrEmpty(PathID))
            {
                PathID = TransformCacher.GeneratePathID(transform);
            }
            
            if (string.IsNullOrEmpty(ItemID))
            {
                ItemID = TransformCacher.GenerateItemID(transform);
            }
            
            Debug.Log($"[TransformCacher] Tag active on: {gameObject.name} with ID: {UniqueId}, PathID: {PathID}, ItemID: {ItemID}");
        }
    }

    public class TransformCacher : MonoBehaviour
    {
        private static TransformCacher _instance;
        public static TransformCacher Instance => _instance;

        // Add this field at the class level
        private TransformIdBaker _idBaker;
        
        // Database manager reference
        private DatabaseManager _databaseManager;
        
        // Destroyed objects tracking
        private Dictionary<string, HashSet<string>> _destroyedObjectsCache = new Dictionary<string, HashSet<string>>();
        
        // Currently inspected GameObject
        private GameObject _currentInspectedObject;
        private SelectionHighlighter _currentHighlighter;
        
        // Flag to prevent infinite update loops
        private bool _isApplyingTransform = false;
        
        // Last known transform values for the inspected object
        private Vector3 _lastPosition;
        private Vector3 _lastLocalPosition;
        private Quaternion _lastRotation;
        private Vector3 _lastScale;
        
        // For reflection access to Unity Explorer
        private Type _inspectorManagerType;
        private PropertyInfo _activeInspectorProperty;
        private PropertyInfo _targetProperty;
        
        // Time tracking
        private float _lastCheckTime;
        private const float CHECK_INTERVAL = 0.5f; // Check every half second
        
        // Scene loading tracking
        private string _currentScene = string.Empty;
        private int _transformApplicationAttempts = 0;
        private const float RETRY_DELAY = 2.0f;
        
        // Prefab selection
        private List<GameObject> _availablePrefabs = new List<GameObject>();
        private string _prefabSearchText = "";
        private Vector2 _prefabScrollPosition;
        private bool _showPrefabSelector = false;
        private bool _prefabsLoaded = false;
        private GameObject _selectedPrefab = null;

        // For categorized prefabs
        private Dictionary<string, List<GameObject>> _prefabCategories = new Dictionary<string, List<GameObject>>();
        private List<string> _categoryNames = new List<string>();
        private string _selectedCategory = "All";
        private bool _showCategoryDropdown = false;
        private Vector2 _categoryScrollPosition;
        
        // UI State
        private Rect _windowRect = new Rect(400, 100, 600, 500);
        private Vector2 _mainWindowScrollPosition;

        // New UI resizing and focus features
        private bool _uiHasFocus = false;
        private Vector3 _savedMousePosition;
        private Rect _resizeHandle = new Rect(0, 0, 10, 10);
        private bool _isResizing = false;
        private Vector2 _minWindowSize = new Vector2(400, 300);
        private Vector2 _startResizeSize;
        private Vector2 _startResizeMousePos;

        // Configuration options
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
        
        // Logging
        private static BepInEx.Logging.ManualLogSource Logger;

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
                        
            _instance = this;
            DontDestroyOnLoad(gameObject);
            
            // Get logger from plugin
            Logger = BepInEx.Logging.Logger.CreateLogSource("TransformCacher");

            // Add MouseToggleHotkey if it doesn't exist
            if (MouseToggleHotkey == null)
            {
                var bepinPlugin = GetComponentInParent<TransformCacherPlugin>();
                if (bepinPlugin != null)
                {
                    MouseToggleHotkey = bepinPlugin.Config.Bind("Hotkeys", "MouseToggle", 
                        new KeyboardShortcut(KeyCode.Tab, KeyCode.LeftAlt),
                        "Hotkey to toggle between mouse UI control and game control");
                }
            }

            // Initialize database manager
            _databaseManager = DatabaseManager.Instance;
            _databaseManager.Initialize();

            // Initialize ID Baker
            _idBaker = gameObject.AddComponent<TransformIdBaker>();
            _idBaker.Initialize();
            
            Logger.LogInfo("DatabaseManager initialized");
            
            // Try to locate Unity Explorer types through reflection
            SetupUnityExplorerReflection();
            
            // Subscribe to scene load events
            SceneManager.sceneLoaded += OnSceneLoaded;
            
            // Start a periodic check for scene changes
            StartCoroutine(CheckForSceneChanges());
            
            // Start loading prefabs
            StartCoroutine(LoadPrefabs());
            
            Logger.LogInfo("TransformCacher initialized successfully");
        }

        private IEnumerator LoadPrefabs()
        {
            yield return new WaitForSeconds(5f); // Wait for game to fully load
            
            Logger.LogInfo("Starting to load prefabs...");
            
            // Clear existing data
            _availablePrefabs.Clear();
            _prefabCategories.Clear();
            _categoryNames.Clear();
            
            // Initialize categories
            InitializeCategories();
            
            int maxPrefabsToLoad = 5000; // Limit to prevent performance issues
            int prefabsProcessed = 0;
            int prefabsAdded = 0;
            
            // First try to get prefabs from Resources
            GameObject[] resourceObjects = null;
            try {
                resourceObjects = Resources.FindObjectsOfTypeAll<GameObject>();
                Logger.LogInfo($"Found {resourceObjects.Length} total GameObjects to process");
            }
            catch (Exception ex) {
                Logger.LogError($"Error finding GameObjects: {ex.Message}");
                resourceObjects = new GameObject[0];
            }
            
            // Process all objects from Resources
            if (resourceObjects != null && resourceObjects.Length > 0)
            {
                foreach (var obj in resourceObjects)
                {
                    prefabsProcessed++;
                    
                    bool shouldAdd = false;
                    try {
                        shouldAdd = ShouldAddAsPrefab(obj);
                    }
                    catch {
                        shouldAdd = false;
                    }
                    
                    if (shouldAdd)
                    {
                        try {
                            AddPrefabToCollection(obj);
                            prefabsAdded++;
                        }
                        catch (Exception ex) {
                            Logger.LogWarning($"Error adding prefab {obj.name}: {ex.Message}");
                        }
                        
                        if (prefabsAdded % 50 == 0)
                        {
                            Logger.LogInfo($"Added {prefabsAdded} prefabs so far ({prefabsProcessed} processed)");
                            yield return null; // Don't freeze the game
                        }
                        
                        if (prefabsAdded >= maxPrefabsToLoad)
                        {
                            Logger.LogInfo($"Reached maximum prefab count ({maxPrefabsToLoad}), stopping search");
                            break;
                        }
                    }
                    
                    // Yield occasionally to avoid freezing
                    if (prefabsProcessed % 500 == 0)
                    {
                        yield return null;
                    }
                }
            }
            
            // Second approach: Try collecting objects from the active scene
            GameObject[] rootObjects = null;
            try {
                Scene activeScene = SceneManager.GetActiveScene();
                rootObjects = activeScene.GetRootGameObjects();
                Logger.LogInfo($"Searching for prefabs in active scene ({rootObjects.Length} root objects)");
            }
            catch (Exception ex) {
                Logger.LogError($"Error getting scene objects: {ex.Message}");
                rootObjects = new GameObject[0];
            }
            
            // Process scene objects
            if (rootObjects != null && rootObjects.Length > 0) 
            {
                foreach (var root in rootObjects)
                {
                    Renderer[] renderers = null;
                    try {
                        renderers = root.GetComponentsInChildren<Renderer>(true);
                    }
                    catch (Exception ex) {
                        Logger.LogError($"Error getting renderers: {ex.Message}");
                        continue;
                    }
                    
                    if (renderers == null)
                        continue;
                        
                    foreach (var renderer in renderers)
                    {
                        if (renderer == null || renderer.gameObject == null)
                            continue;
                            
                        prefabsProcessed++;
                        
                        bool shouldAdd = false;
                        try {
                            shouldAdd = ShouldAddAsPrefab(renderer.gameObject);
                        }
                        catch {
                            shouldAdd = false;
                        }
                        
                        if (shouldAdd)
                        {
                            try {
                                AddPrefabToCollection(renderer.gameObject);
                                prefabsAdded++;
                            }
                            catch (Exception ex) {
                                Logger.LogWarning($"Error adding renderer object: {ex.Message}");
                            }
                            
                            if (prefabsAdded % 50 == 0)
                            {
                                Logger.LogInfo($"Added {prefabsAdded} prefabs so far ({prefabsProcessed} processed)");
                                yield return null;
                            }
                            
                            if (prefabsAdded >= maxPrefabsToLoad)
                            {
                                Logger.LogInfo($"Reached maximum prefab count ({maxPrefabsToLoad}), stopping search");
                                break;
                            }
                        }
                        
                        if (prefabsProcessed % 500 == 0)
                        {
                            yield return null;
                        }
                    }
                    
                    if (prefabsAdded >= maxPrefabsToLoad)
                        break;
                }
            }
            
            // If we found no prefabs at all, add primitives as fallback
            if (_availablePrefabs.Count == 0)
            {
                AddPrimitives();
            }
            
            // Update the "All" category
            if (!_prefabCategories.ContainsKey("All"))
            {
                _prefabCategories["All"] = new List<GameObject>();
            }
            _prefabCategories["All"] = new List<GameObject>(_availablePrefabs);
            
            // Sort categories by name, but keep "All" first
            _categoryNames = _prefabCategories.Keys.OrderBy(c => c == "All" ? "" : c).ToList();
            int allIndex = _categoryNames.IndexOf("All");
            if (allIndex > 0)
            {
                _categoryNames.RemoveAt(allIndex);
                _categoryNames.Insert(0, "All");
            }
            
            _prefabsLoaded = true;
            Logger.LogInfo($"Successfully loaded {_availablePrefabs.Count} prefabs across {_prefabCategories.Count} categories");
        }

        private bool ShouldAddAsPrefab(GameObject obj)
        {
            // Same implementation as before
            if (obj == null) return false;
            
            try
            {
                // Skip camera, light, and other utility objects
                if (obj.GetComponent<Camera>() != null || 
                    obj.GetComponent<Light>() != null) 
                    return false;
                    
                // Check for renderers - direct or in children
                bool hasRenderer = obj.GetComponent<MeshRenderer>() != null || 
                                obj.GetComponent<SkinnedMeshRenderer>() != null;
                                
                if (!hasRenderer)
                {
                    // Check for renderers in immediate children
                    foreach (Transform child in obj.transform)
                    {
                        if (child.GetComponent<MeshRenderer>() != null || 
                            child.GetComponent<SkinnedMeshRenderer>() != null)
                        {
                            hasRenderer = true;
                            break;
                        }
                    }
                }
                
                if (!hasRenderer) return false;
                
                // Check for specific patterns in name that we want as prefabs
                string objName = obj.name.ToLower();
                
                // Good candidates include props, doors, containers, furniture, etc.
                bool isGoodCandidate = 
                    objName.Contains("door") || 
                    objName.Contains("prop") || 
                    objName.Contains("container") || 
                    objName.Contains("furniture") ||
                    objName.Contains("box") ||
                    objName.Contains("crate") ||
                    objName.Contains("loot") ||
                    objName.Contains("chair") ||
                    objName.Contains("table") ||
                    objName.Contains("shelf") ||
                    objName.Contains("cabinet") ||
                    objName.Contains("desk") ||
                    objName.Contains("weapon") ||
                    objName.Contains("gun");
                    
                // If path has "props/" in it, it's likely a good candidate
                string path = GetFullPath(obj.transform);
                if (path.Contains("/props/") || path.Contains("props/"))
                {
                    isGoodCandidate = true;
                }
                
                // Skip objects with extremely generic names unless they're good candidates
                if (!isGoodCandidate && (objName == "object" || objName == "gameobject" || objName == "root"))
                {
                    return false;
                }
                
                // Get object bounds if possible
                Bounds bounds = new Bounds(obj.transform.position, Vector3.one);
                Renderer[] renderers = obj.GetComponentsInChildren<Renderer>();
                
                if (renderers.Length > 0)
                {
                    bounds = renderers[0].bounds;
                    for (int i = 1; i < renderers.Length; i++)
                    {
                        if (renderers[i] != null)
                        {
                            bounds.Encapsulate(renderers[i].bounds);
                        }
                    }
                    
                    // Skip extremely tiny or huge objects
                    float size = bounds.size.magnitude;
                    if (size < 0.01f || size > 50f)
                    {
                        return false;
                    }
                }
                
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private void AddPrefabToCollection(GameObject obj)
        {
            if (obj == null) return;
            
            // Check if we already have this object
            if (_availablePrefabs.Contains(obj)) return;
            
            // Add to available prefabs
            _availablePrefabs.Add(obj);
            
            // Categorize the object
            string category = CategorizeObject(obj);
            
            // Add to appropriate category
            if (!_prefabCategories.ContainsKey(category))
            {
                _prefabCategories[category] = new List<GameObject>();
                _categoryNames.Add(category);
            }
            
            _prefabCategories[category].Add(obj);
        }

        private string CategorizeObject(GameObject obj)
        {
            // Same implementation as before
            if (obj == null) return "Misc";
            
            string objName = obj.name.ToLower();
            string path = GetFullPath(obj.transform).ToLower();
            
            // Try to categorize based on name or path patterns
            if (objName.Contains("door") || path.Contains("door"))
            {
                return "Doors";
            }
            else if (objName.Contains("weapon") || objName.Contains("gun") || 
                    objName.Contains("rifle") || objName.Contains("pistol") ||
                    objName.Contains("knife") || objName.Contains("sword") ||
                    path.Contains("weapon"))
            {
                return "Weapons";
            }
            else if (objName.Contains("chair") || objName.Contains("table") || 
                    objName.Contains("desk") || objName.Contains("bed") ||
                    objName.Contains("sofa") || objName.Contains("furniture") ||
                    path.Contains("furniture"))
            {
                return "Furniture";
            }
            else if (objName.Contains("loot") || objName.Contains("item") ||
                    path.Contains("loot") || path.Contains("item"))
            {
                return "Loot";
            }
            else if (objName.Contains("box") || objName.Contains("crate") || 
                    objName.Contains("container") || objName.Contains("chest") ||
                    objName.Contains("barrel") || path.Contains("container"))
            {
                return "Containers";
            }
            else if (path.Contains("prop") || path.Contains("/props/"))
            {
                return "Props";
            }
            else if (objName.Contains("wall") || objName.Contains("floor") || 
                    objName.Contains("ceiling") || objName.Contains("building") ||
                    path.Contains("building") || path.Contains("structure"))
            {
                return "Architecture";
            }
            else if (objName.Contains("transformer") || objName.Contains("electric") ||
                    objName.Contains("panel") || objName.Contains("machine") ||
                    path.Contains("electric") || path.Contains("machinery"))
            {
                return "Machinery";
            }
            
            // Default to Props if no specific category is found
            return "Props";
        }

        private void InitializeCategories()
        {
            // Same implementation as before
            // Add default categories
            string[] categories = new string[]
            {
                "All", 
                "Doors",
                "Containers", 
                "Furniture", 
                "Props",
                "Weapons",
                "Loot",
                "Architecture",
                "Machinery",
                "Misc"
            };
            
            foreach (var category in categories)
            {
                _prefabCategories[category] = new List<GameObject>();
                _categoryNames.Add(category);
            }
        }

        private void AddPrimitives()
        {
            // Same implementation as before
            Logger.LogInfo("No suitable prefabs found, adding primitives as fallback");
            
            try
            {
                AddPrimitive(PrimitiveType.Cube, "Cube", "Props");
                AddPrimitive(PrimitiveType.Sphere, "Sphere", "Props");
                AddPrimitive(PrimitiveType.Capsule, "Capsule", "Props");
                AddPrimitive(PrimitiveType.Cylinder, "Cylinder", "Props");
                AddPrimitive(PrimitiveType.Plane, "Plane", "Props");
                AddPrimitive(PrimitiveType.Quad, "Quad", "Props");
            }
            catch (Exception ex)
            {
                Logger.LogError($"Error creating primitive prefabs: {ex.Message}");
            }
        }

        private void AddPrimitive(PrimitiveType type, string name, string category)
        {
            // Same implementation as before
            var primitive = GameObject.CreatePrimitive(type);
            primitive.name = name;
            primitive.SetActive(false);
            DontDestroyOnLoad(primitive);
            
            _availablePrefabs.Add(primitive);
            
            if (_prefabCategories.ContainsKey(category))
            {
                _prefabCategories[category].Add(primitive);
            }
        }

        private IEnumerator CheckForSceneChanges()
        {
            while (true)
            {
                // Check if scene has changed
                string currentScene = SceneManager.GetActiveScene().name;
                if (currentScene != _currentScene)
                {
                    _currentScene = currentScene;
                    Logger.LogInfo($"Scene changed to: {_currentScene} (detected by periodic check)");
                    
                    if (EnablePersistence.Value && !string.IsNullOrEmpty(_currentScene))
                    {
                        // Reset attempt counter
                        _transformApplicationAttempts = 0;
                        
                        // Apply transforms to the new scene
                        StartCoroutine(ApplyTransformsWithRetry(SceneManager.GetActiveScene()));
                    }
                }
                
                yield return new WaitForSeconds(1.0f);
            }
        }

        private void SetupUnityExplorerReflection()
        {
            // Same implementation as before
            try
            {
                // Try to find UnityExplorer assembly
                var unityExplorerAssembly = AppDomain.CurrentDomain
                    .GetAssemblies()
                    .FirstOrDefault(a => a.GetName().Name.Contains("UnityExplorer"));
                
                if (unityExplorerAssembly != null)
                {
                    Logger.LogInfo($"Found UnityExplorer assembly: {unityExplorerAssembly.FullName}");
                    
                    // Try to get InspectorManager type with the exact namespace specified
                    // Look for types containing "InspectorManager" since we might not know the exact name
                    var possibleManagerTypes = unityExplorerAssembly.GetTypes()
                        .Where(t => t.Name.Contains("InspectorManager"))
                        .ToList();
                        
                    if (possibleManagerTypes.Count > 0)
                    {
                        Logger.LogInfo($"Found {possibleManagerTypes.Count} potential InspectorManager types");
                        
                        // Try each potential type
                        foreach (var type in possibleManagerTypes)
                        {
                            try
                            {
                                _inspectorManagerType = type;
                                Logger.LogInfo($"Trying type: {_inspectorManagerType.FullName}");
                                
                                // Use very specific binding flags to avoid ambiguity
                                var bindingFlags = BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly;
                                
                                // Get ActiveInspector property
                                _activeInspectorProperty = _inspectorManagerType.GetProperty(
                                    "ActiveInspector",
                                    bindingFlags);
                                
                                if (_activeInspectorProperty != null)
                                {
                                    Logger.LogInfo($"Found ActiveInspector property in {type.FullName}");
                                    break;
                                }
                            }
                            catch (Exception ex)
                            {
                                Logger.LogWarning($"Error checking type {type.FullName}: {ex.Message}");
                            }
                        }
                    }
                }
                else
                {
                    Logger.LogWarning("UnityExplorer assembly not found. Some features will be unavailable.");
                }
            }
            catch (Exception ex)
            {
                Logger.LogError($"Error setting up reflection: {ex.Message}");
            }
        }

        private void OnDestroy()
        {
            // Unsubscribe from scene load event
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        private void CheckForInspectedObject()
        {
            // Same implementation as before
            if (_activeInspectorProperty == null) return;
            
            try
            {
                // Get active inspector using explicit binding flags to avoid ambiguity
                object activeInspector = null;
                
                try
                {
                    activeInspector = _activeInspectorProperty.GetValue(null, null);
                }
                catch (AmbiguousMatchException)
                {
                    // If we get an ambiguous match, try with more specific binding flags
                    Logger.LogWarning("Ambiguous match for ActiveInspector property, trying with specific binding flags");
                    var flags = BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly;
                    _activeInspectorProperty = _inspectorManagerType.GetProperty("ActiveInspector", flags);
                    
                    if (_activeInspectorProperty != null)
                    {
                        activeInspector = _activeInspectorProperty.GetValue(null, null);
                    }
                }
                
                if (activeInspector == null) return;
                
                // Check if it's a GameObject inspector by searching for "GameObjectInspector" in the type name
                string inspectorTypeName = activeInspector.GetType().Name;
                if (inspectorTypeName.Contains("GameObject"))
                {
                    // Get Target property from inspector with explicit binding flags
                    if (_targetProperty == null)
                    {
                        _targetProperty = activeInspector.GetType().GetProperty(
                            "Target", 
                            BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
                    }
                    
                    if (_targetProperty != null)
                    {
                        // Get target GameObject
                        var target = _targetProperty.GetValue(activeInspector, null) as GameObject;
                        if (target != _currentInspectedObject)
                        {
                            // Remove highlighter from previous object
                            if (_currentInspectedObject != null && _currentHighlighter != null)
                            {
                                Destroy(_currentHighlighter);
                                _currentHighlighter = null;
                            }
                            
                            // Store the newly inspected GameObject
                            _currentInspectedObject = target;
                            
                            if (_currentInspectedObject != null)
                            {
                                // Cache initial transform values
                                _lastPosition = _currentInspectedObject.transform.position;
                                _lastLocalPosition = _currentInspectedObject.transform.localPosition;
                                _lastRotation = _currentInspectedObject.transform.rotation;
                                _lastScale = _currentInspectedObject.transform.localScale;
                                
                                string uniqueId = GenerateUniqueId(_currentInspectedObject.transform);
                                Logger.LogInfo($"Now inspecting: {_currentInspectedObject.name} with Unique ID: {uniqueId}");
                                
                                // Add highlight to current object if enabled
                                if (EnableObjectHighlight.Value)
                                {
                                    _currentHighlighter = _currentInspectedObject.AddComponent<SelectionHighlighter>();
                                }
                            }
                        }
                    }
                }
                else
                {
                    // Remove highlighter when no GameObject is selected
                    if (_currentInspectedObject != null && _currentHighlighter != null)
                    {
                        Destroy(_currentHighlighter);
                        _currentHighlighter = null;
                    }
                    
                    _currentInspectedObject = null;
                }
            }
            catch (Exception ex)
            {
                // Log details about the exception without causing spam
                if (!(ex is AmbiguousMatchException) || Time.time - _lastCheckTime > 10f)
                {
                    Logger.LogError($"Error checking for inspected object: {ex.Message} ({ex.GetType().Name})");
                }
            }
        }

        private void Update()
        {
            try
            {
                // Check for mouse toggle hotkey
                if (MouseToggleHotkey != null && MouseToggleHotkey.Value.IsDown())
                {
                    ToggleMouseFocus();
                }
                
                // Check other hotkeys
                if (SaveHotkey != null && SaveHotkey.Value.IsDown())
                {
                    SaveAllTaggedObjects();
                }
                
                if (TagHotkey != null && TagHotkey.Value.IsDown() && _currentInspectedObject != null)
                {
                    TagObject(_currentInspectedObject);
                }
                
                if (DestroyHotkey != null && DestroyHotkey.Value.IsDown() && _currentInspectedObject != null)
                {
                    MarkForDestruction(_currentInspectedObject);
                }
                
                if (SpawnHotkey != null && SpawnHotkey.Value.IsDown())
                {
                    _showPrefabSelector = !_showPrefabSelector;
                    if (_showPrefabSelector && !_prefabsLoaded)
                    {
                        StartCoroutine(LoadPrefabs());
                    }
                }
                
                // Periodically check for inspected object changes
                if (Time.time - _lastCheckTime > CHECK_INTERVAL)
                {
                    _lastCheckTime = Time.time;
                    CheckForInspectedObject();
                }
                
                // Only proceed with auto-tracking if we have an inspected object and aren't already applying a transform
                if (_currentInspectedObject == null || _isApplyingTransform)
                    return;

                Transform transform = _currentInspectedObject.transform;
                
                // Check if transform has changed
                if (!TransformValuesEqual(transform.position, _lastPosition) ||
                    !TransformValuesEqual(transform.localPosition, _lastLocalPosition) ||
                    !QuaternionsEqual(transform.rotation, _lastRotation) ||
                    !TransformValuesEqual(transform.localScale, _lastScale))
                {
                    // Update cached values
                    _lastPosition = transform.position;
                    _lastLocalPosition = transform.localPosition;
                    _lastRotation = transform.rotation;
                    _lastScale = transform.localScale;
                    
                    // Save the transform
                    SaveTransform(_currentInspectedObject);
                }
            }
            catch (Exception ex)
            {
                // Log the error but don't let it crash the update loop
                Logger.LogError($"Error in Update: {ex.Message}");
            }
        }

        private bool TransformValuesEqual(Vector3 a, Vector3 b)
        {
            return Vector3.Distance(a, b) < 0.001f;
        }

        private bool QuaternionsEqual(Quaternion a, Quaternion b)
        {
            return Quaternion.Angle(a, b) < 0.001f;
        }

        // Toggle mouse focus between UI and game
        private void ToggleMouseFocus()
        {
            // Same implementation as before
            try 
            {
                _uiHasFocus = !_uiHasFocus;
                
                if (_uiHasFocus)
                {
                    // Don't try to store mouse position from Input class
                    // Just use a default position when returning focus
                    _savedMousePosition = new Vector3(Screen.width / 2, Screen.height / 2, 0f);
                    
                    // Enable cursor for UI interaction
                    Cursor.visible = true;
                    Cursor.lockState = CursorLockMode.None;
                    
                    // Center cursor on main window
                    Vector2 screenCenter = new Vector2(_windowRect.x + _windowRect.width / 2, _windowRect.y + _windowRect.height / 2);
                    SetMousePosition(screenCenter);
                    
                    Logger.LogInfo("Mouse focus switched to UI");
                }
                else
                {
                    // Hide cursor and return to game control
                    Cursor.visible = false;
                    Cursor.lockState = CursorLockMode.Locked;
                    
                    // Restore previous mouse position if we saved one
                    if (_savedMousePosition != Vector3.zero)
                    {
                        SetMousePosition(_savedMousePosition);
                    }
                    
                    Logger.LogInfo("Mouse focus returned to game");
                }
            }
            catch (Exception ex)
            {
                Logger.LogError($"Error in ToggleMouseFocus: {ex.Message}");
            }
        }
        
        // Helper method to set mouse position
        private void SetMousePosition(Vector2 position)
        {
            // Same implementation as before
            // Some games require different methods to set cursor position
            // Try standard Unity method first
            try
            {
                // Convert to screen coordinates if needed
                Vector2 screenPos = new Vector2(position.x, Screen.height - position.y);
                Mouse.SetPosition(screenPos);
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"Could not set mouse position: {ex.Message}");
            }
        }
        
        // Mouse utility class
        private static class Mouse
        {
            // Same implementation as before
            public static void SetPosition(Vector2 position)
            {
                #if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
                // Windows implementation
                SetCursorPos((int)position.x, (int)position.y);
                #elif UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX
                // Mac implementation
                SetCursorPositionMac((int)position.x, (int)position.y);
                #else
                // Fallback to Unity's method which isn't reliable on all platforms
                UnityEngine.Cursor.SetCursor(null, position, CursorMode.Auto);
                #endif
            }
            
            #if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            [System.Runtime.InteropServices.DllImport("user32.dll")]
            private static extern bool SetCursorPos(int x, int y);
            #elif UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX
            [System.Runtime.InteropServices.DllImport("libdl.dylib")]
            private static extern void SetCursorPositionMac(int x, int y);
            #endif
        }

        // Tag an object to be tracked
        public void TagObject(GameObject obj)
        {
            if (obj == null) return;
            
            TransformCacherTag tag = obj.GetComponent<TransformCacherTag>();
            if (tag == null)
            {
                tag = obj.AddComponent<TransformCacherTag>();
                
                // Generate PathID and ItemID
                tag.PathID = GeneratePathID(obj.transform);
                tag.ItemID = GenerateItemID(obj.transform);
                
                string uniqueId = GenerateUniqueId(obj.transform);
                Logger.LogInfo($"Tagged object: {GetFullPath(obj.transform)} with ID: {uniqueId}, PathID: {tag.PathID}, ItemID: {tag.ItemID}");
            }
            else
            {
                Logger.LogInfo($"Object already tagged: {obj.name}");
            }
            
            // Ensure object is not marked as destroyed if we're tagging it
            tag.IsDestroyed = false;
            
            // Save its current transform
            SaveTransform(obj);
        }

        // Mark an object for destruction
        public void MarkForDestruction(GameObject obj)
        {
            if (obj == null) return;
            
            try
            {
                string sceneName = obj.scene.name;
                string objectPath = GetFullPath(obj.transform);
                string uniqueId = GenerateUniqueId(obj.transform);
                
                Logger.LogInfo($"Marking for destruction: {objectPath} with ID: {uniqueId}");
                
                // Add to destroyed objects cache
                if (!_destroyedObjectsCache.ContainsKey(sceneName))
                {
                    _destroyedObjectsCache[sceneName] = new HashSet<string>();
                }
                
                _destroyedObjectsCache[sceneName].Add(objectPath);
                
                // Get or add tag component
                TransformCacherTag tag = obj.GetComponent<TransformCacherTag>();
                if (tag == null)
                {
                    tag = obj.AddComponent<TransformCacherTag>();
                    tag.PathID = GeneratePathID(obj.transform);
                    tag.ItemID = GenerateItemID(obj.transform);
                }
                
                // Mark as destroyed
                tag.IsDestroyed = true;
                
                // Get the transform database
                var transformsDb = _databaseManager.GetTransformsDatabase();
                
                // Make sure the scene exists in the database
                if (!transformsDb.ContainsKey(sceneName))
                {
                    transformsDb[sceneName] = new Dictionary<string, TransformData>();
                    Logger.LogInfo($"Created new scene entry in database: {sceneName}");
                }
                
                // Create or update transform data
                if (transformsDb[sceneName].ContainsKey(uniqueId))
                {
                    transformsDb[sceneName][uniqueId].IsDestroyed = true;
                    Logger.LogInfo($"Updated existing transform data as destroyed: {uniqueId}");
                }
                else
                {
                    // Create new entry with new format
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
                        ParentPath = obj.transform.parent != null ? GetFullPath(obj.transform.parent) : "",
                        IsDestroyed = true,
                        IsSpawned = false,
                        PrefabPath = "",
                        Children = GetChildrenIds(obj.transform)
                    };
                    
                    transformsDb[sceneName][uniqueId] = data;
                    Logger.LogInfo($"Created new transform data marked as destroyed: {uniqueId}");
                }
                
                // Update the database in the manager
                _databaseManager.SetTransformsDatabase(transformsDb);
                
                // Process all children recursively 
                MarkChildrenAsDestroyed(obj.transform, sceneName);
                
                // Hide the object immediately
                obj.SetActive(false);
                
                // Save database
                _databaseManager.SaveTransformsDatabase();
                
                Logger.LogInfo($"Successfully marked object and all children for destruction: {objectPath}");
            }
            catch (Exception ex)
            {
                Logger.LogError($"Error marking object for destruction: {ex.Message}\n{ex.StackTrace}");
            }
        }
        
        // Helper to mark all children as destroyed
        private void MarkChildrenAsDestroyed(Transform parent, string sceneName)
        {
            if (parent == null) return;
            
            // Get the transform database
            var transformsDb = _databaseManager.GetTransformsDatabase();
            
            // Process each child
            foreach (Transform child in parent)
            {
                try
                {
                    string childPath = GetFullPath(child);
                    string childUniqueId = GenerateUniqueId(child);
                    
                    // Add to destroyed objects cache
                    _destroyedObjectsCache[sceneName].Add(childPath);
                    
                    // Get or add tag component
                    TransformCacherTag childTag = child.gameObject.GetComponent<TransformCacherTag>();
                    if (childTag == null)
                    {
                        childTag = child.gameObject.AddComponent<TransformCacherTag>();
                        childTag.PathID = GeneratePathID(child);
                        childTag.ItemID = GenerateItemID(child);
                    }
                    
                    // Mark as destroyed
                    childTag.IsDestroyed = true;
                    
                    // Create or update transform data for child
                    if (transformsDb[sceneName].ContainsKey(childUniqueId))
                    {
                        transformsDb[sceneName][childUniqueId].IsDestroyed = true;
                    }
                    else
                    {
                        // Create new entry for child with new format
                        var childData = new TransformData
                        {
                            UniqueId = childUniqueId,
                            PathID = childTag.PathID,
                            ItemID = childTag.ItemID,
                            ObjectPath = childPath,
                            ObjectName = child.name,
                            SceneName = sceneName,
                            Position = child.position,
                            Rotation = child.eulerAngles,
                            Scale = child.localScale,
                            ParentPath = GetFullPath(child.parent),
                            IsDestroyed = true,
                            IsSpawned = false,
                            PrefabPath = "",
                            Children = GetChildrenIds(child)
                        };
                        
                        transformsDb[sceneName][childUniqueId] = childData;
                    }
                    
                    // Update the database in the manager
                    _databaseManager.SetTransformsDatabase(transformsDb);
                    
                    // Process this child's children recursively
                    MarkChildrenAsDestroyed(child, sceneName);
                }
                catch (Exception ex)
                {
                    Logger.LogError($"Error marking child for destruction: {ex.Message}");
                }
            }
        }
        
        // Get a list of children UniqueIds
        private List<string> GetChildrenIds(Transform parent)
        {
            List<string> childrenIds = new List<string>();
            
            if (parent == null) return childrenIds;
            
            foreach (Transform child in parent)
            {
                string childId = GenerateUniqueId(child);
                childrenIds.Add(childId);
            }
            
            return childrenIds;
        }
        
        // Spawn a new object
        public void SpawnObject(GameObject prefab)
        {
            if (prefab == null)
            {
                Logger.LogWarning("No prefab selected to spawn");
                return;
            }
            
            try
            {
                // Find camera for positioning
                Camera mainCamera = Camera.main;
                if (mainCamera == null)
                {
                    mainCamera = FindObjectOfType<Camera>();
                    if (mainCamera == null)
                    {
                        Logger.LogWarning("No camera found to position spawned object");
                        return;
                    }
                }
                
                // Instantiate the prefab
                GameObject spawnedObj = Instantiate(prefab);
                spawnedObj.name = prefab.name + "_spawned";
                
                // Position in front of camera
                Vector3 spawnPos = mainCamera.transform.position + mainCamera.transform.forward * 3f;
                spawnedObj.transform.position = spawnPos;
                spawnedObj.transform.rotation = mainCamera.transform.rotation;
                
                // Make sure it's activated
                spawnedObj.SetActive(true);
                
                // Tag the object and mark as spawned
                TransformCacherTag tag = spawnedObj.AddComponent<TransformCacherTag>();
                tag.IsSpawned = true;
                tag.PathID = GeneratePathID(spawnedObj.transform);
                tag.ItemID = GenerateItemID(spawnedObj.transform);
                
                // Save the transform data
                SaveTransform(spawnedObj);
                
                // Update database to mark as spawned
                string sceneName = spawnedObj.scene.name;
                string uniqueId = GenerateUniqueId(spawnedObj.transform);
                
                var transformsDb = _databaseManager.GetTransformsDatabase();
                if (transformsDb.ContainsKey(sceneName) && transformsDb[sceneName].ContainsKey(uniqueId))
                {
                    transformsDb[sceneName][uniqueId].IsSpawned = true;
                    transformsDb[sceneName][uniqueId].PrefabPath = prefab.name;
                    _databaseManager.SetTransformsDatabase(transformsDb);
                    _databaseManager.SaveTransformsDatabase();
                }
                
                // Select the new object
                _currentInspectedObject = spawnedObj;
                _lastPosition = spawnedObj.transform.position;
                _lastLocalPosition = spawnedObj.transform.localPosition;
                _lastRotation = spawnedObj.transform.rotation;
                _lastScale = spawnedObj.transform.localScale;
                
                // Hide the prefab selector
                _showPrefabSelector = false;
                
                Logger.LogInfo($"Spawned object: {spawnedObj.name} at {spawnPos}");
            }
            catch (Exception ex)
            {
                Logger.LogError($"Error spawning object: {ex.Message}");
            }
        }

        // Generate a unique ID for a transform that persists across game sessions
        public static string GenerateUniqueId(Transform transform)
        {
            if (transform == null) return string.Empty;
            
            // Try to get a baked ID first if available
            if (Instance != null && Instance._idBaker != null)
            {
                BakedIdData bakedData;
                if (Instance._idBaker.TryGetBakedId(transform, out bakedData))
                {
                    // This now returns the formatted UniqueId from baked data
                    if (!string.IsNullOrEmpty(bakedData.PathID) && !string.IsNullOrEmpty(bakedData.ItemID))
                    {
                        return bakedData.PathID + "+" + bakedData.ItemID;
                    }
                    return bakedData.UniqueId;
                }
            }
            
            // Get PathID and ItemID for this transform
            string pathId = GeneratePathID(transform);
            string itemId = GenerateItemID(transform);
            
            // Combine for the new format
            if (!string.IsNullOrEmpty(pathId) && !string.IsNullOrEmpty(itemId))
            {
                return pathId + "+" + itemId;
            }
            
            // Fall back to old format if generation failed
            string sceneName = transform.gameObject.scene.name;
            string hierarchyPath = GetFullPath(transform);
            
            // Add position to make IDs more unique
            // Round to 2 decimal places to avoid floating point precision issues
            string positionStr = string.Format(
                "pos_x{0:F2}y{1:F2}z{2:F2}",
                Math.Round(transform.position.x, 2),
                Math.Round(transform.position.y, 2),
                Math.Round(transform.position.z, 2)
            );
            
            // Simple, stable ID based on scene, path and position
            return $"{sceneName}_{hierarchyPath}_{positionStr}";
        }

        // Generate a PathID for a transform
        public static string GeneratePathID(Transform transform)
        {
            if (transform == null) return string.Empty;
            
            // Get object path
            string objectPath = GetFullPath(transform);
            
            // Create a hash code from the path for shorter ID
            int hashCode = objectPath.GetHashCode();
            
            // Return a path ID that's "P" prefix + absolute hash code
            return "P" + Math.Abs(hashCode).ToString();
        }
        
        // Generate an ItemID for a transform
        public static string GenerateItemID(Transform transform)
        {
            if (transform == null) return string.Empty;
            
            // Use name + scene + sibling index as a unique identifier
            string name = transform.name;
            string scene = transform.gameObject.scene.name;
            int siblingIndex = transform.GetSiblingIndex();
            
            string idSource = $"{name}_{scene}_{siblingIndex}";
            int hashCode = idSource.GetHashCode();
            
            // Return an item ID that's "I" prefix + absolute hash code
            return "I" + Math.Abs(hashCode).ToString();
        }

        // Get the path of sibling indices, which is more stable than names
        private static string GetSiblingIndicesPath(Transform transform)
        {
            if (transform == null) return string.Empty;
            
            var indices = new Stack<int>();
            
            Transform current = transform;
            while (current != null)
            {
                indices.Push(current.GetSiblingIndex());
                current = current.parent;
            }
            
            return string.Join(".", indices.ToArray());
        }

        // Save all tagged objects
        public void SaveAllTaggedObjects()
        {
            string sceneName = SceneManager.GetActiveScene().name;
            var tags = GameObject.FindObjectsOfType<TransformCacherTag>();
            
            // Get the transforms database
            var transformsDb = _databaseManager.GetTransformsDatabase();
            
            // Create scene entry if it doesn't exist
            if (!transformsDb.ContainsKey(sceneName))
            {
                transformsDb[sceneName] = new Dictionary<string, TransformData>();
            }
            
            var updatedObjects = new Dictionary<string, TransformData>();

            foreach (var tag in tags)
            {
                // Skip destroyed objects (but keep them in the database)
                if (tag.IsDestroyed) continue;
                
                var tr = tag.transform;
                var uniqueId = tag.UniqueId;
                
                if (string.IsNullOrEmpty(uniqueId))
                {
                    uniqueId = GenerateUniqueId(tr);
                }
                
                // Ensure PathID and ItemID are set
                if (string.IsNullOrEmpty(tag.PathID))
                {
                    tag.PathID = GeneratePathID(tr);
                }
                
                if (string.IsNullOrEmpty(tag.ItemID))
                {
                    tag.ItemID = GenerateItemID(tr);
                }
                
                var data = new TransformData
                {
                    UniqueId = uniqueId,
                    PathID = tag.PathID,
                    ItemID = tag.ItemID,
                    ObjectPath = GetFullPath(tr),
                    ObjectName = tr.name,
                    SceneName = sceneName,
                    Position = tr.position,
                    Rotation = tr.eulerAngles,
                    Scale = tr.localScale,
                    ParentPath = tr.parent != null ? GetFullPath(tr.parent) : "",
                    IsDestroyed = tag.IsDestroyed,
                    IsSpawned = tag.IsSpawned,
                    Children = GetChildrenIds(tr)
                };
                
                // Keep the PrefabPath if it was a spawned object
                if (tag.IsSpawned && transformsDb[sceneName].ContainsKey(uniqueId) &&
                    !string.IsNullOrEmpty(transformsDb[sceneName][uniqueId].PrefabPath))
                {
                    data.PrefabPath = transformsDb[sceneName][uniqueId].PrefabPath;
                }

                updatedObjects[uniqueId] = data;
            }
            
            // Preserve destroyed objects in the database
            if (transformsDb.ContainsKey(sceneName))
            {
                foreach (var entry in transformsDb[sceneName])
                {
                    if (entry.Value.IsDestroyed && !updatedObjects.ContainsKey(entry.Key))
                    {
                        updatedObjects[entry.Key] = entry.Value;
                    }
                }
            }

            // Update database
            transformsDb[sceneName] = updatedObjects;
            _databaseManager.SetTransformsDatabase(transformsDb);
            _databaseManager.SaveTransformsDatabase();
            
            Logger.LogInfo($"Saved {updatedObjects.Count} objects for {sceneName}");
        }

        // Save transform data for a GameObject
        public void SaveTransform(GameObject obj)
        {
            if (obj == null) return;
            
            try
            {
                string sceneName = SceneManager.GetActiveScene().name;
                
                // Get the transforms database
                var transformsDb = _databaseManager.GetTransformsDatabase();
                
                if (!transformsDb.ContainsKey(sceneName))
                    transformsDb[sceneName] = new Dictionary<string, TransformData>();
                
                Transform transform = obj.transform;
                
                // Generate IDs
                string uniqueId = GenerateUniqueId(transform);
                string pathId = GeneratePathID(transform);
                string itemId = GenerateItemID(transform);
                
                // Check for existing tag component or add one if needed
                TransformCacherTag tag = obj.GetComponent<TransformCacherTag>();
                if (tag == null)
                {
                    tag = obj.AddComponent<TransformCacherTag>();
                    tag.PathID = pathId;
                    tag.ItemID = itemId;
                }
                
                bool isDestroyed = tag.IsDestroyed;
                bool isSpawned = tag.IsSpawned;
                
                Logger.LogInfo($"Saving transform for {obj.name} (ID: {uniqueId}, PathID: {pathId}, ItemID: {itemId}, Destroyed: {isDestroyed}, Spawned: {isSpawned})");
                
                // Preserve prefab path if it exists
                string prefabPath = "";
                if (transformsDb[sceneName].ContainsKey(uniqueId) && 
                    !string.IsNullOrEmpty(transformsDb[sceneName][uniqueId].PrefabPath))
                {
                    prefabPath = transformsDb[sceneName][uniqueId].PrefabPath;
                }
                
                var data = new TransformData
                {
                    UniqueId = uniqueId,
                    PathID = pathId,
                    ItemID = itemId,
                    ObjectPath = GetFullPath(transform),
                    ObjectName = obj.name,
                    SceneName = sceneName,
                    Position = transform.position,
                    Rotation = transform.eulerAngles,
                    Scale = transform.localScale,
                    ParentPath = transform.parent != null ? GetFullPath(transform.parent) : "",
                    IsDestroyed = isDestroyed,
                    IsSpawned = isSpawned,
                    PrefabPath = prefabPath,
                    Children = GetChildrenIds(transform)
                };
                
                transformsDb[sceneName][uniqueId] = data;
                
                // Update the database in the manager
                _databaseManager.SetTransformsDatabase(transformsDb);
                
                // Also save transforms of all children recursively
                SaveChildTransforms(transform, sceneName);
                
                // Save database after each change
                _databaseManager.SaveTransformsDatabase();
            }
            catch (Exception ex)
            {
                Logger.LogError($"Error saving transform: {ex.Message}");
            }
        }

        // Save transforms for all children
        private void SaveChildTransforms(Transform parent, string sceneName)
        {
            if (parent == null) return;
            
            // Get the transforms database
            var transformsDb = _databaseManager.GetTransformsDatabase();
            
            foreach (Transform child in parent)
            {
                try
                {
                    GameObject childObj = child.gameObject;
                    
                    // Generate IDs
                    string childUniqueId = GenerateUniqueId(child);
                    string childPathId = GeneratePathID(child);
                    string childItemId = GenerateItemID(child);
                    
                    // Check for existing tag component or add one if needed
                    TransformCacherTag childTag = childObj.GetComponent<TransformCacherTag>();
                    if (childTag == null)
                    {
                        childTag = childObj.AddComponent<TransformCacherTag>();
                        childTag.PathID = childPathId;
                        childTag.ItemID = childItemId;
                    }
                    
                    bool childIsDestroyed = childTag.IsDestroyed;
                    bool childIsSpawned = childTag.IsSpawned;
                    
                    // Preserve prefab path if it exists for child
                    string childPrefabPath = "";
                    if (transformsDb[sceneName].ContainsKey(childUniqueId) && 
                        !string.IsNullOrEmpty(transformsDb[sceneName][childUniqueId].PrefabPath))
                    {
                        childPrefabPath = transformsDb[sceneName][childUniqueId].PrefabPath;
                    }
                    
                    var childData = new TransformData
                    {
                        UniqueId = childUniqueId,
                        PathID = childPathId,
                        ItemID = childItemId,
                        ObjectPath = GetFullPath(child),
                        ObjectName = childObj.name,
                        SceneName = sceneName,
                        Position = child.position,
                        Rotation = child.eulerAngles,
                        Scale = child.localScale,
                        ParentPath = GetFullPath(child.parent),
                        IsDestroyed = childIsDestroyed,
                        IsSpawned = childIsSpawned,
                        PrefabPath = childPrefabPath,
                        Children = GetChildrenIds(child)
                    };
                    
                    transformsDb[sceneName][childUniqueId] = childData;
                    
                    // Update the database in the manager
                    _databaseManager.SetTransformsDatabase(transformsDb);
                    
                    // Process this child's children recursively
                    SaveChildTransforms(child, sceneName);
                }
                catch (Exception ex)
                {
                    Logger.LogError($"Error saving child transform: {ex.Message}");
                }
            }
        }

        // Get full path of transform
        public static string GetFullPath(Transform transform)
        {
            if (transform == null) return string.Empty;
            
            var path = new Stack<string>();
            
            var current = transform;
            while (current != null)
            {
                path.Push(current.name);
                current = current.parent;
            }
            
            return string.Join("/", path.ToArray());
        }

        // Apply saved transforms to objects in the scene - enhanced with retry logic
        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            _currentScene = scene.name;
            Logger.LogInfo($"Scene loaded event: {scene.name} (mode: {mode})");
            
            if (!EnablePersistence.Value)
            {
                Logger.LogInfo("Transform persistence is disabled");
                return;
            }
            
            // Reset attempt counter for the new scene
            _transformApplicationAttempts = 0;
            
            // Start the transform application process with retries
            StartCoroutine(ApplyTransformsWithRetry(scene));
        }

        private IEnumerator ApplyTransformsWithRetry(Scene scene)
        {
            // Wait for scene to properly initialize
            float initialDelay = TransformDelay != null ? TransformDelay.Value : 1.0f;
            Logger.LogInfo($"Waiting {initialDelay}s before applying transforms to {scene.name}");
            yield return new WaitForSeconds(initialDelay);
            
            int maxAttempts = MaxRetries != null ? MaxRetries.Value : 3;
            bool success = false;
            
            while (_transformApplicationAttempts < maxAttempts && !success)
            {
                _transformApplicationAttempts++;
                Logger.LogInfo($"Attempt {_transformApplicationAttempts}/{maxAttempts} to apply transforms to {scene.name}");
                
                // Run the coroutine and wait for it to complete
                yield return StartCoroutine(ApplyTransformsToScene(scene, (result) => { success = result; }));
                
                if (success)
                {
                    Logger.LogInfo($"Successfully applied transforms to {scene.name} on attempt {_transformApplicationAttempts}");
                    
                    // Also handle destroyed objects
                    yield return StartCoroutine(ApplyDestroyedObjects(scene.name));
                    
                    // Also respawn any spawned objects
                    yield return StartCoroutine(RespawnObjects(scene.name));
                    
                    yield break;
                }
                
                if (_transformApplicationAttempts < maxAttempts)
                {
                    Logger.LogInfo($"Transform application attempt {_transformApplicationAttempts} incomplete, retrying in {RETRY_DELAY}s");
                    yield return new WaitForSeconds(RETRY_DELAY);
                }
                else
                {
                    Logger.LogWarning($"Failed to apply all transforms after {maxAttempts} attempts");
                }
            }
        }
        
        private IEnumerator ApplyDestroyedObjects(string sceneName)
        {
            Logger.LogInfo($"Applying destroyed objects for scene: {sceneName}");
            
            int hiddenObjects = 0;
            
            // Get the transforms database
            var transformsDb = _databaseManager.GetTransformsDatabase();
            
            // First check the transform database for objects marked as destroyed
            if (transformsDb.ContainsKey(sceneName))
            {
                foreach (var entry in transformsDb[sceneName].Values)
                {
                    if (entry.IsDestroyed)
                    {
                        // Try to find the object
                        GameObject obj = FindObjectByPath(entry.ObjectPath);
                        if (obj != null)
                        {
                            // Hide it
                            obj.SetActive(false);
                            
                            // Tag it as destroyed
                            TransformCacherTag tag = obj.GetComponent<TransformCacherTag>();
                            if (tag == null)
                            {
                                tag = obj.AddComponent<TransformCacherTag>();
                                tag.PathID = entry.PathID;
                                tag.ItemID = entry.ItemID;
                            }
                            tag.IsDestroyed = true;
                            
                            hiddenObjects++;
                            
                            // Add to cache for future reference
                            if (!_destroyedObjectsCache.ContainsKey(sceneName))
                            {
                                _destroyedObjectsCache[sceneName] = new HashSet<string>();
                            }
                            _destroyedObjectsCache[sceneName].Add(entry.ObjectPath);
                        }
                    }
                }
            }
            
            // Also check destroyedObjectsCache
            if (_destroyedObjectsCache.ContainsKey(sceneName))
            {
                foreach (string path in _destroyedObjectsCache[sceneName])
                {
                    GameObject obj = FindObjectByPath(path);
                    if (obj != null)
                    {
                        obj.SetActive(false);
                        
                        // Tag it if not already tagged
                        TransformCacherTag tag = obj.GetComponent<TransformCacherTag>();
                        if (tag == null)
                        {
                            tag = obj.AddComponent<TransformCacherTag>();
                            tag.PathID = GeneratePathID(obj.transform);
                            tag.ItemID = GenerateItemID(obj.transform);
                            tag.IsDestroyed = true;
                        }
                        
                        hiddenObjects++;
                    }
                }
            }
            
            Logger.LogInfo($"Applied destruction to {hiddenObjects} objects in scene {sceneName}");
            
            yield break;
        }
        
        private IEnumerator RespawnObjects(string sceneName)
        {
            Logger.LogInfo($"Respawning objects for scene: {sceneName}");
            
            int respawnedCount = 0;
            
            // Get the transforms database
            var transformsDb = _databaseManager.GetTransformsDatabase();
            
            // Wait for prefabs to be loaded
            if (!_prefabsLoaded)
            {
                Logger.LogInfo("Waiting for prefabs to load...");
                float waitTime = 0;
                while (!_prefabsLoaded && waitTime < 10f)
                {
                    yield return new WaitForSeconds(0.5f);
                    waitTime += 0.5f;
                }
                
                if (!_prefabsLoaded)
                {
                    Logger.LogWarning("Prefabs still not loaded after 10 seconds, trying to load now");
                    yield return StartCoroutine(LoadPrefabs());
                }
            }
            
            // Check database for spawned objects
            if (transformsDb.ContainsKey(sceneName))
            {
                foreach (var entry in transformsDb[sceneName].Values)
                {
                    if (entry.IsSpawned && !entry.IsDestroyed)
                    {
                        // Try to find matching prefab
                        GameObject prefab = null;
                        
                        // First try by name
                        if (!string.IsNullOrEmpty(entry.PrefabPath))
                        {
                            prefab = _availablePrefabs.FirstOrDefault(p => p.name == entry.PrefabPath);
                        }
                        
                        // If not found, try to find by similar name
                        if (prefab == null && !string.IsNullOrEmpty(entry.ObjectName))
                        {
                            string baseName = entry.ObjectName.Replace("_spawned", "");
                            prefab = _availablePrefabs.FirstOrDefault(p => p.name == baseName);
                        }
                        
                        // Use a default if nothing found
                        if (prefab == null && _availablePrefabs.Count > 0)
                        {
                            prefab = _availablePrefabs[0];
                        }
                        
                        // Spawn the object if we have a prefab
                        if (prefab != null)
                        {
                            try
                            {
                                // Instantiate and position
                                GameObject spawnedObj = Instantiate(prefab);
                                spawnedObj.name = entry.ObjectName;
                                spawnedObj.transform.position = entry.Position;
                                spawnedObj.transform.rotation = Quaternion.Euler(entry.Rotation);
                                spawnedObj.transform.localScale = entry.Scale;
                                spawnedObj.SetActive(true);
                                
                                // Tag it
                                TransformCacherTag tag = spawnedObj.AddComponent<TransformCacherTag>();
                                tag.IsSpawned = true;
                                tag.PathID = entry.PathID;
                                tag.ItemID = entry.ItemID;
                                
                                respawnedCount++;
                            }
                            catch (Exception ex)
                            {
                                Logger.LogError($"Error respawning object: {ex.Message}");
                            }
                        }
                    }
                }
            }
            
            Logger.LogInfo($"Respawned {respawnedCount} objects in scene {sceneName}");
            
            yield break;
        }

        private IEnumerator ApplyTransformsToScene(Scene scene, Action<bool> callback)
        {
            // Wait until end of frame to ensure all objects are fully loaded
            yield return new WaitForEndOfFrame();
            
            string sceneName = scene.name;
            
            // Get the transforms database
            var transformsDb = _databaseManager.GetTransformsDatabase();
            
            if (!transformsDb.ContainsKey(sceneName) || transformsDb[sceneName].Count == 0)
            {
                Logger.LogInfo($"No saved transforms for scene: {sceneName}");
                callback(true); // Success (nothing to do)
                yield break;
            }
            
            int totalObjects = transformsDb[sceneName].Count;
            int appliedCount = 0;
            _isApplyingTransform = true;
            
            // First, collect all GameObjects in the scene
            var allObjects = CollectAllGameObjectsInScene(scene);
            Logger.LogInfo($"Found {allObjects.Count} objects in scene {sceneName}, need to apply {totalObjects} transforms");
            
            // Create lookup dictionaries for faster matching
            var objectsByPath = new Dictionary<string, GameObject>();
            var objectsByName = new Dictionary<string, List<GameObject>>();
            var objectsBySiblingPath = new Dictionary<string, GameObject>();
            var objectsByPathId = new Dictionary<string, GameObject>();
            var objectsByItemId = new Dictionary<string, GameObject>();
            
            foreach (var obj in allObjects)
            {
                try
                {
                    // By path
                    string path = GetFullPath(obj.transform);
                    objectsByPath[path] = obj;
                    
                    // By name
                    string name = obj.name;
                    if (!objectsByName.ContainsKey(name))
                        objectsByName[name] = new List<GameObject>();
                    objectsByName[name].Add(obj);
                    
                    // By sibling path
                    string siblingPath = GetSiblingIndicesPath(obj.transform);
                    objectsBySiblingPath[siblingPath] = obj;
                    
                    // By path ID and item ID
                    string pathId = GeneratePathID(obj.transform);
                    string itemId = GenerateItemID(obj.transform);
                    
                    objectsByPathId[pathId] = obj;
                    objectsByItemId[itemId] = obj;
                }
                catch
                {
                    // Skip problematic objects
                    continue;
                }
            }
            
            // Apply transforms - log detailed information about the process
            foreach (var entry in transformsDb[sceneName])
            {
                var data = entry.Value;
                
                // Skip destroyed objects
                if (data.IsDestroyed)
                {
                    Logger.LogInfo($"Skipping destroyed object: {data.ObjectPath}");
                    appliedCount++; // Count as applied
                    continue;
                }
                
                // Skip spawned objects (they'll be handled separately)
                if (data.IsSpawned)
                {
                    Logger.LogInfo($"Skipping spawned object: {data.ObjectPath} (will be handled separately)");
                    appliedCount++; // Count as applied
                    continue;
                }
                
                GameObject targetObj = null;
                string matchMethod = "none";
                
                // Try multiple methods to find the right object
                
                // Method 1: Try by PathID + ItemID combination (new, preferred method)
                if (!string.IsNullOrEmpty(data.PathID) && !string.IsNullOrEmpty(data.ItemID))
                {
                    // Try to find object with matching PathID
                    if (objectsByPathId.TryGetValue(data.PathID, out targetObj))
                    {
                        matchMethod = "path_id";
                    }
                    // If not found by PathID, try ItemID
                    else if (objectsByItemId.TryGetValue(data.ItemID, out targetObj))
                    {
                        matchMethod = "item_id";
                    }
                }
                
                // Method 2: Try by direct path
                if (targetObj == null && objectsByPath.TryGetValue(data.ObjectPath, out targetObj))
                {
                    matchMethod = "direct_path";
                }
                
                // Method 3: Try by hierarchy position (sibling indices)
                if (targetObj == null && !string.IsNullOrEmpty(data.UniqueId))
                {
                    // Extract sibling indices from unique ID
                    string[] parts = data.UniqueId.Split('_');
                    if (parts.Length > 2)
                    {
                        string siblingIndices = parts[parts.Length - 1];
                        if (objectsBySiblingPath.TryGetValue(siblingIndices, out targetObj))
                        {
                            matchMethod = "sibling_indices";
                        }
                    }
                }
                
                // Method 4: Try by name
                if (targetObj == null && !string.IsNullOrEmpty(data.ObjectName) && objectsByName.TryGetValue(data.ObjectName, out var nameMatches))
                {
                    // If multiple objects have this name, try to find the best match
                    if (nameMatches.Count == 1)
                    {
                        targetObj = nameMatches[0];
                        matchMethod = "unique_name";
                    }
                    else if (!string.IsNullOrEmpty(data.ParentPath))
                    {
                        // Try to match by parent path
                        foreach (var obj in nameMatches)
                        {
                            if (obj.transform.parent != null && GetFullPath(obj.transform.parent) == data.ParentPath)
                            {
                                targetObj = obj;
                                matchMethod = "parent_path";
                                break;
                            }
                        }
                    }
                }
                
                // Method 5: Try by GameObject.Find
                if (targetObj == null && !string.IsNullOrEmpty(data.ObjectPath))
                {
                    targetObj = FindObjectByPath(data.ObjectPath);
                    if (targetObj != null)
                    {
                        matchMethod = "gameobject_find";
                    }
                }
                
                // Apply transform if we found a match
                if (targetObj != null)
                {
                    // Add or update tag component with PathID and ItemID
                    TransformCacherTag tag = targetObj.GetComponent<TransformCacherTag>();
                    if (tag == null)
                    {
                        tag = targetObj.AddComponent<TransformCacherTag>();
                    }
                    
                    tag.PathID = data.PathID;
                    tag.ItemID = data.ItemID;
                    
                    // Disable components that might interfere with transform changes
                    bool disabledPhysics = false;
                    bool disabledAnimation = false;
                    
                    // Try to disable Rigidbody if present
                    Rigidbody rb = targetObj.GetComponent<Rigidbody>();
                    if (rb != null)
                    {
                        rb.isKinematic = true;
                        disabledPhysics = true;
                    }
                    
                    // Try to disable animator if present
                    Animator animator = targetObj.GetComponent<Animator>();
                    if (animator != null && animator.enabled)
                    {
                        animator.enabled = false;
                        disabledAnimation = true;
                    }
                    
                    // Explicitly force the transform changes by setting both local and world properties
                    Transform transform = targetObj.transform;
                    
                    // Store original values for debugging
                    Vector3 origPos = transform.position;
                    
                    // Apply new values
                    transform.position = data.Position;
                    transform.rotation = Quaternion.Euler(data.Rotation);
                    transform.localScale = data.Scale;
                    
                    // Force update by applying a small change and then reverting back
                    transform.position += Vector3.one * 0.00001f;
                    transform.position = data.Position;
                    
                    // Double-check the values were applied
                    bool positionApplied = Vector3.Distance(transform.position, data.Position) < 0.01f;
                    bool rotationApplied = Quaternion.Angle(transform.rotation, Quaternion.Euler(data.Rotation)) < 0.01f;
                    bool scaleApplied = Vector3.Distance(transform.localScale, data.Scale) < 0.01f;
                    
                    if (positionApplied && rotationApplied && scaleApplied)
                    {
                        appliedCount++;
                        Logger.LogInfo($"Applied transform to {targetObj.name} (method: {matchMethod}) - " +
                                        $"Pos: {data.Position}, Rot: {data.Rotation}, Scale: {data.Scale}");
                        
                        // Log before/after positions for debugging
                        Logger.LogInfo($"Transform for {targetObj.name} - Before: {origPos}, After: {transform.position}");
                    }
                    else
                    {
                        Logger.LogWarning($"Transform application failed for {targetObj.name} - " +
                                          $"Pos ok: {positionApplied}, Rot ok: {rotationApplied}, Scale ok: {scaleApplied}");
                    }
                    
                    // Re-enable components we disabled
                    if (disabledPhysics && rb != null)
                    {
                        rb.isKinematic = false;
                    }
                    
                    if (disabledAnimation && animator != null)
                    {
                        animator.enabled = true;
                    }
                }
                else
                {
                    Logger.LogWarning($"Could not find object with path: {data.ObjectPath}, name: {data.ObjectName}, PathID: {data.PathID}, ItemID: {data.ItemID}");
                }
                
                // Yield every few objects to avoid freezing
                if (appliedCount % 10 == 0)
                    yield return null;
            }
            
            _isApplyingTransform = false;
            Logger.LogInfo($"Applied {appliedCount}/{totalObjects} transforms in scene {sceneName}");
            
            // Return success if we applied all transforms
            callback(appliedCount == totalObjects);
        }

        // Collect all GameObjects in a scene including inactive ones
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
        
        // Helper to collect all children recursively
        private void CollectChildrenRecursively(Transform parent, List<GameObject> results)
        {
            foreach (Transform child in parent)
            {
                results.Add(child.gameObject);
                CollectChildrenRecursively(child, results);
            }
        }

        private GameObject FindObjectByPath(string fullPath)
        {
            try 
            {
                var segments = fullPath.Split('/');
                
                // Try direct lookup first
                var directObj = GameObject.Find(fullPath);
                if (directObj != null)
                    return directObj;
                
                // Try finding just the root and then traversing
                var rootObj = GameObject.Find(segments[0]);
                if (rootObj == null) return null;
                
                var currentTransform = rootObj.transform;
                for (int i = 1; i < segments.Length; i++)
                {
                    // Try case sensitive find first
                    Transform nextTransform = null;
                    foreach (Transform child in currentTransform)
                    {
                        if (child.name == segments[i])
                        {
                            nextTransform = child;
                            break;
                        }
                    }
                    
                    // If not found, try case insensitive
                    if (nextTransform == null)
                    {
                        foreach (Transform child in currentTransform)
                        {
                            if (child.name.Equals(segments[i], StringComparison.OrdinalIgnoreCase))
                            {
                                nextTransform = child;
                                break;
                            }
                        }
                    }
                    
                    // If still not found, return null
                    if (nextTransform == null)
                        return null;
                    
                    currentTransform = nextTransform;
                }
                
                return currentTransform.gameObject;
            }
            catch (Exception ex)
            {
                Logger.LogError($"Error in FindObjectByPath: {ex.Message}");
                return null;
            }
        }

        // Debug GUI
        private void OnGUI()
        {
            if (!EnableDebugGUI.Value) return;
            
            // Main window
            _windowRect = GUI.Window(0, _windowRect, DrawMainWindow, "Transform Cacher");
            
            // Prefab selector window
            if (_showPrefabSelector)
            {
                Rect selectorRect = new Rect(_windowRect.x + _windowRect.width + 10, _windowRect.y, 500, 500);
                GUI.Window(1, selectorRect, DrawPrefabSelector, "Prefab Selector");
            }
        }
        
        private void DrawMainWindow(int id)
        {
            try
            {
                // Begin scrollable area - adjust if resizing
                _mainWindowScrollPosition = GUILayout.BeginScrollView(_mainWindowScrollPosition);
                
                GUILayout.Label("Object Actions", GUI.skin.box);
                
                // Tagging and saving
                if (GUILayout.Button("Save All Tagged Objects", GUILayout.Height(30)))
                {
                    SaveAllTaggedObjects();
                }

                if (GUILayout.Button("Tag Inspected Object", GUILayout.Height(30)))
                {
                    if (_currentInspectedObject != null)
                        TagObject(_currentInspectedObject);
                    else
                        Logger.LogInfo("No object currently inspected");
                }
                
                if (GUILayout.Button("Destroy Inspected Object", GUILayout.Height(30)))
                {
                    if (_currentInspectedObject != null)
                        MarkForDestruction(_currentInspectedObject);
                    else
                        Logger.LogInfo("No object currently inspected");
                }
                
                if (GUILayout.Button("Open Prefab Selector", GUILayout.Height(30)))
                {
                    _showPrefabSelector = !_showPrefabSelector;
                    if (_showPrefabSelector && !_prefabsLoaded)
                    {
                        StartCoroutine(LoadPrefabs());
                    }
                }
                
                if (GUILayout.Button("Force Apply Transforms", GUILayout.Height(30)))
                {
                    Scene currentScene = SceneManager.GetActiveScene();
                    _transformApplicationAttempts = 0;
                    StartCoroutine(ApplyTransformsWithRetry(currentScene));
                }
                
                if (_idBaker != null && GUILayout.Button("Open ID Baker", GUILayout.Height(30)))
                {
                    _idBaker.ToggleBakerWindow();
                }
                
                GUILayout.Space(20);
                
                // Information section
                GUILayout.Label("Information", GUI.skin.box);
                
                // Add null checks for all config entries
                GUILayout.Label($"Save Hotkey: {(SaveHotkey != null ? SaveHotkey.Value.ToString() : "N/A")}");
                GUILayout.Label($"Tag Hotkey: {(TagHotkey != null ? TagHotkey.Value.ToString() : "N/A")}");
                GUILayout.Label($"Destroy Hotkey: {(DestroyHotkey != null ? DestroyHotkey.Value.ToString() : "N/A")}");
                GUILayout.Label($"Spawn Hotkey: {(SpawnHotkey != null ? SpawnHotkey.Value.ToString() : "N/A")}");
                GUILayout.Label($"Mouse Toggle Hotkey: {(MouseToggleHotkey != null ? MouseToggleHotkey.Value.ToString() : "N/A")}");
                GUILayout.Label($"Current Scene: {_currentScene ?? "Unknown"}");
                GUILayout.Label($"Mouse Focus: {(_uiHasFocus ? "UI" : "Game")}");
                
                GUILayout.Space(10);
                
                if (_currentInspectedObject != null)
                {
                    string uniqueId = GenerateUniqueId(_currentInspectedObject.transform);
                    string pathId = GeneratePathID(_currentInspectedObject.transform);
                    string itemId = GenerateItemID(_currentInspectedObject.transform);
                    
                    GUILayout.Label($"Selected: {_currentInspectedObject.name}");
                    GUILayout.Label($"UniqueId: {uniqueId}");
                    GUILayout.Label($"PathID: {pathId}");
                    GUILayout.Label($"ItemID: {itemId}");
                    
                    // Display its transform
                    GUILayout.Label($"Position: {_currentInspectedObject.transform.position}");
                    GUILayout.Label($"Rotation: {_currentInspectedObject.transform.eulerAngles}");
                    GUILayout.Label($"Scale: {_currentInspectedObject.transform.localScale}");
                    
                    // Check if it's tagged
                    TransformCacherTag tag = _currentInspectedObject.GetComponent<TransformCacherTag>();
                    if (tag != null)
                    {
                        GUILayout.Label($"Tagged: YES");
                        GUILayout.Label($"Is Destroyed: {tag.IsDestroyed}");
                        GUILayout.Label($"Is Spawned: {tag.IsSpawned}");
                    }
                    else
                    {
                        GUILayout.Label($"Tagged: NO");
                    }
                }
                else
                {
                    GUILayout.Label("No object currently selected");
                }
                
                GUILayout.Space(20);
                
                // Database stats
                GUILayout.Label("Database Statistics", GUI.skin.box);
                
                // Get the transforms database
                var transformsDb = _databaseManager.GetTransformsDatabase();
                
                int totalObjects = 0;
                int totalDestroyed = 0;
                int totalSpawned = 0;
                
                if (transformsDb != null)
                {
                    foreach (var scene in transformsDb.Keys)
                    {
                        if (transformsDb[scene] != null)
                        {
                            int objectCount = transformsDb[scene].Count;
                            int destroyedCount = transformsDb[scene].Values.Count(data => data != null && data.IsDestroyed);
                            int spawnedCount = transformsDb[scene].Values.Count(data => data != null && data.IsSpawned);
                            
                            GUILayout.Label($"Scene '{scene}': {objectCount} objects ({destroyedCount} destroyed, {spawnedCount} spawned)");
                            
                            totalObjects += objectCount;
                            totalDestroyed += destroyedCount;
                            totalSpawned += spawnedCount;
                        }
                    }
                }
                
                GUILayout.Label($"Total: {totalObjects} objects ({totalDestroyed} destroyed, {totalSpawned} spawned)");
                
                GUILayout.EndScrollView();
                
                // Draw resize handle in the bottom-right corner
                _resizeHandle = new Rect(_windowRect.width - 15, _windowRect.height - 15, 15, 15);
                GUI.Box(_resizeHandle, "↘");
                
                // Allow the window to be dragged (but only from the top bar)
                GUI.DragWindow(new Rect(0, 0, _windowRect.width, 20));
                
                // Handle resize
                HandleResizing();
            }
            catch (Exception ex)
            {
                Logger.LogError($"Error in DrawMainWindow: {ex.Message}\n{ex.StackTrace}");
            }
        }
        
        // Add this method for window resizing
        private void HandleResizing()
        {
            var currentEvent = Event.current;
            
            switch (currentEvent.type)
            {
                case EventType.MouseDown:
                    if (_resizeHandle.Contains(currentEvent.mousePosition))
                    {
                        _isResizing = true;
                        _startResizeSize = new Vector2(_windowRect.width, _windowRect.height);
                        _startResizeMousePos = currentEvent.mousePosition;
                        currentEvent.Use();
                    }
                    break;
                    
                case EventType.MouseUp:
                    _isResizing = false;
                    break;
                    
                case EventType.MouseDrag:
                    if (_isResizing)
                    {
                        // Calculate new size based on mouse movement
                        Vector2 difference = currentEvent.mousePosition - _startResizeMousePos;
                        _windowRect.width = Mathf.Max(_minWindowSize.x, _startResizeSize.x + difference.x);
                        _windowRect.height = Mathf.Max(_minWindowSize.y, _startResizeSize.y + difference.y);
                        currentEvent.Use();
                    }
                    break;
            }
        }
        
        private void DrawPrefabSelector(int id)
        {
            // Same implementation as before
            GUILayout.BeginVertical();
            
            // Search field
            GUILayout.BeginHorizontal();
            GUILayout.Label("Search:", GUILayout.Width(60));
            _prefabSearchText = GUILayout.TextField(_prefabSearchText, GUILayout.Width(300));
            if (GUILayout.Button("Clear", GUILayout.Width(60)))
            {
                _prefabSearchText = "";
            }
            GUILayout.EndHorizontal();
            
            // Loading indicator
            if (!_prefabsLoaded)
            {
                GUILayout.Box("Loading prefabs, please wait...");
            }
            else
            {
                // Category selector
                GUILayout.BeginHorizontal();
                GUILayout.Label("Category:", GUILayout.Width(60));
                
                if (GUILayout.Button(_selectedCategory, GUILayout.Width(150)))
                {
                    // Toggle dropdown
                    _showCategoryDropdown = !_showCategoryDropdown;
                }
                
                GUILayout.EndHorizontal();
                
                // Category dropdown
                if (_showCategoryDropdown)
                {
                    Rect dropdownRect = GUILayoutUtility.GetLastRect();
                    dropdownRect.y += dropdownRect.height;
                    dropdownRect.width = 210;
                    dropdownRect.height = Mathf.Min(_categoryNames.Count * 25, 200);
                    
                    GUILayout.BeginArea(dropdownRect, GUI.skin.box);
                    _categoryScrollPosition = GUILayout.BeginScrollView(_categoryScrollPosition, GUILayout.Height(dropdownRect.height - 10));
                    
                    foreach (var category in _categoryNames)
                    {
                        int count = _prefabCategories[category].Count;
                        if (GUILayout.Button($"{category} ({count})"))
                        {
                            _selectedCategory = category;
                            _showCategoryDropdown = false;
                        }
                    }
                    
                    GUILayout.EndScrollView();
                    GUILayout.EndArea();
                }
                
                // Get filtered prefabs
                List<GameObject> filteredPrefabs;
                
                if (!_prefabCategories.ContainsKey(_selectedCategory))
                {
                    _selectedCategory = "All";
                }
                
                if (string.IsNullOrEmpty(_prefabSearchText))
                {
                    // Just filter by category
                    filteredPrefabs = _prefabCategories[_selectedCategory];
                }
                else
                {
                    // Filter by search and category
                    filteredPrefabs = _prefabCategories[_selectedCategory]
                        .Where(p => p != null && p.name.IndexOf(_prefabSearchText, StringComparison.OrdinalIgnoreCase) >= 0)
                        .ToList();
                }
                
                GUILayout.Label($"Found {filteredPrefabs.Count} prefabs in '{_selectedCategory}'{(!string.IsNullOrEmpty(_prefabSearchText) ? $" matching '{_prefabSearchText}'" : "")}");
                
                // Scrollable list
                _prefabScrollPosition = GUILayout.BeginScrollView(_prefabScrollPosition, GUILayout.Height(350));
                
                foreach (var prefab in filteredPrefabs)
                {
                    if (prefab == null) continue;
                    
                    bool isSelected = _selectedPrefab == prefab;
                    GUI.color = isSelected ? Color.green : Color.white;
                    
                    if (GUILayout.Button(prefab.name, GUILayout.Height(30)))
                    {
                        _selectedPrefab = prefab;
                    }
                    
                    GUI.color = Color.white;
                }
                
                GUILayout.EndScrollView();
                
                // Spawn button
                GUI.enabled = _selectedPrefab != null;
                if (GUILayout.Button("Spawn Selected Object", GUILayout.Height(40)))
                {
                    SpawnObject(_selectedPrefab);
                }
                GUI.enabled = true;
                
                // Close button
                if (GUILayout.Button("Close", GUILayout.Height(30)))
                {
                    _showPrefabSelector = false;
                }
            }
            
            GUILayout.EndVertical();
            
            // Allow the window to be dragged
            GUI.DragWindow();
        }
    }
}