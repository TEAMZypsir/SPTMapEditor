using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using BepInEx;
using Newtonsoft.Json;

namespace TransformCacher
{
    /// <summary>
    /// Manages all database operations for the TransformCacher plugin.
    /// Handles saving and loading of transforms database and baked IDs database.
    /// </summary>
    public class DatabaseManager
    {
        private static DatabaseManager _instance;
        public static DatabaseManager Instance => _instance ?? (_instance = new DatabaseManager());

        // Paths for database files
        private string _transformsPath;
        private string _bakedIdsPath;

        // Databases
        private Dictionary<string, Dictionary<string, TransformData>> _transformsDatabase = 
            new Dictionary<string, Dictionary<string, TransformData>>();
        
        private Dictionary<string, Dictionary<string, BakedIdData>> _bakedIdsDatabase = 
            new Dictionary<string, Dictionary<string, BakedIdData>>();

        // Logger
        private static BepInEx.Logging.ManualLogSource Logger;

        // Flag to prevent re-initialization
        private bool _isInitialized = false;

        /// <summary>
        /// Initialize the DatabaseManager with the necessary paths and logging
        /// </summary>
        public void Initialize()
        {
            if (_isInitialized) return;

            // Get logger
            Logger = BepInEx.Logging.Logger.CreateLogSource("DatabaseManager");

            // Set up save paths
            string baseDir = Path.Combine(Paths.PluginPath, "TransformCacher");
            _transformsPath = Path.Combine(baseDir, "transforms.json");
            _bakedIdsPath = Path.Combine(baseDir, "baked_ids.json");

            // Create directory if it doesn't exist
            Directory.CreateDirectory(baseDir);

            // Load databases
            LoadTransformsDatabase();
            LoadBakedIdsDatabase();

            _isInitialized = true;
            Logger.LogInfo("DatabaseManager initialized successfully");
        }

        #region Transforms Database

        /// <summary>
        /// Gets the transforms database
        /// </summary>
        public Dictionary<string, Dictionary<string, TransformData>> GetTransformsDatabase()
        {
            return _transformsDatabase;
        }

        /// <summary>
        /// Sets the transforms database
        /// </summary>
        public void SetTransformsDatabase(Dictionary<string, Dictionary<string, TransformData>> database)
        {
            _transformsDatabase = database;
        }

        /// <summary>
        /// Loads the transforms database from disk
        /// </summary>
        public void LoadTransformsDatabase()
        {
            try
            {
                if (File.Exists(_transformsPath))
                {
                    Logger.LogInfo($"Loading transforms database from {_transformsPath}");
                    string json = File.ReadAllText(_transformsPath);
                    
                    // Check if the file has content
                    if (string.IsNullOrWhiteSpace(json))
                    {
                        Logger.LogWarning("Transforms database file exists but is empty. Creating new database.");
                        _transformsDatabase = new Dictionary<string, Dictionary<string, TransformData>>();
                        return;
                    }
                    
                    // Deserialize with error handling
                    try
                    {
                        _transformsDatabase = JsonConvert.DeserializeObject<Dictionary<string, Dictionary<string, TransformData>>>(json);
                        
                        // Validate the deserialized data
                        if (_transformsDatabase == null)
                        {
                            Logger.LogWarning("Deserialized transforms database is null. Creating new database.");
                            _transformsDatabase = new Dictionary<string, Dictionary<string, TransformData>>();
                            return;
                        }
                        
                        // Log some stats
                        int totalObjects = 0;
                        int destroyedObjects = 0;
                        int spawnedObjects = 0;
                        
                        foreach (var scene in _transformsDatabase.Keys)
                        {
                            totalObjects += _transformsDatabase[scene].Count;
                            foreach (var obj in _transformsDatabase[scene].Values)
                            {
                                if (obj.IsDestroyed) destroyedObjects++;
                                if (obj.IsSpawned) spawnedObjects++;
                            }
                        }
                        
                        Logger.LogInfo($"Successfully loaded transforms database with {_transformsDatabase.Count} scenes, {totalObjects} objects ({destroyedObjects} destroyed, {spawnedObjects} spawned)");
                    }
                    catch (Exception ex)
                    {
                        Logger.LogError($"Error deserializing transforms database: {ex.Message}. Creating new database.");
                        _transformsDatabase = new Dictionary<string, Dictionary<string, TransformData>>();
                    }
                }
                else
                {
                    Logger.LogInfo("No transforms database file found. Creating new database.");
                    _transformsDatabase = new Dictionary<string, Dictionary<string, TransformData>>();
                }
            }
            catch (Exception e)
            {
                Logger.LogError($"Failed to load transforms database: {e.Message}\n{e.StackTrace}");
                _transformsDatabase = new Dictionary<string, Dictionary<string, TransformData>>();
            }
        }

