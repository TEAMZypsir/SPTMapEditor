using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TransformCacher
{
    public class UIManager : MonoBehaviour
    {
        // UI State
        private bool _showExportWindow = false;
        private bool _includeChildren = true;
        private Vector2 _exportWindowScrollPosition = Vector2.zero;
        
        // Reference to the bundle loader
        private BundleLoader _bundleLoader;
        
        // UI Positions
        private Rect _exportWindowRect = new Rect(20, 60, 400, 300);
        
        // Export options
        private string _customFilename = "";
        private bool _useGlb = false;
        
        // References
        private ExporterComponent _exporter;
        private TransformCacher _transformCacher;
        
        // Hotkey for export window
        private KeyCode _exportHotkey = KeyCode.E; // Ctrl+E
        
        public void Initialize()
        {
            _exporter = GetComponent<ExporterComponent>();
            if (_exporter == null)
            {
                _exporter = gameObject.AddComponent<ExporterComponent>();
                _exporter.Initialize();
            }
            
            _transformCacher = GetComponent<TransformCacher>();
            
            // Get reference to the bundle loader
            _bundleLoader = GetComponent<BundleLoader>();
            if (_bundleLoader == null)
            {
                _bundleLoader = FindObjectOfType<BundleLoader>();
            }
            
            TransformCacherPlugin.Log.LogInfo("UIManager initialized");
        }
        
        private void Update()
        {
            // Toggle export window with Ctrl+E
            if (Input.GetKey(KeyCode.LeftControl) && Input.GetKeyDown(_exportHotkey))
            {
                _showExportWindow = !_showExportWindow;
                TransformCacherPlugin.Log.LogInfo($"Export window toggled: {_showExportWindow}");
            }
        }
        
        private void OnGUI()
        {
            // Draw export window if visible
            if (_showExportWindow)
            {
                _exportWindowRect = GUILayout.Window(1001, _exportWindowRect, DrawExportWindow, "Export Objects");
            }
            
            // Add a small button in the corner when export window is not shown
            if (!_showExportWindow)
            {
                if (GUI.Button(new Rect(Screen.width - 120, 10, 110, 30), "Export Model"))
                {
                    _showExportWindow = true;
                }
            }
        }
        
        private void DrawExportWindow(int windowID)
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
            
            bool newUseGlb = GUILayout.Toggle(_useGlb, "GLB (Binary)", GUILayout.Width(100));
            if (newUseGlb != _useGlb)
            {
                _useGlb = newUseGlb;
                _exporter.UseGlbFormat = _useGlb;
            }
            
            GUILayout.Toggle(!_useGlb, "GLTF (Text)", GUILayout.Width(100));
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
            
            // Display export status if in progress
            if (_exporter.ExportInProgress)
            {
                GUILayout.Label(_exporter.ExportStatus);
                
                // Draw progress bar
                Rect progressRect = GUILayoutUtility.GetRect(100, 20);
                EditorGUI.ProgressBar(progressRect, _exporter.ExportProgress, 
                    $"{Mathf.RoundToInt(_exporter.ExportProgress * 100)}%");
            }
            
            // Export button
            GUI.enabled = selectedObjects.Count > 0 && !_exporter.ExportInProgress;
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
            
            // Get objects from exporter
            List<GameObject> openItems = _exporter != null ? 
                _exporter.GetCurrentlyOpenItems() : 
                new List<GameObject>();
            
            foreach (var item in openItems)
            {
                if (item != null)
                {
                    result.Add(item);
                    
                    // Add children if needed
                    if (_includeChildren)
                    {
                        foreach (Transform child in item.transform)
                        {
                            if (child.gameObject != item)
                            {
                                result.Add(child.gameObject);
                            }
                        }
                    }
                }
            }
            
            return result;
        }
        
        private void ExportSelectedObjects(HashSet<GameObject> selectedObjects)
        {
            if (selectedObjects.Count == 0)
            {
                TransformCacherPlugin.Log.LogWarning("No objects selected for export");
                return;
            }
            
            // Generate filename if not specified
            string filename = string.IsNullOrEmpty(_customFilename) ? 
                selectedObjects.First().name : _customFilename;
                
            // Add extension if not present
            if (!filename.EndsWith(".glb") && !filename.EndsWith(".gltf"))
            {
                filename += _useGlb ? ".glb" : ".gltf";
            }
            
            // Export
            _exporter.Export(selectedObjects, filename);
        }
        
        // UI helper for progress bar
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
    }}