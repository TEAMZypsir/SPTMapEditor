using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TransformCacher
{
    public class DatabaseManager : MonoBehaviour
    {
        private static DatabaseManager _instance;
        public static DatabaseManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    GameObject go = new GameObject("DatabaseManager");
                    DontDestroyOnLoad(go);
                    _instance = go.AddComponent<DatabaseManager>();
                }
                return _instance;
            }
        }

        // Database files
        private string _transformsDbPath;
        private Dictionary<string, Dictionary<string, TransformData>> _transformsDb;
        
        // Pending changes that haven't been written to files yet
        private Dictionary<string, Dictionary<string, TransformData>> _pendingChanges;
        
        // Track which scenes have pending changes
        private HashSet<string> _modifiedScenes = new HashSet<string>();
        
        // Asset paths
        private string _modDirectory;
        private string _originalAssetsPath;
        private string _modifiedAssetsPath;
        
        // Logger reference
        private BepInEx.Logging.ManualLogSource Logger;

        public void Initialize()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            
            _instance = this;
            
            // Get logger
            Logger = BepInEx.Logging.Logger.CreateLogSource("DatabaseManager");
            
            // Set up database paths
            string pluginPath = Path.GetDirectoryName(typeof(TransformCacherPlugin).Assembly.Location);
            _modDirectory = Path.Combine(pluginPath, "ModifiedAssets");
            _transformsDbPath = Path.Combine(pluginPath, "transforms_db.json");
            _originalAssetsPath = Application.dataPath;
            _modifiedAssetsPath = Path.Combine(_modDirectory, "Assets");
            
            // Initialize databases
            _transformsDb = new Dictionary<string, Dictionary<string, TransformData>>();
            _pendingChanges = new Dictionary<string, Dictionary<string, TransformData>>();
            
            // Create mod directory if it doesn't exist
            if (!Directory.Exists(_modDirectory))
            {
                Directory.CreateDirectory(_modDirectory);
            }
            
            if (!Directory.Exists(_modifiedAssetsPath))
            {
                Directory.CreateDirectory(_modifiedAssetsPath);
            }
            
            // Load existing databases
            LoadTransformsDatabase();
            
            Logger.LogInfo("DatabaseManager initialized");
        }

        #region Transforms Database

        /// <summary>
        /// Load the transforms database from disk
        /// </summary>
        public void LoadTransformsDatabase()
        {
            try
            {
                if (File.Exists(_transformsDbPath))
                {
                    string json = File.ReadAllText(_transformsDbPath);
                    _transformsDb = JsonConvert.DeserializeObject<Dictionary<string, Dictionary<string, TransformData>>>(json);
                    
                    if (_transformsDb == null)
                    {
                        _transformsDb = new Dictionary<string, Dictionary<string, TransformData>>();
                    }
                    
                    Logger.LogInfo($"Loaded transforms database with {_transformsDb.Count} scenes");
                }
                else
                {
                    _transformsDb = new Dictionary<string, Dictionary<string, TransformData>>();
                    Logger.LogInfo("No transforms database found, created new one");
                }
            }
            catch (Exception ex)
            {
                Logger.LogError($"Error loading transforms database: {ex.Message}");
                _transformsDb = new Dictionary<string, Dictionary<string, TransformData>>();
            }
        }

        /// <summary>
        /// Save the transforms database to disk
        /// </summary>
        public void SaveTransformsDatabase()
        {
            try
            {
                string json = JsonConvert.SerializeObject(_transformsDb, Formatting.Indented);
                File.WriteAllText(_transformsDbPath, json);
                Logger.LogInfo("Saved transforms database");
            }
            catch (Exception ex)
            {
                Logger.LogError($"Error saving transforms database: {ex.Message}");
            }
        }

        /// <summary>
        /// Get the transforms database
        /// </summary>
        public Dictionary<string, Dictionary<string, TransformData>> GetTransformsDatabase()
        {
            return _transformsDb;
        }

        /// <summary>
        /// Set the transforms database
        /// </summary>
        public void SetTransformsDatabase(Dictionary<string, Dictionary<string, TransformData>> db)
        {
            _transformsDb = db;
        }

        /// <summary>
        /// Add a pending transform change (not committed to files yet)
        /// </summary>
        public void AddPendingChange(string sceneName, TransformData transformData)
        {
            if (!_pendingChanges.ContainsKey(sceneName))
            {
                _pendingChanges[sceneName] = new Dictionary<string, TransformData>();
            }
            
            _pendingChanges[sceneName][transformData.UniqueId] = transformData;
            _modifiedScenes.Add(sceneName);
            
            Logger.LogInfo($"Added pending change for {transformData.ObjectName} in {sceneName}");
        }

        /// <summary>
        /// Get pending changes for a scene
        /// </summary>
        public Dictionary<string, TransformData> GetPendingChanges(string sceneName)
        {
            if (_pendingChanges.ContainsKey(sceneName))
            {
                return _pendingChanges[sceneName];
            }
            
            return new Dictionary<string, TransformData>();
        }

        /// <summary>
        /// Check if there are pending changes
        /// </summary>
        public bool HasPendingChanges()
        {
            return _pendingChanges.Any(scene => scene.Value.Count > 0);
        }

        /// <summary>
        /// Check if a specific scene has pending changes
        /// </summary>
        public bool HasPendingChanges(string sceneName)
        {
            return _pendingChanges.ContainsKey(sceneName) && _pendingChanges[sceneName].Count > 0;
        }

        /// <summary>
        /// Commit all pending changes to the asset files
        /// </summary>
        public void CommitPendingChanges()
        {
            if (!HasPendingChanges())
            {
                Logger.LogInfo("No pending changes to commit");
                return;
            }
            
            // Hook into AssetManager to commit changes
            AssetManager assetManager = AssetManager.Instance;
            bool changesApplied = false;
            
            foreach (string sceneName in _modifiedScenes)
            {
                if (_pendingChanges.ContainsKey(sceneName) && _pendingChanges[sceneName].Count > 0)
                {
                    // Copy the scene file if it doesn't exist in mod directory
                    assetManager.CopySceneIfNeeded(sceneName);
                    
                    // Apply modifications to the copied scene - get result status
                    bool sceneChangeSuccess = assetManager.ApplyChangesToScene(sceneName, _pendingChanges[sceneName].Values.ToList());
                    
                    if (sceneChangeSuccess)
                    {
                        Logger.LogInfo($"Successfully applied changes to bundle file for scene {sceneName}. Removing changes from transforms_db.json");
                        changesApplied = true;
                        
                        // Since changes were successfully written to a bundle, don't add to transforms_db.json
                        // If the scene already exists in the database, remove its entries
                        if (_transformsDb.ContainsKey(sceneName))
                        {
                            _transformsDb.Remove(sceneName);
                            Logger.LogInfo($"Removed scene {sceneName} from transforms database after applying to bundle");
                        }
                    }
                    else
                    {
                        // Changes couldn't be applied to bundle, so keep them in the transforms database
                        Logger.LogWarning($"Could not apply changes directly to bundle for {sceneName}, keeping in transforms database");
                        
                        // Fallback to storing in transforms_db.json
                        if (!_transformsDb.ContainsKey(sceneName))
                        {
                            _transformsDb[sceneName] = new Dictionary<string, TransformData>();
                        }
                        
                        foreach (var change in _pendingChanges[sceneName])
                        {
                            _transformsDb[sceneName][change.Key] = change.Value;
                            
                            // Make sure PrefabPath is preserved for spawned objects
                            if (change.Value.IsSpawned && !string.IsNullOrEmpty(change.Value.PrefabPath))
                            {
                                _transformsDb[sceneName][change.Key].PrefabPath = change.Value.PrefabPath;
                            }
                        }
                    }
                    
                    Logger.LogInfo($"Committed {_pendingChanges[sceneName].Count} changes to {sceneName}");
                }
            }
            
            // Save the transforms database
            SaveTransformsDatabase();
            
            // Clear pending changes
            _pendingChanges.Clear();
            _modifiedScenes.Clear();
            
            // Ensure the asset redirector knows to use our modified files
            if (changesApplied)
            {
                // Register all scenes that have bundle files
                var bundleScenes = assetManager.GetScenesThatHaveBundleFiles();
                AssetRedirector.Instance.RegisterModifiedScenes(bundleScenes);
            }
            else
            {
                // Fall back to registering scenes from the transforms database
                AssetRedirector.Instance.RegisterModifiedScenes(_transformsDb.Keys.ToList());
            }
            
            Logger.LogInfo("All pending changes committed to asset files");
        }

        /// <summary>
        /// Discard all pending changes
        /// </summary>
        public void DiscardPendingChanges()
        {
            _pendingChanges.Clear();
            _modifiedScenes.Clear();
            Logger.LogInfo("Discarded all pending changes");
        }

        #endregion
    }

    /// <summary>
    /// Class to store transform data in the database
    /// </summary>
    public class TransformData
    {
        public string UniqueId { get; set; }
        public string ObjectName { get; set; }
        public string SceneName { get; set; }
        public Vector3 Position { get; set; }
        public Vector3 Rotation { get; set; }
        public Vector3 Scale { get; set; }
        public string ParentPath { get; set; }
        public bool IsDestroyed { get; set; }
        public bool IsSpawned { get; set; }
        public string PrefabPath { get; set; } // Added property for prefab source

        // Add these for backward compatibility
        public string PathID { get; set; }
        public string ItemID { get; set; }
        public string ObjectPath { get; set; }
    }
}