        /// <summary>
        /// Saves the transforms database to disk
        /// </summary>
        public void SaveTransformsDatabase()
        {
            try
            {
                // Create a backup of the existing file first
                if (File.Exists(_transformsPath))
                {
                    string backupPath = _transformsPath + ".bak";
                    try
                    {
                        File.Copy(_transformsPath, backupPath, true);
                        Logger.LogInfo($"Created backup at {backupPath}");
                    }
                    catch (Exception ex)
                    {
                        Logger.LogWarning($"Failed to create backup: {ex.Message}");
                    }
                }
                
                // Create a temporary file first to prevent corruption
                string tempPath = _transformsPath + ".tmp";
                string json = JsonConvert.SerializeObject(_transformsDatabase, Formatting.Indented);
                
                // Write to temp file
                File.WriteAllText(tempPath, json);
                
                // Delete the destination file if it exists
                if (File.Exists(_transformsPath))
                {
                    File.Delete(_transformsPath);
                }
                
                // Move temp file to final location
                File.Move(tempPath, _transformsPath);
                Logger.LogInfo($"Transforms database saved to {_transformsPath}");
            }
            catch (Exception e)
            {
                Logger.LogError($"Failed to save transforms database: {e.Message}\n{e.StackTrace}");
            }
        }

        /// <summary>
        /// Updates a single transform in the database
        /// </summary>
        public void UpdateTransform(string sceneName, string uniqueId, TransformData data)
        {
            if (!_transformsDatabase.ContainsKey(sceneName))
            {
                _transformsDatabase[sceneName] = new Dictionary<string, TransformData>();
            }
            
            _transformsDatabase[sceneName][uniqueId] = data;
        }

        #endregion

        #region Baked IDs Database

        /// <summary>
        /// Gets the baked IDs database
        /// </summary>
        public Dictionary<string, Dictionary<string, BakedIdData>> GetBakedIdsDatabase()
        {
            return _bakedIdsDatabase;
        }

        /// <summary>
        /// Sets the baked IDs database
        /// </summary>
        public void SetBakedIdsDatabase(Dictionary<string, Dictionary<string, BakedIdData>> database)
        {
            _bakedIdsDatabase = database;
        }

        /// <summary>
        /// Loads the baked IDs database from disk
        /// </summary>
        public void LoadBakedIdsDatabase()
        {
            try
            {
                if (File.Exists(_bakedIdsPath))
                {
                    string json = File.ReadAllText(_bakedIdsPath);
                    
                    _bakedIdsDatabase = JsonConvert.DeserializeObject<Dictionary<string, Dictionary<string, BakedIdData>>>(json)
                                      ?? new Dictionary<string, Dictionary<string, BakedIdData>>();
                    
                    // Count total baked objects
                    int totalBakedObjects = 0;
                    foreach (var scene in _bakedIdsDatabase)
                    {
                        totalBakedObjects += scene.Value.Count;
                    }
                    
                    Logger.LogInfo($"Loaded baked IDs database with {_bakedIdsDatabase.Count} scenes and {totalBakedObjects} objects");
                }
                else
                {
                    _bakedIdsDatabase = new Dictionary<string, Dictionary<string, BakedIdData>>();
                    Logger.LogInfo("No baked IDs database found, creating new one");
                }
            }
            catch (Exception e)
            {
                Logger.LogError($"Failed to load baked IDs database: {e.Message}");
                _bakedIdsDatabase = new Dictionary<string, Dictionary<string, BakedIdData>>();
            }
        }

