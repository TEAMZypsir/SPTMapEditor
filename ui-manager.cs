using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace TarkinItemExporter.UI
{
    public class SimpleUIManager : MonoBehaviour
    {
        private static SimpleUIManager _instance;
        
        // UI State
        private bool _showExportWindow = false;
        private bool _showPrefabSelector = false;
        private bool _includeChildren = true;
        private Vector2 _prefabScrollPosition = Vector2.zero;
        private string _searchText = "";
        private string _selectedCategory = "All";
        
        // UI Positions
        private Rect _exportWindowRect = new Rect(20, 60, 400, 300);
        private Rect _prefabSelectorRect = new Rect(440, 60, 400, 500);
        
        public static SimpleUIManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    GameObject go = new GameObject("TarkinUIManager");
                    _instance = go.AddComponent<SimpleUIManager>();
                    DontDestroyOnLoad(go);
                }
                return _instance;
            }
        }
        
        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            
            _instance = this;
            DontDestroyOnLoad(gameObject);
            
            // Log initialization
            Plugin.Log.LogInfo("Simple UI Manager initialized");
        }
        
        private void Update()
        {
            // Toggle export window with Ctrl+E
            if (Input.GetKey(KeyCode.LeftControl) && Input.GetKeyDown(KeyCode.E))
            {
                _showExportWindow = !_showExportWindow;
                Plugin.Log.LogInfo($"Export window toggled: {_showExportWindow}");
            }
            
            // Toggle prefab selector with Ctrl+P
            if (Input.GetKey(KeyCode.LeftControl) && Input.GetKeyDown(KeyCode.P))
            {
                _showPrefabSelector = !_showPrefabSelector;
                Plugin.Log.LogInfo($"Prefab selector toggled: {_showPrefabSelector}");
            }
        }
        
        private void OnGUI()
        {
            // Draw status box in corner to show the mod is active
            GUI.Box(new Rect(10, 10, 200, 30), "Tarkin Item Exporter Active");
            
            // Draw windows if visible
            if (_showExportWindow)
            {
                _exportWindowRect = GUILayout.Window(1001, _exportWindowRect, DrawExportWindow, "Export Objects");
            }
            
            if (_showPrefabSelector)
            {
                _prefabSelectorRect = GUILayout.Window(1002, _prefabSelectorRect, DrawPrefabSelector, "Prefab Selector");
            }
        }
        
        private void DrawExportWindow(int windowID)
        {
            GUILayout.BeginVertical(GUILayout.ExpandHeight(true));
            
            GUILayout.Label("Export Settings", GUI.skin.box);
            GUILayout.Space(10);
            
            // Toggle for including children
            _includeChildren = GUILayout.Toggle(_includeChildren, "Include Children");
            
            GUILayout.Space(20);
            
            // Get selected objects
            HashSet<GameObject> selectedObjects = SimpleExporter.GetCurrentlyOpenItems();
            
            // Display selected objects
            GUILayout.Label($"Selected Objects: {selectedObjects.Count}", GUI.skin.box);
            
            foreach (var obj in selectedObjects)
            {
                GUILayout.Label(obj.name);
            }
            
            GUILayout.FlexibleSpace();
            
            // Format selection
            GUILayout.BeginHorizontal();
            GUILayout.Label("Format:", GUILayout.Width(60));
            if (GUILayout.Toggle(!SimpleExporter.glb, "GLTF", GUILayout.Width(60)))
            {
                SimpleExporter.glb = false;
            }
            if (GUILayout.Toggle(SimpleExporter.glb, "GLB", GUILayout.Width(60)))
            {
                SimpleExporter.glb = true;
            }
            GUILayout.EndHorizontal();
            
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
            
            // Make the window draggable
            GUI.DragWindow();
        }
        
        private void DrawPrefabSelector(int windowID)
        {
            GUILayout.BeginVertical();
            
            // Search field
            GUILayout.BeginHorizontal();
            GUILayout.Label("Search:", GUILayout.Width(60));
            _searchText = GUILayout.TextField(_searchText, GUILayout.Width(250));
            if (GUILayout.Button("Clear", GUILayout.Width(60)))
            {
                _searchText = "";
            }
            GUILayout.EndHorizontal();
            
            // Category selector
            GUILayout.BeginHorizontal();
            GUILayout.Label("Category:", GUILayout.Width(60));
            
            if (GUILayout.Button(_selectedCategory, GUILayout.Width(150)))
            {
                // Toggle category dropdown in a real implementation
            }
            
            GUILayout.EndHorizontal();
            
            // Ensure bundle loader is available
            SimpleBundleLoader bundleLoader = SimpleBundleLoader.Instance;
            if (bundleLoader == null)
            {
                GUILayout.Label("Bundle loader not available");
                return;
            }
            
            // Get available prefabs
            var allPrefabs = bundleLoader.GetAvailablePrefabs();
            var filteredPrefabs = FilterPrefabs(allPrefabs, _searchText, _selectedCategory);
            
            GUILayout.Label($"Found {filteredPrefabs.Count} prefabs matching criteria", GUI.skin.box);
            
            // Prefab list
            _prefabScrollPosition = GUILayout.BeginScrollView(_prefabScrollPosition, GUILayout.Height(300));
            
            foreach (var prefab in filteredPrefabs)
            {
                GUILayout.BeginHorizontal();
                
                if (GUILayout.Button(prefab.DisplayName, GUILayout.Height(30)))
                {
                    SpawnPrefab(prefab);
                    _showPrefabSelector = false;
                }
                
                GUILayout.EndHorizontal();
            }
            
            GUILayout.EndScrollView();
            
            // Refresh button
            if (GUILayout.Button("Refresh Bundles"))
            {
                bundleLoader.RefreshBundles();
            }
            
            // Close button
            if (GUILayout.Button("Close"))
            {
                _showPrefabSelector = false;
            }
            
            GUILayout.EndVertical();
            
            // Make the window draggable
            GUI.DragWindow();
        }
        
        private List<SimpleBundleLoader.BundlePrefabInfo> FilterPrefabs(
            List<SimpleBundleLoader.BundlePrefabInfo> prefabs, 
            string searchText, 
            string category)
        {
            List<SimpleBundleLoader.BundlePrefabInfo> result = new List<SimpleBundleLoader.BundlePrefabInfo>();
            
            foreach (var prefab in prefabs)
            {
                // Skip if doesn't match category (except for "All" category)
                if (category != "All" && !prefab.Path.Contains(category))
                {
                    continue;
                }
                
                // Skip if doesn't match search text
                if (!string.IsNullOrEmpty(searchText) && 
                    !prefab.DisplayName.ToLower().Contains(searchText.ToLower()))
                {
                    continue;
                }
                
                result.Add(prefab);
            }
            
            return result;
        }
        
        private void SpawnPrefab(SimpleBundleLoader.BundlePrefabInfo prefab)
        {
            // Find camera for positioning
            Camera mainCamera = Camera.main;
            if (mainCamera == null)
            {
                mainCamera = FindObjectOfType<Camera>();
                if (mainCamera == null)
                {
                    Plugin.Log.LogWarning("No camera found to position spawned object");
                    return;
                }
            }
            
            // Position in front of camera
            Vector3 spawnPos = mainCamera.transform.position + mainCamera.transform.forward * 3f;
            Quaternion spawnRotation = mainCamera.transform.rotation;
            
            // Spawn the prefab
            SimpleBundleLoader.Instance.SpawnPrefabInScene(
                prefab.BundleName,
                prefab.AssetName,
                spawnPos,
                spawnRotation
            );
        }
        
        private void ExportSelectedObjects(HashSet<GameObject> selectedObjects)
        {
            if (selectedObjects.Count == 0)
            {
                Debug.LogWarning("No objects selected for export");
                return;
            }
            
            // Process each selected object
            foreach (GameObject selectedObject in selectedObjects)
            {
                // Create a set of GameObjects to export
                HashSet<GameObject> objectsToExport = new HashSet<GameObject>();
                objectsToExport.Add(selectedObject);
                
                // Include children if option is enabled
                if (_includeChildren)
                {
                    foreach (Transform child in selectedObject.GetComponentsInChildren<Transform>(true))
                    {
                        if (child.gameObject != selectedObject)
                        {
                            objectsToExport.Add(child.gameObject);
                        }
                    }
                }
                
                // Get the scene name
                string sceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
                if (string.IsNullOrEmpty(sceneName))
                {
                    sceneName = "DefaultScene";
                }
                
                // Create output directory
                string exportDirectory = Path.Combine(Plugin.OutputDir.Value, sceneName, selectedObject.name);
                
                // Create filename
                string filename = selectedObject.name + (SimpleExporter.glb ? ".glb" : ".gltf");
                
                // Export the objects
                SimpleExporter.Export(objectsToExport, exportDirectory, filename);
            }
        }
    }
}