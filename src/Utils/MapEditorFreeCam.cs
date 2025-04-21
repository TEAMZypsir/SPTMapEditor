using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using EFT;
using Comfort.Common;
using BSG.CameraEffects;
using EFT.CameraControl;

namespace TransformCacher
{
    public class MapEditorFreeCam : MonoBehaviour
    {
        #region Properties
        public static MapEditorFreeCam Instance { get; private set; }
        public bool IsActive { get; private set; }
        public static bool UserPrefersFreeCamera { get; set; } = false;
        public bool IsAvailable => true; // Available in all scenes
        #endregion
        
        #region Settings
        private const float MOVEMENT_SPEED_NORMAL = 2.0f;
        private const float MOVEMENT_SPEED_FAST = 15.0f;
        private const float MOVEMENT_SPEED_TURBO = 50.0f;
        private const float LOOK_SENSITIVITY = 3.0f;
        private const float MIN_FOV = 10.0f;
        private const float FOV_CHANGE_SPEED = 100.0f;
        #endregion
        
        #region Private Fields
        private Camera _mainCamera;
        private Vector3 _originalPosition;
        private Quaternion _originalRotation;
        private float _originalFov;
        private float _yaw;
        private float _pitch;
        private bool _showUI = true;
        private Vector3 _lastPosition;
        private GameObject _cameraParent;
        private CameraLodBiasController _lodBiasController;
        
        // Key bindings
        private KeyCode _forwardKey = KeyCode.W;
        private KeyCode _backKey = KeyCode.S;
        private KeyCode _leftKey = KeyCode.A;
        private KeyCode _rightKey = KeyCode.D;
        private KeyCode _upKey = KeyCode.E;
        private KeyCode _downKey = KeyCode.Q;
        private KeyCode _toggleUIKey = KeyCode.U;
        
        // References to Tarkov systems
        private NightVision _nightVision;
        private ThermalVision _thermalVision;
        private Player _originalPlayer;
        private GamePlayerOwner _gamePlayerOwner;
        private PlayerCameraController _playerCameraController;
        private EPointOfView _originalPointOfView;
        private bool _wasPlayerActive = true;
        private bool _wasPhysicsEnabled = true;
        private static BepInEx.Logging.ManualLogSource _logger;
        private object _originalPositionLimit;
        #endregion
        
        #region Unity Lifecycle
        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            
            Instance = this;
            DontDestroyOnLoad(gameObject);
            
            _logger = BepInEx.Logging.Logger.CreateLogSource("MapEditorFreeCam");
            
            // Create a camera parent object
            _cameraParent = new GameObject("FreeCamParent");
            _cameraParent.transform.position = new Vector3(0, 100, 0);
            DontDestroyOnLoad(_cameraParent);
        }
        
        private void Start()
        {
            // Get the camera from CameraClass
            if (CameraClass.Instance != null)
            {
                _mainCamera = CameraClass.Instance.Camera;
                _logger.LogInfo($"Found camera from CameraClass: {_mainCamera?.name ?? "null"}");
            }
            
            // Fallback to Camera.main if CameraClass.Instance.Camera is null
            if (_mainCamera == null)
            {
                _mainCamera = Camera.main;
                _logger.LogInfo($"Using Camera.main: {_mainCamera?.name ?? "null"}");
            }
            
            // Last resort - find any camera
            if (_mainCamera == null)
            {
                _mainCamera = FindObjectOfType<Camera>();
                _logger.LogInfo($"Found camera via FindObjectOfType: {_mainCamera?.name ?? "null"}");
            }
            
            if (_mainCamera == null)
            {
                _logger.LogError("Could not find any camera!");
                enabled = false;
                return;
            }
            
            // Cache original values
            _originalFov = _mainCamera.fieldOfView;
            
            // Try to auto-activate if user preference is set
            if (UserPrefersFreeCamera)
            {
                ActivateFreeCam();
            }
        }
        
        private void Update()
        {
            // Handle UI toggle
            if (IsActive && Input.GetKeyDown(_toggleUIKey))
            {
                _showUI = !_showUI;
            }
            
            // Handle camera movement if active
            if (IsActive)
            {
                // Add this line to continuously prevent camera snapping
                if (_originalPlayer != null && CameraClass.Instance != null && 
                    Vector3.Distance(_cameraParent.transform.position, _originalPlayer.Transform.position) > 3.0f)
                {
                    // This prevents other code from repositioning the camera
                    CameraClass.Instance.ForceSetPosition(_cameraParent.transform.position);
                }
                
                HandleFreeCamMovement();
            }
        }
        
