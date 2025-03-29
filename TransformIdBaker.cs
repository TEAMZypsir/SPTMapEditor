using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TransformCacher
{
    /// <summary>
    /// Class responsible for "baking" unique IDs for objects in scenes to ensure
    /// consistent IDs across different users and game sessions.
    /// </summary>
    public class TransformIdBaker : MonoBehaviour
    {
        // Baking status
        private bool _isBaking = false;
        private string _bakingStatus = "";
        private float _bakingProgress = 0f;
        
        // Scene statistics
        private int _totalObjectsInScene = 0;
        private int _objectsBaked = 0;
        private int _objectsIgnored = 0;
        private Dictionary<string, int> _objectCountByType = new Dictionary<string, int>();
        
        // Progress properties for GUI access
        public bool IsBaking() => _isBaking;
        public string GetBakingStatus() => _bakingStatus;
        public float GetBakingProgress() => _bakingProgress;
        
        private static TransformIdBaker _instance;
        public static TransformIdBaker Instance => _instance;

        // Reference to database manager
        private DatabaseManager _databaseManager;
        
        // Logging
        private static BepInEx.Logging.ManualLogSource Logger;

        public void Initialize()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(this);
                return;
            }
            
            _instance = this;
            
            // Get logger
            Logger = BepInEx.Logging.Logger.CreateLogSource("TransformIdBaker");
            
            // Get reference to database manager
            _databaseManager = DatabaseManager.Instance;
            if (_databaseManager == null)
            {
                Logger.LogError("Failed to get DatabaseManager instance");
            }
            
            Logger.LogInfo("TransformIdBaker initialized successfully");
        }
        
        /// <summary>
        /// Checks if the current scene has been baked already
        /// </summary>
        public bool IsSceneBaked(Scene scene)
        {
            string sceneName = scene.name;
            return _databaseManager.IsSceneBaked(sceneName);
        }
        
        /// <summary>
        /// Try to get a baked ID for an object
        /// </summary>
        public bool TryGetBakedId(Transform transform, out BakedIdData bakedData)
        {
            bakedData = null;
            
            if (transform == null || _databaseManager == null)
                return false;
                
            string sceneName = transform.gameObject.scene.name;
            string objectPath = FixUtility.GetFullPath(transform);
            string itemId = FixUtility.GenerateItemID(transform);
            
            // First try to get by path
            if (_databaseManager.TryGetBakedIdByPath(sceneName, objectPath, out bakedData))
            {
                return true;
            }
            
            // Then try by itemId
            if (_databaseManager.TryGetBakedDataById(sceneName, itemId, out bakedData))
            {
                return true;
            }
            
            return false;
        }
        
        /// <summary>
        /// Start the baking process for the current scene or multiple scenes
        /// </summary>
        public void StartBaking()
        {
            // Implemented in the actual class, but GUI now handles this
        }
        
        // Helper method for GUI integration - not used directly but needed for the signature
        private IEnumerator BakeScene(Scene scene)
        {
            yield break;
        }
    }
}