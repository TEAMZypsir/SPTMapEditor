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
            if (TransformCacher.EnableDebugGUI == null || !TransformCacher.EnableDebugGUI.Value)
                return;
                
            try
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
                
                // Quick export button when export window is not shown
                if (!_showExportWindow)
                {
                    if (GUI.Button(new Rect(Screen.width - 120, 10, 110, 30), "Export Model"))
                    {
                        _showExportWindow = true;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogError($"Error in OnGUI: {ex.Message}\n{ex.StackTrace}");
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
                    _transformCacher.SaveAllTaggedObjects();
                }

                if (GUILayout.Button("Tag Inspected Object", GUILayout.Height(30)))
                {
                    GameObject currentInspectedObject = _transformCacher.GetCurrentInspectedObject();
                    if (currentInspectedObject != null)
                        _transformCacher.TagObject(currentInspectedObject);
                    else
                        Logger.LogInfo("No object currently inspected");
                }
                
                if (GUILayout.Button("Destroy Inspected Object", GUILayout.Height(30)))
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
                    if (_showPrefabSelector && !_transformCacher.ArePrefabsLoaded())
                    {
                        _transformCacher.LoadPrefabs();
                    }
                }
                
                if (GUILayout.Button("Force Apply Transforms", GUILayout.Height(30)))
                {
                    Scene currentScene = SceneManager.GetActiveScene();
                    _transformCacher.ResetTransformApplicationAttempts();
                    _transformCacher.ApplyTransformsWithRetry(currentScene);
                }
                
                if (_idBaker != null && GUILayout.Button("Open ID Baker", GUILayout.Height(30)))
                {
                    _idBaker.ToggleBakerWindow();
                }
                
                GUILayout.Space(20);
                
                // Information section
                GUILayout.Label("Information", GUI.skin.box);
                
                // Add null checks for all config entries
                GUILayout.Label($"Save Hotkey: {(TransformCacher.SaveHotkey != null ? TransformCacher.SaveHotkey.Value.ToString() : "N/A")}");
                GUILayout.Label($"Tag Hotkey: {(TransformCacher.TagHotkey != null ? TransformCacher.TagHotkey.Value.ToString() : "N/A")}");
                GUILayout.Label($"Destroy Hotkey: {(TransformCacher.DestroyHotkey != null ? TransformCacher.DestroyHotkey.Value.ToString() : "N/A")}");
                GUILayout.Label($"Spawn Hotkey: {(TransformCacher.SpawnHotkey != null ? TransformCacher.SpawnHotkey.Value.ToString() : "N/A")}");
                GUILayout.Label($"Mouse Toggle Hotkey: {(TransformCacher.MouseToggleHotkey != null ? TransformCacher.MouseToggleHotkey.Value.ToString() : "N/A")}");
                GUILayout.Label($"Current Scene: {_transformCacher.GetCurrentScene() ?? "Unknown"}");
                GUILayout.Label($"Mouse Focus: {(_transformCacher.IsUIFocused() ? "UI" : "Game")}");
                
                GUILayout.Space(10);
                
                GameObject currentInspectedObjectInfo = _transformCacher.GetCurrentInspectedObject();
                if (currentInspectedObjectInfo != null)
                {
                    string uniqueId = TransformCacher.GenerateUniqueId(currentInspectedObjectInfo.transform);
                    string pathId = TransformCacher.GeneratePathID(currentInspectedObjectInfo.transform);
                    string itemId = TransformCacher.GenerateItemID(currentInspectedObjectInfo.transform);
                    
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
            foreach (Transform child in parent)
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
                    
                    // Get the output directory
                    var outputDirField = Type.GetType("TarkinItemExporter.Plugin")?.GetField("OutputDir");
                    string outputDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Exported Models");
                    
                    if (outputDirField != null && outputDirField.GetValue(null) != null)
                    {
                        outputDir = outputDirField.GetValue(null).ToString();
                    }
                    
                    // Call the export method
                    var exportMethod = tarkinExporter.GetMethod("Export", new[] { typeof(HashSet<GameObject>), typeof(string), typeof(string) });
                    if (exportMethod != null)
                    {
                        exportMethod.Invoke(null, new object[] { selectedObjects, outputDir, filename });
                        Logger.LogInfo($"Export initiated to {Path.Combine(outputDir, filename)}");
                        
                        // Show notification
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
    }
    
    /// <summary>
    /// Static utility class to fix ambiguous method calls
    /// </summary>
    public static class FixUtility
    {
        // Get the hierarchy path using sibling indices
        public static string GetSiblingIndicesPath(Transform transform)
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
        
        // Get the full path of a transform in the hierarchy
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
    }
}
