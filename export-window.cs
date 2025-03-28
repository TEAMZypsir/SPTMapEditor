using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace TarkinItemExporter
{
    public static class SimpleExporter
    {
        // Flag to control binary (glb) vs text (gltf) format
        public static bool glb = false;
        
        // Export a set of GameObjects to GLTF/GLB
        public static void Export(HashSet<GameObject> objects, string outputDirectory, string filename)
        {
            if (objects == null || objects.Count == 0)
            {
                Plugin.Log.LogError("No objects to export");
                return;
            }
            
            try
            {
                // Ensure output directory exists
                Directory.CreateDirectory(outputDirectory);
                string outputPath = Path.Combine(outputDirectory, filename);
                
                // Generate a simple JSON representation of the objects
                // This is a placeholder for actual GLTF/GLB exporter functionality
                // In a real implementation, you would use a library like UnityGLTF
                ExportSimpleJson(objects, outputPath);
                
                Plugin.Log.LogInfo($"Exported {objects.Count} objects to {outputPath}");
                
                // Display notification message in-game if possible
                DisplayExportNotification(outputPath);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"Error exporting objects: {ex.Message}\n{ex.StackTrace}");
            }
        }
        
        // Simple JSON export as a placeholder
        private static void ExportSimpleJson(HashSet<GameObject> objects, string outputPath)
        {
            using (StreamWriter writer = new StreamWriter(outputPath))
            {
                writer.WriteLine("{");
                writer.WriteLine("  \"objects\": [");
                
                int count = 0;
                foreach (GameObject obj in objects)
                {
                    writer.WriteLine("    {");
                    writer.WriteLine($"      \"name\": \"{obj.name}\",");
                    writer.WriteLine($"      \"position\": [{obj.transform.position.x}, {obj.transform.position.y}, {obj.transform.position.z}],");
                    writer.WriteLine($"      \"rotation\": [{obj.transform.rotation.eulerAngles.x}, {obj.transform.rotation.eulerAngles.y}, {obj.transform.rotation.eulerAngles.z}],");
                    writer.WriteLine($"      \"scale\": [{obj.transform.localScale.x}, {obj.transform.localScale.y}, {obj.transform.localScale.z}]");
                    
                    if (count < objects.Count - 1)
                        writer.WriteLine("    },");
                    else
                        writer.WriteLine("    }");
                    
                    count++;
                }
                
                writer.WriteLine("  ]");
                writer.WriteLine("}");
            }
            
            // In a real implementation, you would export a proper GLTF/GLB file here
            Plugin.Log.LogInfo($"Note: This is a placeholder JSON export. For actual GLTF export, implement UnityGLTF integration.");
        }
        
        // Get currently selected objects
        public static HashSet<GameObject> GetCurrentlyOpenItems()
        {
            HashSet<GameObject> selectedObjects = new HashSet<GameObject>();
            
            // Try to get currently selected objects from TransformCacher
            try
            {
                var transformCacher = UnityEngine.Object.FindObjectOfType<TransformCacher.TransformCacher>();
                if (transformCacher != null)
                {
                    // Use reflection to access _currentInspectedObject field
                    var field = typeof(TransformCacher.TransformCacher).GetField("_currentInspectedObject", 
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    
                    if (field != null)
                    {
                        GameObject inspectedObject = field.GetValue(transformCacher) as GameObject;
                        if (inspectedObject != null)
                        {
                            selectedObjects.Add(inspectedObject);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"Could not get current inspected object from TransformCacher: {ex.Message}");
            }
            
            // If no objects selected, use the currently selected object in the scene
            if (selectedObjects.Count == 0)
            {
                try
                {
                    GameObject selectedObject = Selection.GetSelectedGameObject();
                    if (selectedObject != null)
                    {
                        selectedObjects.Add(selectedObject);
                    }
                }
                catch (Exception)
                {
                    // Selection utility might not be available
                }
            }
            
            return selectedObjects;
        }
        
        // Helper for getting selected GameObject
        private static class Selection
        {
            public static GameObject GetSelectedGameObject()
            {
                // Try to get the active GameObject using Unity's Selection if available
                try
                {
                    var unityEngine = typeof(UnityEngine.Object).Assembly;
                    var selectionType = unityEngine.GetType("UnityEngine.Selection");
                    
                    if (selectionType != null)
                    {
                        var activeGameObjectProperty = selectionType.GetProperty("activeGameObject");
                        if (activeGameObjectProperty != null)
                        {
                            return activeGameObjectProperty.GetValue(null) as GameObject;
                        }
                    }
                }
                catch
                {
                    // Unity's Selection API might not be available
                }
                
                // Fallback: Return the first active GameObject in the scene
                return GameObject.FindObjectOfType<GameObject>();
            }
        }
        
        // Display a notification that the export was successful
        private static void DisplayExportNotification(string outputPath)
        {
            Debug.Log($"<color=green>Export Successful:</color> Objects exported to {outputPath}");
            
            // Try to show a GUI notification
            try
            {
                // Create a temporary GameObject for showing notification
                GameObject notificationObj = new GameObject("ExportNotification");
                var notification = notificationObj.AddComponent<ExportNotificationDisplay>();
                notification.Initialize(outputPath);
                UnityEngine.Object.DontDestroyOnLoad(notificationObj);
            }
            catch (Exception)
            {
                // Cannot create notification object, just log to console
            }
        }
        
        // Small helper class for showing a temporary notification
        private class ExportNotificationDisplay : MonoBehaviour
        {
            private string message;
            private float displayTime = 3.0f;
            private float timer = 0f;
            
            public void Initialize(string path)
            {
                message = $"Export Successful: {Path.GetFileName(path)}";
                timer = 0f;
            }
            
            private void Update()
            {
                timer += Time.deltaTime;
                if (timer > displayTime)
                {
                    Destroy(gameObject);
                }
            }
            
            private void OnGUI()
            {
                // Create a box at the top of the screen
                GUIStyle style = new GUIStyle(GUI.skin.box);
                style.normal.textColor = Color.green;
                style.fontSize = 16;
                style.alignment = TextAnchor.MiddleCenter;
                
                GUI.Box(new Rect(Screen.width/2 - 200, 20, 400, 40), message, style);
            }
        }
    }
}