        private void OnDestroy()
        {
            if (IsActive)
            {
                DeactivateFreeCam();
            }
            
            if (_cameraParent != null)
            {
                Destroy(_cameraParent);
            }
            
            Instance = null;
        }
        
        void OnGUI()
        {
            // Show UI when either active OR when user prefers free camera
            if ((IsActive || UserPrefersFreeCamera) && _showUI)
            {
                DrawFreeCamUI();
            }
        }
        #endregion
        
        #region Public Methods
        /// <summary>
        /// Toggle free camera mode on or off
        /// </summary>
        public void ToggleFreeCam()
        {
            // Always update the user preference
            UserPrefersFreeCamera = !IsActive;
            
            if (TransformCacherPlugin.EnableFreeCamOnStartup != null)
            {
                TransformCacherPlugin.EnableFreeCamOnStartup.Value = UserPrefersFreeCamera;
            }
            
            if (IsActive)
            {
                DeactivateFreeCam();
            }
            else
            {
                ActivateFreeCam();
            }
            
            // Ensure user preference matches actual state
            UserPrefersFreeCamera = IsActive;
        }
        
        /// <summary>
        /// Directly disable occlusion culling
        /// </summary>
        public void DisableCulling()
        {
            try
            {
                if (CameraClass.Instance != null)
                {
                    CameraClass.Instance.SetOcclusionCullingEnabled(false);
                    _logger.LogInfo("Disabled occlusion culling");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to disable culling: {ex}");
            }
        }
        
        /// <summary>
        /// Set the camera to a specific position and rotation
        /// </summary>
        public void SetCameraPosition(Vector3 position, Quaternion rotation)
        {
            if (!IsActive)
            {
                ActivateFreeCam();
            }
            
            _cameraParent.transform.position = position;
            _cameraParent.transform.rotation = rotation;
            
            // Update tracking values
            _yaw = _cameraParent.transform.eulerAngles.y;
            _pitch = -_cameraParent.transform.eulerAngles.x;
            _lastPosition = position;
        }

        /// <summary>
        /// Check the current scene and handle free camera activation based on user preference
        /// </summary>
        public void CheckCurrentScene()
        {
            string currentSceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            
            // If logging is enabled, log the current scene
            if (_logger != null)
            {
                _logger.LogInfo($"Free camera detected scene change to: {currentSceneName}");
            }
            
            // The free camera is universally available now
            // so we don't need to check for specific scenes
            
            // If user has set preference to have camera active, activate it
            if (UserPrefersFreeCamera && !IsActive)
            {
                _logger.LogInfo("Auto-activating free camera based on user preference");
                ActivateFreeCam();
            }
        }
        #endregion
        
        #region Private Methods
        private void ActivateFreeCam()
        {
            _logger.LogInfo("Activating free camera");
            
            // Check if main camera exists
            if (_mainCamera == null)
            {
                _logger.LogError("Cannot activate free camera: Main camera is null");
                return;
            }

            // Store original camera values
            _originalPosition = _mainCamera.transform.position;
            _originalRotation = _mainCamera.transform.rotation;
            
            // CRITICAL: Disable occlusion culling for camera
            DisableCulling();
            
            // If we have a game world and player, use the proper player camera controller
            if (Singleton<GameWorld>.Instantiated && Singleton<GameWorld>.Instance.MainPlayer != null)
            {
                _originalPlayer = Singleton<GameWorld>.Instance.MainPlayer;
                
                // Get player camera controller
                _playerCameraController = _originalPlayer.GetComponent<PlayerCameraController>();
                if (_playerCameraController != null)
                {
                    _logger.LogInfo("Found PlayerCameraController, using native free camera mode");
                    
                    // Store original point of view
                    _originalPointOfView = _originalPlayer.PointOfView;
                    
                    // Disable player controls
                    _gamePlayerOwner = _originalPlayer.GetComponentInChildren<GamePlayerOwner>();
                    if (_gamePlayerOwner != null)
                    {
                        _wasPlayerActive = _gamePlayerOwner.enabled;
                        _gamePlayerOwner.enabled = false;
                    }
                    
                    // Disable physics
                    CharacterController characterController = _originalPlayer.GetComponent<CharacterController>();
                    if (characterController != null)
                    {
                        _wasPhysicsEnabled = characterController.enabled;
                        characterController.enabled = false;
                    }
                    
                    // IMPORTANT: Set point of view to FreeCamera
                    _originalPlayer.PointOfView = EPointOfView.FreeCamera;
                    
                    // Set player body point of view
                    if (_originalPlayer.PlayerBody != null)
                    {
                        _originalPlayer.PlayerBody.PointOfView.Value = EPointOfView.FreeCamera;
                    }
                    
                    // Update the camera controller to apply the free camera mode
                    _playerCameraController.UpdatePointOfView();
                }
            }
            else
            {
                _logger.LogInfo("No player found, using standalone free camera mode");
            }
            
            // Position camera parent slightly above current position to avoid clipping
            _cameraParent.transform.position = _mainCamera.transform.position + new Vector3(0, 1.5f, 0);
            _cameraParent.transform.rotation = _mainCamera.transform.rotation;
            
            // Set initial rotation values
            _yaw = _cameraParent.transform.eulerAngles.y;
            _pitch = -_cameraParent.transform.eulerAngles.x;
            
            // Parent camera to our controlled object
            _mainCamera.transform.SetParent(_cameraParent.transform);
            _mainCamera.transform.localPosition = Vector3.zero;
            _mainCamera.transform.localRotation = Quaternion.identity;
            
            // Add or get LOD bias controller
            _lodBiasController = _mainCamera.GetComponent<CameraLodBiasController>();
            if (_lodBiasController == null)
            {
                _lodBiasController = _mainCamera.gameObject.AddComponent<CameraLodBiasController>();
            }
            
            // Set LOD bias for better distant object detail
            _lodBiasController.LodBiasFactor = 2.0f; // Higher value to see distant objects clearly
            
            // Reset FOV
            _mainCamera.fieldOfView = _originalFov;
            
            // Disable camera effects
            if (CameraClass.Instance != null && CameraClass.Instance.EffectsController != null)
            {
                try
                {
                    CameraClass.Instance.EffectsController.method_4(false);
                    _logger.LogInfo("Disabled camera effects");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"Could not disable camera effects: {ex.Message}");
                }
            }
            
            // Get vision components
            _nightVision = CameraClass.Instance?.NightVision;
            _thermalVision = CameraClass.Instance?.ThermalVision;
            
            // Call our new method to disable constraints
            DisableCameraConstraints();
            
            // Activate free camera
            IsActive = true;
            
            // Store last position
            _lastPosition = _cameraParent.transform.position;
        }
        
