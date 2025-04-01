using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TransformCacher
{
    public partial class TransformCacherTag : MonoBehaviour
    {
        public string UniqueId { get; private set; }
        public string PathID { get; set; }
        public string ItemID { get; set; }

        public bool IsSpawned { get; set; } = false;
        public bool IsDestroyed { get; set; } = false;
        
        public void Awake()
        {
            // Generate a unique ID based on hierarchical path and other properties
            UniqueId = FixUtility.GenerateUniqueId(transform);
            
            // Generate PathID and ItemID if they don't already exist
            if (string.IsNullOrEmpty(PathID))
            {
                PathID = FixUtility.GeneratePathID(transform);
            }
            
            if (string.IsNullOrEmpty(ItemID))
            {
                ItemID = FixUtility.GenerateItemID(transform);
            }
            
            Debug.Log($"[TransformCacher] Tag active on: {gameObject.name} with ID: {UniqueId}, PathID: {PathID}, ItemID: {ItemID}");
        }
    }

    public partial class TransformCacher : MonoBehaviour
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
        private bool _prefabsLoaded = false;
        
        // For categorized prefabs
        private Dictionary<string, List<GameObject>> _prefabCategories = new Dictionary<string, List<GameObject>>();
        private List<string> _categoryNames = new List<string>();
        private string _selectedCategory = "All";
        
        // GUI reference
        private TransformCacherGUI _gui;
        
        // Logging
        private static BepInEx.Logging.ManualLogSource Logger;

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            LoadingNotification loadingNotification = LoadingNotification.Instance;
            loadingNotification.Initialize();
                        
            _instance = this;
            DontDestroyOnLoad(gameObject);
            
            // Get logger from plugin
            Logger = BepInEx.Logging.Logger.CreateLogSource("TransformCacher");

            // Initialize database manager
            _databaseManager = DatabaseManager.Instance;
            _databaseManager.Initialize();

            // Initialize ID Baker
            _idBaker = gameObject.AddComponent<TransformIdBaker>();
            _idBaker.Initialize();
            
            // Initialize GUI
            _gui = gameObject.AddComponent<TransformCacherGUI>();
            _gui.Initialize(this, _databaseManager, _idBaker);
            
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

        // Public methods for GUI access
        public GameObject GetCurrentInspectedObject() => _currentInspectedObject;
        public bool ArePrefabsLoaded() => _prefabsLoaded;
        public string GetCurrentScene() => _currentScene;
        public void ResetTransformApplicationAttempts() => _transformApplicationAttempts = 0;
        
        public List<string> GetCategoryNames() => _categoryNames;
        
        public int GetPrefabCountForCategory(string category)
        {
            if (_prefabCategories.ContainsKey(category))
                return _prefabCategories[category].Count;
            return 0;
        }
        
        public void SetSelectedCategory(string category)
        {
            _selectedCategory = category;
        }
        
        public List<GameObject> GetFilteredPrefabs(string searchText)
        {
            if (!_prefabsLoaded || _prefabCategories == null)
                return new List<GameObject>();
                
            // Get prefabs from the selected category
            List<GameObject> categoryPrefabs = new List<GameObject>();
            if (_prefabCategories.ContainsKey(_selectedCategory))
            {
                categoryPrefabs = _prefabCategories[_selectedCategory];
            }
            else if (_prefabCategories.ContainsKey("All"))
            {
                categoryPrefabs = _prefabCategories["All"];
            }
            else
            {
                categoryPrefabs = _availablePrefabs;
            }
            
            // If no search text, return all from category
            if (string.IsNullOrWhiteSpace(searchText))
                return categoryPrefabs;
                
            // Filter by search text
            return categoryPrefabs.Where(p => 
                p != null && 
                p.name.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0
            ).ToList();
        }

        internal IEnumerator LoadPrefabs()
        {
            yield return new WaitForSeconds(3f); // Wait for game to fully load
            
            Logger.LogInfo("Starting to load prefabs...");
            
            // Clear existing data
            _availablePrefabs.Clear();
            _prefabCategories.Clear();
            _categoryNames.Clear();
            
            // Initialize categories
            InitializeCategories();
            
            int maxPrefabsToLoad = 10000; // Increased from 5000 to find more objects
            int prefabsProcessed = 0;
            int prefabsAdded = 0;
            
            // First collect all GameObjects in the scene hierarchy
            List<GameObject> allSceneObjects = new List<GameObject>();
            
            // Get all currently loaded scenes
            for (int sceneIndex = 0; sceneIndex < SceneManager.sceneCount; sceneIndex++)
            {
                Scene scene = SceneManager.GetSceneAt(sceneIndex);
                if (scene.isLoaded)
                {
                    Logger.LogInfo($"Gathering objects from scene: {scene.name}");
                    GameObject[] rootObjects = scene.GetRootGameObjects();
                    
                    // Process root objects and their hierarchies using CollectionHelper
                    foreach (GameObject root in rootObjects)
                    {
                        CollectGameObjectsRecursively(root, allSceneObjects);
                        
                        // Yield occasionally to avoid freezing
                        if (allSceneObjects.Count % 500 == 0)
                        {
                            yield return null;
                        }
                    }
                }
            }
            
            Logger.LogInfo($"Found {allSceneObjects.Count} total GameObjects in scenes");
            
            // Then find all prefabs from Resources
            GameObject[] resourceObjects = null;
            try {
                resourceObjects = Resources.FindObjectsOfTypeAll<GameObject>();
                Logger.LogInfo($"Found {resourceObjects.Length} GameObjects from Resources");
            }
            catch (Exception ex) {
                Logger.LogError($"Error finding GameObjects from Resources: {ex.Message}");
                resourceObjects = new GameObject[0];
            }
            
            // Combine both collections
            HashSet<GameObject> allPotentialPrefabs = new HashSet<GameObject>(allSceneObjects);
            foreach (var obj in resourceObjects)
            {
                if (obj != null && !allPotentialPrefabs.Contains(obj))
                {
                    allPotentialPrefabs.Add(obj);
                }
            }
            
            Logger.LogInfo($"Processing {allPotentialPrefabs.Count} total unique GameObjects");
            
            // Process all potential prefabs
            foreach (var obj in allPotentialPrefabs)
            {
                prefabsProcessed++;
                
                // Check if this object should be added as a prefab (less restrictive criteria)
                bool shouldAdd = ShouldAddAsPrefabImproved(obj);
                
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

        private void CollectGameObjectsRecursively(GameObject obj, List<GameObject> collection)
        {
            if (obj == null) return;
            
            // Add the current object to the collection
            collection.Add(obj);
            
            // Process all children
            foreach (Transform child in obj.transform)
            {
                CollectGameObjectsRecursively(child.gameObject, collection);
            }
        }

        // Helper method to collect all GameObjects in a hierarchy recursively
        private void CollectChildrenRecursively(Transform parent, List<GameObject> results)
        {
            foreach (Transform child in parent)
            {
                results.Add(child.gameObject);
                CollectChildrenRecursively(child, results);
            }
        }

        // Less restrictive ShouldAddAsPrefab method
        private bool ShouldAddAsPrefabImproved(GameObject obj)
        {
            if (obj == null) return false;
            
            try
            {
                // Skip certain utility objects we definitely don't want
                if (obj.name.Contains("UnityExplorer") || 
                    obj.name.Contains("BepInEx") ||
                    obj.name.StartsWith("TEMP_") ||
                    obj.name.StartsWith("tmp_") ||
                    obj.name.Contains("Canvas") && obj.GetComponent<Canvas>() != null)
                    return false;
                
                // Skip camera, light, and pure utility objects - but allow their parents/containers
                if ((obj.GetComponent<Camera>() != null && obj.transform.childCount == 0) || 
                    (obj.GetComponent<Light>() != null && obj.transform.childCount == 0) ||
                    (obj.GetComponent<Canvas>() != null && obj.transform.childCount == 0))
                    return false;
                    
                // Check for visual components or colliders - either direct or in children
                bool hasRenderer = obj.GetComponent<Renderer>() != null;
                bool hasCollider = obj.GetComponent<Collider>() != null;
                bool hasImportantComponent = obj.GetComponent<Light>() != null ||
                                            obj.GetComponent<ParticleSystem>() != null ||
                                            obj.name.Contains("Effect") ||
                                            obj.name.Contains("Spotlight") ||
                                            obj.name.Contains("light");
                        
                if (!hasRenderer && !hasCollider && !hasImportantComponent)
                {
                    // Check for components in immediate children before rejecting
                    bool hasChildWithImportantComponent = false;
                    foreach (Transform child in obj.transform)
                    {
                        if (child.GetComponent<Renderer>() != null || 
                            child.GetComponent<Collider>() != null ||
                            child.GetComponent<Light>() != null ||
                            child.GetComponent<ParticleSystem>() != null ||
                            child.name.Contains("Effect") ||
                            child.name.Contains("Spotlight") ||
                            child.name.Contains("light"))
                        {
                            hasChildWithImportantComponent = true;
                            break;
                        }
                    }
                    
                    // If neither the object nor its immediate children have important components, skip
                    if (!hasChildWithImportantComponent)
                        return false;
                }
                
                // Always include objects with specific keywords in their paths
                string path = FixUtility.GetFullPath(obj.transform).ToLower();
                if (path.Contains("workbench") || 
                    path.Contains("highlight") || 
                    path.Contains("laptop") || 
                    path.Contains("light") ||
                    path.Contains("effect") ||
                    path.Contains("model") ||
                    path.Contains("prop") ||
                    path.Contains("furniture") ||
                    path.Contains("container") ||
                    path.Contains("item") ||
                    path.Contains("weapon") ||
                    path.Contains("loot"))
                {
                    return true;
                }
                
                // For other objects, include them if they meet basic criteria
                return hasRenderer || hasCollider || hasImportantComponent || obj.transform.childCount > 0;
            }
            catch (Exception)
            {
                return false;
            }
        }

        // Rest of the methods remain unchanged...
        
        private bool ShouldAddAsPrefab(GameObject obj)
        {
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
                string path = FixUtility.GetFullPath(obj.transform);
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
            if (obj == null) return "Misc";
            
            string objName = obj.name.ToLower();
            string path = FixUtility.GetFullPath(obj.transform).ToLower();
            
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
                    
                    if (TransformCacherPlugin.EnablePersistence.Value && !string.IsNullOrEmpty(_currentScene))
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
            // Unsubscribe from scene load events
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        private void CheckForInspectedObject()
        {
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
                                
                                string uniqueId = FixUtility.GenerateUniqueId(_currentInspectedObject.transform);
                                Logger.LogInfo($"Now inspecting: {_currentInspectedObject.name} with Unique ID: {uniqueId}");
                                
                                // Add highlight to current object if enabled
                                if (TransformCacherPlugin.EnableObjectHighlight.Value)
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
                // Only check the hotkeys that are still needed
                if (TransformCacherPlugin.SaveHotkey != null && TransformCacherPlugin.SaveHotkey.Value.IsDown())
                {
                    SaveAllTaggedObjects();
                }
                
                if (TransformCacherPlugin.TagHotkey != null && TransformCacherPlugin.TagHotkey.Value.IsDown() && _currentInspectedObject != null)
                {
                    TagObject(_currentInspectedObject);
                }
                
                if (TransformCacherPlugin.DestroyHotkey != null && TransformCacherPlugin.DestroyHotkey.Value.IsDown() && _currentInspectedObject != null)
                {
                    MarkForDestruction(_currentInspectedObject);
                }
                
                if (TransformCacherPlugin.SpawnHotkey != null && TransformCacherPlugin.SpawnHotkey.Value.IsDown())
                {
                    // This is now handled by TransformCacherGUI
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

        // Tag an object to be tracked
        public void TagObject(GameObject obj)
        {
            if (obj == null) return;
            
            TransformCacherTag tag = obj.GetComponent<TransformCacherTag>();
            if (tag == null)
            {
                tag = obj.AddComponent<TransformCacherTag>();
                
                // Generate PathID and ItemID
                tag.PathID = FixUtility.GeneratePathID(obj.transform);
                tag.ItemID = FixUtility.GenerateItemID(obj.transform);
                
                string uniqueId = FixUtility.GenerateUniqueId(obj.transform);
                Logger.LogInfo($"Tagged object: {FixUtility.GetFullPath(obj.transform)} with ID: {uniqueId}, PathID: {tag.PathID}, ItemID: {tag.ItemID}");
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
                string objectPath = FixUtility.GetFullPath(obj.transform);
                string uniqueId = FixUtility.GenerateUniqueId(obj.transform);
                
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
                    tag.PathID = FixUtility.GeneratePathID(obj.transform);
                    tag.ItemID = FixUtility.GenerateItemID(obj.transform);
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
                        ParentPath = obj.transform.parent != null ? FixUtility.GetFullPath(obj.transform.parent) : "",
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
                    string childPath = FixUtility.GetFullPath(child);
                    string childUniqueId = FixUtility.GenerateUniqueId(child);
                    
                    // Add to destroyed objects cache
                    _destroyedObjectsCache[sceneName].Add(childPath);
                    
                    // Get or add tag component
                    TransformCacherTag childTag = child.gameObject.GetComponent<TransformCacherTag>();
                    if (childTag == null)
                    {
                        childTag = child.gameObject.AddComponent<TransformCacherTag>();
                        childTag.PathID = FixUtility.GeneratePathID(child);
                        childTag.ItemID = FixUtility.GenerateItemID(child);
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
                            ParentPath = FixUtility.GetFullPath(child.parent),
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
                string childId = FixUtility.GenerateUniqueId(child);
                childrenIds.Add(childId);
            }
            
            return childrenIds;
        }
        
        // Spawn a new object from a prefab
        public void SpawnObject(GameObject prefab, string bundlePath = "")
        {
            if (prefab == null)
            {
                // Handle the case where prefab is null
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
                tag.PathID = FixUtility.GeneratePathID(spawnedObj.transform);
                tag.ItemID = FixUtility.GenerateItemID(spawnedObj.transform);
                
                // Save the transform data
                SaveTransform(spawnedObj);
                
                // Update database to mark as spawned
                string sceneName = spawnedObj.scene.name;
                string uniqueId = FixUtility.GenerateUniqueId(spawnedObj.transform);
                
                var transformsDb = _databaseManager.GetTransformsDatabase();
                if (transformsDb.ContainsKey(sceneName) && transformsDb[sceneName].ContainsKey(uniqueId))
                {
                    transformsDb[sceneName][uniqueId].IsSpawned = true;
                    
                    // If this is from a bundle, store a relative path
                    if (!string.IsNullOrEmpty(bundlePath))
                    {
                        // Convert to a relative path if it's an absolute path
                        string basePath = Path.Combine(Paths.PluginPath, "TransformCacher");
                        string relativePath = bundlePath;
                        
                        // If it's an absolute path, make it relative to the plugin path
                        if (Path.IsPathRooted(bundlePath) && bundlePath.Contains(basePath))
                        {
                            relativePath = bundlePath.Replace(basePath, "").TrimStart('\\', '/');
                        }
                        
                        // Store with bundle prefix
                        transformsDb[sceneName][uniqueId].PrefabPath = "bundle:" + relativePath;
                        Logger.LogInfo($"Storing relative bundle path in PrefabPath: bundle:{relativePath}");
                    }
                    else
                    {
                        // Otherwise just store the prefab name as before
                        transformsDb[sceneName][uniqueId].PrefabPath = prefab.name;
                    }
                    
                    _databaseManager.SetTransformsDatabase(transformsDb);
                    _databaseManager.SaveTransformsDatabase();
                }
                
                // Select the new object
                _currentInspectedObject = spawnedObj;
                _lastPosition = spawnedObj.transform.position;
                _lastLocalPosition = spawnedObj.transform.localPosition;
                _lastRotation = spawnedObj.transform.rotation;
                _lastScale = spawnedObj.transform.localScale;
                
                Logger.LogInfo($"Spawned object: {spawnedObj.name} at {spawnPos}");
            }
            catch (Exception ex)
            {
                Logger.LogError($"Error spawning object: {ex.Message}");
            }
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
                    uniqueId = FixUtility.GenerateUniqueId(tr);
                }
                
                // Ensure PathID and ItemID are set
                if (string.IsNullOrEmpty(tag.PathID))
                {
                    tag.PathID = FixUtility.GeneratePathID(tr);
                }
                
                if (string.IsNullOrEmpty(tag.ItemID))
                {
                    tag.ItemID = FixUtility.GenerateItemID(tr);
                }
                
                var data = new TransformData
                {
                    UniqueId = uniqueId,
                    PathID = tag.PathID,
                    ItemID = tag.ItemID,
                    ObjectPath = FixUtility.GetFullPath(tr),
                    ObjectName = tr.name,
                    SceneName = sceneName,
                    Position = tr.position,
                    Rotation = tr.eulerAngles,
                    Scale = tr.localScale,
                    ParentPath = tr.parent != null ? FixUtility.GetFullPath(tr.parent) : "",
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
                // Skip saving transforms for temporary objects
                if (obj.name == "InvisibleHighlighter")
                {
                    Logger.LogInfo($"Skipping saving transform for temporary object: {obj.name}");
                    return;
                }
                
                string sceneName = SceneManager.GetActiveScene().name;
                
                // Get the transforms database
                var transformsDb = _databaseManager.GetTransformsDatabase();
                
                if (!transformsDb.ContainsKey(sceneName))
                    transformsDb[sceneName] = new Dictionary<string, TransformData>();
                
                Transform transform = obj.transform;
                
                // Generate IDs
                string uniqueId = FixUtility.GenerateUniqueId(transform);
                string pathId = FixUtility.GeneratePathID(transform);
                string itemId = FixUtility.GenerateItemID(transform);
                
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
                    ObjectPath = FixUtility.GetFullPath(transform),
                    ObjectName = obj.name,
                    SceneName = sceneName,
                    Position = transform.position,
                    Rotation = transform.eulerAngles,
                    Scale = transform.localScale,
                    ParentPath = transform.parent != null ? FixUtility.GetFullPath(transform.parent) : "",
                    IsDestroyed = isDestroyed,
                    IsSpawned = isSpawned,
                    PrefabPath = prefabPath,
                    Children = GetChildrenIds(transform)
                };
                
                transformsDb[sceneName][uniqueId] = data;
                
                // Update the database in the manager
                _databaseManager.SetTransformsDatabase(transformsDb);
                
                // Also save transforms of all children recursively, but skip InvisibleHighlighter
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
                    // Skip InvisibleHighlighter objects
                    if (child.name == "InvisibleHighlighter")
                    {
                        Logger.LogInfo($"Skipping saving transform for temporary child object: {child.name}");
                        continue;
                    }
                    
                    GameObject childObj = child.gameObject;
                    
                    // Generate IDs
                    string childUniqueId = FixUtility.GenerateUniqueId(child);
                    string childPathId = FixUtility.GeneratePathID(child);
                    string childItemId = FixUtility.GenerateItemID(child);
                    
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
                        ObjectPath = FixUtility.GetFullPath(child),
                        ObjectName = childObj.name,
                        SceneName = sceneName,
                        Position = child.position,
                        Rotation = child.eulerAngles,
                        Scale = child.localScale,
                        ParentPath = FixUtility.GetFullPath(child.parent),
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

        // Apply saved transforms to objects in the scene - enhanced with retry logic
        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            _currentScene = scene.name;
            Logger.LogInfo($"Scene loaded event: {scene.name} (mode: {mode})");
            
            if (!TransformCacherPlugin.EnablePersistence.Value)
            {
                Logger.LogInfo("Transform persistence is disabled");
                return;
            }
            
            // Reset attempt counter for the new scene
            _transformApplicationAttempts = 0;
            
            // Start the transform application process with retries
            StartCoroutine(ApplyTransformsWithRetry(scene));
        }

        public IEnumerator ApplyTransformsWithRetry(Scene scene)
        {
            // Initialize loading notification
            LoadingNotification loadingNotification = LoadingNotification.Instance;
            
            // Wait for scene to properly initialize
            float initialDelay = TransformCacherPlugin.TransformDelay != null ? TransformCacherPlugin.TransformDelay.Value : 1.0f;
            Logger.LogInfo($"Waiting {initialDelay}s before applying transforms to {scene.name}");
            yield return new WaitForSeconds(initialDelay);
            
            int maxAttempts = TransformCacherPlugin.MaxRetries != null ? TransformCacherPlugin.MaxRetries.Value : 3;
            bool success = false;
            
            while (_transformApplicationAttempts < maxAttempts && !success)
            {
                _transformApplicationAttempts++;
                Logger.LogInfo($"Attempt {_transformApplicationAttempts}/{maxAttempts} to apply transforms to {scene.name}");
                
                // Run the coroutine and wait for it to complete
                loadingNotification.SetLoadingMessage("Moving Items");
                yield return StartCoroutine(ApplyTransformsToScene(scene, (result) => { success = result; }));
                
                if (success)
                {
                    Logger.LogInfo($"Successfully applied transforms to {scene.name} on attempt {_transformApplicationAttempts}");
                    
                    // Also handle destroyed objects
                    loadingNotification.SetLoadingMessage("Deleting Items");
                    yield return StartCoroutine(ApplyDestroyedObjects(scene.name));
                    
                    // Also respawn any spawned objects
                    loadingNotification.SetLoadingMessage("Generating Custom Assets");
                    yield return StartCoroutine(RespawnObjects(scene.name));
                    
                    // Run a final cleanup pass to catch any missed objects
                    loadingNotification.SetLoadingMessage("Final Cleanup");
                    yield return StartCoroutine(PerformFinalCleanup(scene));
                    
                    // Show any errors that occurred during loading
                    loadingNotification.ShowErrorPopupIfNeeded();
                    
                    // Clear the loading message
                    loadingNotification.SetLoadingMessage("");
                    
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
                    loadingNotification.AddError("Transform Application", $"Failed to apply all transforms after {maxAttempts} attempts");
                    loadingNotification.ShowErrorPopupIfNeeded();
                }
            }
        }
        
        private IEnumerator ApplyDestroyedObjects(string sceneName)
        {
            // Initialize notification for tracking
            LoadingNotification loadingNotification = LoadingNotification.Instance;
    
            Logger.LogInfo($"Applying destroyed objects for scene: {sceneName}");
            
            int hiddenObjects = 0;
            int failedObjects = 0;
            
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
                            tag.PathID = FixUtility.GeneratePathID(obj.transform);
                            tag.ItemID = FixUtility.GenerateItemID(obj.transform);
                            tag.IsDestroyed = true;
                        }
                        
                        hiddenObjects++;
                    }
                }
            }
            
            Logger.LogInfo($"Applied destruction to {hiddenObjects} objects in scene {sceneName}");
            
            loadingNotification.TrackProgress("Deleting Items", hiddenObjects, failedObjects);

            yield break;
        }
        
        private IEnumerator RespawnObjects(string sceneName)
        {
            // Initialize notification for tracking
            LoadingNotification loadingNotification = LoadingNotification.Instance;
            
            Logger.LogInfo($"Respawning objects for scene: {sceneName}");
            
            int respawnedCount = 0;
            int failedCount = 0;
            
            // Get the transforms database
            var transformsDb = _databaseManager.GetTransformsDatabase();
            
            // Wait for prefabs to be loaded
            if (!_prefabsLoaded)
            {
                loadingNotification.SetLoadingMessage("Generating Custom Assets - Loading prefabs...");
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
                    loadingNotification.SetLoadingMessage("Generating Custom Assets - Forcing prefab load...");
                    yield return StartCoroutine(LoadPrefabs());
                }
            }
            
            // Check database for spawned objects
            if (transformsDb.ContainsKey(sceneName))
            {
                int totalToSpawn = transformsDb[sceneName].Values.Count(data => data.IsSpawned && !data.IsDestroyed);
                loadingNotification.SetLoadingMessage($"Generating Custom Assets - Found {totalToSpawn} objects to spawn");
                
                foreach (var entry in transformsDb[sceneName].Values)
                {
                    if (entry.IsSpawned && !entry.IsDestroyed)
                    {
                        // Try to find matching prefab
                        GameObject prefab = null;
                        string prefabPath = entry.PrefabPath ?? "";
                        
                        try
                        {
                            // Check if this is a bundle path
                            if (prefabPath.StartsWith("bundle:"))
                            {
                                // Extract the relative bundle path from the PrefabPath
                                string relativeBundlePath = prefabPath.Substring(7); // Remove "bundle:" prefix
                                
                                // Convert to full path based on current plugin location
                                string basePath = Path.Combine(Paths.PluginPath, "TransformCacher");
                                string fullBundlePath = Path.Combine(basePath, relativeBundlePath);
                                
                                // Ensure the path is valid
                                if (File.Exists(fullBundlePath))
                                {
                                    Logger.LogInfo($"Loading from bundle: {fullBundlePath} (relative path: {relativeBundlePath})");
                                    
                                    // Try to load from the bundle
                                    AssetBundle bundle = AssetBundle.LoadFromFile(fullBundlePath);
                                    if (bundle != null)
                                    {
                                        try
                                        {
                                            // Get the first asset in the bundle
                                            string[] assetNames = bundle.GetAllAssetNames();
                                            if (assetNames.Length > 0)
                                            {
                                                prefab = bundle.LoadAsset<GameObject>(assetNames[0]);
                                                
                                                if (prefab == null)
                                                {
                                                    Logger.LogWarning($"Failed to load asset from bundle: {fullBundlePath}");
                                                    loadingNotification.AddError("Generating Custom Assets", $"Failed to load asset from bundle: {fullBundlePath}");
                                                }
                                                else
                                                {
                                                    Logger.LogInfo($"Successfully loaded asset from bundle: {fullBundlePath}");
                                                }
                                            }
                                            else
                                            {
                                                Logger.LogWarning($"No assets found in bundle: {fullBundlePath}");
                                                loadingNotification.AddError("Generating Custom Assets", $"No assets found in bundle: {fullBundlePath}");
                                            }
                                        }
                                        catch (Exception ex)
                                        {
                                            Logger.LogError($"Error loading asset from bundle: {ex.Message}");
                                            loadingNotification.AddError("Generating Custom Assets", $"Error loading asset from bundle: {ex.Message}");
                                        }
                                        finally
                                        {
                                            // Unload the bundle but keep the loaded assets
                                            bundle.Unload(false);
                                        }
                                    }
                                    else
                                    {
                                        Logger.LogWarning($"Failed to load bundle: {fullBundlePath}");
                                        loadingNotification.AddError("Generating Custom Assets", $"Failed to load bundle: {fullBundlePath}");
                                    }
                                }
                                else
                                {
                                    Logger.LogWarning($"Bundle file not found: {fullBundlePath}");
                                    loadingNotification.AddError("Generating Custom Assets", $"Bundle file not found: {fullBundlePath}");
                                }
                            }
                            // First try by name if not a bundle
                            else if (!string.IsNullOrEmpty(prefabPath))
                            {
                                prefab = _availablePrefabs.FirstOrDefault(p => p != null && p.name == prefabPath);
                            }
                            
                            // If not found, try to find by similar name
                            if (prefab == null && !string.IsNullOrEmpty(entry.ObjectName))
                            {
                                string baseName = entry.ObjectName.Replace("_spawned", "");
                                prefab = _availablePrefabs.FirstOrDefault(p => p != null && p.name == baseName);
                            }
                            
                            // Use a default if nothing found
                            if (prefab == null && _availablePrefabs.Count > 0)
                            {
                                prefab = _availablePrefabs[0];
                                Logger.LogWarning($"Couldn't find specific prefab for {entry.ObjectName}, using default");
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
                                    Logger.LogInfo($"Successfully respawned object: {entry.ObjectName}");
                                }
                                catch (Exception ex)
                                {
                                    failedCount++;
                                    Logger.LogError($"Error respawning object {entry.ObjectName}: {ex.Message}");
                                    loadingNotification.AddError("Generating Custom Assets", $"Failed to spawn {entry.ObjectName}: {ex.Message}");
                                }
                            }
                            else
                            {
                                failedCount++;
                                Logger.LogWarning($"No suitable prefab found for {entry.ObjectName}");
                                loadingNotification.AddError("Generating Custom Assets", $"No suitable prefab found for {entry.ObjectName}");
                            }
                        }
                        catch (Exception ex)
                        {
                            failedCount++;
                            Logger.LogError($"Error during respawn process for {entry.ObjectName}: {ex.Message}");
                            loadingNotification.AddError("Generating Custom Assets", $"Error during respawn process for {entry.ObjectName}: {ex.Message}");
                        }
                        
                        // Yield occasionally to prevent freezing
                        if ((respawnedCount + failedCount) % 5 == 0)
                        {
                            loadingNotification.SetLoadingMessage($"Generating Custom Assets - Processed {respawnedCount + failedCount}/{totalToSpawn}");
                            yield return null;
                        }
                    }
                }
            }
            
            Logger.LogInfo($"Respawned {respawnedCount} objects in scene {sceneName}, {failedCount} failed");
            
            // Track progress
            loadingNotification.TrackProgress("Generating Custom Assets", respawnedCount, failedCount);
            
            yield break;
        }

        private IEnumerator ApplyTransformsToScene(Scene scene, Action<bool> callback)
        {
            // Initialize notification for tracking
            LoadingNotification loadingNotification = LoadingNotification.Instance;
    
            // Wait until end of frame to ensure all objects are fully loaded
            yield return new WaitForEndOfFrame();
            
            // Declare variables outside the try block to avoid redeclaration issues
            string sceneName = null;
            Dictionary<string, Dictionary<string, TransformData>> transformsDb = null;
            int totalObjects = 0;
            bool shouldContinue = true;
            
            try
            {
                // Safe guard against null or empty scene name
                sceneName = scene.name;
                if (string.IsNullOrEmpty(sceneName))
                {
                    Logger.LogError("Scene name is null or empty, cannot apply transforms");
                    callback(false); // Failure
                    shouldContinue = false;
                }
                
                // Check if database manager is valid
                if (_databaseManager == null)
                {
                    Logger.LogError("DatabaseManager is null, cannot apply transforms");
                    callback(false); // Failure
                    shouldContinue = false;
                }
                
                // Get the transforms database with defensive null checking
                if (shouldContinue)
                {
                    transformsDb = _databaseManager.GetTransformsDatabase();
                    if (transformsDb == null)
                    {
                        Logger.LogError("Transforms database is null, cannot apply transforms");
                        callback(false); // Failure
                        shouldContinue = false;
                    }
                }
                
                // Check if we have transforms for this scene
                if (shouldContinue && (!transformsDb.ContainsKey(sceneName) || transformsDb[sceneName] == null || transformsDb[sceneName].Count == 0))
                {
                    Logger.LogInfo($"No saved transforms for scene: {sceneName}");
                    callback(true); // Success (nothing to do)
                    shouldContinue = false;
                }
                
                // Set total objects count if continuing
                if (shouldContinue)
                {
                    totalObjects = transformsDb[sceneName].Count;
                }
            }
            catch (Exception ex)
            {
                Logger.LogError($"Error initializing ApplyTransformsToScene: {ex.Message}\n{ex.StackTrace}");
                callback(false); // Failure
                shouldContinue = false;
            }
            
            // Exit early if initialization failed
            if (!shouldContinue)
            {
                yield break;
            }
            
            // Now we have verified sceneName, transformsDb, and totalObjects are valid
            int appliedCount = 0;
            int skippedCount = 0;
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
            
            // Build lookup dictionaries
            foreach (var obj in allObjects)
            {
                if (obj == null) continue;
                
                try
                {
                    // By path
                    string path = FixUtility.GetFullPath(obj.transform);
                    if (!string.IsNullOrEmpty(path))
                    {
                        objectsByPath[path] = obj;
                    }
                    
                    // By name
                    string name = obj.name;
                    if (!string.IsNullOrEmpty(name))
                    {
                        if (!objectsByName.ContainsKey(name))
                            objectsByName[name] = new List<GameObject>();
                        objectsByName[name].Add(obj);
                    }
                    
                    // By sibling path
                    string siblingPath = FixUtility.GetSiblingIndicesPath(obj.transform);
                    if (!string.IsNullOrEmpty(siblingPath))
                    {
                        objectsBySiblingPath[siblingPath] = obj;
                    }
                    
                    // By path ID and item ID
                    string pathId = FixUtility.GeneratePathID(obj.transform);
                    string itemId = FixUtility.GenerateItemID(obj.transform);
                    
                    if (!string.IsNullOrEmpty(pathId))
                    {
                        objectsByPathId[pathId] = obj;
                    }
                    
                    if (!string.IsNullOrEmpty(itemId))
                    {
                        objectsByItemId[itemId] = obj;
                    }
                }
                catch (Exception ex)
                {
                    // Skip problematic objects
                    Logger.LogWarning($"Error processing object for lookup: {ex.Message}");
                }
            }
            
            // Apply transforms - log detailed information about the process
            if (transformsDb[sceneName] != null)
            {
                // Apply transforms
                foreach (var entry in transformsDb[sceneName])
                {
                    if (entry.Key == null || entry.Value == null)
                    {
                        Logger.LogWarning("Skipping null database entry");
                        appliedCount++; // Count as applied to maintain proper counts
                        continue;
                    }
                    
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
                    
                    // Skip objects that are likely temporary (like InvisibleHighlighter)
                    if (data.ObjectPath != null && data.ObjectPath.Contains("InvisibleHighlighter"))
                    {
                        Logger.LogInfo($"Skipping temporary object: {data.ObjectPath}");
                        appliedCount++; // Count as applied
                        skippedCount++;
                        continue;
                    }
                    
                    try
                    {
                        GameObject targetObj = null;
                        string matchMethod = "none";
                        
                        // Method 1: Try with our enhanced path finding
                        if (targetObj == null && !string.IsNullOrEmpty(data.ObjectPath))
                        {
                            // First check our path map for a direct match
                            if (objectsByPath.TryGetValue(data.ObjectPath, out targetObj))
                            {
                                matchMethod = "direct_path_map";
                            }
                            // Then try case insensitive
                            else
                            {
                                foreach (var kvp in objectsByPath)
                                {
                                    if (kvp.Key.Equals(data.ObjectPath, StringComparison.OrdinalIgnoreCase))
                                    {
                                        targetObj = kvp.Value;
                                        matchMethod = "case_insensitive_path_map";
                                        break;
                                    }
                                }
                            }
                            
                            // If still not found, try our custom find method
                            if (targetObj == null)
                            {
                                targetObj = FindObjectByPath(data.ObjectPath);
                                if (targetObj != null)
                                {
                                    matchMethod = "custom_find";
                                }
                            }
                        }
                        
                        // Method 2: Try by PathID + ItemID combination
                        if (targetObj == null && !string.IsNullOrEmpty(data.PathID) && !string.IsNullOrEmpty(data.ItemID))
                        {
                            // Try to find object by PathID and ItemID
                            foreach (GameObject obj in allObjects)
                            {
                                if (obj == null) continue;
                                
                                string pathId = FixUtility.GeneratePathID(obj.transform);
                                string itemId = FixUtility.GenerateItemID(obj.transform);
                                
                                if (pathId == data.PathID || itemId == data.ItemID)
                                {
                                    targetObj = obj;
                                    matchMethod = pathId == data.PathID ? "path_id" : "item_id";
                                    break;
                                }
                            }
                        }
                        
                        // Method 3: Try by name
                        if (targetObj == null && !string.IsNullOrEmpty(data.ObjectName))
                        {
                            // First check our name map
                            if (objectsByName.TryGetValue(data.ObjectName, out var nameMatches) && nameMatches.Count > 0)
                            {
                                targetObj = nameMatches[0];
                                matchMethod = "direct_name_map";
                            }
                            
                            // If not found and parent path is known, try to find by name and parent
                            if (targetObj == null && !string.IsNullOrEmpty(data.ParentPath))
                            {
                                GameObject parentObj = null;
                                
                                if (objectsByPath.TryGetValue(data.ParentPath, out parentObj) ||
                                    (parentObj = FindObjectByPath(data.ParentPath)) != null)
                                {
                                    // Search in parent for child by name
                                    Transform childTrans = null;
                                    foreach (Transform child in parentObj.transform)
                                    {
                                        if (child.name == data.ObjectName || 
                                            child.name.Equals(data.ObjectName, StringComparison.OrdinalIgnoreCase))
                                        {
                                            childTrans = child;
                                            break;
                                        }
                                    }
                                    
                                    if (childTrans != null)
                                    {
                                        targetObj = childTrans.gameObject;
                                        matchMethod = "parent_path_then_name";
                                    }
                                }
                            }
                        }
                        
                        // Apply transform if we found a match
                        if (targetObj != null)
                        {
                            try
                            {
                                // Add or update tag component with PathID and ItemID
                                TransformCacherTag tag = targetObj.GetComponent<TransformCacherTag>();
                                if (tag == null)
                                {
                                    tag = targetObj.AddComponent<TransformCacherTag>();
                                }
                                
                                tag.PathID = data.PathID;
                                tag.ItemID = data.ItemID;
                                
                                // Apply transform
                                targetObj.transform.position = data.Position;
                                targetObj.transform.eulerAngles = data.Rotation;
                                targetObj.transform.localScale = data.Scale;
                                
                                appliedCount++;
                                Logger.LogInfo($"Applied transform to {targetObj.name} (method: {matchMethod}) - " +
                                            $"Pos: {data.Position}, Rot: {data.Rotation}, Scale: {data.Scale}");
                            }
                            catch (Exception ex)
                            {
                                Logger.LogError($"Error setting transform data on {targetObj.name}: {ex.Message}");
                                // Still count as applied
                                appliedCount++;
                            }
                        }
                        else
                        {
                            Logger.LogWarning($"Could not find object with path: {data.ObjectPath}, name: {data.ObjectName}, PathID: {data.PathID}, ItemID: {data.ItemID}");
                            // Still increment the counter to avoid retry loops with missing objects
                            skippedCount++;
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.LogError($"Error applying transform for entry {entry.Key}: {ex.Message}");
                        skippedCount++;
                    }
                    
                    // Yield every few objects to avoid freezing - OUTSIDE the try-catch block
                    if ((appliedCount + skippedCount) % 10 == 0)
                        yield return null;
                }
            }
            
            _isApplyingTransform = false;
            Logger.LogInfo($"Applied {appliedCount}/{totalObjects} transforms in scene {sceneName}");
            
            // Track progress and errors
            loadingNotification.TrackProgress("Moving Items", appliedCount, skippedCount);
            
            // Return success if we applied at least 80% of transforms or if we applied all that could be found
            bool success = (float)(appliedCount + skippedCount) / totalObjects >= 0.9f;
            callback(success);
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

        private IEnumerator PerformFinalCleanup(Scene scene)
        {
            // Initialize notification for tracking
            LoadingNotification loadingNotification = LoadingNotification.Instance;
            
            Logger.LogInfo($"Performing final cleanup for scene: {scene.name}");
            
            string sceneName = scene.name;
            int deletedItems = 0;
            int failedDeletions = 0;
            
            // Get the transforms database
            var transformsDb = _databaseManager.GetTransformsDatabase();
            if (transformsDb == null || !transformsDb.ContainsKey(sceneName))
            {
                loadingNotification.TrackProgress("Final Cleanup", 0, 0);
                yield break;
            }
            
            // Find all objects that should be destroyed
            var toDestroy = new List<GameObject>();
            
            // Check for objects marked as destroyed in the database but still active
            foreach (var entry in transformsDb[sceneName].Values)
            {
                if (entry.IsDestroyed)
                {
                    GameObject obj = FindObjectByPath(entry.ObjectPath);
                    if (obj != null && obj.activeInHierarchy)
                    {
                        toDestroy.Add(obj);
                    }
                }
            }
            
            // Try to destroy these objects
            foreach (var obj in toDestroy)
            {
                try
                {
                    obj.SetActive(false);
                    deletedItems++;
                }
                catch (Exception ex)
                {
                    Logger.LogError($"Failed to disable object {obj.name} during final cleanup: {ex.Message}");
                    failedDeletions++;
                    loadingNotification.AddError("Final Cleanup", $"Failed to disable {obj.name}: {ex.Message}");
                }
                
                // Yield occasionally to avoid freezing
                if (deletedItems % 20 == 0)
                {
                    yield return null;
                }
            }
            
            // Log results
            Logger.LogInfo($"Final cleanup: Deleted {deletedItems} missed objects, {failedDeletions} failed");
            
            // Track progress
            loadingNotification.TrackProgress("Final Cleanup", deletedItems, failedDeletions);
            
            yield break;
        }

        private GameObject FindObjectByPath(string fullPath)
        {
            if (string.IsNullOrEmpty(fullPath))
                return null;
            
            try 
            {
                var segments = fullPath.Split('/');
                if (segments.Length == 0)
                    return null;
                    
                // Try direct lookup first
                var directObj = GameObject.Find(fullPath);
                if (directObj != null)
                    return directObj;
                
                // First try to find just the root object - could be a spawned object
                var rootObj = GameObject.Find(segments[0]);
                if (rootObj != null)
                {
                    if (segments.Length == 1)
                        return rootObj;
                        
                    // Try to navigate down the hierarchy
                    var currentTransform = rootObj.transform;
                    for (int i = 1; i < segments.Length; i++)
                    {
                        Transform nextTransform = null;
                        bool found = false;
                        
                        // Skip "InvisibleHighlighter" - it's a dynamically created object
                        if (segments[i] == "InvisibleHighlighter")
                        {
                            Logger.LogInfo($"Skipping 'InvisibleHighlighter' segment in path, as it's dynamically created");
                            // Just return the parent instead
                            return currentTransform.gameObject;
                        }
                        
                        // Search through all children with both case-sensitive and case-insensitive matching
                        foreach (Transform child in currentTransform)
                        {
                            // First try exact match
                            if (child.name == segments[i])
                            {
                                nextTransform = child;
                                found = true;
                                break;
                            }
                            
                            // Then try case-insensitive match
                            if (!found && child.name.Equals(segments[i], StringComparison.OrdinalIgnoreCase))
                            {
                                nextTransform = child;
                                found = true;
                            }
                        }
                        
                        // If path segment wasn't found, return null
                        if (!found || nextTransform == null)
                        {
                            // Don't log a warning for InvisibleHighlighter as we know it's dynamically created
                            if (i < segments.Length - 1 || segments[i] != "InvisibleHighlighter")
                            {
                                Logger.LogWarning($"Could not find child '{segments[i]}' in '{currentTransform.name}'");
                            }
                            return currentTransform.gameObject; // Return parent instead of null for better recovery
                        }
                        
                        currentTransform = nextTransform;
                    }
                    
                    return currentTransform.gameObject;
                }
                
                // If still not found, try a more comprehensive search through all loaded scenes
                Logger.LogInfo($"Root object '{segments[0]}' not found directly, searching in all scenes...");
                
                for (int sceneIdx = 0; sceneIdx < SceneManager.sceneCount; sceneIdx++)
                {
                    Scene scene = SceneManager.GetSceneAt(sceneIdx);
                    if (!scene.isLoaded) continue;
                    
                    // Get all top-level objects in this scene
                    GameObject[] sceneRootObjects = scene.GetRootGameObjects();
                    
                    // First try to find the root by exact name
                    foreach (var sceneRoot in sceneRootObjects)
                    {
                        if (sceneRoot.name == segments[0])
                        {
                            // Found the root, now try to find the full path in this root
                            Transform rootTrans = sceneRoot.transform;
                            if (segments.Length == 1)
                                return rootTrans.gameObject;
                                
                            // Try to navigate hierarchy
                            Transform currTrans = rootTrans;
                            bool pathValid = true;
                            
                            for (int i = 1; i < segments.Length; i++)
                            {
                                // Skip InvisibleHighlighter segment
                                if (segments[i] == "InvisibleHighlighter")
                                {
                                    return currTrans.gameObject;
                                }
                                
                                Transform nextTrans = null;
                                bool segmentFound = false;
                                
                                foreach (Transform child in currTrans)
                                {
                                    if (child.name == segments[i] || child.name.Equals(segments[i], StringComparison.OrdinalIgnoreCase))
                                    {
                                        nextTrans = child;
                                        segmentFound = true;
                                        break;
                                    }
                                }
                                
                                if (!segmentFound)
                                {
                                    // If we've navigated most of the path but can't find the last segment,
                                    // return what we have so far rather than failing entirely
                                    if (i > segments.Length / 2)
                                    {
                                        Logger.LogInfo($"Couldn't find complete path but returning partial match up to '{currTrans.name}'");
                                        return currTrans.gameObject;
                                    }
                                    
                                    pathValid = false;
                                    break;
                                }
                                
                                currTrans = nextTrans;
                            }
                            
                            if (pathValid)
                                return currTrans.gameObject;
                        }
                    }
                    
                    // If not found by direct name match, try more aggressive approaches
                    // Search all objects in scene for matching paths or names
                    foreach (var sceneRoot in sceneRootObjects)
                    {
                        // Try to find by reconstructing path from this root
                        string rootPath = FixUtility.GetFullPath(sceneRoot.transform);
                        string targetRootPath = segments[0];
                        
                        // If paths don't match at all, continue
                        if (!rootPath.EndsWith(targetRootPath, StringComparison.OrdinalIgnoreCase) &&
                            !targetRootPath.EndsWith(rootPath, StringComparison.OrdinalIgnoreCase))
                            continue;
                            
                        // Try to find the rest of the path
                        if (segments.Length == 1)
                            return sceneRoot;
                            
                        // Try to find by navigating from this root
                        var result = TryFindInHierarchy(sceneRoot.transform, segments, 1);
                        if (result != null)
                            return result;
                    }
                }
                
                // If everything else failed, try a last-resort approach using recursion
                // This might be expensive but can find deeply nested objects
                for (int sceneIdx = 0; sceneIdx < SceneManager.sceneCount; sceneIdx++)
                {
                    Scene scene = SceneManager.GetSceneAt(sceneIdx);
                    if (!scene.isLoaded) continue;
                    
                    GameObject[] sceneRootObjects = scene.GetRootGameObjects();
                    
                    foreach (var root in sceneRootObjects)
                    {
                        // For the last segment, try to find it anywhere in this hierarchy
                        string lastSegment = segments[segments.Length - 1];
                        
                        // Skip searching for InvisibleHighlighter
                        if (lastSegment == "InvisibleHighlighter")
                        {
                            continue;
                        }
                        
                        var result = FindGameObjectByNameRecursive(root.transform, lastSegment);
                        if (result != null)
                        {
                            // Check if the whole path matches
                            string foundPath = FixUtility.GetFullPath(result.transform);
                            if (foundPath.EndsWith(fullPath, StringComparison.OrdinalIgnoreCase) ||
                                fullPath.EndsWith(foundPath, StringComparison.OrdinalIgnoreCase))
                            {
                                return result;
                            }
                            
                            // If just the name matches but not the full path, still return it
                            // since it might be the closest match
                            Logger.LogInfo($"Found object with matching name '{lastSegment}' but path doesn't match. Found: {foundPath}, Expected: {fullPath}");
                            return result;
                        }
                    }
                }
                
                return null;
            }
            catch (Exception ex)
            {
                Logger.LogError($"Error in FindObjectByPath for '{fullPath}': {ex.Message}");
                return null;
            }
        }

        // Helper method to find GameObjects by name recursively
        private GameObject FindGameObjectByNameRecursive(Transform parent, string name)
        {
            if (parent.name == name || parent.name.Equals(name, StringComparison.OrdinalIgnoreCase))
                return parent.gameObject;
            
            foreach (Transform child in parent)
            {
                GameObject result = FindGameObjectByNameRecursive(child, name);
                if (result != null)
                    return result;
            }
            
            return null;
        }

        // Helper method to try finding an object by traversing path segments
        private GameObject TryFindInHierarchy(Transform current, string[] segments, int startIndex)
        {
            if (startIndex >= segments.Length)
                return current.gameObject;
            
            foreach (Transform child in current)
            {
                if (child.name == segments[startIndex] || 
                    child.name.Equals(segments[startIndex], StringComparison.OrdinalIgnoreCase))
                {
                    return TryFindInHierarchy(child, segments, startIndex + 1);
                }
            }
            
            return null;
        }
    }
}