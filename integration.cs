using System;
using System.Collections.Generic;
using UnityEngine;
using BepInEx;

namespace TransformCacher
{
    /// <summary>
    /// This class provides integration points between TransformCacher and TarkinItemExporter
    /// It should be added to the same GameObject as TransformCacher
    /// </summary>
    public class ExporterIntegration : MonoBehaviour
    {
        private TransformCacher _transformCacher;
        private DatabaseManager _databaseManager;
        
        // UI state
        private bool _showExportButton = true;
        private Rect _exportButtonRect = new Rect(10, 50, 120, 30);
        
        private void Awake()
        {
            try
            {
                // Find TransformCacher component
                _transformCacher = GetComponent<TransformCacher>();
                
                if (_transformCacher == null)
                {
                    Debug.LogError("ExporterIntegration must be attached to the same GameObject as TransformCacher");
                    return;
                }
                
                // Get database manager
                _databaseManager = DatabaseManager.Instance;
                
                // Log success
                BepInEx.Logging.Logger.CreateLogSource("ExporterIntegration").LogInfo("ExporterIntegration initialized successfully");
            }
            catch (Exception ex)
            {
                Debug.LogError($"Failed to initialize ExporterIntegration: {ex.Message}\n{ex.StackTrace}");
            }
        }
        
        private void OnGUI()
        {
            if (_showExportButton)
            {
                // Draw a button to open the export window if TarkinItemExporter is available
                if (GUI.Button(_exportButtonRect, "Open Exporter"))
                {
                    OpenExporter();
                }
            }
        }
        
        private void OpenExporter()
        {
            try
            {
                // Try to find the TarkinItemExporter assembly
                var assembly = AppDomain.CurrentDomain.GetAssemblies();
                Type uiManagerType = null;
                
                foreach (var asm in assembly)
                {
                    if (asm.GetName().Name.Contains("TransformCacher"))
                    {
                        uiManagerType = asm.GetType("TarkinItemExporter.UI.SimpleUIManager");
                        if (uiManagerType != null)
                            break;
                    }
                }
                
                if (uiManagerType != null)
                {
                    // Get the Instance property
                    var instanceProperty = uiManagerType.GetProperty("Instance", 
                        System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                    
                    if (instanceProperty != null)
                    {
                        // Get the UI manager instance
                        var uiManager = instanceProperty.GetValue(null);
                        
                        // Toggle the export window using reflection
                        var field = uiManagerType.GetField("_showExportWindow", 
                            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        
                        if (field != null)
                        {
                            // Toggle the export window
                            bool currentValue = (bool)field.GetValue(uiManager);
                            field.SetValue(uiManager, !currentValue);
                        }
                    }
                }
                else
                {
                    Debug.LogWarning("TarkinItemExporter UI manager not found. Make sure the mod is installed.");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error opening exporter: {ex.Message}");
            }
        }
        
        // Method to get currently selected object from TransformCacher
        // This is used by TarkinItemExporter
        public GameObject GetCurrentInspectedObject()
        {
            if (_transformCacher != null)
            {
                // Use reflection to get _currentInspectedObject field
                var field = typeof(TransformCacher).GetField("_currentInspectedObject", 
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                
                if (field != null)
                {
                    return field.GetValue(_transformCacher) as GameObject;
                }
            }
            
            return null;
        }
        
        // Helper method to get all tagged objects
        public List<GameObject> GetAllTaggedObjects()
        {
            List<GameObject> result = new List<GameObject>();
            
            if (_transformCacher != null)
            {
                // Find all TransformCacherTag components in the scene
                TransformCacherTag[] tags = GameObject.FindObjectsOfType<TransformCacherTag>();
                
                foreach (var tag in tags)
                {
                    if (tag != null && tag.gameObject != null && !tag.IsDestroyed)
                    {
                        result.Add(tag.gameObject);
                    }
                }
            }
            
            return result;
        }
    }
}