        private void DeactivateFreeCam()
        {
            if (!IsActive) return;
            
            _logger.LogInfo("Deactivating free camera");
            
            // Restore constraints first
            RestoreCameraConstraints();
            
            // Re-enable culling
            if (CameraClass.Instance != null)
            {
                CameraClass.Instance.SetOcclusionCullingEnabled(true);
            }
            
            // Reset camera
            _mainCamera.transform.SetParent(null);
            _mainCamera.transform.position = _originalPosition;
            _mainCamera.transform.rotation = _originalRotation;
            _mainCamera.fieldOfView = _originalFov;
            
            // Reset player if we have one
            if (_originalPlayer != null)
            {
                // Re-enable player controls
                if (_gamePlayerOwner != null)
                {
                    _gamePlayerOwner.enabled = _wasPlayerActive;
                }
                
                // Reset point of view
                _originalPlayer.PointOfView = _originalPointOfView;
                
                // Update the camera controller
                if (_playerCameraController != null)
                {
                    _playerCameraController.UpdatePointOfView();
                }
                
                // Re-enable physics
                CharacterController characterController = _originalPlayer.GetComponent<CharacterController>();
                if (characterController != null)
                {
                    characterController.enabled = _wasPhysicsEnabled;
                }
                
                // Update player body point of view
                if (_originalPlayer.PlayerBody != null)
                {
                    _originalPlayer.PlayerBody.PointOfView.Value = _originalPointOfView;
                }
            }
            
            // Reset LOD bias
            if (_lodBiasController != null)
            {
                _lodBiasController.LodBiasFactor = 1.0f;
            }
            
            // Disable night/thermal vision
            if (_nightVision != null && _nightVision.On)
            {
                _nightVision.On = false;
            }
            
            if (_thermalVision != null && _thermalVision.On)
            {
                _thermalVision.On = false;
            }
            
            IsActive = false;
        }
        
