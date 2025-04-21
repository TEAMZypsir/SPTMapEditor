using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using EFT;
using EFT.UI;

namespace TransformCacher
{
    /// <summary>
    /// Enhanced notification system that directly hooks into Tarkov's UI
    /// </summary>
    public static class EnhancedNotification
    {
        // Logging
        private static BepInEx.Logging.ManualLogSource Logger;
        
        // Current notification message
        private static string _currentMessage = "";
        public static string CurrentMessage => _currentMessage;
        
        // Error tracking
        private static Dictionary<string, List<string>> _errors = new Dictionary<string, List<string>>();
        private static bool _hasErrors = false;
        public static bool HasErrors => _hasErrors;
        
        // Status tracking
        private static Dictionary<string, int> _processedObjects = new Dictionary<string, int>();
        private static Dictionary<string, int> _failedObjects = new Dictionary<string, int>();
        
        // EFT Game instance for status updates
        private static AbstractGame _gameInstance;
        
        // Native UI references - directly hooked into Tarkov's UI
        private static GameObject _deployingCaptionObj;
        private static EFT.UI.LocalizedText _deployingCaptionText;
        private static CanvasRenderer _deployingCaptionRenderer;
        private static RectTransform _deployingCaptionTransform;
        
        // Progress text references
        private static GameObject _loadingProgressObj;
        private static TextMeshProUGUI _loadingProgressText;
        private static CanvasRenderer _loadingProgressRenderer;
        
        // Original text content (to restore later)
        private static string _originalCaptionText;
        private static string _originalProgressText;
        
        // Flag to determine if we're in custom loading phase
        private static bool _isInCustomLoadingPhase = false;
        
        // Original alpha values
        private static float _originalCaptionAlpha;
        private static float _originalProgressAlpha;
        
        // Coroutines management
        private static MonoBehaviour _coroutineRunner; // We need a MonoBehaviour to run coroutines
        private static Coroutine _clearMessageCoroutine;

        // Add these new fields to store pending changes
        private static string _pendingMessage = null;
        private static float _pendingProgress = -1f;
        private static bool _hasQueuedChanges = false;
        
        /// <summary>
        /// Initialize the notification system
        /// </summary>
        public static void Initialize()
        {
            Logger = BepInEx.Logging.Logger.CreateLogSource("EnhancedNotification");
            Logger.LogInfo("Enhanced notification system initializing...");
            
            // Clear previous errors
            _errors.Clear();
            _hasErrors = false;
            _processedObjects.Clear();
            _failedObjects.Clear();
            
            // Find game instance
            FindGameInstance();
            
            // Create a temporary MonoBehaviour to run coroutines if needed
            if (_coroutineRunner == null)
            {
                GameObject coroutineObj = new GameObject("NotificationCoroutineRunner");
                UnityEngine.Object.DontDestroyOnLoad(coroutineObj);
                _coroutineRunner = coroutineObj.AddComponent<CoroutineRunner>();
                coroutineObj.hideFlags = HideFlags.HideInHierarchy;
            }
            
            // Find UI elements
            _coroutineRunner.StartCoroutine(FindDeployingCaption());
        }

        /// <summary>
        /// Helper class for running coroutines
        /// </summary>
        private class CoroutineRunner : MonoBehaviour { }

