using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
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

        // EFT notification manager (accessed via reflection)
        private object _notificationManager;
        private MethodInfo _notificationMethod;
        private Type _notificationClass;
        private bool _eftNotificationAvailable = false;

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
            
            // Try to set up EFT notification integration
            SetupEFTNotification();
        }

        private void SetupEFTNotification()
        {
            try
            {
                // Look for the NotificationManagerClass
                Type managerClass = Type.GetType("EFT.Communications.NotificationManagerClass, Assembly-CSharp");
                if (managerClass == null)
                {
                    Logger.LogWarning("Could not find EFT NotificationManagerClass type");
                    return;
                }
                
                // Look for the NotificationAbstractClass
                _notificationClass = Type.GetType("EFT.Communications.NotificationAbstractClass, Assembly-CSharp");
                if (_notificationClass == null)
                {
                    Logger.LogWarning("Could not find EFT NotificationAbstractClass type");
                    return;
                }
                
                // Look for NotifierView
                Type notifierViewType = Type.GetType("EFT.UI.NotifierView, Assembly-CSharp");
                if (notifierViewType == null)
                {
                    Logger.LogWarning("Could not find EFT NotifierView type");
                    return;
                }
                
                // Try to get instance of notification manager
                PropertyInfo instanceProperty = managerClass.GetProperty("Instance", 
                    BindingFlags.Public | BindingFlags.Static);
                if (instanceProperty == null)
                {
                    Logger.LogWarning("Could not find Instance property on NotificationManagerClass");
                    return;
                }
                
                // Find the notification method 
                _notificationMethod = managerClass.GetMethod("AddNotification", 
                    BindingFlags.Public | BindingFlags.Instance);
                if (_notificationMethod == null)
                {
                    Logger.LogWarning("Could not find AddNotification method on NotificationManagerClass");
                    return;
                }
                
                // Setup was successful
                _eftNotificationAvailable = true;
                Logger.LogInfo("Successfully set up EFT notification integration");
            }
            catch (Exception ex)
            {
                Logger.LogError($"Error setting up EFT notification: {ex.Message}");
                _eftNotificationAvailable = false;
            }
        }

        // Set the current loading notification message
        public void SetLoadingMessage(string message)
        {
            _currentMessage = message;
            Logger.LogInfo($"Loading status: {message}");
            
            // Try to show EFT notification if available
            if (!string.IsNullOrEmpty(message))
            {
                SendEFTNotification("Transform Cacher", message, false);
            }
        }

        // Send a notification using EFT's system if available
        private void SendEFTNotification(string title, string text, bool isError)
        {
            if (!_eftNotificationAvailable || _notificationClass == null || _notificationMethod == null)
                return;
                
            try
            {
                // Get the notification manager instance through reflection
                object manager = GetNotificationManager();
                if (manager == null)
                {
                    Logger.LogWarning("Could not get NotificationManagerClass instance");
                    return;
                }
                
                // Create custom notification object using reflection
                object notification = CreateCustomNotification(title, text, isError);
                if (notification == null)
                {
                    Logger.LogWarning("Could not create custom notification");
                    return;
                }
                
                // Call the notification method
                _notificationMethod.Invoke(manager, new object[] { notification });
                Logger.LogInfo($"Sent EFT notification: {title} - {text}");
            }
            catch (Exception ex)
            {
                Logger.LogError($"Error sending EFT notification: {ex.Message}");
            }
        }
        
        // Get the notification manager instance using reflection
        private object GetNotificationManager()
        {
            try
            {
                // Look for the NotificationManagerClass
                Type managerClass = Type.GetType("EFT.Communications.NotificationManagerClass, Assembly-CSharp");
                if (managerClass == null)
                    return null;
                    
                // Try to get instance using Singleton class
                Type singletonType = Type.GetType("Comfort.Common.Singleton`1, Comfort.Common");
                if (singletonType == null)
                    return null;
                    
                // Create generic Singleton<NotificationManagerClass> type
                Type singletonManagerType = singletonType.MakeGenericType(managerClass);
                
                // Get Instance property
                PropertyInfo instanceProperty = singletonManagerType.GetProperty("Instance", 
                    BindingFlags.Public | BindingFlags.Static);
                if (instanceProperty == null)
                    return null;
                    
                // Get the instance
                return instanceProperty.GetValue(null, null);
            }
            catch (Exception ex)
            {
                Logger.LogError($"Error getting notification manager: {ex.Message}");
                return null;
            }
        }
        
        // Create a custom notification using reflection
        private object CreateCustomNotification(string title, string text, bool isError)
        {
            try
            {
                // Create a simple notification type
                Type simpleNotificationType = Type.GetType("EFT.Communications.SimpleNotification, Assembly-CSharp");
                if (simpleNotificationType == null)
                {
                    Logger.LogWarning("Could not find SimpleNotification type");
                    return null;
                }
                
                // Create instance with constructor
                object notification = Activator.CreateInstance(simpleNotificationType);
                
                // Set properties using reflection
                PropertyInfo titleProp = simpleNotificationType.GetProperty("Title");
                PropertyInfo textProp = simpleNotificationType.GetProperty("Text");
                PropertyInfo showProp = simpleNotificationType.GetProperty("ShowNotification");
                
                if (titleProp != null)
                    titleProp.SetValue(notification, title, null);
                    
                if (textProp != null)
                    textProp.SetValue(notification, text, null);
                    
                if (showProp != null)
                    showProp.SetValue(notification, true, null);
                    
                return notification;
            }
            catch (Exception ex)
            {
                Logger.LogError($"Error creating custom notification: {ex.Message}");
                return null;
            }
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
            
            // Try to show EFT notification if available
            SendEFTNotification($"Error: {phase}", errorMessage, true);
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
            
            // Show a notification with the progress
            string message = $"{phase}: {processed} processed";
            if (failed > 0) {
                message += $", {failed} failed";
                SendEFTNotification(phase, message, true);
            }
            else if (processed > 0 && processed % 50 == 0) {
                // Only show periodic updates for successful operations
                SendEFTNotification(phase, message, false);
            }
        }

        // Show the error popup after loading completes
        public void ShowErrorPopupIfNeeded()
        {
            if (_hasErrors && TransformCacherPlugin.ShowErrorPopups != null && TransformCacherPlugin.ShowErrorPopups.Value)
            {
                Logger.LogInfo("Showing error popup");
                
                // Send a summary notification
                foreach (var phaseErrors in _errors)
                {
                    SendEFTNotification(
                        "Transform Cacher Errors", 
                        $"{phaseErrors.Key}: {phaseErrors.Value.Count} errors", 
                        true);
                }
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
            // Only display the loading message if enabled and not using EFT notifications
            if (!string.IsNullOrEmpty(_currentMessage) && 
                TransformCacherPlugin.EnableLoadingMessages != null && 
                TransformCacherPlugin.EnableLoadingMessages.Value && 
                !_eftNotificationAvailable)
            {
                // Position in the lower part of the screen
                GUIStyle style = new GUIStyle(GUI.skin.label);
                style.fontSize = 18;
                style.normal.textColor = Color.white;
                style.alignment = TextAnchor.LowerCenter;
                
                GUI.Label(new Rect(0, Screen.height - 160, Screen.width, 30), 
                    $"TransformCacher: {_currentMessage}", style);
            }

            // Display error popup if needed and EFT notifications are not available
            if (_showErrorPopup && TransformCacherPlugin.ShowErrorPopups != null && 
                TransformCacherPlugin.ShowErrorPopups.Value && !_eftNotificationAvailable)
            {
                _errorPopupRect = GUI.Window(999, _errorPopupRect, DrawErrorPopup, "TransformCacher Loading Results");
            }
        }

        // UI State for error popup
        private bool _showErrorPopup = false;
        private Vector2 _errorScrollPosition = Vector2.zero;
        private Rect _errorPopupRect = new Rect(Screen.width / 2 - 300, Screen.height / 2 - 200, 600, 400);

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