        private void HandleFreeCamMovement()
        {
            if (!IsActive) return;
            
            float movementSpeed = MOVEMENT_SPEED_NORMAL;
            
            // Speed modifiers
            if (Input.GetKey(KeyCode.LeftShift))
            {
                movementSpeed = MOVEMENT_SPEED_FAST;
            }
            
            if (Input.GetKey(KeyCode.LeftControl))
            {
                movementSpeed = MOVEMENT_SPEED_TURBO;
            }
            
            // Movement
            if (Input.GetKey(_forwardKey))
            {
                _cameraParent.transform.position += _cameraParent.transform.forward * (movementSpeed * Time.deltaTime);
            }
            
            if (Input.GetKey(_backKey))
            {
                _cameraParent.transform.position += -_cameraParent.transform.forward * (movementSpeed * Time.deltaTime);
            }
            
            if (Input.GetKey(_leftKey))
            {
                _cameraParent.transform.position += -_cameraParent.transform.right * (movementSpeed * Time.deltaTime);
            }
            
            if (Input.GetKey(_rightKey))
            {
                _cameraParent.transform.position += _cameraParent.transform.right * (movementSpeed * Time.deltaTime);
            }
            
            if (Input.GetKey(_upKey))
            {
                _cameraParent.transform.position += _cameraParent.transform.up * (movementSpeed * Time.deltaTime);
            }
            
            if (Input.GetKey(_downKey))
            {
                _cameraParent.transform.position += -_cameraParent.transform.up * (movementSpeed * Time.deltaTime);
            }
            
            // Save position
            _lastPosition = _cameraParent.transform.position;
            
            // Look rotation with right mouse button
            if (Input.GetKey(KeyCode.Mouse1))
            {
                float x = Input.GetAxis("Mouse X");
                float y = Input.GetAxis("Mouse Y");
                
                _yaw += x * LOOK_SENSITIVITY;
                _pitch += y * LOOK_SENSITIVITY;
                _pitch = Mathf.Clamp(_pitch, -89f, 89f);
                
                _cameraParent.transform.eulerAngles = new Vector3(-_pitch, _yaw, 0);
            }
            
            // Handle FOV with scroll wheel
            float scrollValue = Input.GetAxis("Mouse ScrollWheel");
            if (scrollValue != 0 && _mainCamera != null)
            {
                float currentFov = _mainCamera.fieldOfView;
                if (currentFov >= MIN_FOV && currentFov <= _originalFov)
                {
                    float newFov = Mathf.Clamp(currentFov - (scrollValue * FOV_CHANGE_SPEED), MIN_FOV, _originalFov);
                    _mainCamera.fieldOfView = newFov;
                    
                    // Update LOD bias based on FOV zoom
                    if (_lodBiasController != null)
                    {
                        _lodBiasController.SetBiasByFov(newFov);
                    }
                }
            }
            
            // Toggle vision modes with N key
            if (Input.GetKeyDown(KeyCode.N))
            {
                ToggleVisionMode();
            }
            
            // Return to original position with Home key
            if (Input.GetKeyDown(KeyCode.Home))
            {
                _cameraParent.transform.position = _originalPosition;
                _cameraParent.transform.rotation = _originalRotation;
                _mainCamera.fieldOfView = _originalFov;
                
                // Reset rotation values
                _yaw = _cameraParent.transform.eulerAngles.y;
                _pitch = -_cameraParent.transform.eulerAngles.x;
            }
        }
        
        private void ToggleVisionMode()
        {
            if (_nightVision != null && _thermalVision != null)
            {
                if (!_nightVision.On && !_thermalVision.On)
                {
                    _nightVision.On = true;
                }
                else if (_nightVision.On && !_thermalVision.On)
                {
                    _nightVision.On = false;
                    _thermalVision.On = true;
                }
                else if (_thermalVision.On)
                {
                    _thermalVision.On = false;
                }
            }
        }
        
        private void DrawFreeCamUI()
        {
            GUILayout.BeginArea(new Rect(10, 10, 250, 220));
            GUILayout.BeginVertical("box");
            
            GUILayout.Label("<b>Map Editor Free Camera</b>");
            
            GUILayout.Space(5);
            
            GUILayout.Label("WASD - Move camera");
            GUILayout.Label("E/Q - Up/Down");
            GUILayout.Label("Right Mouse - Look around");
            GUILayout.Label("Shift - Fast speed");
            GUILayout.Label("Ctrl - Turbo speed");
            GUILayout.Label("Mouse wheel - Zoom");
            GUILayout.Label("N - Toggle vision mode");
            GUILayout.Label("U - Toggle this UI");
            GUILayout.Label("Home - Reset position");
            
            GUILayout.EndVertical();
            GUILayout.EndArea();
        }
        
