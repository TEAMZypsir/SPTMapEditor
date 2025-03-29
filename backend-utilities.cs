using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using AssetStudio;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace TransformCacher
{
    #region Utilities

    /// <summary>
    /// Static utility class providing helper methods for transformations and object manipulation
    /// </summary>
    public static class UtilityFunctions
    {
        /// <summary>
        /// Gets the full hierarchical path of a transform
        /// </summary>
        public static string GetFullPath(Transform transform)
        {
            if (transform == null) return string.Empty;
            
            var path = new Stack<string>();
            
            var current = transform;
            while (current != null)
            {
                path.Push(current.name);
                current = current.parent;
            }
            
            return string.Join("/", path.ToArray());
        }
        
        /// <summary>
        /// Gets the path of sibling indices, which is more stable than names
        /// </summary>
        public static string GetSiblingIndicesPath(Transform transform)
        {
            if (transform == null) return string.Empty;
            
            var indices = new Stack<int>();
            
            Transform current = transform;
            while (current != null)
            {
                indices.Push(current.GetSiblingIndex());
                current = current.parent;
            }
            
            return string.Join(".", indices.ToArray());
        }
        
        /// <summary>
        /// Safe object lookup by path with error handling
        /// </summary>
        public static GameObject FindObjectByPath(string fullPath)
        {
            try 
            {
                if (string.IsNullOrEmpty(fullPath))
                    return null;
                    
                var segments = fullPath.Split('/');
                if (segments.Length == 0)
                    return null;
                
                // Try direct lookup first
                var directObj = GameObject.Find(fullPath);
                if (directObj != null)
                    return directObj;
                
                // Try finding just the root and then traversing
                var rootObj = GameObject.Find(segments[0]);
                if (rootObj == null) return null;
                
                var currentTransform = rootObj.transform;
                for (int i = 1; i < segments.Length; i++)
                {
                    // Try case sensitive find first
                    Transform nextTransform = null;
                    foreach (Transform child in currentTransform)
                    {
                        if (child.name == segments[i])
                        {
                            nextTransform = child;
                            break;
                        }
                    }
                    
                    // If not found, try case insensitive
                    if (nextTransform == null)
                    {
                        foreach (Transform child in currentTransform)
                        {
                            if (child.name.Equals(segments[i], StringComparison.OrdinalIgnoreCase))
                            {
                                nextTransform = child;
                                break;
                            }
                        }
                    }
                    
                    // If still not found, return null
                    if (nextTransform == null)
                        return null;
                    
                    currentTransform = nextTransform;
                }
                
                return currentTransform.gameObject;
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error in FindObjectByPath: {ex.Message}");
                return null;
            }
        }
        
        /// <summary>
        /// Check if transform values are approximately equal
        /// </summary>
        public static bool TransformValuesEqual(Vector3 a, Vector3 b, float threshold = 0.001f)
        {
            return Vector3.Distance(a, b) < threshold;
        }
        
        /// <summary>
        /// Check if rotations are approximately equal
        /// </summary>
        public static bool QuaternionsEqual(Quaternion a, Quaternion b, float threshold = 0.001f)
        {
            return Quaternion.Angle(a, b) < threshold;
        }
        
        /// <summary>
        /// Safe way to get or create a component
        /// </summary>
        public static T GetOrAddComponent<T>(GameObject obj) where T : Component
        {
            if (obj == null) return null;
            
            T component = obj.GetComponent<T>();
            if (component == null)
            {
                try
                {
                    component = obj.AddComponent<T>();
                }
                catch (Exception ex)
                {
                    Debug.LogError($"Error adding component {typeof(T).Name} to {obj.name}: {ex.Message}");
                }
            }
            
            return component;
        }

        /// <summary>
        /// Generate a PathID for a transform
        /// </summary>
        public static string GeneratePathID(Transform transform)
        {
            if (transform == null) return string.Empty;
            
            // Get object path
            string objectPath = GetFullPath(transform);
            
            // Create a hash code from the path for shorter ID
            int hashCode = objectPath.GetHashCode();
            
            // Return a path ID that's "P" prefix + absolute hash code
            return "P" + Math.Abs(hashCode).ToString();
        }
        
        /// <summary>
        /// Generate an ItemID for a transform
        /// </summary>
        public static string GenerateItemID(Transform transform)
        {
            if (transform == null) return string.Empty;
            
            // Use name + scene + sibling index as a unique identifier
            string name = transform.name;
            string scene = transform.gameObject.scene.name;
            int siblingIndex = transform.GetSiblingIndex();
            
            string idSource = $"{name}_{scene}_{siblingIndex}";
            int hashCode = idSource.GetHashCode();
            
            // Return an item ID that's "I" prefix + absolute hash code
            return "I" + Math.Abs(hashCode).ToString();
        }

        /// <summary>
        /// Generate a unique ID for a transform that persists across game sessions
        /// </summary>
        public static string GenerateUniqueId(Transform transform)
        {
            if (transform == null) return string.Empty;
            
            // Get PathID and ItemID for this transform
            string pathId = GeneratePathID(transform);
            string itemId = GenerateItemID(transform);
            
            // Combine for the new format
            if (!string.IsNullOrEmpty(pathId) && !string.IsNullOrEmpty(itemId))
            {
                return pathId + "+" + itemId;
            }
            
            // Fall back to old format if generation failed
            string sceneName = transform.gameObject.scene.name;
            string hierarchyPath = GetFullPath(transform);
            
            // Add position to make IDs more unique
            // Round to 2 decimal places to avoid floating point precision issues
            string positionStr = string.Format(
                "pos_x{0:F2}y{1:F2}z{2:F2}",
                Math.Round(transform.position.x, 2),
                Math.Round(transform.position.y, 2),
                Math.Round(transform.position.z, 2)
            );
            
            // Simple, stable ID based on scene, path and position
            return $"{sceneName}_{hierarchyPath}_{positionStr}";
        }
    }

    /// <summary>
    /// Unity transform extension methods
    /// </summary>
    public static class UnityExtensions
    {
        /// <summary>
        /// Get the root transform of a given transform
        /// </summary>
        public static Transform GetRoot(this Transform tr)
        {
            while (tr.parent != null)
            {
                tr = tr.parent;
            }
            return tr;
        }

        /// <summary>
        /// Zero out all transforms up the hierarchy
        /// </summary>
        public static void ZeroTransformAndItsParents(this Transform tr)
        {
            do
            {
                tr.localPosition = Vector3.zero;
                tr.localRotation = Quaternion.identity;
                tr.localScale = Vector3.one;
                tr = tr.parent;
            }
            while (tr.parent != null);
        }

        /// <summary>
        /// Destroy all components of a given type
        /// </summary>
        public static void DestroyAll<T>(this T[] components) where T : Component
        {
            if (components == null) return;
            
            foreach (T t in components)
            {
                if (t != null)
                {
                    UnityEngine.Object.Destroy(t);
                }
            }
        }
    }

    #endregion

    #region Logging

    /// <summary>
    /// BepInEx logger implementation for AssetStudio
    /// </summary>
    public class BepinexLogger : ILogger
    {
        private ManualLogSource _logger;

        public BepinexLogger(ManualLogSource logger)
        {
            _logger = logger;
        }

        public void Log(LoggerEvent loggerEvent, string message, bool ignoreLevel = false)
        {
            switch (loggerEvent)
            {
                case LoggerEvent.Verbose:
                case LoggerEvent.Debug:
                case LoggerEvent.Info:
                    _logger.LogInfo(message);
                    break;
                case LoggerEvent.Warning:
                    _logger.LogWarning(message);
                    break;
                case LoggerEvent.Error:
                    _logger.LogError(message);
                    break;
            }
        }
    }

    /// <summary>
    /// Progress logger for AssetStudio operations
    /// </summary>
    public class ProgressLogger : IProgress<int>
    {
        public static event Action<int> OnProgress;
        
        public void Report(int value)
        {
            OnProgress?.Invoke(value);
        }
    }

    #endregion

    #region Database

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
        private static ManualLogSource Logger;

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

    #endregion

    #region Transform Component

    /// <summary>
    /// Component added to tracked transforms
    /// </summary>
    public class TransformCacherTag : MonoBehaviour
    {
        public string UniqueId { get; private set; }
        public string PathID { get; set; }
        public string ItemID { get; set; }

        public bool IsSpawned { get; set; } = false;
        public bool IsDestroyed { get; set; } = false;
        
        public void Awake()
        {
            // Generate a unique ID based on hierarchical path and other properties
            UniqueId = UtilityFunctions.GenerateUniqueId(transform);
            
            // Generate PathID and ItemID if they don't already exist
            if (string.IsNullOrEmpty(PathID))
            {
                PathID = UtilityFunctions.GeneratePathID(transform);
            }
            
            if (string.IsNullOrEmpty(ItemID))
            {
                ItemID = UtilityFunctions.GenerateItemID(transform);
            }
            
            Debug.Log($"[TransformCacher] Tag active on: {gameObject.name} with ID: {UniqueId}, PathID: {PathID}, ItemID: {ItemID}");
        }
    }

    #endregion

    #region Mesh Processing

    /// <summary>
    /// Utilities for Unity mesh conversion
    /// </summary>
    public static class UnityMeshConverter
    {
        /// <summary>
        /// Converts an AssetStudio Mesh to a Unity Mesh
        /// </summary>
        public static UnityEngine.Mesh ConvertToUnityMesh(AssetStudio.Mesh asMesh)
        {
            if (asMesh == null || asMesh.m_VertexCount <= 0)
            {
                Debug.LogError("AssetStudioMesh is null or has no vertices.");
                return null;
            }

            UnityEngine.Mesh mesh = new UnityEngine.Mesh();
            mesh.name = asMesh.m_Name;

            if (asMesh.m_Vertices == null || asMesh.m_Vertices.Length == 0)
            {
                Debug.LogError("AssetStudioMesh has no vertex data.");
                return null;
            }

            // Process vertices
            UnityEngine.Vector3[] vertices = new UnityEngine.Vector3[asMesh.m_VertexCount];
            int vertexStride = (asMesh.m_Vertices.Length == asMesh.m_VertexCount * 4) ? 4 : 3;

            for (int i = 0; i < asMesh.m_VertexCount; i++)
            {
                vertices[i] = new UnityEngine.Vector3(
                    asMesh.m_Vertices[i * vertexStride], 
                    asMesh.m_Vertices[i * vertexStride + 1], 
                    asMesh.m_Vertices[i * vertexStride + 2]
                );
            }
            mesh.vertices = vertices;

            // Process indices/triangles
            if (asMesh.m_Indices == null || asMesh.m_Indices.Count == 0)
            {
                Debug.LogError("AssetStudioMesh has no index data.");
                return null;
            }

            if (asMesh.m_SubMeshes != null && asMesh.m_SubMeshes.Length != 0)
            {
                mesh.subMeshCount = asMesh.m_SubMeshes.Length;
                int indexStart = 0;

                for (int j = 0; j < asMesh.m_SubMeshes.Length; j++)
                {
                    SubMesh subMesh = asMesh.m_SubMeshes[j];
                    int indexCount = (int)subMesh.indexCount;
                    int[] triangles = new int[indexCount];

                    for (int k = 0; k < indexCount; k++)
                    {
                        triangles[k] = (int)asMesh.m_Indices[indexStart + k];
                    }

                    mesh.SetTriangles(triangles, j);
                    indexStart += indexCount;
                }
            }
            else
            {
                int[] triangles = new int[asMesh.m_Indices.Count];
                for (int l = 0; l < asMesh.m_Indices.Count; l++)
                {
                    triangles[l] = (int)asMesh.m_Indices[l];
                }
                mesh.triangles = triangles;
            }

            // Process UVs
            ProcessUVs(mesh, asMesh);

            // Process normals
            if (asMesh.m_Normals != null && asMesh.m_Normals.Length != 0)
            {
                UnityEngine.Vector3[] normals = new UnityEngine.Vector3[asMesh.m_VertexCount];
                int normalStride = (asMesh.m_Normals.Length == asMesh.m_VertexCount * 4) ? 4 : 3;

                for (int n = 0; n < asMesh.m_VertexCount; n++)
                {
                    normals[n] = new UnityEngine.Vector3(
                        asMesh.m_Normals[n * normalStride],
                        asMesh.m_Normals[n * normalStride + 1],
                        asMesh.m_Normals[n * normalStride + 2]
                    );
                }
                mesh.normals = normals;
            }

            // Process colors
            if (asMesh.m_Colors != null && asMesh.m_Colors.Length != 0)
            {
                UnityEngine.Color[] colors = new UnityEngine.Color[asMesh.m_VertexCount];
                int colorStride = (asMesh.m_Colors.Length == asMesh.m_VertexCount * 3) ? 3 : 4;

                for (int i = 0; i < asMesh.m_VertexCount; i++)
                {
                    if (colorStride == 4)
                    {
                        colors[i] = new UnityEngine.Color(
                            asMesh.m_Colors[i * colorStride],
                            asMesh.m_Colors[i * colorStride + 1],
                            asMesh.m_Colors[i * colorStride + 2],
                            asMesh.m_Colors[i * colorStride + 3]
                        );
                    }
                    else
                    {
                        colors[i] = new UnityEngine.Color(
                            asMesh.m_Colors[i * colorStride],
                            asMesh.m_Colors[i * colorStride + 1],
                            asMesh.m_Colors[i * colorStride + 2]
                        );
                    }
                }
                mesh.colors = colors;
            }

            // Process tangents
            if (asMesh.m_Tangents != null && asMesh.m_Tangents.Length != 0)
            {
                UnityEngine.Vector4[] tangents = new UnityEngine.Vector4[asMesh.m_VertexCount];
                int tangentStride = (asMesh.m_Tangents.Length == asMesh.m_VertexCount * 3) ? 3 : 4;

                for (int i = 0; i < asMesh.m_VertexCount; i++)
                {
                    if (tangentStride == 4)
                    {
                        tangents[i] = new UnityEngine.Vector4(
                            asMesh.m_Tangents[i * tangentStride],
                            asMesh.m_Tangents[i * tangentStride + 1],
                            asMesh.m_Tangents[i * tangentStride + 2],
                            asMesh.m_Tangents[i * tangentStride + 3]
                        );
                    }
                    else
                    {
                        tangents[i] = new UnityEngine.Vector4(
                            asMesh.m_Tangents[i * tangentStride],
                            asMesh.m_Tangents[i * tangentStride + 1],
                            asMesh.m_Tangents[i * tangentStride + 2],
                            1f
                        );
                    }
                }
                mesh.tangents = tangents;
            }

            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>
        /// Process all UV channels
        /// </summary>
        private static void ProcessUVs(UnityEngine.Mesh mesh, AssetStudio.Mesh asMesh)
        {
            if (asMesh.m_UV0 != null && asMesh.m_UV0.Length != 0)
            {
                mesh.uv = ConvertFloatArrayToUV(asMesh.m_UV0, asMesh.m_VertexCount);
            }
            
            if (asMesh.m_UV1 != null && asMesh.m_UV1.Length != 0)
            {
                mesh.uv2 = ConvertFloatArrayToUV(asMesh.m_UV1, asMesh.m_VertexCount);
            }
            
            if (asMesh.m_UV2 != null && asMesh.m_UV2.Length != 0)
            {
                mesh.uv3 = ConvertFloatArrayToUV(asMesh.m_UV2, asMesh.m_VertexCount);
            }
            
            if (asMesh.m_UV3 != null && asMesh.m_UV3.Length != 0)
            {
                mesh.uv4 = ConvertFloatArrayToUV(asMesh.m_UV3, asMesh.m_VertexCount);
            }
            
            if (asMesh.m_UV4 != null && asMesh.m_UV4.Length != 0)
            {
                mesh.uv5 = ConvertFloatArrayToUV(asMesh.m_UV4, asMesh.m_VertexCount);
            }
            
            if (asMesh.m_UV5 != null && asMesh.m_UV5.Length != 0)
            {
                mesh.uv6 = ConvertFloatArrayToUV(asMesh.m_UV5, asMesh.m_VertexCount);
            }
            
            if (asMesh.m_UV6 != null && asMesh.m_UV6.Length != 0)
            {
                mesh.uv7 = ConvertFloatArrayToUV(asMesh.m_UV6, asMesh.m_VertexCount);
            }
            
            if (asMesh.m_UV7 != null && asMesh.m_UV7.Length != 0)
            {
                mesh.uv8 = ConvertFloatArrayToUV(asMesh.m_UV7, asMesh.m_VertexCount);
            }
        }

        /// <summary>
        /// Convert a float array to Vector2 UV array
        /// </summary>
        private static UnityEngine.Vector2[] ConvertFloatArrayToUV(float[] floatUVs, int vertexCount)
        {
            UnityEngine.Vector2[] uvs = new UnityEngine.Vector2[vertexCount];
            int stride = 4;
            
            if (floatUVs.Length == vertexCount * 2)
            {
                stride = 2;
            }
            else if (floatUVs.Length == vertexCount * 3)
            {
                stride = 3;
            }
            
            for (int i = 0; i < vertexCount; i++)
            {
                uvs[i] = new UnityEngine.Vector2(floatUVs[i * stride], floatUVs[i * stride + 1]);
            }
            
            return uvs;
        }
    }

    /// <summary>
    /// Handles conversion of materials to GLTF compatible formats
    /// </summary>
    public static class MaterialConverter
    {
        private static Dictionary<Texture, Material> cache = new Dictionary<Texture, Material>();

        /// <summary>
        /// Converts a material to be compatible with UnityGLTF
        /// </summary>
        public static Material ConvertToUnityGLTFCompatible(this Material origMat)
        {
            if (origMat.shader.name.Contains("CW FX/BackLens"))
            {
                origMat.shader = Shader.Find("Sprites/Default");
                origMat.color = Color.black;
                return origMat;
            }
            else if (origMat.shader.name.Contains("Custom/OpticGlass"))
            {
                origMat.color = Color.black;
                return origMat;
            }
            else if (!origMat.shader.name.Contains("Bumped Specular"))
            {
                return origMat;
            }
            else
            {
                Debug.Log(origMat.name + ": converting to gltf specular-gloss...");
                try
                {
                    Texture texture = origMat.GetTexture("_MainTex");
                    if (texture == null)
                    {
                        return origMat;
                    }

                    if (cache.ContainsKey(texture))
                    {
                        Material material = cache[texture];
                        Debug.Log("Using cached converted material " + material.name);
                        return material;
                    }
                    else
                    {
                        bool isReflective = origMat.shader.name == "p0/Reflective/Bumped Specular";
                        Texture texture2;
                        
                        if (isReflective)
                        {
                            float shininess = origMat.GetFloat("_Shininess");
                            texture2 = TextureConverter.CreateSolidColorTexture(texture.width, texture.height, shininess, 1f);
                            texture2.name = texture.name.ReplaceLastWord('_', "GLOSSINESS");
                            
                            Material material2 = new Material(BundleShaders.Find("Hidden/SetAlpha"));
                            material2.SetFloat("_Alpha", origMat.GetColor("_SpecColor").r);
                            texture = TextureConverter.Convert(texture, material2);
                        }
                        else
                        {
                            texture2 = origMat.GetTexture("_SpecMap");
                        }
                        
                        Texture normal;
                        if (!origMat.HasProperty("_BumpMap"))
                        {
                            normal = Texture2D.normalTexture;
                        }
                        else
                        {
                            normal = origMat.GetTexture("_BumpMap");
                        }
                        
                        Texture2D specGloss = TextureConverter.ConvertAlbedoSpecGlosToSpecGloss(texture, texture2);
                        Color color = origMat.color;
                        
                        Material result = new Material(BundleShaders.Find("Hidden/DummySpecularOpaque"));
                        result.EnableKeyword("_NORMALMAP");
                        result.EnableKeyword("_SPECGLOSSMAP");
                        result.EnableKeyword("_EMISSION");
                        result.EnableKeyword("_BUMPMAP");
                        result.SetColor("_Color", color);
                        result.SetTexture("_MainTex", texture);
                        result.SetTexture("_SpecGlossMap", specGloss);
                        result.SetColor("_SpecColor", Color.white);
                        result.SetTexture("_BumpMap", normal);
                        
                        if (origMat.HasProperty("_EmissionMap"))
                        {
                            result.SetTexture("_EmissionMap", origMat.GetTexture("_EmissionMap"));
                            Color emissionColor = Color.white * origMat.GetFloat("_EmissionPower");
                            result.SetColor("_EmissionColor", emissionColor);
                        }
                        else
                        {
                            result.SetColor("_EmissionColor", Color.black);
                        }
                        
                        result.name = origMat.name;
                        cache[texture] = result;
                        
                        return result;
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogError(ex.Message);
                    return origMat;
                }
            }
        }
    }

    /// <summary>
    /// Texture conversion utilities
    /// </summary>
    public static class TextureConverter
    {
        private static Dictionary<Texture, Texture2D> cache = new Dictionary<Texture, Texture2D>();

        /// <summary>
        /// Converts a texture using a material
        /// </summary>
        public static Texture2D Convert(Texture inputTexture, Material mat)
        {
            bool sRGBWrite = GL.sRGBWrite;
            GL.sRGBWrite = false;
            
            RenderTexture temporary = RenderTexture.GetTemporary(
                inputTexture.width, 
                inputTexture.height, 
                0, 
                RenderTextureFormat.ARGB32, 
                RenderTextureReadWrite.Linear
            );
            
            Graphics.Blit(inputTexture, temporary, mat);
            Texture2D result = temporary.ToTexture2D();
            
            RenderTexture.ReleaseTemporary(temporary);
            GL.sRGBWrite = sRGBWrite;
            
            result.name = inputTexture.name;
            return result;
        }

        /// <summary>
        /// Converts albedo+specular and glossiness textures to a combined spec-gloss texture
        /// </summary>
        public static Texture2D ConvertAlbedoSpecGlosToSpecGloss(Texture inputTextureAlbedoSpec, Texture inputTextureGloss)
        {
            if (cache.ContainsKey(inputTextureAlbedoSpec))
            {
                Texture2D texture2D = cache[inputTextureAlbedoSpec];
                Debug.Log("Using cached converted texture " + texture2D.name);
                return texture2D;
            }
            else
            {
                Material material = new Material(BundleShaders.Find("Hidden/AlbedoSpecGlosToSpecGloss"));
                material.SetTexture("_AlbedoSpecTex", inputTextureAlbedoSpec);
                material.SetTexture("_GlossinessTex", inputTextureGloss);
                
                bool sRGBWrite = GL.sRGBWrite;
                GL.sRGBWrite = false;
                
                RenderTexture temporary = RenderTexture.GetTemporary(
                    inputTextureAlbedoSpec.width, 
                    inputTextureAlbedoSpec.height, 
                    0, 
                    RenderTextureFormat.ARGB32, 
                    RenderTextureReadWrite.Linear
                );
                
                Graphics.Blit(inputTextureAlbedoSpec, temporary, material);
                Texture2D result = temporary.ToTexture2D();
                result.name = inputTextureAlbedoSpec.name.ReplaceLastWord('_', "SPECGLOS");
                
                RenderTexture.ReleaseTemporary(temporary);
                GL.sRGBWrite = sRGBWrite;
                
                cache[inputTextureAlbedoSpec] = result;
                return result;
            }
        }

        /// <summary>
        /// Converts a RenderTexture to a Texture2D
        /// </summary>
        public static Texture2D ToTexture2D(this RenderTexture rTex)
        {
            Texture2D tex = new Texture2D(rTex.width, rTex.height, TextureFormat.RGBA32, false);
            RenderTexture.active = rTex;
            tex.ReadPixels(new Rect(0f, 0f, (float)rTex.width, (float)rTex.height), 0, 0);
            tex.Apply();
            return tex;
        }

        /// <summary>
        /// Replaces the last word in a string with a replacement
        /// </summary>
        public static string ReplaceLastWord(this string input, char separator, string replacement)
        {
            int num = input.LastIndexOf(separator);
            
            if (num == -1)
            {
                return replacement;
            }
            else
            {
                return input.Substring(0, num + 1) + replacement;
            }
        }

        /// <summary>
        /// Creates a solid color texture
        /// </summary>
        public static Texture2D CreateSolidColorTexture(int width, int height, float r, float g, float b, float a)
        {
            Texture2D texture = new Texture2D(width, height);
            Color[] pixels = new Color[width * height];
            Color color = new Color(r, g, b, a);
            
            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = color;
            }
            
            texture.SetPixels(pixels);
            texture.Apply();
            return texture;
        }

        /// <summary>
        /// Creates a solid grayscale texture
        /// </summary>
        public static Texture2D CreateSolidColorTexture(int width, int height, float c, float a)
        {
            return CreateSolidColorTexture(width, height, c, c, c, a);
        }
    }

    /// <summary>
    /// Class that reimports meshes from asset bundles
    /// </summary>
    public class MeshReimporter
    {
        public bool Done { get; private set; }
        public bool Success { get; private set; }
        public string ErrorMessage { get; private set; }

        private static Dictionary<int, UnityEngine.Mesh> cacheConvertedMesh = new Dictionary<int, UnityEngine.Mesh>();
        private FieldInfo resourceTypeField;

        /// <summary>
        /// Constructor
        /// </summary>
        public MeshReimporter()
        {
            // Try to find ResourceType field in AssetPoolObject using reflection
            try
            {
                var assetPoolObjectType = AppDomain.CurrentDomain.GetAssemblies()
                    .SelectMany(a => a.GetTypes())
                    .FirstOrDefault(t => t.Name == "AssetPoolObject");

                if (assetPoolObjectType != null)
                {
                    resourceTypeField = assetPoolObjectType.GetField("ResourceType", 
                        BindingFlags.Instance | BindingFlags.NonPublic);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error finding ResourceType field: {ex.Message}");
            }
        }

        /// <summary>
        /// Reimports mesh assets from asset bundles and replaces existing meshes
        /// </summary>
        public void ReimportMeshAssetsAndReplace(HashSet<GameObject> uniqueRootNodes)
        {
            Done = false;
            Success = false;
            ErrorMessage = null;

            if (uniqueRootNodes == null || uniqueRootNodes.Count == 0)
            {
                Done = true;
                Success = false;
                ErrorMessage = "No root nodes provided.";
                return;
            }

            // Find all AssetPoolObjects in the scene
            List<Component> assetPoolObjects = new List<Component>();
            foreach (var rootNode in uniqueRootNodes)
            {
                assetPoolObjects.AddRange(rootNode.GetComponentsInChildren(
                    Type.GetType("AssetPoolObject", false)
                ));
            }

            if (assetPoolObjects.Count == 0)
            {
                Done = true;
                Success = false;
                ErrorMessage = "No AssetPoolObjects found in the provided root nodes.";
                return;
            }

            HashSet<string> pathsToLoad = new HashSet<string>();

            try
            {
                foreach (var assetPoolObject in assetPoolObjects)
                {
                    // Get all mesh filters in this object
                    MeshFilter[] meshFilters = assetPoolObject.GetComponentsInChildren<MeshFilter>();
                    
                    // Check if any meshes are not readable
                    bool allMeshesReadable = true;
                    foreach (var meshFilter in meshFilters)
                    {
                        if (meshFilter.sharedMesh != null && !meshFilter.sharedMesh.isReadable)
                        {
                            allMeshesReadable = false;
                            break;
                        }
                    }

                    // If all meshes are readable, skip this asset
                    if (allMeshesReadable)
                        continue;

                    // Get the resource path
                    if (resourceTypeField != null)
                    {
                        // Attempt to get the resource path
                        object resourceType = resourceTypeField.GetValue(assetPoolObject);
                        
                        // Try to navigate to ItemTemplate.Prefab.path
                        try
                        {
                            Type resourceTypeType = resourceType.GetType();
                            
                            // Get ItemTemplate field
                            var itemTemplateField = resourceTypeType.GetField("ItemTemplate");
                            if (itemTemplateField != null)
                            {
                                var itemTemplate = itemTemplateField.GetValue(resourceType);
                                if (itemTemplate != null)
                                {
                                    // Get Prefab field
                                    var prefabField = itemTemplate.GetType().GetField("Prefab");
                                    if (prefabField != null)
                                    {
                                        var prefab = prefabField.GetValue(itemTemplate);
                                        if (prefab != null)
                                        {
                                            // Get path property
                                            var pathProperty = prefab.GetType().GetProperty("path");
                                            if (pathProperty != null)
                                            {
                                                string path = (string)pathProperty.GetValue(prefab);
                                                if (!string.IsNullOrEmpty(path))
                                                {
                                                    string fullPath = Path.GetFullPath(Path.Combine(
                                                        Application.streamingAssetsPath, "Windows", path
                                                    ));
                                                    
                                                    if (File.Exists(fullPath))
                                                    {
                                                        pathsToLoad.Add(fullPath);
                                                    }
                                                    else
                                                    {
                                                        Debug.LogError("File doesn't exist: " + fullPath);
                                                    }
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.LogError($"Error getting resource path: {ex.Message}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                ErrorMessage = "Error preparing asset paths: " + ex.Message;
                Done = true;
                Success = false;
                Debug.LogError(ex);
                return;
            }

            if (pathsToLoad.Count == 0)
            {
                Done = true;
                Success = true;
                Debug.Log("No assets need to be reimported.");
                return;
            }

            // Load assets from AssetStudio
            List<AssetStudio.AssetItem> assets;
            if (AssetStudio.Studio.LoadAssets(pathsToLoad, out assets))
            {
                ReplaceMesh(uniqueRootNodes, assets);
            }
            else
            {
                ErrorMessage = "Failed to load assets with AssetStudio.";
                Done = true;
                Success = false;
            }
        }

        /// <summary>
        /// Replaces mesh components with reimported meshes
        /// </summary>
        private void ReplaceMesh(HashSet<GameObject> uniqueRootNodes, List<AssetStudio.AssetItem> assets)
        {
            try
            {
                foreach (var meshFilter in uniqueRootNodes.SelectMany(rootNode => 
                    rootNode.GetComponentsInChildren<MeshFilter>()))
                {
                    if (meshFilter.sharedMesh == null)
                        continue;

                    if (!meshFilter.sharedMesh.isReadable)
                    {
                        int hashCode = meshFilter.sharedMesh.GetHashCode();
                        
                        if (cacheConvertedMesh.ContainsKey(hashCode))
                        {
                            meshFilter.sharedMesh = cacheConvertedMesh[hashCode];
                            Debug.Log(meshFilter.name + ": found mesh already converted in cache");
                        }
                        else
                        {
                            Debug.Log(meshFilter.name + ": mesh unreadable, requires reimport. Attempting...");
                            
                            // Find a matching mesh in the assets
                            AssetStudio.AssetItem assetItem = assets
                                .Where(asset => {
                                    AssetStudio.Mesh mesh = asset.Asset as AssetStudio.Mesh;
                                    return mesh != null && mesh.m_VertexCount == meshFilter.sharedMesh.vertexCount;
                                })
                                .OrderByDescending(asset => asset.Text == meshFilter.sharedMesh.name)
                                .FirstOrDefault();
                                
                            if (assetItem == null)
                            {
                                Debug.LogError(meshFilter.name + ": couldn't find replacement mesh!");
                            }
                            else
                            {
                                AssetStudio.Mesh asMesh = assetItem.Asset as AssetStudio.Mesh;
                                meshFilter.sharedMesh = UnityMeshConverter.ConvertToUnityMesh(asMesh);
                                cacheConvertedMesh[hashCode] = meshFilter.sharedMesh;
                                Debug.Log(meshFilter.name + ": success reimporting and replacing mesh");
                            }
                        }
                    }
                }
                
                Success = true;
            }
            catch (Exception ex)
            {
                ErrorMessage = "Error replacing meshes: " + ex.Message;
                Success = false;
                Debug.LogError(ex);
            }
            finally
            {
                Done = true;
            }
        }
    }

    #endregion

    #region Shader Management

    /// <summary>
    /// Class to manage shaders loaded from asset bundles
    /// </summary>
    public static class BundleShaders
    {
        private static Dictionary<string, Shader> shaders;

        /// <summary>
        /// Add a shader to the bundle
        /// </summary>
        public static void Add(Shader add)
        {
            if (shaders == null)
            {
                shaders = new Dictionary<string, Shader>();
            }
            
            if (add != null && !string.IsNullOrEmpty(add.name))
            {
                if (!shaders.ContainsKey(add.name))
                {
                    Debug.Log("Added " + add.name + " shader to BundleShaders.");
                    shaders[add.name] = add;
                }
                else
                {
                    Debug.LogWarning("Shader with name '" + add.name + "' already exists in BundleShaders.");
                }
            }
            else
            {
                Debug.LogError("Cannot add null shader or shader with empty name to BundleShaders.");
            }
        }

        /// <summary>
        /// Add multiple shaders to the bundle
        /// </summary>
        public static void Add(Shader[] add)
        {
            if (add == null)
            {
                Debug.LogError("Cannot add null shader array to BundleShaders.");
            }
            else
            {
                foreach (Shader shader in add)
                {
                    Add(shader);
                }
            }
        }

        /// <summary>
        /// Find a shader by name
        /// </summary>
        public static Shader Find(string name)
        {
            if (shaders == null)
            {
                Debug.LogWarning("No shaders have been added to BundleShaders yet.");
                return null;
            }
            
            if (string.IsNullOrEmpty(name))
            {
                Debug.LogError("Cannot find shader with null or empty name.");
                return null;
            }
            
            Shader shader;
            if (shaders.TryGetValue(name, out shader))
            {
                Debug.Log("Shader '" + name + "' found successfully!");
                return shader;
            }
            else
            {
                Debug.LogWarning("Shader '" + name + "' not found in BundleShaders.");
                return null;
            }
        }
    }

    /// <summary>
    /// Handles loading asset bundles
    /// </summary>
    public static class AssetBundleLoader
    {
        private static Dictionary<string, AssetBundle> loadedBundles = new Dictionary<string, AssetBundle>();

        /// <summary>
        /// Load an asset bundle from a file path
        /// </summary>
        public static AssetBundle LoadAssetBundle(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                Debug.LogError("Cannot load asset bundle with null or empty path.");
                return null;
            }

            if (loadedBundles.ContainsKey(path))
            {
                return loadedBundles[path];
            }

            try
            {
                AssetBundle bundle = AssetBundle.LoadFromFile(path);
                if (bundle != null)
                {
                    loadedBundles[path] = bundle;
                    Debug.Log("Loaded asset bundle: " + path);
                    return bundle;
                }
                else
                {
                    Debug.LogError("Failed to load asset bundle: " + path);
                    return null;
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error loading asset bundle {path}: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Unload all loaded asset bundles
        /// </summary>
        public static void UnloadAllBundles(bool unloadAllLoadedObjects)
        {
            foreach (var bundle in loadedBundles.Values)
            {
                if (bundle != null)
                {
                    bundle.Unload(unloadAllLoadedObjects);
                }
            }
            
            loadedBundles.Clear();
            Debug.Log("Unloaded all asset bundles.");
        }
    }

    #endregion

    #region Integration

    /// <summary>
    /// Class to handle integration between TransformCacher and TarkinItemExporter
    /// </summary>
    public class ExporterIntegration : MonoBehaviour
    {
        private object _transformCacher;
        private DatabaseManager _databaseManager;
        
        private void Awake()
        {
            try
            {
                // Find TransformCacher component using reflection
                var transformCacherType = GetType().Assembly.GetType("TransformCacher.TransformCacher");
                if (transformCacherType != null)
                {
                    _transformCacher = GetComponent(transformCacherType);
                }
                
                if (_transformCacher == null)
                {
                    Debug.LogError("ExporterIntegration must be attached to the same GameObject as TransformCacher");
                    return;
                }
                
                // Get database manager
                _databaseManager = DatabaseManager.Instance;
                
                // Log success
                Debug.Log("ExporterIntegration initialized successfully");
            }
            catch (Exception ex)
            {
                Debug.LogError($"Failed to initialize ExporterIntegration: {ex.Message}\n{ex.StackTrace}");
            }
        }
        
        /// <summary>
        /// Get the currently inspected object from TransformCacher
        /// </summary>
        public GameObject GetCurrentInspectedObject()
        {
            if (_transformCacher != null)
            {
                // Use reflection to get _currentInspectedObject field
                var field = _transformCacher.GetType().GetField("_currentInspectedObject", 
                    BindingFlags.NonPublic | BindingFlags.Instance);
                
                if (field != null)
                {
                    return field.GetValue(_transformCacher) as GameObject;
                }
            }
            
            return null;
        }
        
        /// <summary>
        /// Get all tagged objects in the scene
        /// </summary>
        public List<GameObject> GetAllTaggedObjects()
        {
            List<GameObject> result = new List<GameObject>();
            
            // Find all TransformCacherTag components in the scene
            TransformCacherTag[] tags = GameObject.FindObjectsOfType<TransformCacherTag>();
            
            foreach (var tag in tags)
            {
                if (tag != null && tag.gameObject != null && !tag.IsDestroyed)
                {
                    result.Add(tag.gameObject);
                }
            }
            
            return result;
        }
    }

    #endregion
}

namespace TarkinItemExporter
{
    #region Export Utilities

    /// <summary>
    /// Static class for handling GameObject exports
    /// </summary>
    public static class Exporter
    {
        public static Action CallbackFinished;
        public static bool glb;
        private static Coroutine coroutineExport;

        /// <summary>
        /// Create directory if it doesn't exist
        /// </summary>
        private static void CreateDirIfDoesntExist(string pathDir)
        {
            if (!Directory.Exists(pathDir))
            {
                Directory.CreateDirectory(pathDir);
            }
        }

        /// <summary>
        /// Delete all files in a directory
        /// </summary>
        private static void NukeDir(string pathDir)
        {
            DirectoryInfo directoryInfo = new DirectoryInfo(pathDir);
            foreach (FileInfo fileInfo in directoryInfo.GetFiles())
            {
                fileInfo.IsReadOnly = false;
                fileInfo.Delete();
            }
        }

        /// <summary>
        /// Get items that are currently open/visible
        /// </summary>
        public static List<Component> GetCurrentlyOpenItems()
        {
            // Try to find AssetPoolObject type
            Type assetPoolObjectType = AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(a => a.GetTypes())
                .FirstOrDefault(t => t.Name == "AssetPoolObject");
                
            if (assetPoolObjectType == null)
            {
                Debug.LogError("AssetPoolObject type not found.");
                return new List<Component>();
            }
            
            // Find all AssetPoolObjects in the scene
            var assetPoolObjects = UnityEngine.Object.FindObjectsOfType(assetPoolObjectType) as Component[];
            if (assetPoolObjects == null)
            {
                return new List<Component>();
            }
            
            // Filter by those that have a PreviewPivot component
            return assetPoolObjects
                .Where(o => o.gameObject.GetComponent("PreviewPivot") != null)
                .ToList();
        }

        /// <summary>
        /// Generate a hashed name for an item
        /// </summary>
        public static string GenerateHashedName(object item)
        {
            // Use reflection to get the necessary item properties
            try
            {
                Type itemType = item.GetType();
                
                // Get Template property
                var templateProperty = itemType.GetProperty("Template");
                if (templateProperty == null)
                {
                    return "Unknown_Item";
                }
                
                var template = templateProperty.GetValue(item);
                if (template == null)
                {
                    return "Unknown_Template";
                }
                
                // Get _name field from template
                var nameField = template.GetType().GetField("_name");
                if (nameField == null)
                {
                    return "Unknown_Name";
                }
                
                string name = nameField.GetValue(template) as string;
                
                // Get item hash
                var getItemHashMethod = Type.GetType("GClass906")?.GetMethod("GetItemHash", 
                    BindingFlags.Public | BindingFlags.Static);
                    
                if (getItemHashMethod == null)
                {
                    return name + "_unknown";
                }
                
                int hash = (int)getItemHashMethod.Invoke(null, new object[] { item });
                
                return name + "_" + hash.ToString();
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error generating hashed name: {ex.Message}");
                return "Error_" + Guid.NewGuid().ToString("N").Substring(0, 8);
            }
        }

        /// <summary>
        /// Export the specified GameObjects to GLTF/GLB
        /// </summary>
        public static void Export(HashSet<GameObject> uniqueRootNodes, string pathDir, string filename)
        {
            // Cancel any existing export
            if (coroutineExport != null)
            {
                var coroutineRunnerType = AppDomain.CurrentDomain.GetAssemblies()
                    .SelectMany(a => a.GetTypes())
                    .FirstOrDefault(t => t.Name == "CoroutineRunner");
                    
                if (coroutineRunnerType != null)
                {
                    var instanceProperty = coroutineRunnerType.GetProperty("Instance");
                    if (instanceProperty != null)
                    {
                        var instance = instanceProperty.GetValue(null);
                        var stopMethod = coroutineRunnerType.GetMethod("StopCoroutine");
                        if (stopMethod != null)
                        {
                            stopMethod.Invoke(instance, new object[] { coroutineExport });
                        }
                    }
                }
            }
            
            // Create a coroutine runner if needed
            var coroutineRunnerObj = new GameObject("CoroutineRunner");
            var monoBehaviour = coroutineRunnerObj.AddComponent<MonoBehaviour>();
            
            // Start the export coroutine
            StartExportCoroutine(monoBehaviour, uniqueRootNodes, pathDir, filename);
        }
        
        /// <summary>
        /// Starts the export coroutine using the provided MonoBehaviour
        /// </summary>
        private static void StartExportCoroutine(MonoBehaviour runner, HashSet<GameObject> uniqueRootNodes, string pathDir, string filename)
        {
            if (runner == null)
            {
                Debug.LogError("Coroutine runner is null");
                return;
            }
            
            // Show progress UI if available
            ShowProgressUI();
            
            // Check if we need to reimport meshes
            var meshReimporter = new TransformCacher.MeshReimporter();
            meshReimporter.ReimportMeshAssetsAndReplace(uniqueRootNodes);
            
            if (!meshReimporter.Success)
            {
                HideProgressUI();
                Debug.LogError("Export failed: Error loading bundles.");
                ShowNotification("Export failed. Something went wrong loading bundle files.");
                return;
            }
            
            try
            {
                // Process LODs and materials
                HandleLODs(uniqueRootNodes);
                DisableAllUnreadableMesh(uniqueRootNodes);
                PreprocessMaterials(uniqueRootNodes);
                
                // Export to GLTF/GLB
                GameObject[] toExport = uniqueRootNodes.ToArray();
                Debug.Log("Writing to disk: " + Path.Combine(pathDir, filename));
                
                try
                {
                    Export_UnityGLTF(toExport, pathDir, filename);
                }
                catch (Exception ex)
                {
                    Debug.LogError($"Export failed: {ex.Message}");
                    ShowNotification("Export failed. UnityGLTF failure. Or writing to disk failure.");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"Export failed: {ex.Message}");
                ShowNotification("Export failed. Something went wrong while handling scene objects.");
            }
            
            HideProgressUI();
        }
        
        /// <summary>
        /// Show progress UI if available
        /// </summary>
        private static void ShowProgressUI()
        {
            try
            {
                // Try to find ProgressScreen type and call ShowGameObject
                var progressScreenType = AppDomain.CurrentDomain.GetAssemblies()
                    .SelectMany(a => a.GetTypes())
                    .FirstOrDefault(t => t.Name == "ProgressScreen");
                    
                if (progressScreenType != null)
                {
                    var instanceProperty = progressScreenType.GetProperty("Instance");
                    if (instanceProperty != null)
                    {
                        var instance = instanceProperty.GetValue(null);
                        var showMethod = progressScreenType.GetMethod("ShowGameObject");
                        if (showMethod != null)
                        {
                            showMethod.Invoke(instance, new object[] { true });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error showing progress UI: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Hide progress UI if available
        /// </summary>
        private static void HideProgressUI()
        {
            try
            {
                // Try to find ProgressScreen type and call HideGameObject
                var progressScreenType = AppDomain.CurrentDomain.GetAssemblies()
                    .SelectMany(a => a.GetTypes())
                    .FirstOrDefault(t => t.Name == "ProgressScreen");
                    
                if (progressScreenType != null)
                {
                    var instanceProperty = progressScreenType.GetProperty("Instance");
                    if (instanceProperty != null)
                    {
                        var instance = instanceProperty.GetValue(null);
                        var hideMethod = progressScreenType.GetMethod("HideGameObject");
                        if (hideMethod != null)
                        {
                            hideMethod.Invoke(instance, new object[] { });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error hiding progress UI: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Show a notification message
        /// </summary>
        private static void ShowNotification(string message, string durationType = "Long", string iconType = "Default")
        {
            try
            {
                // Try to find NotificationManagerClass and call DisplayMessageNotification
                var notificationManagerType = AppDomain.CurrentDomain.GetAssemblies()
                    .SelectMany(a => a.GetTypes())
                    .FirstOrDefault(t => t.Name == "NotificationManagerClass");
                    
                if (notificationManagerType != null)
                {
                    var displayMethod = notificationManagerType.GetMethod("DisplayMessageNotification");
                    if (displayMethod != null)
                    {
                        // Convert durationType and iconType to enum values
                        var durationEnum = Enum.Parse(
                            Type.GetType("ENotificationDurationType"), 
                            durationType
                        );
                        
                        var iconEnum = Enum.Parse(
                            Type.GetType("ENotificationIconType"), 
                            iconType
                        );
                        
                        displayMethod.Invoke(null, new object[] { 
                            message, durationEnum, iconEnum, null 
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error showing notification: {ex.Message}");
            }
        }

        /// <summary>
        /// Disable meshes that are not readable
        /// </summary>
        public static void DisableAllUnreadableMesh(HashSet<GameObject> uniqueRootNodes)
        {
            foreach (GameObject gameObject in uniqueRootNodes)
            {
                MeshFilter[] components = gameObject.GetComponentsInChildren<MeshFilter>();
                foreach (MeshFilter meshFilter in components)
                {
                    if (meshFilter.sharedMesh == null)
                        continue;
                        
                    if (!meshFilter.sharedMesh.isReadable || meshFilter.sharedMesh.vertexCount == 0)
                    {
                        Debug.LogWarning(meshFilter.name + " has an unreadable mesh, disabling it.");
                        meshFilter.gameObject.SetActive(false);
                    }
                }
            }
        }

        /// <summary>
        /// Handle LOD groups in objects
        /// </summary>
        public static void HandleLODs(HashSet<GameObject> uniqueRootNodes)
        {
            Debug.Log("Handling LODs...");
            
            foreach (GameObject gameObject in uniqueRootNodes)
            {
                // Disable LOD groups
                LODGroup[] lodGroups = gameObject.GetComponentsInChildren<LODGroup>();
                foreach (LODGroup group in lodGroups)
                {
                    group.enabled = false;
                    
                    // Only enable renderers for LOD 0
                    LOD[] lods = group.GetLODs();
                    for (int j = 0; j < lods.Length; j++)
                    {
                        foreach (Renderer renderer in lods[j].renderers)
                        {
                            if (renderer != null)
                            {
                                renderer.enabled = (j == 0);
                            }
                        }
                    }
                }
                
                // Disable shadow-only mesh renderers
                foreach (MeshRenderer renderer in gameObject.GetComponentsInChildren<MeshRenderer>())
                {
                    if (renderer.shadowCastingMode == ShadowCastingMode.ShadowsOnly)
                    {
                        renderer.enabled = false;
                    }
                }
            }
        }

        /// <summary>
        /// Convert materials to GLTF-compatible formats
        /// </summary>
        public static void PreprocessMaterials(HashSet<GameObject> uniqueRootNodes)
        {
            foreach (GameObject gameObject in uniqueRootNodes)
            {
                foreach (Renderer renderer in gameObject.GetComponentsInChildren<Renderer>())
                {
                    Material[] materials = renderer.materials;
                    for (int j = 0; j < materials.Length; j++)
                    {
                        if (materials[j] != null)
                        {
                            materials[j] = materials[j].ConvertToUnityGLTFCompatible();
                        }
                    }
                    renderer.materials = materials;
                }
            }
        }

        /// <summary>
        /// Export to GLTF/GLB using UnityGLTF
        /// </summary>
        private static void Export_UnityGLTF(GameObject[] rootLevelNodes, string pathDir, string filename)
        {
            if (!Directory.Exists(pathDir))
            {
                Directory.CreateDirectory(pathDir);
            }
            
            // Get transforms from game objects
            Transform[] rootTransforms = rootLevelNodes
                .Select(go => go?.transform)
                .Where(t => t != null)
                .ToArray();
                
            try
            {
                // Use reflection to access UnityGLTF types
                var gltfSettingsType = AppDomain.CurrentDomain.GetAssemblies()
                    .SelectMany(a => a.GetTypes())
                    .FirstOrDefault(t => t.Name == "GLTFSettings");
                    
                var exportContextType = AppDomain.CurrentDomain.GetAssemblies()
                    .SelectMany(a => a.GetTypes())
                    .FirstOrDefault(t => t.Name == "ExportContext");
                    
                var gltfExporterType = AppDomain.CurrentDomain.GetAssemblies()
                    .SelectMany(a => a.GetTypes())
                    .FirstOrDefault(t => t.Name == "GLTFSceneExporter");
                    
                if (gltfSettingsType == null || exportContextType == null || gltfExporterType == null)
                {
                    Debug.LogError("UnityGLTF types not found.");
                    return;
                }
                
                // Get GLTFSettings
                var getSettingsMethod = gltfSettingsType.GetMethod("GetOrCreateSettings");
                var settings = getSettingsMethod.Invoke(null, null);
                
                // Set settings properties
                var exportDisabledProperty = gltfSettingsType.GetProperty("ExportDisabledGameObjects");
                var requireExtensionsProperty = gltfSettingsType.GetProperty("RequireExtensions");
                var useTextureFileTypeProperty = gltfSettingsType.GetProperty("UseTextureFileTypeHeuristic");
                
                exportDisabledProperty.SetValue(settings, false);
                requireExtensionsProperty.SetValue(settings, true);
                useTextureFileTypeProperty.SetValue(settings, false);
                
                // Create export context
                var contextConstructor = exportContextType.GetConstructor(new[] { gltfSettingsType });
                var context = contextConstructor.Invoke(new[] { settings });
                
                // Create exporter
                var exporterConstructor = gltfExporterType.GetConstructor(
                    new[] { typeof(Transform[]), exportContextType });
                var exporter = exporterConstructor.Invoke(new[] { rootTransforms, context });
                
                // Export
                if (glb)
                {
                    // Export as GLB
                    var saveGlbMethod = gltfExporterType.GetMethod("SaveGLB");
                    saveGlbMethod.Invoke(exporter, new[] { pathDir, filename });
                }
                else
                {
                    // Export as GLTF+BIN
                    var saveGltfMethod = gltfExporterType.GetMethod("SaveGLTFandBin");
                    saveGltfMethod.Invoke(exporter, new[] { pathDir, filename, true });
                }
                
                Debug.Log("Successful export with UnityGLTF. Output to: " + Path.Combine(pathDir, filename));
                ShowNotification("Successful export to " + Path.Combine(pathDir, filename));
            }
            catch (Exception ex)
            {
                Debug.LogError(ex);
            }
            
            // Call callback if set
            if (CallbackFinished != null)
            {
                CallbackFinished();
                CallbackFinished = null;
            }
        }
    }

    #endregion
}