        /// <summary>
        /// Saves the baked IDs database to disk
        /// </summary>
        public void SaveBakedIdsDatabase()
        {
            try
            {
                // Create a backup of the existing file first
                if (File.Exists(_bakedIdsPath))
                {
                    string backupPath = _bakedIdsPath + ".bak";
                    try
                    {
                        File.Copy(_bakedIdsPath, backupPath, true);
                        Logger.LogInfo($"Created backup at {backupPath}");
                    }
                    catch (Exception ex)
                    {
                        Logger.LogWarning($"Failed to create backup: {ex.Message}");
                    }
                }
                
                // Create a temporary file first to prevent corruption
                string tempPath = _bakedIdsPath + ".tmp";
                string json = JsonConvert.SerializeObject(_bakedIdsDatabase, Formatting.Indented);
                
                // Write to temp file
                File.WriteAllText(tempPath, json);
                
                // Delete the destination file if it exists
                if (File.Exists(_bakedIdsPath))
                {
                    File.Delete(_bakedIdsPath);
                }
                
                // Move temp file to final location
                File.Move(tempPath, _bakedIdsPath);
                Logger.LogInfo($"Baked IDs database saved to {_bakedIdsPath}");
            }
            catch (Exception e)
            {
                Logger.LogError($"Failed to save baked IDs database: {e.Message}\n{e.StackTrace}");
            }
        }

        /// <summary>
        /// Updates a single baked ID in the database
        /// </summary>
        public void UpdateBakedId(string sceneName, string itemId, BakedIdData data)
        {
            if (!_bakedIdsDatabase.ContainsKey(sceneName))
            {
                _bakedIdsDatabase[sceneName] = new Dictionary<string, BakedIdData>();
            }
            
            _bakedIdsDatabase[sceneName][itemId] = data;
        }

        /// <summary>
        /// Checks if a scene has any baked IDs
        /// </summary>
        public bool IsSceneBaked(string sceneName)
        {
            return _bakedIdsDatabase.ContainsKey(sceneName) && _bakedIdsDatabase[sceneName].Count > 0;
        }

        /// <summary>
        /// Tries to get a baked ID for an object by its path
        /// </summary>
        public bool TryGetBakedIdByPath(string sceneName, string objectPath, out BakedIdData bakedData)
        {
            bakedData = null;
            
            if (_bakedIdsDatabase.ContainsKey(sceneName))
            {
                foreach (var data in _bakedIdsDatabase[sceneName].Values)
                {
                    if (data.ItemPath == objectPath)
                    {
                        bakedData = data;
                        return true;
                    }
                }
            }
            
            return false;
        }

        /// <summary>
        /// Tries to get a baked ID data by item ID
        /// </summary>
        public bool TryGetBakedDataById(string sceneName, string itemId, out BakedIdData bakedData)
        {
            bakedData = null;
            
            if (_bakedIdsDatabase.ContainsKey(sceneName) && 
                _bakedIdsDatabase[sceneName].ContainsKey(itemId))
            {
                bakedData = _bakedIdsDatabase[sceneName][itemId];
                return true;
            }
            
            return false;
        }

        #endregion
    }

    /// <summary>
    /// Class to store transform data in the database
    /// </summary>
    [Serializable]
    public class TransformData
    {
        public string UniqueId;
        public string PathID;
        public string ItemID;
        public List<string> Children = new List<string>();
        public string SceneName;
        public Vector3 Position;
        public Vector3 Rotation;
        public Vector3 Scale;
        public string ParentPath;
        public bool IsDestroyed;
        public bool IsSpawned;
        public string PrefabPath;
        public string ObjectPath;
        public string ObjectName;
    }

    /// <summary>
    /// Class to store baked ID data in the database
    /// </summary>
    [Serializable]
    public class BakedIdData
    {
        public string PathID;
        public string UniqueId;
        public string ItemID;
        public string ItemPath;
        public List<string> Children = new List<string>();
        public string SceneName;
        public Vector3 Position;
        public Vector3 Rotation;
        public Vector3 Scale;
        public string ParentPath;
        public bool IsDestroyed;
        public bool IsSpawned;
        public string PrefabPath;
    }
}