        /// <summary>
        /// Helper method to recursively set the layer of an object and all its children
        /// </summary>
        private void SetLayerRecursively(GameObject obj, int newLayer)
        {
            if (obj == null) return;
            
            obj.layer = newLayer;
            
            foreach (Transform child in obj.transform)
            {
                if (child == null) continue;
                SetLayerRecursively(child.gameObject, newLayer);
            }
        }
        
        private void DisableCameraConstraints()
        {
            if (_originalPlayer == null) return;
            
            // Get the PlayerCameraController that's controlling our camera
            var playerCameraController = _originalPlayer.GetComponent<PlayerCameraController>();
            if (playerCameraController != null)
            {
                // Most important: Store a reference to bypass position check
                _playerCameraController = playerCameraController;
                
                // Completely disable the PlayerCameraController component
                // This prevents all the automatic camera repositioning logic
                playerCameraController.enabled = false;
                _logger.LogInfo("Disabled PlayerCameraController constraints");
                
                // Make sure point of view is set to FreeCamera
                // This helps ensure the character model is visible
                if (_originalPlayer.PointOfView != EPointOfView.FreeCamera)
                {
                    _originalPointOfView = _originalPlayer.PointOfView;
                    _originalPlayer.PointOfView = EPointOfView.FreeCamera;
                    
                    if (_originalPlayer.PlayerBody != null)
                    {
                        _originalPlayer.PlayerBody.PointOfView.Value = EPointOfView.FreeCamera;
                    }
                }
            }
            
            // Disable camera snap-back through CameraClass if possible
            if (CameraClass.Instance != null)
            {
                // Use reflection to access and disable any private position limiting fields
                var cameraClassType = CameraClass.Instance.GetType();
                var positionLimitField = cameraClassType.GetField("_positionLimit", 
                    System.Reflection.BindingFlags.NonPublic | 
                    System.Reflection.BindingFlags.Instance);
                    
                if (positionLimitField != null)
                {
                    // Cache the original value to restore later
                    _originalPositionLimit = positionLimitField.GetValue(CameraClass.Instance);
                    positionLimitField.SetValue(CameraClass.Instance, float.MaxValue);
                    _logger.LogInfo("Disabled CameraClass position limits");
                }
            }
        }
        
        private void RestoreCameraConstraints()
        {
            // Re-enable the PlayerCameraController if we disabled it
            if (_playerCameraController != null)
            {
                _playerCameraController.enabled = true;
                _logger.LogInfo("Restored PlayerCameraController");
                _playerCameraController = null;
            }
            
            // Restore position limit if we changed it
            if (_originalPositionLimit != null && CameraClass.Instance != null)
            {
                var cameraClassType = CameraClass.Instance.GetType();
                var positionLimitField = cameraClassType.GetField("_positionLimit", 
                    System.Reflection.BindingFlags.NonPublic | 
                    System.Reflection.BindingFlags.Instance);
                    
                if (positionLimitField != null)
                {
                    positionLimitField.SetValue(CameraClass.Instance, _originalPositionLimit);
                    _originalPositionLimit = null;
                    _logger.LogInfo("Restored CameraClass position limits");
                }
            }
        }
        #endregion
    }

    /// <summary>
    /// GUI extension methods for the MapEditorFreeCam
    /// </summary>
    public static class MapEditorFreeCamGUIExtension
    {
        /// <summary>
        /// Draw a free camera toggle button with proper styling
        /// </summary>
        public static bool DrawFreeCamToggle(bool isActive, GUIStyle style)
        {
            // Cache current GUI state
            Color originalColor = GUI.backgroundColor;
            
            // Set color based on active state
            GUI.backgroundColor = isActive ? 
                new Color(0.7f, 1f, 0.7f) : // Green when active
                new Color(0.9f, 0.9f, 0.9f); // Light gray when inactive
            
            // Create checkbox-like button text
            string buttonText = (isActive ? "☑ " : "☐ ") + "Free Camera";
            
            // Draw button
            bool result = GUILayout.Button(buttonText, style, GUILayout.Height(30));
            
            // Reset color
            GUI.backgroundColor = originalColor;
            
            return result;
        }
    }
}