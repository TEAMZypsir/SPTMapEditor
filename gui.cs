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
        
        // Prefab selector state
        private bool _showPrefabSelector = false;
        private Vector2 _prefabScrollPosition = Vector2.zero;
        private Vector2 _categoryScrollPosition = Vector2.zero;
        private bool _showCategoryDropdown = false;
        private string _prefabSearchText = "";
        private GameObject _selectedPrefab = null;
        private string _selectedCategory = "All";
        
        // Export window properties
        private bool _showExportWindow = false;
        private bool _includeChildren = true;
        private Vector2 _exportWindowScrollPosition = Vector2.zero;
        private Rect _exportWindowRect = new Rect(20, 60, 400, 300);
        private string _customFilename = "";
        private bool _useGlbFormat = false;
        
        // Add this to the TransformCacherGUI class variable section:

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
            _transformCacher = transformCacher;
            _databaseManager = databaseManager;
            _idBaker = idBaker;
            
            // Get logger
            Logger = BepInEx.Logging.Logger.CreateLogSource("TransformCacherGUI");
            Logger.LogInfo("TransformCacherGUI initialized successfully");
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
                    
                    // Prefab selector window
                    if (_showPrefabSelector)
                    {
                        Rect selectorRect = new Rect(_windowRect.x + _windowRect.width + 10, _windowRect.y, 500, 500);
                        GUI.Window(1, selectorRect, DrawPrefabSelector, "Prefab Selector");
                    }
                    
                    // Export window
                    if (_showExportWindow)
                    {
                        _exportWindowRect = GUILayout.Window(2, _exportWindowRect, DrawExportWindow, "Export Objects");
                    }
                    
                    // Baker window from TransformIdBaker
                    if (_showBakerWindow && _idBaker != null)
                    {
                        _bakerWindowRect = GUI.Window(100, _bakerWindowRect, DrawBakerWindow, "Transform ID Baker");
                    }
                    
                    // Quick export button when export window is not shown
                    if (!_showExportWindow)
                    {
                        if (GUI.Button(new Rect(Screen.width - 120, 10, 110, 30), "Export Model"))
                        {
                            _showExportWindow = true;
                        }
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
        
        private void DrawMainWindow(int id)
        {
            try
            {
                // Begin scrollable area
                _mainWindowScrollPosition = GUILayout.BeginScrollView(_mainWindowScrollPosition);
                
                try
                {
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
                    
                    if (GUILayout.Button("Open Prefab Selector", GUILayout.Height(30)))
                    {
                        _showPrefabSelector = !_showPrefabSelector;
                        if (_showPrefabSelector && _transformCacher != null && !_transformCacher.ArePrefabsLoaded())
                        {
                            _transformCacher.LoadPrefabs();
                        }
                    }
                    
                    if (GUILayout.Button("Force Apply Transforms", GUILayout.Height(30)) && _transformCacher != null)
                    {
                        Scene currentScene = SceneManager.GetActiveScene();
                        _transformCacher.ResetTransformApplicationAttempts();
                        _transformCacher.StartCoroutine(_transformCacher.ApplyTransformsWithRetry(currentScene));
                    }
                    
                    if (_idBaker != null && GUILayout.Button("Open ID Baker", GUILayout.Height(30)))
                    {
                        _showBakerWindow = !_showBakerWindow;
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogError($"Error in action buttons: {ex.Message}");
                }
                
                try
                {
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
                }
                catch (Exception ex)
                {
                    Logger.LogError($"Error in information section: {ex.Message}");
                }
                
                try
                {
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
                }
                catch (Exception ex)
                {
                    Logger.LogError($"Error in object info section: {ex.Message}");
                }
                
                try
                {
                    GUILayout.Space(20);
                    
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
                        }
                        catch (Exception ex)
                        {
                            GUILayout.Label($"Error accessing database: {ex.Message}");
                            transformsDb = null;
                        }
                        
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
                                string[] sceneNames = transformsDb.Keys.ToArray();
                                
                                // Static index for scene selection
                                if (_selectedSceneIndex >= sceneNames.Length)
                                    _selectedSceneIndex = 0;
                                
                                // Scene selector
                                GUILayout.Space(10);
                                GUILayout.Label("Scene Details:", GUI.skin.box);
                                
                                // Previous/Next buttons for scene selection
                                GUILayout.BeginHorizontal();
                                
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
                                
                                GUILayout.EndHorizontal();
                                
                                // Display scene stats
                                string selectedScene = sceneNames[_selectedSceneIndex];
                                var sceneData = transformsDb[selectedScene];
                                
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
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogError($"Error in database section: {ex.Message}");
                    GUILayout.Label("Error displaying database statistics");
                }
                
                // Always end the scroll view to avoid layout errors
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
                // In case of a catastrophic error, still ensure the scroll view is ended
                try { GUILayout.EndScrollView(); } catch { }
                Logger.LogError($"Error in DrawMainWindow: {ex.Message}\n{ex.StackTrace}");
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
        
        private void DrawPrefabSelector(int id)
        {
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
            if (!_transformCacher.ArePrefabsLoaded())
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
                    
                    var categoryNames = _transformCacher.GetCategoryNames();
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
                
                // Get filtered prefabs
                List<GameObject> filteredPrefabs = _transformCacher.GetFilteredPrefabs(_prefabSearchText);
                
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
                    _transformCacher.SpawnObject(_selectedPrefab);
                    // Clear selection and hide the selector
                    _selectedPrefab = null;
                    _showPrefabSelector = false;
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
        
        private void DrawExportWindow(int id)
        {
            GUILayout.BeginVertical(GUILayout.ExpandHeight(true));
            
            // Title
            GUILayout.Label("Export Settings", GUI.skin.box);
            GUILayout.Space(10);
            
            // Toggle for including children
            _includeChildren = GUILayout.Toggle(_includeChildren, "Include Children");
            
            // Export format selection
            GUILayout.BeginHorizontal();
            GUILayout.Label("Format:", GUILayout.Width(60));
            
            bool newUseGlbFormat = GUILayout.Toggle(_useGlbFormat, "GLB (Binary)", GUILayout.Width(100));
            if (newUseGlbFormat != _useGlbFormat)
            {
                _useGlbFormat = newUseGlbFormat;
            }
            
            GUILayout.Toggle(!_useGlbFormat, "GLTF (Text)", GUILayout.Width(100));
            GUILayout.EndHorizontal();
            
            GUILayout.Space(10);
            
            // Filename input
            GUILayout.BeginHorizontal();
            GUILayout.Label("Filename:", GUILayout.Width(60));
            _customFilename = GUILayout.TextField(_customFilename, GUILayout.Width(200));
            GUILayout.EndHorizontal();
            
            GUILayout.Space(10);
            
            // Display selected objects
            var selectedObjects = GetObjectsToExport();
            
            GUILayout.Label($"Selected Objects: {selectedObjects.Count}", GUI.skin.box);
            
            _exportWindowScrollPosition = GUILayout.BeginScrollView(_exportWindowScrollPosition, GUILayout.Height(100));
            
            foreach (var obj in selectedObjects)
            {
                if (obj != null)
                {
                    GUILayout.Label(obj.name);
                }
            }
            
            GUILayout.EndScrollView();
            
            GUILayout.FlexibleSpace();
            
            // Export button
            GUI.enabled = selectedObjects.Count > 0;
            if (GUILayout.Button("Export Selected Objects", GUILayout.Height(40)))
            {
                ExportSelectedObjects(selectedObjects);
            }
            GUI.enabled = true;
            
            // Close button
            if (GUILayout.Button("Close"))
            {
                _showExportWindow = false;
            }
            
            GUILayout.EndVertical();
            
            // Allow the window to be dragged
            GUI.DragWindow();
        }
        
        // Baker window code integrated from TransformIdBaker
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
                int count = sceneBakedIds[currentScene.name].Count;
                bakeStatus += $" ({count} objects)";
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
            
            // Baking controls
            GUI.enabled = !_idBaker.IsBaking();
            if (GUILayout.Button("Bake Scene(s)", GUILayout.Height(40)))
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
            
            _bakerScrollPosition = GUILayout.BeginScrollView(_bakerScrollPosition);
            
            // Get baked IDs database from database manager
            var currentBakedIdsDb = _databaseManager.GetBakedIdsDatabase();
            
            // Show scenes with baked IDs
            foreach (var scene in currentBakedIdsDb.Keys)
            {
                int count = currentBakedIdsDb[scene].Count;
                GUILayout.Label($"Scene '{scene}': {count} objects");
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
        
        private HashSet<GameObject> GetObjectsToExport()
        {
            var result = new HashSet<GameObject>();
            
            // Get currently inspected object
            GameObject currentObject = _transformCacher.GetCurrentInspectedObject();
            if (currentObject != null)
            {
                result.Add(currentObject);
                
                // Add children if needed
                if (_includeChildren)
                {
                    AddChildrenRecursively(currentObject.transform, result);
                }
            }
            
            return result;
        }
        
        private void AddChildrenRecursively(Transform parent, HashSet<GameObject> collection)
        {
            // Avoid using Stack to prevent reference errors
            List<Transform> children = new List<Transform>();
            foreach (Transform child in parent)
            {
                children.Add(child);
            }
            
            foreach (var child in children)
            {
                collection.Add(child.gameObject);
                AddChildrenRecursively(child, collection);
            }
        }
        
        private void ExportSelectedObjects(HashSet<GameObject> selectedObjects)
        {
            if (selectedObjects.Count == 0)
            {
                Logger.LogWarning("No objects selected for export");
                return;
            }
            
            try
            {
                // Generate filename if not specified
                string filename = string.IsNullOrEmpty(_customFilename) ? 
                    selectedObjects.First().name : _customFilename;
                    
                // Add extension if not present
                if (!filename.EndsWith(".glb") && !filename.EndsWith(".gltf"))
                {
                    filename += _useGlbFormat ? ".glb" : ".gltf";
                }
                
                // Try to find TarkinItemExporter
                Type tarkinExporter = null;
                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    tarkinExporter = assembly.GetType("TarkinItemExporter.Exporter");
                    if (tarkinExporter != null) break;
                }
                
                if (tarkinExporter != null)
                {
                    // Set glb format flag
                    var glbField = tarkinExporter.GetField("glb");
                    if (glbField != null)
                    {
                        glbField.SetValue(null, _useGlbFormat);
                    }
                    
                    // Get default output directory
                    string outputDir = Path.Combine(Paths.PluginPath, "TransformCacher", "Exports");
                    
                    // Try to get configured output directory
                    var pluginType = Type.GetType("TarkinItemExporter.Plugin");
                    if (pluginType != null)
                    {
                        var outputDirField = pluginType.GetField("OutputDir");
                        if (outputDirField != null)
                        {
                            var configEntry = outputDirField.GetValue(null);
                            if (configEntry != null)
                            {
                                // Try to get Value property from ConfigEntry
                                var valueProperty = configEntry.GetType().GetProperty("Value");
                                if (valueProperty != null)
                                {
                                    var value = valueProperty.GetValue(configEntry);
                                    if (value != null)
                                    {
                                        outputDir = value.ToString();
                                    }
                                }
                            }
                        }
                    }
                    
                    // Create directory if it doesn't exist
                    Directory.CreateDirectory(outputDir);
                    
                    // Call the export method
                    var exportMethod = tarkinExporter.GetMethod("Export", new[] { 
                        typeof(HashSet<GameObject>), typeof(string), typeof(string) 
                    });
                    
                    if (exportMethod != null)
                    {
                        exportMethod.Invoke(null, new object[] { selectedObjects, outputDir, filename });
                        Logger.LogInfo($"Export initiated to {Path.Combine(outputDir, filename)}");
                        _showExportWindow = false;
                    }
                    else
                    {
                        Logger.LogError("Export method not found in TarkinItemExporter");
                    }
                }
                else
                {
                    Logger.LogWarning("TarkinItemExporter not found. Install it for model export support.");
                }
            }
            catch (Exception ex)
            {
                Logger.LogError($"Error during export: {ex.Message}\n{ex.StackTrace}");
            }
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