        /// <summary>
        /// Find the game instance for status updates
        /// </summary>
        private static void FindGameInstance()
        {
            try
            {
                // Try to get from Singleton
                Type singletonType = typeof(Comfort.Common.Singleton<AbstractGame>);
                if (singletonType != null)
                {
                    PropertyInfo instanceProperty = singletonType.GetProperty("Instance");
                    if (instanceProperty != null)
                    {
                        _gameInstance = instanceProperty.GetValue(null) as AbstractGame;
                    }
                }
                
                // Try with FindObjectOfType as fallback
                if (_gameInstance == null)
                {
                    _gameInstance = UnityEngine.Object.FindObjectOfType<AbstractGame>();
                }
                
                if (_gameInstance != null)
                {
                    Logger.LogInfo($"Found game instance: {_gameInstance.GetType().Name}");
                }
                else
                {
                    Logger.LogWarning("Could not find game instance");
                }
            }
            catch (Exception ex)
            {
                Logger.LogError($"Error finding game instance: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Find the Deploying Caption object in the scene by its exact path
        /// </summary>
        private static IEnumerator FindDeployingCaption()
        {
            // Wait a bit to ensure UI is loaded
            yield return new WaitForSeconds(1.0f);
            
            int attempts = 0;
            const int maxAttempts = 10;
            bool foundElements = false;
            
            while (attempts < maxAttempts && !foundElements)
            {
                attempts++;
                bool attemptSuccessful = false;
                GameObject captionObj = null;
                
                try
                {
                    // Try with the exact path the user specified
                    captionObj = GameObject.Find("DontDestroyOnLoad/Menu UI/UI/Matchmaker Time Has Come/Deploying Caption");
                    
                    if (captionObj == null)
                    {
                        // Try slight variations if exact path fails
                        captionObj = GameObject.Find("Menu UI/UI/Matchmaker Time Has Come/Deploying Caption");
                        
                        if (captionObj == null)
                        {
                            // Try find by parent object first
                            GameObject matchmakerObj = GameObject.Find("Matchmaker Time Has Come");
                            if (matchmakerObj != null)
                            {
                                Transform captionTrans = matchmakerObj.transform.Find("Deploying Caption");
                                if (captionTrans != null)
                                    captionObj = captionTrans.gameObject;
                            }
                        }
                    }
                    
                    if (captionObj != null)
                    {
                        _deployingCaptionObj = captionObj;
                        Logger.LogInfo($"Found Deploying Caption at path: {GetFullPath(_deployingCaptionObj.transform)}");
                        
                        // Get the required components
                        _deployingCaptionText = _deployingCaptionObj.GetComponent<EFT.UI.LocalizedText>();
                        _deployingCaptionRenderer = _deployingCaptionObj.GetComponent<CanvasRenderer>();
                        _deployingCaptionTransform = _deployingCaptionObj.GetComponent<RectTransform>();
                        
                        if (_deployingCaptionText != null)
                        {
                            // Store original text
                            var textComponent = _deployingCaptionText.GetComponent<TextMeshProUGUI>();
                            _originalCaptionText = textComponent?.text ?? "";
                            Logger.LogInfo($"Found Deploying Caption text: \"{_originalCaptionText}\"");
                            
                            // Continue with loading progress UI
                            attemptSuccessful = true;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogError($"Error finding UI elements: {ex.Message}");
                    attemptSuccessful = false;
                }
                
                if (attemptSuccessful)
                {
                    try
                    {
                        // Store original alpha if renderer available
                        if (_deployingCaptionRenderer != null)
                        {
                            _originalCaptionAlpha = _deployingCaptionRenderer.GetAlpha();
                        }
                        
                        // Now look for the loading progress
                        _loadingProgressObj = GameObject.Find("DontDestroyOnLoad/Menu UI/UI/Matchmaker Time Has Come/Loading Progress");
                        if (_loadingProgressObj == null)
                        {
                            _loadingProgressObj = GameObject.Find("Menu UI/UI/Matchmaker Time Has Come/Loading Progress");
                        }
                        
                        if (_loadingProgressObj != null)
                        {
                            _loadingProgressText = _loadingProgressObj.GetComponent<TextMeshProUGUI>();
                            _loadingProgressRenderer = _loadingProgressObj.GetComponent<CanvasRenderer>();
                            
                            if (_loadingProgressText != null)
                            {
                                _originalProgressText = _loadingProgressText.text;
                                Logger.LogInfo($"Found Loading Progress text: \"{_originalProgressText}\"");
                                
                                if (_loadingProgressRenderer != null)
                                {
                                    _originalProgressAlpha = _loadingProgressRenderer.GetAlpha();
                                }
                                
                                foundElements = true;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.LogError($"Error finding loading progress UI: {ex.Message}");
                    }
                }
                
                if (!foundElements)
                {
                    Logger.LogInfo($"Attempt {attempts}/{maxAttempts} to find UI elements");
                    yield return new WaitForSeconds(1.0f);
                }
            }
            
            if (!foundElements)
            {
                Logger.LogWarning("Could not find all required UI elements. Loading notifications may not display correctly.");
            }
            else
            {
                Logger.LogInfo("Successfully found all required UI elements for loading notifications");
            }
        }

        /// <summary>
        /// Begin custom loading phase
        /// </summary>
        public static void BeginCustomLoadingPhase()
        {
            _isInCustomLoadingPhase = true;
            
            // If we have a pending message clear coroutine, stop it
            if (_clearMessageCoroutine != null && _coroutineRunner != null)
            {
                _coroutineRunner.StopCoroutine(_clearMessageCoroutine);
                _clearMessageCoroutine = null;
            }
            
            // Make elements visible only if they're already active
            if (_deployingCaptionRenderer != null && _deployingCaptionObj != null && _deployingCaptionObj.activeInHierarchy)
            {
                _deployingCaptionRenderer.SetAlpha(1.0f);
            }
            
            if (_loadingProgressRenderer != null && _loadingProgressObj != null && _loadingProgressObj.activeInHierarchy)
            {
                _loadingProgressRenderer.SetAlpha(1.0f);
            }
            
            // Instead, just log if it's inactive so we're aware
            if (_deployingCaptionObj != null && _deployingCaptionObj.transform.parent != null && 
                !_deployingCaptionObj.transform.parent.gameObject.activeInHierarchy)
            {
                Logger.LogInfo("Custom loading phase began - UI parent is inactive, changes will apply when activated");
            }
            else
            {
                Logger.LogInfo("Custom loading phase began - taking over UI");
            }
            
            // Set initial message
            SetLoadingMessage("Preparing map modifications", 0);
            
            // Start the UI monitor if we need to apply changes when objects become active
            if (_coroutineRunner != null && _hasQueuedChanges)
            {
                _coroutineRunner.StartCoroutine(MonitorUIActivation());
            }
        }

        /// <summary>
        /// End custom loading phase and restore original UI
        /// </summary>
        public static void EndCustomLoadingPhase()
        {
            if (!_isInCustomLoadingPhase)
                return;
                
            // Restore original UI
            RestoreOriginalUI();
            _isInCustomLoadingPhase = false;
            
            Logger.LogInfo("Custom loading phase ended - returning control to game");
        }

        /// <summary>
        /// Set the current loading notification message
        /// </summary>
        public static void SetLoadingMessage(string message, float progress = -1f)
        {
            _currentMessage = message;
            
            // Store pending changes
            _pendingMessage = message;
            _pendingProgress = progress;
            _hasQueuedChanges = true;
            
            Logger.LogInfo($"Loading status: {message}" + (progress >= 0 ? $" ({progress:F0}%)" : ""));
            
            // Try to update UI elements
            UpdateLoadingUI(message, progress);
            
            // Also update game status for more complete integration
            UpdateGameStatus(message, progress >= 0 ? progress / 100f : null);
        }
        
        /// <summary>
        /// Update loading UI with our message only if the GameObjects are active
        /// </summary>
        private static void UpdateLoadingUI(string message, float progress = -1f)
        {
            try
            {
                // Update caption text if available and active
                if (_deployingCaptionText != null && _deployingCaptionObj != null && _deployingCaptionObj.activeInHierarchy)
                {
                    var textComponent = _deployingCaptionText.GetComponent<TextMeshProUGUI>();
                    if (textComponent != null)
                        textComponent.text = message;
                    
                    // Make sure it's visible
                    if (_deployingCaptionRenderer != null)
                    {
                        _deployingCaptionRenderer.SetAlpha(1.0f);
                        _deployingCaptionRenderer.cull = false;
                    }
                }
                
                // Update loading progress text if available, active, and progress is provided
                if (_loadingProgressText != null && _loadingProgressObj != null && 
                    _loadingProgressObj.activeInHierarchy && progress >= 0)
                {
                    string progressText = $"{progress:F0}%";
                    _loadingProgressText.text = progressText;
                    
                    // Make sure it's visible
                    if (_loadingProgressRenderer != null)
                    {
                        _loadingProgressRenderer.SetAlpha(1.0f);
                        _loadingProgressRenderer.cull = false;
                    }
                }
                
                // Force update the parent Canvas if active
                if (_deployingCaptionObj != null && _deployingCaptionObj.activeInHierarchy)
                {
                    ForceUpdateCanvas();
                }
            }
            catch (Exception ex)
            {
                Logger.LogError($"Error updating loading UI: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Monitor UI activation to apply pending changes when they become active
        /// </summary>
        private static IEnumerator MonitorUIActivation()
        {
            float checkInterval = 0.2f;
            float maxMonitorTime = 5f; // Reduced from 30 seconds to 5 seconds
            float elapsedTime = 0f;
            
            // Continue monitoring until we've applied changes or exceeded max time
            while (_hasQueuedChanges && elapsedTime < maxMonitorTime)
            {
                // Check if the UI elements exist first
                if (_deployingCaptionObj == null && _loadingProgressObj == null)
                {
                    // Both UI elements are missing, so bail out early
                    Logger.LogWarning("UI elements not found, cannot apply changes");
                    _hasQueuedChanges = false;
                    yield break;
                }
                
                // Check if the UI elements are now active
                bool captionActive = _deployingCaptionObj != null && _deployingCaptionObj.activeInHierarchy;
                bool progressActive = _loadingProgressObj != null && _loadingProgressObj.activeInHierarchy;
                
                // If either became active and we have pending changes, apply them
                if ((captionActive || progressActive) && _pendingMessage != null)
                {
                    UpdateLoadingUI(_pendingMessage, _pendingProgress);
                    
                    // If both are active or one is active and the other doesn't exist, consider changes applied
                    if ((captionActive && progressActive) || 
                        (captionActive && _loadingProgressObj == null) ||
                        (progressActive && _deployingCaptionObj == null))
                    {
                        _hasQueuedChanges = false;
                        Logger.LogInfo("Applied queued UI changes after UI activation");
                    }
                }
                
                yield return new WaitForSeconds(checkInterval);
                elapsedTime += checkInterval;
            }
            
            // If we couldn't apply changes after max time, log it
            if (_hasQueuedChanges)
            {
                Logger.LogWarning("Timed out waiting for UI to activate, some changes may not have been applied");
                _hasQueuedChanges = false; // Reset flag to avoid repeated warnings
            }
        }

        /// <summary>
        /// Force update Canvas to ensure text is visible
        /// </summary>
        private static void ForceUpdateCanvas()
        {
            try
            {
                if (_deployingCaptionObj != null)
                {
                    // Find parent Canvas
                    Canvas parentCanvas = _deployingCaptionObj.GetComponentInParent<Canvas>();
                    if (parentCanvas != null)
                    {
                        // Toggle canvas to force refresh
                        parentCanvas.enabled = false;
                        parentCanvas.enabled = true;
                        
                        // Force graphic rebuilds on any graphic elements
                        Graphic[] graphics = parentCanvas.GetComponentsInChildren<Graphic>(true);
                        foreach (Graphic graphic in graphics)
                        {
                            graphic.SetAllDirty();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogError($"Error forcing canvas update: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Update game status through AbstractGame
        /// </summary>
        private static void UpdateGameStatus(string message, float? progress = null)
        {
            if (_gameInstance == null) return;
            
            try
            {
                // Use the game's status update method
                _gameInstance.InvokeMatchingStatusChanged($"{message}", progress);
            }
            catch (Exception ex)
            {
                Logger.LogError($"Error updating game status: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Restore original UI text when we're done, but only for active objects
        /// </summary>
        public static void RestoreOriginalUI()
        {
            try
            {
                // Clear pending changes
                _pendingMessage = null;
                _pendingProgress = -1f;
                _hasQueuedChanges = false;
                
                // Restore caption text if active
                if (_deployingCaptionText != null && _deployingCaptionObj != null && 
                    _deployingCaptionObj.activeInHierarchy && !string.IsNullOrEmpty(_originalCaptionText))
                {
                    var textComponent = _deployingCaptionText.GetComponent<TextMeshProUGUI>();
                    if (textComponent != null)
                        textComponent.text = _originalCaptionText;
                    
                    // Restore original alpha
                    if (_deployingCaptionRenderer != null)
                    {
                        _deployingCaptionRenderer.SetAlpha(_originalCaptionAlpha);
                    }
                }
                
                // Restore loading progress text if active
                if (_loadingProgressText != null && _loadingProgressObj != null && 
                    _loadingProgressObj.activeInHierarchy && !string.IsNullOrEmpty(_originalProgressText))
                {
                    _loadingProgressText.text = _originalProgressText;
                    
                    // Restore original alpha
                    if (_loadingProgressRenderer != null)
                    {
                        _loadingProgressRenderer.SetAlpha(_originalProgressAlpha);
                    }
                }
                
                // Only update canvas if elements are active
                if ((_deployingCaptionObj != null && _deployingCaptionObj.activeInHierarchy) ||
                    (_loadingProgressObj != null && _loadingProgressObj.activeInHierarchy))
                {
                    ForceUpdateCanvas();
                    Logger.LogInfo("Restored original UI text");
                }
            }
            catch (Exception ex)
            {
                Logger.LogError($"Error restoring original UI: {ex.Message}");
            }
        }

        /// <summary>
        /// Add an error for a specific phase
        /// </summary>
        public static void AddError(string phase, string errorMessage)
        {
            if (!_errors.ContainsKey(phase))
            {
                _errors[phase] = new List<string>();
            }
            
            _errors[phase].Add(errorMessage);
            _hasErrors = true;
            
            Logger.LogWarning($"Error during {phase}: {errorMessage}");
            
            // Update UI with error info
            SetLoadingMessage($"Error in {phase}: {errorMessage}");
        }

        /// <summary>
        /// Track processed and failed objects
        /// </summary>
        public static void TrackProgress(string phase, int processed, int failed = 0, float progressPercent = -1f)
        {
            _processedObjects[phase] = processed;
            
            if (failed > 0)
            {
                _failedObjects[phase] = failed;
                _hasErrors = true;
            }
            
            // Show a notification with the progress
            string message = $"{phase}: {processed} processed";
            if (failed > 0)
            {
                message += $", {failed} failed";
            }
            
            // Update UI with both message and progress percentage
            SetLoadingMessage(message, progressPercent);
        }

        /// <summary>
        /// Show the error popup after loading completes
        /// </summary>
        public static void ShowErrorPopupIfNeeded()
        {
            if (_hasErrors)
            {
                // Prepare error summary
                int totalErrors = 0;
                foreach (var phaseErrors in _errors)
                {
                    totalErrors += phaseErrors.Value.Count;
                }
                
                SetLoadingMessage($"Completed with {totalErrors} errors", 100);
                
                // Keep error message visible for a moment
                _clearMessageCoroutine = _coroutineRunner.StartCoroutine(ClearMessageAfterDelay(5.0f));
            }
            else
            {
                // No errors - show completion message
                SetLoadingMessage("Map modifications applied successfully", 100);
                
                // Clear the message after a short delay
                _clearMessageCoroutine = _coroutineRunner.StartCoroutine(ClearMessageAfterDelay(3.0f));
            }
        }
        
        /// <summary>
        /// Clear message and restore UI after delay
        /// </summary>
        private static IEnumerator ClearMessageAfterDelay(float delay)
        {
            yield return new WaitForSeconds(delay);
            
            // Restore original UI
            RestoreOriginalUI();
            
            // Clear current message
            _currentMessage = "";
            
            // End custom loading phase
            if (_isInCustomLoadingPhase)
            {
                EndCustomLoadingPhase();
            }
            
            _clearMessageCoroutine = null;
        }

        /// <summary>
        /// Show a temporary message
        /// </summary>
        public static void ShowMessage(string message, float displayTime = 3.0f)
        {
            if (string.IsNullOrEmpty(message))
                return;
                
            _currentMessage = message;
            Logger.LogInfo($"Showing message: {message} (duration: {displayTime}s)");
            
            // Cancel any previous clear message coroutine
            if (_clearMessageCoroutine != null && _coroutineRunner != null)
            {
                _coroutineRunner.StopCoroutine(_clearMessageCoroutine);
                _clearMessageCoroutine = null;
            }
            
            // Update UI with the message
            UpdateLoadingUI(message);
            
            // Start coroutine to clear message after display time
            if (_coroutineRunner != null)
            {
                _clearMessageCoroutine = _coroutineRunner.StartCoroutine(ClearMessageAfterDelay(displayTime));
            }
        }

        /// <summary>
        /// Check if any errors occurred during a specific phase
        /// </summary>
        public static bool HasErrorsInPhase(string phase)
        {
            return _errors.ContainsKey(phase) && _errors[phase].Count > 0;
        }
        
        /// <summary>
        /// Get full path of transform for logging
        /// </summary>
        private static string GetFullPath(Transform transform)
        {
            if (transform == null)
                return "";
                
            string path = transform.name;
            Transform parent = transform.parent;
            
            while (parent != null)
            {
                path = parent.name + "/" + path;
                parent = parent.parent;
            }
            
            return path;
        }
    }
}