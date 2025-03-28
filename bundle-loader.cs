using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace TarkinItemExporter
{
    public class SimpleBundleLoader : MonoBehaviour
    {
        private static SimpleBundleLoader _instance;
        private Dictionary<string, AssetBundle> loadedBundles = new Dictionary<string, AssetBundle>();
        private List<BundlePrefabInfo> availablePrefabs = new List<BundlePrefabInfo>();
        
        public static SimpleBundleLoader Instance
        {
            get
            {
                if (_instance == null)
                {
                    GameObject go = new GameObject("BundleLoader");
                    _instance = go.AddComponent<SimpleBundleLoader>();
                    DontDestroyOnLoad(go);
                }
                return _instance;
            }
        }
        
        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            
            _instance = this;
            DontDestroyOnLoad(gameObject);
            
            // Load all available bundles
            LoadAllBundles();
        }
        
        public void LoadAllBundles()
        {
            // Clear previous data
            foreach (var bundle in loadedBundles.Values)
            {
                if (bundle != null)
                {
                    bundle.Unload(false);
                }
            }
            
            loadedBundles.Clear();
            availablePrefabs.Clear();
            
            if (Plugin.Instance == null)
            {
                Debug.LogError("Plugin instance is null, cannot load bundles");
                return;
            }
            
            string bundlesDirectory = Plugin.Instance.BundlesDirectory;
            
            if (!Directory.Exists(bundlesDirectory))
            {
                Plugin.Log.LogError("Bundles directory not found: " + bundlesDirectory);
                return;
            }
            
            // Get all .bundle files in the bundle directory and subdirectories
            string[] bundleFiles = Directory.GetFiles(bundlesDirectory, "*.bundle", SearchOption.AllDirectories);
            
            foreach (string bundleFile in bundleFiles)
            {
                LoadBundle(bundleFile);
            }
            
            Plugin.Log.LogInfo($"Loaded {loadedBundles.Count} bundles with {availablePrefabs.Count} prefabs from: {bundlesDirectory}");
        }
        
        private void LoadBundle(string bundlePath)
        {
            try
            {
                // Load the asset bundle
                AssetBundle bundle = AssetBundle.LoadFromFile(bundlePath);
                
                if (bundle == null)
                {
                    Plugin.Log.LogError("Failed to load bundle: " + bundlePath);
                    return;
                }
                
                string bundleName = Path.GetFileNameWithoutExtension(bundlePath);
                string relativePath = GetRelativePath(bundlePath, Plugin.Instance.BundlesDirectory);
                
                // Add to loaded bundles dictionary
                loadedBundles[bundleName] = bundle;
                
                // Get all prefabs/assets in the bundle
                string[] assetNames = bundle.GetAllAssetNames();
                
                foreach (string assetName in assetNames)
                {
                    // Only add GameObjects as they can be spawned
                    UnityEngine.Object asset = bundle.LoadAsset(assetName);
                    if (asset is GameObject)
                    {
                        // Create a prefab info for this asset
                        BundlePrefabInfo prefabInfo = new BundlePrefabInfo
                        {
                            BundleName = bundleName,
                            AssetName = assetName,
                            DisplayName = Path.GetFileNameWithoutExtension(assetName),
                            Path = relativePath,
                            Prefab = asset as GameObject
                        };
                        
                        availablePrefabs.Add(prefabInfo);
                    }
                }
                
                Plugin.Log.LogInfo($"Loaded bundle '{bundleName}' from {relativePath} with {assetNames.Length} assets");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"Error loading bundle {bundlePath}: {ex.Message}");
            }
        }
        
        private string GetRelativePath(string fullPath, string basePath)
        {
            // Get path relative to the bundles directory
            if (!basePath.EndsWith(Path.DirectorySeparatorChar.ToString()))
            {
                basePath += Path.DirectorySeparatorChar;
            }
            
            if (fullPath.StartsWith(basePath))
            {
                return fullPath.Substring(basePath.Length);
            }
            
            return fullPath;
        }
        
        public GameObject SpawnPrefabInScene(string bundleName, string assetName, Vector3 position, Quaternion rotation)
        {
            if (!loadedBundles.ContainsKey(bundleName))
            {
                Plugin.Log.LogError($"Bundle '{bundleName}' not loaded.");
                return null;
            }
            
            AssetBundle bundle = loadedBundles[bundleName];
            GameObject prefab = bundle.LoadAsset<GameObject>(assetName);
            
            if (prefab == null)
            {
                Plugin.Log.LogError($"Asset '{assetName}' not found in bundle '{bundleName}'.");
                return null;
            }
            
            // Instantiate the prefab
            GameObject spawnedObject = Instantiate(prefab, position, rotation);
            spawnedObject.name = Path.GetFileNameWithoutExtension(assetName);
            
            // Register the spawned object in the database
            RegisterSpawnedObject(spawnedObject, bundleName, assetName);
            
            // Try to tag the object with TransformCacher if available
            if (typeof(TransformCacher.TransformCacher).Assembly != null)
            {
                try
                {
                    var transformCacher = FindObjectOfType<TransformCacher.TransformCacher>();
                    if (transformCacher != null)
                    {
                        transformCacher.TagObject(spawnedObject);
                        Plugin.Log.LogInfo($"Tagged spawned object with TransformCacher: {spawnedObject.name}");
                    }
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogWarning($"Could not tag object with TransformCacher: {ex.Message}");
                }
            }
            
            return spawnedObject;
        }
        
        private void RegisterSpawnedObject(GameObject spawnedObject, string bundleName, string assetName)
        {
            // Add a component to track this as a spawned bundle object
            BundleObjectIdentifier identifier = spawnedObject.AddComponent<BundleObjectIdentifier>();
            identifier.BundleName = bundleName;
            identifier.AssetName = assetName;
            
            Plugin.Log.LogInfo($"Spawned object '{spawnedObject.name}' from bundle '{bundleName}'");
        }
        
        public List<BundlePrefabInfo> GetAvailablePrefabs()
        {
            return availablePrefabs;
        }
        
        // Method to refresh bundles (can be called when new bundles are added)
        public void RefreshBundles()
        {
            LoadAllBundles();
        }
        
        // Simple component to identify spawned bundle objects
        public class BundleObjectIdentifier : MonoBehaviour
        {
            public string BundleName;
            public string AssetName;
        }
        
        // Class to store prefab information
        public class BundlePrefabInfo
        {
            public string BundleName;
            public string AssetName;
            public string DisplayName;
            public string Path;
            public GameObject Prefab;
            
            public override string ToString()
            {
                return $"{Path}/{DisplayName}";
            }
        }
    }
}