using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TransformCacher
{
    /// <summary>
    /// Dedicated class for TransformCacher GUI functionality
    /// Separates UI code from core functionality
    /// </summary>
    public class TransformCacherGUI : MonoBehaviour
    {
        // References to other components
        private TransformCacher _transformCacher;
        private DatabaseManager _databaseManager;
        private TransformIdBaker _idBaker;
        
        // UI State for main window
        private Rect _windowRect = new Rect(400, 100, 600, 500);
        private Vector2 _mainWindowScrollPosition = Vector2.zero;
        
        // Spawn Item selector state
        private bool _showSpawnItemSelector = false;
        private Vector2 _prefabScrollPosition = Vector2.zero;
        private Vector2 _categoryScrollPosition = Vector2.zero;
        private bool _showCategoryDropdown = false;
        private string _prefabSearchText = "";
        public GameObject _selectedPrefab = null; // Made public for access from TransformCacher
        private string _selectedCategory = "All";
        
        // Bundle loading state
        private bool _showBundleSelector = false;
        private bool _showBuiltInSelector = true;
        private List<string> _bundleFiles = new List<string>();
        private List<string> _bundleDirectories = new List<string>();
        private string _selectedBundle = null;
        private Dictionary<string, AssetBundle> _loadedBundles = new Dictionary<string, AssetBundle>();
        private List<GameObject> _bundleObjects = new List<GameObject>();
        private Vector2 _bundleScrollPosition = Vector2.zero;
        
        // For database scene selection
        private int _selectedSceneIndex = 0;
        
        // TransformIdBaker UI
        private bool _showBakerWindow = false;
        private Rect _bakerWindowRect = new Rect(400, 200, 500, 500);
        private Vector2 _bakerScrollPosition;
        private Vector2 _sceneScrollPosition;
        private string _ignorePrefix = "Weapon spawn";
        private List<string> _scenesToBake = new List<string>();
        private bool _bakeMultipleScenes = false;
        private bool _showBakerSettings = false;
        
        // Window resizing
        private Rect _resizeHandle = new Rect(0, 0, 10, 10);
        private bool _isResizing = false;
        private Vector2 _minWindowSize = new Vector2(400, 300);
        private Vector2 _startResizeSize;
        private Vector2 _startResizeMousePos;
        
        // Logging
        private static BepInEx.Logging.ManualLogSource Logger;
        
        public void Initialize(TransformCacher transformCacher, DatabaseManager databaseManager, TransformIdBaker idBaker)
        {
            try
            {
                _transformCacher = transformCacher;
                _databaseManager = databaseManager;
                _idBaker = idBaker;
                
                // Add debug log to verify component references
                if (_transformCacher == null)
                {
                    Logger.LogError("TransformCacher reference is null during initialization");
                }
                
                if (_databaseManager == null)
                {
                    Logger.LogError("DatabaseManager reference is null during initialization");
                }
                
                if (_idBaker == null)
                {
                    Logger.LogError("TransformIdBaker reference is null during initialization");
                    
                    // Try to find it in the scene if it wasn't passed correctly
                    _idBaker = FindObjectOfType<TransformIdBaker>();
                    
                    if (_idBaker == null)
                    {
                        Logger.LogError("Failed to find TransformIdBaker in the scene");
                        
                        // Create one as a last resort
                        GameObject idBakerObj = new GameObject("TransformIdBaker");
                        _idBaker = idBakerObj.AddComponent<TransformIdBaker>();
                        
                        if (_idBaker != null)
                        {
                            _idBaker.Initialize();
                            Logger.LogWarning("Created new TransformIdBaker as fallback");
                        }
                        else
                        {
                            Logger.LogError("Failed to create TransformIdBaker component");
                        }
                    }
                    else
                    {
                        Logger.LogWarning("Found TransformIdBaker in scene instead of passed reference");
                    }
                }
                
                // Get logger
                Logger = BepInEx.Logging.Logger.CreateLogSource("TransformCacherGUI");
                
                // Scan for bundle files
                RefreshBundleFiles();
                
                Logger.LogInfo("TransformCacherGUI initialized successfully");
            }
            catch (Exception ex)
            {
                if (Logger != null)
                {
                    Logger.LogError($"Error during TransformCacherGUI initialization: {ex.Message}\n{ex.StackTrace}");
                }
                else
                {
                    Debug.LogError($"Error during TransformCacherGUI initialization: {ex.Message}\n{ex.StackTrace}");
                }
            }
        }
        
        public void OnGUI()
        {
            if (TransformCacherPlugin.EnableDebugGUI == null || !TransformCacherPlugin.EnableDebugGUI.Value)
                return;
                
            try
            {
                // Handle the current event - important for GUI stability
                Event currentEvent = Event.current;
                
                // Prevent layout/repaint mismatch by skipping unnecessary events
                if (currentEvent.type == EventType.Layout || 
                    currentEvent.type == EventType.Repaint || 
                    currentEvent.type == EventType.MouseDown ||
                    currentEvent.type == EventType.MouseUp ||
                    currentEvent.type == EventType.MouseDrag ||
                    currentEvent.type == EventType.KeyDown)
                {
                    // Status box to show mod is active
                    GUI.Box(new Rect(10, 10, 200, 30), "Transform Cacher Active");
                    
                    // Main window
                    _windowRect = GUI.Window(0, _windowRect, DrawMainWindow, "Transform Cacher");
                    
                    // Spawn item selector window - only draw at top level
                    if (_showSpawnItemSelector)
                    {
                        Rect selectorRect = new Rect(_windowRect.x + _windowRect.width + 10, _windowRect.y, 500, 500);
                        GUI.Window(1, selectorRect, DrawSpawnItemSelector, "Spawn Item");
                    }
                    
                    // Baker window - only draw at top level
                    if (_showBakerWindow && _idBaker != null)
                    {
                        _bakerWindowRect = GUI.Window(100, _bakerWindowRect, DrawBakerWindow, "Transform ID Baker");
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogError($"Error in OnGUI: {ex.Message}\n{ex.StackTrace}");
            }
        }

        private void OnDisable()
        {
            try
            {
                // Ensure all resources are properly released
                Resources.UnloadUnusedAssets();
                
                // Unload any loaded asset bundles
                foreach (var bundle in _loadedBundles.Values)
                {
                    if (bundle != null)
                    {
                        bundle.Unload(false);
                    }
                }
                _loadedBundles.Clear();
                _bundleObjects.Clear();
                
                // Log that we're shutting down cleanly
                if (Logger != null)
                {
                    Logger.LogInfo("TransformCacherGUI disabled, resources cleaned up");
                }
            }
            catch (Exception ex)
            {
                if (Logger != null)
                {
                    Logger.LogError($"Error during TransformCacherGUI cleanup: {ex.Message}");
                }
            }
        }

        private void OnDestroy()
        {
            try
            {
                // Final cleanup
                if (Logger != null)
                {
                    Logger.LogInfo("TransformCacherGUI destroyed");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error during TransformCacherGUI destruction: {ex.Message}");
            }
        }
        
        private void RefreshBundleFiles()
        {
            try 
            {
                _bundleFiles.Clear();
                _bundleDirectories.Clear();
                
                // Get path to bundle directory
                string baseDir = Path.Combine(Paths.PluginPath, "TransformCacher", "bundles");
                
                // Create directory if it doesn't exist
                if (!Directory.Exists(baseDir))
                {
                    Directory.CreateDirectory(baseDir);
                    Logger.LogInfo($"Created bundles directory: {baseDir}");
                    return;
                }
                
                // Get all bundle files (without extension filtering since bundles can have any extension)
                string[] files = Directory.GetFiles(baseDir, "*", SearchOption.AllDirectories);
                foreach (string file in files)
                {
                    // Skip meta files and other non-bundle files (best effort)
                    if (file.EndsWith(".meta") || file.EndsWith(".manifest") || file.EndsWith(".txt"))
                        continue;
                        
                    _bundleFiles.Add(file);
                }
                
                // Get subdirectories
                string[] directories = Directory.GetDirectories(baseDir);
                foreach (string dir in directories)
                {
                    _bundleDirectories.Add(dir);
                }
                
                Logger.LogInfo($"Found {_bundleFiles.Count} bundle files and {_bundleDirectories.Count} directories");
            }
            catch (Exception ex)
            {
                Logger.LogError($"Error refreshing bundle files: {ex.Message}\n{ex.StackTrace}");
            }
        }
        
        private GameObject LoadObjectFromBundle(string bundlePath, string assetName = null)
        {
            try
            {
                // Check if bundle is already loaded
                if (!_loadedBundles.TryGetValue(bundlePath, out AssetBundle bundle))
                {
                    // Load the asset bundle
                    bundle = AssetBundle.LoadFromFile(bundlePath);
                    if (bundle == null)
                    {
                        Logger.LogError($"Failed to load bundle: {bundlePath}");
                        return null;
                    }
                    
                    _loadedBundles[bundlePath] = bundle;
                }
                
                // Get the first asset if no specific name provided
                if (string.IsNullOrEmpty(assetName))
                {
                    // Load all assets from the bundle
                    _bundleObjects.Clear();
                    string[] assetNames = bundle.GetAllAssetNames();
                    
                    if (assetNames.Length == 0)
                    {
                        Logger.LogWarning($"No assets found in bundle: {bundlePath}");
                        return null;
                    }
                    
                    // Try to load all game objects from bundle
                    foreach (string name in assetNames)
                    {
                        try
                        {
                            GameObject obj = bundle.LoadAsset<GameObject>(name);
                            if (obj != null)
                            {
                                _bundleObjects.Add(obj);
                            }
                        }
                        catch (Exception ex)
                        {
                            Logger.LogWarning($"Error loading asset {name}: {ex.Message}");
                        }
                    }
                    
                    if (_bundleObjects.Count > 0)
                    {
                        return _bundleObjects[0]; // Return the first object
                    }
                    else
                    {
                        Logger.LogWarning($"No GameObject assets found in bundle: {bundlePath}");
                        return null;
                    }
                }
                else
                {
                    // Load the specific named asset
                    GameObject obj = bundle.LoadAsset<GameObject>(assetName);
                    if (obj == null)
                    {
                        Logger.LogError($"Failed to load asset {assetName} from bundle: {bundlePath}");
                        return null;
                    }
                    return obj;
                }
            }
            catch (Exception ex)
            {
                Logger.LogError($"Error loading bundle {bundlePath}: {ex.Message}\n{ex.StackTrace}");
                return null;
            }
        }
        
        private void DrawMainWindow(int id)
        {
            try
            {
                try
                {
                    // Begin scrollable area with try/finally to ensure proper closing
                    _mainWindowScrollPosition = GUILayout.BeginScrollView(_mainWindowScrollPosition);
                    try
                    {
                        // Object Actions section
                        GUILayout.Label("Object Actions", GUI.skin.box);
                        
                        // Tagging and saving - with null checks
                        if (GUILayout.Button("Save All Tagged Objects", GUILayout.Height(30)) && _transformCacher != null)
                        {
                            _transformCacher.SaveAllTaggedObjects();
                        }

                        if (GUILayout.Button("Tag Inspected Object", GUILayout.Height(30)) && _transformCacher != null)
                        {
                            GameObject currentInspectedObject = _transformCacher.GetCurrentInspectedObject();
                            if (currentInspectedObject != null)
                                _transformCacher.TagObject(currentInspectedObject);
                            else
                                Logger.LogInfo("No object currently inspected");
                        }
                        
                        if (GUILayout.Button("Destroy Inspected Object", GUILayout.Height(30)) && _transformCacher != null)
                        {
                            GameObject currentInspectedObject = _transformCacher.GetCurrentInspectedObject();
                            if (currentInspectedObject != null)
                                _transformCacher.MarkForDestruction(currentInspectedObject);
                            else
                                Logger.LogInfo("No object currently inspected");
                        }
                        
                        // Fixed: Changed button text to "Spawn Item" and fixed handler
                        if (GUILayout.Button("Spawn Item", GUILayout.Height(30)))
                        {
                            _showSpawnItemSelector = !_showSpawnItemSelector;
                            if (_showSpawnItemSelector && _transformCacher != null && !_transformCacher.ArePrefabsLoaded())
                            {
                                _transformCacher.StartCoroutine(_transformCacher.LoadPrefabs());
                            }
                        }
                        
                        if (GUILayout.Button("Force Apply Transforms", GUILayout.Height(30)) && _transformCacher != null)
                        {
                            Scene currentScene = SceneManager.GetActiveScene();
                            _transformCacher.ResetTransformApplicationAttempts();
                            _transformCacher.StartCoroutine(_transformCacher.ApplyTransformsWithRetry(currentScene));
                        }
                        
                        // Fixed: Properly enable ID Baker button
                        GUI.enabled = _idBaker != null;
                        if (GUILayout.Button("Open ID Baker", GUILayout.Height(30)))
                        {
                            _showBakerWindow = !_showBakerWindow;
                        }
                        GUI.enabled = true; // Reset enabled state

                        GUILayout.Space(20);
                        
                        // Information section
                        GUILayout.Label("Information", GUI.skin.box);
                        
                        // Add null checks for all config entries
                        if (TransformCacherPlugin.SaveHotkey != null)
                            GUILayout.Label($"Save Hotkey: {TransformCacherPlugin.SaveHotkey.Value.ToString()}");
                        else
                            GUILayout.Label("Save Hotkey: N/A");
                            
                        if (TransformCacherPlugin.TagHotkey != null)
                            GUILayout.Label($"Tag Hotkey: {TransformCacherPlugin.TagHotkey.Value.ToString()}");
                        else
                            GUILayout.Label("Tag Hotkey: N/A");
                            
                        if (TransformCacherPlugin.DestroyHotkey != null)
                            GUILayout.Label($"Destroy Hotkey: {TransformCacherPlugin.DestroyHotkey.Value.ToString()}");
                        else
                            GUILayout.Label("Destroy Hotkey: N/A");
                            
                        if (TransformCacherPlugin.SpawnHotkey != null)
                            GUILayout.Label($"Spawn Hotkey: {TransformCacherPlugin.SpawnHotkey.Value.ToString()}");
                        else
                            GUILayout.Label("Spawn Hotkey: N/A");
                            
                        if (TransformCacherPlugin.MouseToggleHotkey != null)
                            GUILayout.Label($"Mouse Toggle Hotkey: {TransformCacherPlugin.MouseToggleHotkey.Value.ToString()}");
                        else
                            GUILayout.Label("Mouse Toggle Hotkey: N/A");
                            
                        if (_transformCacher != null)
                            GUILayout.Label($"Current Scene: {_transformCacher.GetCurrentScene() ?? "Unknown"}");
                        else
                            GUILayout.Label("Current Scene: Unknown");
                            
                        if (_transformCacher != null)
                            GUILayout.Label($"Mouse Focus: {(_transformCacher.IsUIFocused() ? "UI" : "Game")}");
                        else
                            GUILayout.Label("Mouse Focus: Unknown");

                        GUILayout.Space(10);
                        
                        // Inspected object info
                        if (_transformCacher != null)
                        {
                            GameObject currentInspectedObjectInfo = _transformCacher.GetCurrentInspectedObject();
                            if (currentInspectedObjectInfo != null)
                            {
                                string uniqueId = FixUtility.GenerateUniqueId(currentInspectedObjectInfo.transform);
                                string pathId = FixUtility.GeneratePathID(currentInspectedObjectInfo.transform);
                                string itemId = FixUtility.GenerateItemID(currentInspectedObjectInfo.transform);
                                
                                GUILayout.Label($"Selected: {currentInspectedObjectInfo.name}");
                                GUILayout.Label($"UniqueId: {uniqueId}");
                                GUILayout.Label($"PathID: {pathId}");
                                GUILayout.Label($"ItemID: {itemId}");
                                
                                // Display its transform
                                GUILayout.Label($"Position: {currentInspectedObjectInfo.transform.position}");
                                GUILayout.Label($"Rotation: {currentInspectedObjectInfo.transform.eulerAngles}");
                                GUILayout.Label($"Scale: {currentInspectedObjectInfo.transform.localScale}");
                                
                                // Check if it's tagged
                                TransformCacherTag tag = currentInspectedObjectInfo.GetComponent<TransformCacherTag>();
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
                        }
                        else
                        {
                            GUILayout.Label("Transform cacher not initialized");
                        }

                        GUILayout.Space(20);
                        
                        // Database stats section with improved error handling
                        try
                        {
                            // Database stats header
                            GUILayout.Label("Database Statistics", GUI.skin.box);
                            
                            // Use a simple, consistent display that won't change between layout and repaint
                            if (_databaseManager == null)
                            {
                                GUILayout.Label("Database manager not initialized");
                            }
                            else
                            {
                                // Get database safely
                                Dictionary<string, Dictionary<string, TransformData>> transformsDb = null;
                                
                                try
                                {
                                    transformsDb = _databaseManager.GetTransformsDatabase();
                                    
                                    if (transformsDb == null || transformsDb.Count == 0)
                                    {
                                        GUILayout.Label("No database entries found");
                                    }
                                    else
                                    {
                                        // Display a fixed format summary instead of dynamic per-scene entries
                                        int totalScenes = 0;
                                        int totalObjects = 0;
                                        int totalDestroyed = 0;
                                        int totalSpawned = 0;
                                        
                                        foreach (var sceneEntry in transformsDb)
                                        {
                                            if (sceneEntry.Value != null)
                                            {
                                                totalScenes++;
                                                totalObjects += sceneEntry.Value.Count;
                                                
                                                foreach (var dataEntry in sceneEntry.Value.Values)
                                                {
                                                    if (dataEntry != null)
                                                    {
                                                        if (dataEntry.IsDestroyed) totalDestroyed++;
                                                        if (dataEntry.IsSpawned) totalSpawned++;
                                                    }
                                                }
                                            }
                                        }
                                        
                                        // Display summary stats - consistent across layout/repaint
                                        GUILayout.Label($"Total Scenes: {totalScenes}");
                                        GUILayout.Label($"Total Objects: {totalObjects}");
                                        GUILayout.Label($"Destroyed Objects: {totalDestroyed}");
                                        GUILayout.Label($"Spawned Objects: {totalSpawned}");
                                        
                                        // Show one scene at a time with a selector to avoid dynamic GUI changes
                                        if (totalScenes > 0)
                                        {
                                            List<string> sceneKeys = new List<string>(transformsDb.Keys);
                                            string[] sceneNames = sceneKeys.ToArray();
                                            
                                            // Static index for scene selection
                                            if (_selectedSceneIndex >= sceneNames.Length)
                                                _selectedSceneIndex = 0;
                                            
                                            // Scene selector
                                            GUILayout.Space(10);
                                            GUILayout.Label("Scene Details:", GUI.skin.box);
                                            
                                            // Previous/Next buttons for scene selection
                                            try
                                            {
                                                GUILayout.BeginHorizontal();
                                                try
                                                {
                                                    if (GUILayout.Button("◄ Prev", GUILayout.Width(70)))
                                                    {
                                                        _selectedSceneIndex--;
                                                        if (_selectedSceneIndex < 0)
                                                            _selectedSceneIndex = sceneNames.Length - 1;
                                                    }
                                                    
                                                    // Center scene name
                                                    GUILayout.FlexibleSpace();
                                                    GUILayout.Label(sceneNames[_selectedSceneIndex], GUILayout.Width(150));
                                                    GUILayout.FlexibleSpace();
                                                    
                                                    if (GUILayout.Button("Next ►", GUILayout.Width(70)))
                                                    {
                                                        _selectedSceneIndex++;
                                                        if (_selectedSceneIndex >= sceneNames.Length)
                                                            _selectedSceneIndex = 0;
                                                    }
                                                }
                                                finally
                                                {
                                                    GUILayout.EndHorizontal();
                                                }
                                                
                                                // Display scene stats
                                                string selectedScene = sceneNames[_selectedSceneIndex];
                                                if (!string.IsNullOrEmpty(selectedScene) && transformsDb.ContainsKey(selectedScene))
                                                {
                                                    var sceneData = transformsDb[selectedScene];
                                                    
                                                    if (sceneData != null)
                                                    {
                                                        int sceneObjects = sceneData.Count;
                                                        int sceneDestroyed = 0;
                                                        int sceneSpawned = 0;
                                                        
                                                        foreach (var data in sceneData.Values)
                                                        {
                                                            if (data != null)
                                                            {
                                                                if (data.IsDestroyed) sceneDestroyed++;
                                                                if (data.IsSpawned) sceneSpawned++;
                                                            }
                                                        }
                                                        
                                                        GUILayout.Label($"Objects: {sceneObjects}");
                                                        GUILayout.Label($"Destroyed: {sceneDestroyed}");
                                                        GUILayout.Label($"Spawned: {sceneSpawned}");
                                                    }
                                                    else
                                                    {
                                                        GUILayout.Label("Scene data is null");
                                                    }
                                                }
                                                else
                                                {
                                                    GUILayout.Label("Invalid scene selection");
                                                }
                                            }
                                            catch (Exception ex)
                                            {
                                                Logger.LogError($"Error displaying scene details: {ex.Message}");
                                                GUILayout.Label("Error displaying scene details");
                                            }
                                        }
                                    }
                                }
                                catch (Exception ex)
                                {
                                    Logger.LogError($"Error accessing database: {ex.Message}");
                                    GUILayout.Label("Error accessing database");
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Logger.LogError($"Error in database section: {ex.Message}");
                            GUILayout.Label("Error displaying database statistics");
                        }
                    }
                    finally
                    {
                        // Always end the scroll view to avoid layout errors
                        GUILayout.EndScrollView();
                    }
                    
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
                    // In case of a catastrophic error in the main body
                    Logger.LogError($"Error in GUI main drawing: {ex.Message}\n{ex.StackTrace}");
                }
            }
            catch (Exception ex)
            {
                // Outermost error handler for truly unexpected errors
                Logger.LogError($"Critical error in DrawMainWindow: {ex.Message}\n{ex.StackTrace}");
            }
        }
        
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
        
        private void DrawSpawnItemSelector(int id)
        {
            GUILayout.BeginVertical();
            
            // Check for proper transform cacher reference
            if (_transformCacher == null)
            {
                GUILayout.Label("Error: TransformCacher reference is missing");
                if (GUILayout.Button("Close"))
                {
                    _showSpawnItemSelector = false;
                }
                GUILayout.EndVertical();
                GUI.DragWindow();
                return;
            }
            
            // Search field
            GUILayout.BeginHorizontal();
            GUILayout.Label("Search:", GUILayout.Width(60));
            _prefabSearchText = GUILayout.TextField(_prefabSearchText, GUILayout.Width(300));
            if (GUILayout.Button("Clear", GUILayout.Width(60)))
            {
                _prefabSearchText = "";
            }
            GUILayout.EndHorizontal();
            
            // Source selector buttons
            GUILayout.BeginHorizontal();
            
            GUI.enabled = true;
            if (GUILayout.Button(_showBuiltInSelector ? "Built-in ✓" : "Built-in", GUILayout.Width(150), GUILayout.Height(30)))
            {
                _showBuiltInSelector = true;
                _showBundleSelector = false;
            }
            
            if (GUILayout.Button(_showBundleSelector ? "Bundles ✓" : "Bundles", GUILayout.Width(150), GUILayout.Height(30)))
            {
                _showBundleSelector = true;
                _showBuiltInSelector = false;
                RefreshBundleFiles();
            }
            
            GUILayout.EndHorizontal();
            
            GUILayout.Space(10);
            
            // Loading indicator for built-in prefabs
            if (_showBuiltInSelector)
            {
                if (!_transformCacher.ArePrefabsLoaded())
                {
                    GUILayout.Box("Loading game objects, please wait...");
                }
                else
                {
                    // Category selector (for built-in items only)
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
                        
                        var categoryNames = _transformCacher.GetCategoryNames();
                        if (categoryNames != null && categoryNames.Count > 0)
                        {
                            dropdownRect.height = Mathf.Min(categoryNames.Count * 25, 200);
                            
                            GUILayout.BeginArea(dropdownRect, GUI.skin.box);
                            _categoryScrollPosition = GUILayout.BeginScrollView(_categoryScrollPosition, GUILayout.Height(dropdownRect.height - 10));
                            
                            foreach (var category in categoryNames)
                            {
                                int count = _transformCacher.GetPrefabCountForCategory(category);
                                if (GUILayout.Button($"{category} ({count})"))
                                {
                                    _selectedCategory = category;
                                    _transformCacher.SetSelectedCategory(_selectedCategory);
                                    _showCategoryDropdown = false;
                                }
                            }
                            
                            GUILayout.EndScrollView();
                            GUILayout.EndArea();
                        }
                    }
                    
                    try
                    {
                        // Get filtered prefabs
                        List<GameObject> filteredPrefabs = _transformCacher.GetFilteredPrefabs(_prefabSearchText);
                        
                        if (filteredPrefabs != null)
                        {
                            GUILayout.Label($"Found {filteredPrefabs.Count} items in '{_selectedCategory}'{(!string.IsNullOrEmpty(_prefabSearchText) ? $" matching '{_prefabSearchText}'" : "")}");
                            
                            // Scrollable list
                            _prefabScrollPosition = GUILayout.BeginScrollView(_prefabScrollPosition, GUILayout.Height(280));
                            
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
                        }
                        else
                        {
                            GUILayout.Label("No items found");
                        }
                    }
                    catch (Exception ex)
                    {
                        GUILayout.Label($"Error getting prefabs: {ex.Message}");
                        Logger.LogError($"Error in prefab selector: {ex.Message}");
                    }
                }
            }
            // Bundle selector
            else if (_showBundleSelector)
            {
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Refresh Bundles", GUILayout.Width(150)))
                {
                    RefreshBundleFiles();
                }
                GUILayout.Label($"Found {_bundleFiles.Count} bundle files");
                GUILayout.EndHorizontal();
                
                // Show bundle files
                _bundleScrollPosition = GUILayout.BeginScrollView(_bundleScrollPosition, GUILayout.Height(280));
                
                // Filter bundle files by search text if provided
                List<string> filteredBundles = _bundleFiles;
                if (!string.IsNullOrEmpty(_prefabSearchText))
                {
                    filteredBundles = _bundleFiles.Where(file => 
                        Path.GetFileName(file).IndexOf(_prefabSearchText, StringComparison.OrdinalIgnoreCase) >= 0)
                        .ToList();
                }
                
                foreach (string bundlePath in filteredBundles)
                {
                    string displayName = Path.GetFileName(bundlePath);
                    bool isSelected = _selectedBundle == bundlePath;
                    GUI.color = isSelected ? Color.green : Color.white;
                    
                    if (GUILayout.Button(displayName, GUILayout.Height(30)))
                    {
                        // Handle bundle selection
                        _selectedBundle = bundlePath;
                        
                        // Try to load the bundle and its objects
                        GameObject bundleObj = LoadObjectFromBundle(bundlePath);
                        if (bundleObj != null)
                        {
                            _selectedPrefab = bundleObj;
                        }
                    }
                    
                    GUI.color = Color.white;
                }
                
                // If we loaded bundle objects, show them too
                if (_bundleObjects.Count > 0 && _selectedBundle != null)
                {
                    GUILayout.Space(10);
                    GUILayout.Label($"Assets in bundle:");
                    
                    foreach (GameObject obj in _bundleObjects)
                    {
                        bool isSelected = _selectedPrefab == obj;
                        GUI.color = isSelected ? Color.green : Color.white;
                        
                        if (GUILayout.Button(obj.name, GUILayout.Height(30)))
                        {
                            _selectedPrefab = obj;
                        }
                        
                        GUI.color = Color.white;
                    }
                }
                
                GUILayout.EndScrollView();
            }
            
            // Spawn button
            GUI.enabled = _selectedPrefab != null;
            if (GUILayout.Button("Spawn Selected Object", GUILayout.Height(40)))
            {
                try
                {
                    _transformCacher.SpawnObject(_selectedPrefab);
                    // Clear selection and hide the selector
                    _selectedPrefab = null;
                    _showSpawnItemSelector = false;
                }
                catch (Exception ex)
                {
                    Logger.LogError($"Error spawning object: {ex.Message}");
                }
            }
            GUI.enabled = true;
            
            // Close button
            if (GUILayout.Button("Close", GUILayout.Height(30)))
            {
                _showSpawnItemSelector = false;
            }
            
            GUILayout.EndVertical();
            
            // Allow the window to be dragged
            GUI.DragWindow();
        }
        
        private void DrawBakerWindow(int id)
        {
            GUILayout.BeginVertical();
            
            // Scene information
            Scene currentScene = SceneManager.GetActiveScene();
            GUILayout.Label($"Current Scene: {currentScene.name}");
            
            bool isSceneBaked = _idBaker.IsSceneBaked(currentScene);
            string bakeStatus = isSceneBaked 
                ? "<color=green>Baked</color>"
                : "<color=yellow>Not Baked</color>";
                
            // Get the number of baked objects if available
            if (isSceneBaked)
            {
                var sceneBakedIds = _databaseManager.GetBakedIdsDatabase();
                if (sceneBakedIds.ContainsKey(currentScene.name))
                {
                    int count = sceneBakedIds[currentScene.name].Count;
                    bakeStatus += $" ({count} objects)";
                }
            }
            
            GUILayout.Label($"Bake Status: {bakeStatus}", new GUIStyle(GUI.skin.label) { richText = true });
            
            GUILayout.Space(10);
            
            // Settings section
            _showBakerSettings = GUILayout.Toggle(_showBakerSettings, "Show Settings");
            
            if (_showBakerSettings)
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
            
            // Baking controls - fix for Bake ID button
            GUI.enabled = !_idBaker.IsBaking();
            if (GUILayout.Button("Bake Scene IDs", GUILayout.Height(40)))
            {
                _idBaker.StartBaking();
            }
            GUI.enabled = true;
            
            // Baking progress
            if (_idBaker.IsBaking())
            {
                GUILayout.Space(10);
                GUILayout.Label(_idBaker.GetBakingStatus());
                
                Rect progressRect = GUILayoutUtility.GetRect(100, 20);
                EditorGUI.ProgressBar(progressRect, _idBaker.GetBakingProgress(), 
                    $"{Mathf.RoundToInt(_idBaker.GetBakingProgress() * 100)}%");
            }
            
            GUILayout.Space(10);
            
            // Database statistics
            GUILayout.Label("Database Statistics", GUI.skin.box);
            
            _bakerScrollPosition = GUILayout.BeginScrollView(_bakerScrollPosition, GUILayout.Height(150));
            
            try
            {
                // Get baked IDs database from database manager
                var currentBakedIdsDb = _databaseManager.GetBakedIdsDatabase();
                
                // Show scenes with baked IDs
                if (currentBakedIdsDb != null && currentBakedIdsDb.Count > 0)
                {
                    foreach (var scene in currentBakedIdsDb.Keys)
                    {
                        int count = currentBakedIdsDb[scene].Count;
                        GUILayout.Label($"Scene '{scene}': {count} objects");
                    }
                }
                else
                {
                    GUILayout.Label("No scenes have been baked yet");
                }
            }
            catch (Exception ex)
            {
                Logger.LogError($"Error displaying baked database: {ex.Message}");
                GUILayout.Label("Error displaying baked database");
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
        
        // UI Helper methods
        private static class EditorGUI
        {
            public static void ProgressBar(Rect rect, float value, string text)
            {
                // Draw background
                GUI.Box(rect, "");
                
                // Draw filled portion
                Rect fillRect = new Rect(rect.x, rect.y, rect.width * value, rect.height);
                GUI.Box(fillRect, "");
                
                // Center text
                GUIStyle centeredStyle = new GUIStyle(GUI.skin.label);
                centeredStyle.alignment = TextAnchor.MiddleCenter;
                
                GUI.Label(rect, text, centeredStyle);
            }
        }
        
        // Additional helper for Editor styles
        private static class EditorStyles
        {
            public static GUIStyle miniLabel
            {
                get
                {
                    GUIStyle style = new GUIStyle(GUI.skin.label);
                    style.fontSize = style.fontSize - 2;
                    return style;
                }
            }
            
            public static GUIStyle boldLabel
            {
                get
                {
                    GUIStyle style = new GUIStyle(GUI.skin.label);
                    style.fontStyle = FontStyle.Bold;
                    return style;
                }
            }
        }
    }
    
    /// <summary>
    /// Helper class with utilities for fixing and generating IDs
    /// </summary>
    public static class FixUtility
    {
        // Get the hierarchy path using sibling indices
        public static string GetSiblingIndicesPath(Transform transform)
        {
            if (transform == null) return string.Empty;
            
            // Use List instead of Stack to avoid reference issues
            List<int> indices = new List<int>();
            
            Transform current = transform;
            while (current != null)
            {
                indices.Insert(0, current.GetSiblingIndex());
                current = current.parent;
            }
            
            return string.Join(".", indices.ToArray());
        }
        
        // Get the full path of a transform in the hierarchy
        public static string GetFullPath(Transform transform)
        {
            if (transform == null) return string.Empty;
            
            // Use List instead of Stack to avoid reference issues
            List<string> path = new List<string>();
            
            var current = transform;
            while (current != null)
            {
                path.Insert(0, current.name);
                current = current.parent;
            }
            
            return string.Join("/", path.ToArray());
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
        
        // Generate a unique ID for a transform that persists across game sessions
        public static string GenerateUniqueId(Transform transform)
        {
            if (transform == null) return string.Empty;
            
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
    }
}