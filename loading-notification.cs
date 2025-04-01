using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace TransformCacher
{
    /// <summary>
    /// Handles displaying loading notifications during scene transitions
    /// and tracking errors that occur during the loading process
    /// </summary>
    public class LoadingNotification : MonoBehaviour
    {
        private static LoadingNotification _instance;
        public static LoadingNotification Instance
        {
            get
            {
                if (_instance == null)
                {
                    GameObject go = new GameObject("LoadingNotification");
                    DontDestroyOnLoad(go);
                    _instance = go.AddComponent<LoadingNotification>();
                }
                return _instance;
            }
        }

        // Logger
        private static BepInEx.Logging.ManualLogSource Logger;

        // Current notification message
        private string _currentMessage = "";
        public string CurrentMessage => _currentMessage;

        // Error tracking
        private Dictionary<string, List<string>> _errors = new Dictionary<string, List<string>>();
        private bool _hasErrors = false;
        public bool HasErrors => _hasErrors;

        // Status tracking
        private Dictionary<string, int> _processedObjects = new Dictionary<string, int>();
        private Dictionary<string, int> _failedObjects = new Dictionary<string, int>();

        // UI State
        private bool _showErrorPopup = false;
        private Vector2 _errorScrollPosition = Vector2.zero;
        private Rect _errorPopupRect = new Rect(Screen.width / 2 - 300, Screen.height / 2 - 200, 600, 400);

        // Initialize the notification system
        public void Initialize()
        {
            Logger = BepInEx.Logging.Logger.CreateLogSource("LoadingNotification");
            Logger.LogInfo("Loading notification system initialized");
            
            // Clear previous errors
            _errors.Clear();
            _hasErrors = false;
            _processedObjects.Clear();
            _failedObjects.Clear();
        }

        // Set the current loading notification message
        public void SetLoadingMessage(string message)
        {
            _currentMessage = message;
            Logger.LogInfo($"Loading status: {message}");
        }

        // Add an error for a specific phase
        public void AddError(string phase, string errorMessage)
        {
            if (!_errors.ContainsKey(phase))
            {
                _errors[phase] = new List<string>();
            }
            
            _errors[phase].Add(errorMessage);
            _hasErrors = true;
            
            Logger.LogWarning($"Error during {phase}: {errorMessage}");
        }

        // Track processed and failed objects
        public void TrackProgress(string phase, int processed, int failed = 0)
        {
            _processedObjects[phase] = processed;
            
            if (failed > 0)
            {
                _failedObjects[phase] = failed;
                _hasErrors = true;
            }
        }

        // Show the error popup after loading completes
        public void ShowErrorPopupIfNeeded()
        {
            if (_hasErrors && TransformCacherPlugin.ShowErrorPopups != null && TransformCacherPlugin.ShowErrorPopups.Value)
            {
                Logger.LogInfo("Showing error popup");
                _showErrorPopup = true;
            }
            else if (_hasErrors)
            {
                // Log errors to console even if popup is disabled
                Logger.LogWarning("Errors occurred during loading, but popup is disabled by configuration");
                foreach (var phaseErrors in _errors)
                {
                    foreach (string error in phaseErrors.Value)
                    {
                        Logger.LogWarning($"[{phaseErrors.Key}] {error}");
                    }
                }
            }
        }

        // Check if any errors occurred during a specific phase
        public bool HasErrorsInPhase(string phase)
        {
            return _errors.ContainsKey(phase) && _errors[phase].Count > 0;
        }

        private void OnGUI()
        {
            // Display the current loading message in the game's loading screen
            // This assumes the loading message will be visible during scene transitions
            if (!string.IsNullOrEmpty(_currentMessage) && TransformCacherPlugin.EnableLoadingMessages != null && TransformCacherPlugin.EnableLoadingMessages.Value)
            {
                // Position in the lower part of the screen
                GUIStyle style = new GUIStyle(GUI.skin.label);
                style.fontSize = 18;
                style.normal.textColor = Color.white;
                style.alignment = TextAnchor.LowerCenter;
                
                GUI.Label(new Rect(0, Screen.height - 160, Screen.width, 30), 
                    $"TransformCacher: {_currentMessage}", style);
            }

            // Display error popup if needed
            if (_showErrorPopup && TransformCacherPlugin.ShowErrorPopups != null && TransformCacherPlugin.ShowErrorPopups.Value)
            {
                _errorPopupRect = GUI.Window(999, _errorPopupRect, DrawErrorPopup, "TransformCacher Loading Results");
            }
        }

        private void DrawErrorPopup(int windowID)
        {
            // Title
            GUILayout.Label("Loading Process Results", GUI.skin.box);
            
            // Begin scrollable content
            _errorScrollPosition = GUILayout.BeginScrollView(_errorScrollPosition, GUILayout.Height(300));

            // Display processing statistics
            GUILayout.Label("Processing Statistics:", GUI.skin.box);
            
            foreach (var entry in _processedObjects)
            {
                string phase = entry.Key;
                int processed = entry.Value;
                int failed = _failedObjects.ContainsKey(phase) ? _failedObjects[phase] : 0;
                
                // Colored status based on errors
                if (failed > 0)
                {
                    GUILayout.Label($"{phase}: {processed} processed, <color=red>{failed} failed</color>", 
                        new GUIStyle(GUI.skin.label) { richText = true });
                }
                else
                {
                    GUILayout.Label($"{phase}: {processed} processed, <color=green>0 failed</color>", 
                        new GUIStyle(GUI.skin.label) { richText = true });
                }
            }
            
            // Display errors by phase
            if (_errors.Count > 0)
            {
                GUILayout.Space(10);
                GUILayout.Label("Errors by Phase:", GUI.skin.box);
                
                foreach (var phaseErrors in _errors)
                {
                    if (phaseErrors.Value.Count > 0)
                    {
                        GUILayout.Label($"{phaseErrors.Key} ({phaseErrors.Value.Count} errors):", 
                            new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold });
                        
                        // List the first 20 errors (to avoid excessive text)
                        int count = 0;
                        foreach (string error in phaseErrors.Value.Take(20))
                        {
                            GUILayout.Label($"• {error}", 
                                new GUIStyle(GUI.skin.label) { wordWrap = true });
                            count++;
                        }
                        
                        // If there are more, show a message
                        if (phaseErrors.Value.Count > 20)
                        {
                            GUILayout.Label($"... and {phaseErrors.Value.Count - 20} more errors", 
                                new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Italic });
                        }
                    }
                }
            }
            
            GUILayout.EndScrollView();
            
            // Close button
            if (GUILayout.Button("Close", GUILayout.Height(30)))
            {
                _showErrorPopup = false;
            }
            
            // Make the window draggable
            GUI.DragWindow(new Rect(0, 0, _errorPopupRect.width, 20));
        }
    }
}