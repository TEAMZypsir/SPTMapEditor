using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace TarkinItemExporter
{
    #region Core Exporter

    /// <summary>
    /// Main exporter class for 3D models
    /// </summary>
    public static class Exporter
    {
        // Action to call when export is finished
        public static Action CallbackFinished;
        
        // Flag to control binary (glb) vs text (gltf) format
        public static bool glb;
        
        // Active coroutine for export process
        private static Coroutine coroutineExport;

        /// <summary>
        /// Export a set of GameObjects to GLTF/GLB
        /// </summary>
        public static void Export(HashSet<GameObject> uniqueRootNodes, string pathDir, string filename)
        {
            // Cancel any ongoing export
            if (coroutineExport != null)
            {
                CoroutineRunner.Instance.StopCoroutine(coroutineExport);
            }
            
            // Start the export coroutine
            coroutineExport = CoroutineRunner.Instance.StartCoroutine(ExportCoroutine(uniqueRootNodes, pathDir, filename));
        }

        /// <summary>
        /// Main export coroutine that handles the export process
        /// </summary>
        private static IEnumerator ExportCoroutine(HashSet<GameObject> uniqueRootNodes, string pathDir, string filename)
        {
            // Store original time scale and pause the game
            float origTimeScale = Time.timeScale;
            Time.timeScale = 0f;
            
            // Show progress UI
            ProgressScreen.Instance.ShowGameObject(true);
            
            // Step 1: Reimport unreadable meshes
            MeshReimporter meshReimporter = new MeshReimporter();
            meshReimporter.ReimportMeshAssetsAndReplace(uniqueRootNodes);
            
            // Wait for mesh reimporter to finish
            while (!meshReimporter.Done)
            {
                yield return null;
            }
            
            // Check if mesh reimporting was successful
            if (!meshReimporter.Success)
            {
                ProgressScreen.Instance.HideGameObject();
                Plugin.Log.LogInfo("Export failed: Error loading bundles.");
                NotificationManagerClass.DisplayMessageNotification("Export failed. Something went wrong loading bundle files.", ENotificationDurationType.Long, ENotificationIconType.Default, null);
                yield break;
            }
            
            try
            {
                // Step 2: Process scene objects
                HandleLODs(uniqueRootNodes);
                DisableAllUnreadableMesh(uniqueRootNodes);
                PreprocessMaterials(uniqueRootNodes);
            }
            catch (Exception ex)
            {
                ProgressScreen.Instance.HideGameObject();
                Plugin.Log.LogInfo(string.Format("Export failed: {0}", ex));
                NotificationManagerClass.DisplayMessageNotification("Export failed. Something went wrong while handling scene objects.", ENotificationDurationType.Long, ENotificationIconType.Default, null);
                yield break;
            }
            
            // Convert to array for export
            GameObject[] toExport = uniqueRootNodes.ToArray();
            Plugin.Log.LogInfo("Writing to disk: " + Path.Combine(pathDir, filename));
            yield return null;
            
            try
            {
                // Step 3: Export to GLTF/GLB
                Export_UnityGLTF(toExport, pathDir, filename);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogInfo(string.Format("Export failed: {0}", ex));
                NotificationManagerClass.DisplayMessageNotification("Export failed. UnityGLTF failure. Or writing to disk failure.", ENotificationDurationType.Long, ENotificationIconType.Default, null);
            }
            
            // Clean up and restore state
            ProgressScreen.Instance.HideGameObject();
            Time.timeScale = origTimeScale;
        }

        /// <summary>
        /// Disable any mesh that is unreadable or has zero vertices
        /// </summary>
        public static void DisableAllUnreadableMesh(HashSet<GameObject> uniqueRootNodes)
        {
            foreach (GameObject gameObject in uniqueRootNodes)
            {
                MeshFilter[] componentsInChildren = gameObject.GetComponentsInChildren<MeshFilter>();
                foreach (MeshFilter meshFilter in componentsInChildren)
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
        /// Handle LOD groups by disabling LOD switching and showing only the highest quality LOD
        /// </summary>
        public static void HandleLODs(HashSet<GameObject> uniqueRootNodes)
        {
            Plugin.Log.LogInfo("Handling LODs...");
            
            foreach (GameObject gameObject in uniqueRootNodes)
            {
                // Disable all LOD groups
                LODGroup[] lodGroups = gameObject.GetComponentsInChildren<LODGroup>();
                foreach (LODGroup group in lodGroups)
                {
                    group.enabled = false;
                }
                
                // Only enable the highest quality LOD (index 0)
                foreach (LODGroup lodgroup in lodGroups)
                {
                    LOD[] lods = lodgroup.GetLODs();
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
                
                // Disable shadow-only renderers
                foreach (MeshRenderer meshRenderer in gameObject.GetComponentsInChildren<MeshRenderer>())
                {
                    if (meshRenderer.shadowCastingMode == ShadowCastingMode.ShadowsOnly)
                    {
                        meshRenderer.enabled = false;
                    }
                }
            }
        }

        /// <summary>
        /// Process materials to make them compatible with GLTF export
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
        /// Export the GameObjects using UnityGLTF library
        /// </summary>
        private static void Export_UnityGLTF(GameObject[] rootLevelNodes, string pathDir, string filename)
        {
            // Create output directory if it doesn't exist
            if (!Directory.Exists(pathDir))
            {
                Directory.CreateDirectory(pathDir);
            }
            
            // Get all root transforms
            Transform[] rootTransforms = rootLevelNodes
                .Where(go => go != null)
                .Select(go => go.transform)
                .ToArray();
            
            // Configure GLTF export settings
            GLTFSettings orCreateSettings = GLTFSettings.GetOrCreateSettings();
            orCreateSettings.ExportDisabledGameObjects = false;
            orCreateSettings.RequireExtensions = true;
            orCreateSettings.UseTextureFileTypeHeuristic = false;
            
            // Create context and exporter
            ExportContext context = new ExportContext(orCreateSettings);
            GLTFSceneExporter gltfsceneExporter = new GLTFSceneExporter(rootTransforms, context);
            
            try
            {
                // Export as GLB or GLTF+BIN depending on setting
                if (glb)
                {
                    gltfsceneExporter.SaveGLB(pathDir, filename);
                }
                else
                {
                    gltfsceneExporter.SaveGLTFandBin(pathDir, filename, true);
                }
                
                // Log success and show notification
                Plugin.Log.LogInfo("Successful export with UnityGLTF. Output to: " + Path.Combine(pathDir, filename));
                NotificationManagerClass.DisplayMessageNotification("Successful export to " + Path.Combine(pathDir, filename), ENotificationDurationType.Long, ENotificationIconType.Default, null);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError(ex);
                throw;
            }
            
            // Call the completion callback if it exists
            if (CallbackFinished != null)
            {
                CallbackFinished();
            }
            
            // Reset the callback
            CallbackFinished = null;
        }

        /// <summary>
        /// Get the currently open items in the inspector or scene
        /// </summary>
        public static List<AssetPoolObject> GetCurrentlyOpenItems()
        {
            return Object.FindObjectsOfType<AssetPoolObject>()
                .Where(o => o.gameObject.GetComponent<PreviewPivot>() != null)
                .ToList();
        }

        /// <summary>
        /// Generate a unique name for an item based on its template
        /// </summary>
        public static string GenerateHashedName(Item item)
        {
            int itemHash = GClass906.GetItemHash(item);
            return item.Template._name + "_" + itemHash.ToString();
        }
    }

    #endregion

    #region Material Conversion

    /// <summary>
    /// Handles material conversion for GLTF compatibility
    /// </summary>
    public static class MaterialConverter
    {
        private static Dictionary<Texture, Material> cache = new Dictionary<Texture, Material>();
        
        /// <summary>
        /// Convert a material to be compatible with UnityGLTF
        /// </summary>
        public static Material ConvertToUnityGLTFCompatible(this Material origMat)
        {
            // Handle special case shaders
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
            
            Plugin.Log.LogInfo(origMat.name + ": converting to gltf specular-gloss...");
            
            try
            {
                // Get main texture
                Texture texture = origMat.GetTexture("_MainTex");
                if (texture == null)
                {
                    return origMat;
                }
                
                // Check cache first
                if (cache.ContainsKey(texture))
                {
                    Material material = cache[texture];
                    Plugin.Log.LogInfo("Using cached converted material " + material.name);
                    return material;
                }
                
                // Handle reflective materials
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
                
                // Get normal map
                Texture normalMap;
                if (!origMat.HasProperty("_BumpMap"))
                {
                    normalMap = Texture2D.normalTexture;
                }
                else
                {
                    normalMap = origMat.GetTexture("_BumpMap");
                }
                
                // Convert textures
                Texture2D specGlossTexture = TextureConverter.ConvertAlbedoSpecGlosToSpecGloss(texture, texture2);
                
                // Create new material with GLTF-compatible shader
                Material newMaterial = new Material(BundleShaders.Find("Hidden/DummySpecularOpaque"));
                
                // Setup keywords
                newMaterial.EnableKeyword("_NORMALMAP");
                newMaterial.EnableKeyword("_SPECGLOSSMAP");
                newMaterial.EnableKeyword("_EMISSION");
                newMaterial.EnableKeyword("_BUMPMAP");
                
                // Set textures and properties
                newMaterial.SetColor("_Color", origMat.color);
                newMaterial.SetTexture("_MainTex", texture);
                newMaterial.SetTexture("_SpecGlossMap", specGlossTexture);
                newMaterial.SetColor("_SpecColor", Color.white);
                newMaterial.SetTexture("_BumpMap", normalMap);
                
                // Handle emission maps
                if (origMat.HasProperty("_EmissionMap"))
                {
                    newMaterial.SetTexture("_EmissionMap", origMat.GetTexture("_EmissionMap"));
                    Color emissionColor = Color.white * origMat.GetFloat("_EmissionPower");
                    newMaterial.SetColor("_EmissionColor", emissionColor);
                }
                else
                {
                    newMaterial.SetColor("_EmissionColor", Color.black);
                }
                
                newMaterial.name = origMat.name;
                
                // Cache the converted material
                cache[texture] = newMaterial;
                
                return newMaterial;
            }
            catch (Exception ex)
            {
                Debug.LogError("Error converting material: " + ex.Message);
                return origMat;
            }
        }
    }

    #endregion

    #region Texture Conversion

    /// <summary>
    /// Handles texture conversion and creation
    /// </summary>
    public static class TextureConverter
    {
        private static Dictionary<Texture, Texture2D> cache = new Dictionary<Texture, Texture2D>();
        
        /// <summary>
        /// Convert a texture using a material
        /// </summary>
        public static Texture2D Convert(Texture inputTexture, Material mat)
        {
            // Store and restore GL sRGB write state
            bool sRGBWrite = GL.sRGBWrite;
            GL.sRGBWrite = false;
            
            // Create a render texture and blit the input texture through the material
            RenderTexture temporary = RenderTexture.GetTemporary(
                inputTexture.width, inputTexture.height, 0, 
                RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
                
            Graphics.Blit(inputTexture, temporary, mat);
            
            // Convert render texture to Texture2D
            Texture2D texture2D = temporary.ToTexture2D();
            
            // Clean up and restore state
            RenderTexture.ReleaseTemporary(temporary);
            GL.sRGBWrite = sRGBWrite;
            
            // Copy name
            texture2D.name = inputTexture.name;
            
            return texture2D;
        }
        
        /// <summary>
        /// Convert albedo/spec and gloss textures to a single spec-gloss texture
        /// </summary>
        public static Texture2D ConvertAlbedoSpecGlosToSpecGloss(Texture inputTextureAlbedoSpec, Texture inputTextureGloss)
        {
            // Check if already in cache
            if (cache.ContainsKey(inputTextureAlbedoSpec))
            {
                Texture2D texture2D = cache[inputTextureAlbedoSpec];
                Plugin.Log.LogInfo("Using cached converted texture " + texture2D.name);
                return texture2D;
            }
            
            // Create conversion material
            Material material = new Material(BundleShaders.Find("Hidden/AlbedoSpecGlosToSpecGloss"));
            material.SetTexture("_AlbedoSpecTex", inputTextureAlbedoSpec);
            material.SetTexture("_GlossinessTex", inputTextureGloss);
            
            // Set up conversion
            bool sRGBWrite = GL.sRGBWrite;
            GL.sRGBWrite = false;
            
            RenderTexture temporary = RenderTexture.GetTemporary(
                inputTextureAlbedoSpec.width, inputTextureAlbedoSpec.height, 0, 
                RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
                
            Graphics.Blit(inputTextureAlbedoSpec, temporary, material);
            
            Texture2D texture2D2 = temporary.ToTexture2D();
            texture2D2.name = inputTextureAlbedoSpec.name.ReplaceLastWord('_', "SPECGLOS");
            
            // Clean up
            RenderTexture.ReleaseTemporary(temporary);
            GL.sRGBWrite = sRGBWrite;
            
            // Cache the result
            cache[inputTextureAlbedoSpec] = texture2D2;
            
            return texture2D2;
        }
        
        /// <summary>
        /// Convert RenderTexture to Texture2D
        /// </summary>
        private static Texture2D ToTexture2D(this RenderTexture rTex)
        {
            Texture2D texture2D = new Texture2D(rTex.width, rTex.height, TextureFormat.RGBA32, false);
            RenderTexture.active = rTex;
            texture2D.ReadPixels(new Rect(0f, 0f, (float)rTex.width, (float)rTex.height), 0, 0);
            texture2D.Apply();
            return texture2D;
        }
        
        /// <summary>
        /// Replace the last word in a string (after separator) with a replacement
        /// </summary>
        public static string ReplaceLastWord(this string input, char separator, string replacement)
        {
            int num = input.LastIndexOf(separator);
            
            if (num == -1)
                return replacement;
            
            return input.Substring(0, num + 1) + replacement;
        }
        
        /// <summary>
        /// Create a solid color texture
        /// </summary>
        public static Texture2D CreateSolidColorTexture(int width, int height, float r, float g, float b, float a)
        {
            Texture2D texture2D = new Texture2D(width, height);
            Color[] array = new Color[width * height];
            Color color = new Color(r, g, b, a);
            
            for (int i = 0; i < array.Length; i++)
            {
                array[i] = color;
            }
            
            texture2D.SetPixels(array);
            texture2D.Apply();
            
            return texture2D;
        }
        
        /// <summary>
        /// Create a solid grayscale texture
        /// </summary>
        public static Texture2D CreateSolidColorTexture(int width, int height, float c, float a)
        {
            return CreateSolidColorTexture(width, height, c, c, c, a);
        }
    }

    #endregion

    #region Mesh Reimporter

    /// <summary>
    /// Handles reimporting mesh assets from bundles and asset files
    /// </summary>
    public class MeshReimporter
    {
        // Status properties
        public bool Done { get; private set; }
        public bool Success { get; private set; }
        public string ErrorMessage { get; private set; }
        
        // Cache to avoid reimporting the same mesh multiple times
        private static Dictionary<int, UnityEngine.Mesh> cacheConvertedMesh = new Dictionary<int, UnityEngine.Mesh>();
        
        // Field info to access resource type information
        private FieldInfo fieldInfo = typeof(AssetPoolObject).GetField("ResourceType", BindingFlags.Instance | BindingFlags.NonPublic);

        /// <summary>
        /// Reimport mesh assets and replace unreadable meshes in the provided GameObjects
        /// </summary>
        public void ReimportMeshAssetsAndReplace(HashSet<UnityEngine.GameObject> uniqueRootNodes)
        {
            // Reset status
            Done = false;
            Success = false;
            ErrorMessage = null;
            
            // Validate input
            if (uniqueRootNodes == null || uniqueRootNodes.Count == 0)
            {
                Done = true;
                Success = false;
                ErrorMessage = "No root nodes provided.";
                return;
            }
            
            // Collect all AssetPoolObject components
            List<AssetPoolObject> assetPoolObjects = uniqueRootNodes
                .SelectMany(rootNode => rootNode.GetComponentsInChildren<AssetPoolObject>())
                .ToList();
            
            // Set of paths to load for mesh reimporting
            HashSet<string> pathsToLoad = new HashSet<string>();
            
            try
            {
                // Process each asset pool object
                foreach (AssetPoolObject assetPoolObject in assetPoolObjects)
                {
                    // Check if meshes need to be reimported
                    UnityEngine.MeshFilter[] meshFilters = assetPoolObject.GetComponentsInChildren<UnityEngine.MeshFilter>();
                    bool needsReimport = meshFilters.Any(mf => 
                        mf.sharedMesh == null || !mf.sharedMesh.isReadable);
                        
                    if (!needsReimport)
                        continue;
                        
                    // Get resource type to find the asset path
                    ResourceTypeStruct resourceTypeStruct = (ResourceTypeStruct)fieldInfo.GetValue(assetPoolObject);
                    
                    if (resourceTypeStruct.ItemTemplate == null || resourceTypeStruct.ItemTemplate.Prefab == null)
                        continue;
                        
                    // Get the asset path and convert to full path
                    string path = resourceTypeStruct.ItemTemplate.Prefab.path;
                    string fullPath = Path.GetFullPath(Path.Combine(Application.streamingAssetsPath, "Windows", path));
                    
                    if (!File.Exists(fullPath))
                    {
                        Plugin.Log.LogError("File doesn't exist: " + fullPath);
                        continue;
                    }
                    
                    pathsToLoad.Add(fullPath);
                }
            }
            catch (Exception ex)
            {
                ErrorMessage = "Error preparing asset paths: " + ex.Message;
                Done = true;
                Success = false;
                Plugin.Log.LogError(ex);
                return;
            }
            
            // Run the asset loading and mesh replacement in a background task
            Action processComplete = null;
            
            Task.Run(() =>
            {
                List<AssetItem> assets;
                bool loadSuccess = Studio.LoadAssets(pathsToLoad, out assets);
                
                if (loadSuccess)
                {
                    AsyncWorker.RunInMainTread(() => 
                    {
                        ReplaceMesh(uniqueRootNodes, assets);
                    });
                }
                else
                {
                    AsyncWorker.RunInMainTread(() => 
                    {
                        ErrorMessage = "Failed to load assets with AssetStudio.";
                        Done = true;
                        Success = false;
                    });
                }
            }).ContinueWith((task) =>
            {
                if (task.IsFaulted)
                {
                    AsyncWorker.RunInMainTread(() => 
                    {
                        ErrorMessage = "Error in AssetStudio task: " +
                            (task.Exception.InnerException?.Message ?? task.Exception.Message);
                        Done = true;
                        Success = false;
                        Plugin.Log.LogError(task.Exception);
                    });
                }
            });
        }

        /// <summary>
        /// Replace unreadable meshes with readable ones from loaded assets
        /// </summary>
        private void ReplaceMesh(HashSet<UnityEngine.GameObject> uniqueRootNodes, List<AssetItem> assets)
        {
            try
            {
                // Process each mesh filter in the scene
                foreach (UnityEngine.MeshFilter meshFilter in uniqueRootNodes
                    .SelectMany(rootNode => rootNode.GetComponentsInChildren<UnityEngine.MeshFilter>()))
                {
                    if (meshFilter.sharedMesh == null)
                        continue;
                        
                    // Skip meshes that are already readable
                    if (meshFilter.sharedMesh.isReadable)
                        continue;
                        
                    // Check if we've already converted this mesh
                    int hashCode = meshFilter.sharedMesh.GetHashCode();
                    if (cacheConvertedMesh.ContainsKey(hashCode))
                    {
                        meshFilter.sharedMesh = cacheConvertedMesh[hashCode];
                        Plugin.Log.LogInfo(meshFilter.name + ": found mesh already converted in cache");
                        continue;
                    }
                    
                    Plugin.Log.LogInfo(meshFilter.name + ": mesh unreadable, requires reimport. Attempting...");
                    
                    // Find a matching mesh in the loaded assets
                    AssetItem assetItem = assets
                        .Where(asset => 
                            asset.Asset is AssetStudio.Mesh && 
                            ((AssetStudio.Mesh)asset.Asset).m_VertexCount == meshFilter.sharedMesh.vertexCount)
                        .OrderByDescending(asset => asset.Text == meshFilter.sharedMesh.name)
                        .FirstOrDefault();
                        
                    if (assetItem == null)
                    {
                        Plugin.Log.LogError(meshFilter.name + ": couldn't find replacement mesh!");
                        continue;
                    }
                    
                    // Convert the AssetStudio mesh to Unity mesh
                    AssetStudio.Mesh asMesh = assetItem.Asset as AssetStudio.Mesh;
                    meshFilter.sharedMesh = asMesh.ConvertToUnityMesh();
                    
                    // Cache the converted mesh
                    cacheConvertedMesh[hashCode] = meshFilter.sharedMesh;
                    Plugin.Log.LogInfo(meshFilter.name + ": success reimporting and replacing mesh");
                }
                
                Success = true;
            }
            catch (Exception ex)
            {
                ErrorMessage = "Error replacing meshes: " + ex.Message;
                Success = false;
                Plugin.Log.LogError(ex);
            }
            finally
            {
                Done = true;
            }
        }
    }

    #endregion

    #region Asset Loading and Management

    /// <summary>
    /// Handles loading asset bundles and tracking loaded assets
    /// </summary>
    public class SimpleBundleLoader : MonoBehaviour
    {
        private static SimpleBundleLoader _instance;
        private Dictionary<string, AssetBundle> loadedBundles = new Dictionary<string, AssetBundle>();
        private List<BundlePrefabInfo> availablePrefabs = new List<BundlePrefabInfo>();
        
        /// <summary>
        /// Singleton instance
        /// </summary>
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
        
        /// <summary>
        /// Load all asset bundles from the bundles directory
        /// </summary>
        public void LoadAllBundles()
        {
            // Clean up previously loaded bundles
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
            
            // Load all .bundle files in the directory and subdirectories
            string[] bundleFiles = Directory.GetFiles(bundlesDirectory, "*.bundle", SearchOption.AllDirectories);
            
            foreach (string bundleFile in bundleFiles)
            {
                LoadBundle(bundleFile);
            }
            
            Plugin.Log.LogInfo($"Loaded {loadedBundles.Count} bundles with {availablePrefabs.Count} prefabs from: {bundlesDirectory}");
        }
        
        /// <summary>
        /// Load a specific asset bundle
        /// </summary>
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
        
        /// <summary>
        /// Get path relative to the bundles directory
        /// </summary>
        private string GetRelativePath(string fullPath, string basePath)
        {
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
        
        /// <summary>
        /// Spawn a prefab from loaded asset bundles
        /// </summary>
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
            
            return spawnedObject;
        }
        
        /// <summary>
        /// Register a spawned object for tracking
        /// </summary>
        private void RegisterSpawnedObject(GameObject spawnedObject, string bundleName, string assetName)
        {
            // Add a component to track this as a spawned bundle object
            BundleObjectIdentifier identifier = spawnedObject.AddComponent<BundleObjectIdentifier>();
            identifier.BundleName = bundleName;
            identifier.AssetName = assetName;
            
            Plugin.Log.LogInfo($"Spawned object '{spawnedObject.name}' from bundle '{bundleName}'");
        }
        
        /// <summary>
        /// Get a list of all available prefabs from loaded bundles
        /// </summary>
        public List<BundlePrefabInfo> GetAvailablePrefabs()
        {
            return availablePrefabs;
        }
        
        /// <summary>
        /// Refresh and reload all asset bundles
        /// </summary>
        public void RefreshBundles()
        {
            LoadAllBundles();
        }
        
        /// <summary>
        /// Simple component to identify spawned bundle objects
        /// </summary>
        public class BundleObjectIdentifier : MonoBehaviour
        {
            public string BundleName;
            public string AssetName;
        }
        
        /// <summary>
        /// Class to store prefab information
        /// </summary>
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

    /// <summary>
    /// Static helper for shader management
    /// </summary>
    public static class BundleShaders
    {
        private static Dictionary<string, Shader> shaders;
        
        /// <summary>
        /// Add a shader to the collection
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
                    Plugin.Log.LogInfo("Added " + add.name + " shader to BundleShaders.");
                    shaders[add.name] = add;
                }
                else
                {
                    Plugin.Log.LogWarning("Shader with name '" + add.name + "' already exists in BundleShaders.");
                }
            }
            else
            {
                Plugin.Log.LogError("Cannot add null shader or shader with empty name to BundleShaders.");
            }
        }
        
        /// <summary>
        /// Add multiple shaders
        /// </summary>
        public static void Add(Shader[] add)
        {
            if (add == null)
            {
                Plugin.Log.LogError("Cannot add null shader array to BundleShaders.");
            }
            else
            {
                foreach (Shader add2 in add)
                {
                    Add(add2);
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
                Plugin.Log.LogWarning("No shaders have been added to BundleShaders yet.");
                return null;
            }
            
            if (string.IsNullOrEmpty(name))
            {
                Plugin.Log.LogError("Cannot find shader with null or empty name.");
                return null;
            }
            
            Shader shader;
            if (shaders.TryGetValue(name, out shader))
            {
                Plugin.Log.LogInfo("Shader '" + name + "' found successfully!");
                return shader;
            }
            
            Plugin.Log.LogWarning("Shader '" + name + "' not found in BundleShaders.");
            return null;
        }
    }

    /// <summary>
    /// Helper for loading asset bundles
    /// </summary>
    public static class AssetBundleLoader
    {
        private static Dictionary<string, AssetBundle> _loadedBundles = new Dictionary<string, AssetBundle>();
        
        /// <summary>
        /// Load an asset bundle from disk
        /// </summary>
        public static AssetBundle LoadAssetBundle(string bundlePath)
        {
            if (string.IsNullOrEmpty(bundlePath))
            {
                Plugin.Log.LogError("Bundle path is null or empty");
                return null;
            }
            
            // Check if bundle is already loaded
            string bundleName = Path.GetFileName(bundlePath);
            if (_loadedBundles.ContainsKey(bundleName))
            {
                return _loadedBundles[bundleName];
            }
            
            try
            {
                // Load the asset bundle
                AssetBundle bundle = AssetBundle.LoadFromFile(bundlePath);
                
                if (bundle == null)
                {
                    Plugin.Log.LogError($"Failed to load asset bundle: {bundlePath}");
                    return null;
                }
                
                // Cache the loaded bundle
                _loadedBundles[bundleName] = bundle;
                
                Plugin.Log.LogInfo($"Successfully loaded asset bundle: {bundleName}");
                return bundle;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"Error loading asset bundle {bundlePath}: {ex.Message}");
                return null;
            }
        }
        
        /// <summary>
        /// Unload all asset bundles
        /// </summary>
        public static void UnloadAllBundles(bool unloadAllLoadedObjects = false)
        {
            foreach (var bundle in _loadedBundles.Values)
            {
                if (bundle != null)
                {
                    bundle.Unload(unloadAllLoadedObjects);
                }
            }
            
            _loadedBundles.Clear();
            Plugin.Log.LogInfo("All asset bundles unloaded");
        }
        
        /// <summary>
        /// Load all assets of a specific type from a bundle
        /// </summary>
        public static T[] LoadAllAssetsOfType<T>(string bundlePath) where T : UnityEngine.Object
        {
            AssetBundle bundle = LoadAssetBundle(bundlePath);
            if (bundle == null)
                return new T[0];
                
            return bundle.LoadAllAssets<T>();
        }
    }

    #endregion

    #region Unity Mesh Conversion

    /// <summary>
    /// Handles conversion between AssetStudio Mesh and Unity Mesh
    /// </summary>
    public static class UnityMeshConverter
    {
        /// <summary>
        /// Convert an AssetStudio Mesh to a Unity Mesh
        /// </summary>
        public static UnityEngine.Mesh ConvertToUnityMesh(this AssetStudio.Mesh asMesh)
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
            int vertexStride = 3;
            
            if (asMesh.m_Vertices.Length == asMesh.m_VertexCount * 4)
            {
                vertexStride = 4;
            }
            
            for (int i = 0; i < asMesh.m_VertexCount; i++)
            {
                vertices[i] = new UnityEngine.Vector3(
                    asMesh.m_Vertices[i * vertexStride], 
                    asMesh.m_Vertices[i * vertexStride + 1], 
                    asMesh.m_Vertices[i * vertexStride + 2]);
            }
            
            mesh.vertices = vertices;
            
            // Process indices/triangles
            if (asMesh.m_Indices == null || asMesh.m_Indices.Count == 0)
            {
                Debug.LogError("AssetStudioMesh has no index data.");
                return null;
            }
            
            // Process submeshes if they exist
            if (asMesh.m_SubMeshes != null && asMesh.m_SubMeshes.Length != 0)
            {
                mesh.subMeshCount = asMesh.m_SubMeshes.Length;
                int indexOffset = 0;
                
                for (int j = 0; j < asMesh.m_SubMeshes.Length; j++)
                {
                    var subMesh = asMesh.m_SubMeshes[j];
                    int indexCount = (int)subMesh.indexCount;
                    int[] triangles = new int[indexCount];
                    
                    for (int k = 0; k < indexCount; k++)
                    {
                        triangles[k] = (int)asMesh.m_Indices[indexOffset + k];
                    }
                    
                    mesh.SetTriangles(triangles, j);
                    indexOffset += indexCount;
                }
            }
            else
            {
                // Single mesh with all triangles
                int[] triangles = new int[asMesh.m_Indices.Count];
                for (int l = 0; l < asMesh.m_Indices.Count; l++)
                {
                    triangles[l] = (int)asMesh.m_Indices[l];
                }
                
                mesh.triangles = triangles;
            }
            
            // Process UVs
            if (asMesh.m_UV0 != null && asMesh.m_UV0.Length != 0)
            {
                UnityEngine.Vector2[] uv = new UnityEngine.Vector2[asMesh.m_VertexCount];
                int uvStride = 4;
                
                if (asMesh.m_UV0.Length == asMesh.m_VertexCount * 2)
                {
                    uvStride = 2;
                }
                else if (asMesh.m_UV0.Length == asMesh.m_VertexCount * 3)
                {
                    uvStride = 3;
                }
                
                for (int m = 0; m < asMesh.m_VertexCount; m++)
                {
                    uv[m] = new UnityEngine.Vector2(asMesh.m_UV0[m * uvStride], asMesh.m_UV0[m * uvStride + 1]);
                }
                
                mesh.uv = uv;
            }
            
            // Process additional UV sets (UV1-7)
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
            
            // Process normals
            if (asMesh.m_Normals != null && asMesh.m_Normals.Length != 0)
            {
                UnityEngine.Vector3[] normals = new UnityEngine.Vector3[asMesh.m_VertexCount];
                int normalStride = 3;
                
                if (asMesh.m_Normals.Length == asMesh.m_VertexCount * 4)
                {
                    normalStride = 4;
                }
                
                for (int n = 0; n < asMesh.m_VertexCount; n++)
                {
                    normals[n] = new UnityEngine.Vector3(
                        asMesh.m_Normals[n * normalStride],
                        asMesh.m_Normals[n * normalStride + 1],
                        asMesh.m_Normals[n * normalStride + 2]);
                }
                
                mesh.normals = normals;
            }
            
            // Process colors
            if (asMesh.m_Colors != null && asMesh.m_Colors.Length != 0)
            {
                UnityEngine.Color[] colors = new UnityEngine.Color[asMesh.m_VertexCount];
                int colorStride = 4;
                
                if (asMesh.m_Colors.Length == asMesh.m_VertexCount * 3)
                {
                    colorStride = 3;
                }
                
                for (int c = 0; c < asMesh.m_VertexCount; c++)
                {
                    if (colorStride == 4)
                    {
                        colors[c] = new UnityEngine.Color(
                            asMesh.m_Colors[c * colorStride],
                            asMesh.m_Colors[c * colorStride + 1],
                            asMesh.m_Colors[c * colorStride + 2],
                            asMesh.m_Colors[c * colorStride + 3]);
                    }
                    else
                    {
                        colors[c] = new UnityEngine.Color(
                            asMesh.m_Colors[c * colorStride],
                            asMesh.m_Colors[c * colorStride + 1],
                            asMesh.m_Colors[c * colorStride + 2]);
                    }
                }
                
                mesh.colors = colors;
            }
            
            // Process tangents
            if (asMesh.m_Tangents != null && asMesh.m_Tangents.Length != 0)
            {
                UnityEngine.Vector4[] tangents = new UnityEngine.Vector4[asMesh.m_VertexCount];
                int tangentStride = 4;
                
                if (asMesh.m_Tangents.Length == asMesh.m_VertexCount * 3)
                {
                    tangentStride = 3;
                }
                
                for (int t = 0; t < asMesh.m_VertexCount; t++)
                {
                    if (tangentStride == 4)
                    {
                        tangents[t] = new UnityEngine.Vector4(
                            asMesh.m_Tangents[t * tangentStride],
                            asMesh.m_Tangents[t * tangentStride + 1],
                            asMesh.m_Tangents[t * tangentStride + 2],
                            asMesh.m_Tangents[t * tangentStride + 3]);
                    }
                    else
                    {
                        tangents[t] = new UnityEngine.Vector4(
                            asMesh.m_Tangents[t * tangentStride],
                            asMesh.m_Tangents[t * tangentStride + 1],
                            asMesh.m_Tangents[t * tangentStride + 2],
                            1f);
                    }
                }
                
                mesh.tangents = tangents;
            }
            
            // Recalculate bounds
            mesh.RecalculateBounds();
            
            return mesh;
        }

        /// <summary>
        /// Convert a float array to a Vector2 array for UV sets
        /// </summary>
        private static UnityEngine.Vector2[] ConvertFloatArrayToUV(float[] floatUVs, int vertexCount)
        {
            UnityEngine.Vector2[] array = new UnityEngine.Vector2[vertexCount];
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
                array[i] = new UnityEngine.Vector2(floatUVs[i * stride], floatUVs[i * stride + 1]);
            }
            
            return array;
        }
    }

    #endregion

    #region Utility Classes

    /// <summary>
    /// Helper class to run coroutines
    /// </summary>
    public class CoroutineRunner : MonoBehaviour
    {
        private static CoroutineRunner _instance;
        
        /// <summary>
        /// Singleton instance
        /// </summary>
        public static CoroutineRunner Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = FindObjectOfType<CoroutineRunner>();
                    
                    if (_instance == null)
                    {
                        GameObject gameObject = new GameObject("CoroutineRunner");
                        _instance = gameObject.AddComponent<CoroutineRunner>();
                        DontDestroyOnLoad(gameObject);
                    }
                }
                
                return _instance;
            }
        }
        
        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
            }
            else
            {
                _instance = this;
                DontDestroyOnLoad(gameObject);
            }
        }
    }

    /// <summary>
    /// Helper class for async/background operations
    /// </summary>
    public static class AsyncWorker
    {
        /// <summary>
        /// Queue an action to run on the main Unity thread
        /// </summary>
        public static void RunInMainTread(Action action)
        {
            // Invoke the action on the main thread
            // In a real implementation, this would use synchronization primitives
            // For simplicity, this just executes the action directly
            action();
        }
    }

    /// <summary>
    /// Compatibility layer for AssetStudio
    /// </summary>
    public static class AssetStudioCompat
    {
        /// <summary>
        /// Simplified mesh properties structure
        /// </summary>
        public class AssetStudioMesh
        {
            public string m_Name;
            public int m_VertexCount;
            public float[] m_Vertices;
            public List<uint> m_Indices;
            public SubMesh[] m_SubMeshes;
            public float[] m_UV0;
            public float[] m_UV1;
            public float[] m_UV2;
            public float[] m_UV3;
            public float[] m_UV4;
            public float[] m_UV5;
            public float[] m_UV6;
            public float[] m_UV7;
            public float[] m_Normals;
            public float[] m_Tangents;
            public float[] m_Colors;
        }
        
        /// <summary>
        /// Extract mesh properties from an AssetStudio mesh object
        /// </summary>
        public static AssetStudioMesh GetAssetStudioMeshProperties(object assetObject)
        {
            if (assetObject == null || !assetObject.GetType().Name.Contains("Mesh"))
                return null;
                
            try
            {
                // Use reflection to extract properties
                AssetStudioMesh result = new AssetStudioMesh();
                
                Type assetType = assetObject.GetType();
                
                // Get basic properties
                result.m_Name = (string)assetType.GetProperty("m_Name").GetValue(assetObject);
                result.m_VertexCount = (int)assetType.GetProperty("m_VertexCount").GetValue(assetObject);
                
                // Get vertex data
                result.m_Vertices = (float[])assetType.GetProperty("m_Vertices").GetValue(assetObject);
                result.m_Indices = (List<uint>)assetType.GetProperty("m_Indices").GetValue(assetObject);
                result.m_SubMeshes = (SubMesh[])assetType.GetProperty("m_SubMeshes").GetValue(assetObject);
                
                // Get UV data
                result.m_UV0 = (float[])assetType.GetProperty("m_UV0").GetValue(assetObject);
                result.m_UV1 = (float[])assetType.GetProperty("m_UV1").GetValue(assetObject);
                result.m_UV2 = (float[])assetType.GetProperty("m_UV2").GetValue(assetObject);
                result.m_UV3 = (float[])assetType.GetProperty("m_UV3").GetValue(assetObject);
                result.m_UV4 = (float[])assetType.GetProperty("m_UV4").GetValue(assetObject);
                result.m_UV5 = (float[])assetType.GetProperty("m_UV5").GetValue(assetObject);
                result.m_UV6 = (float[])assetType.GetProperty("m_UV6").GetValue(assetObject);
                result.m_UV7 = (float[])assetType.GetProperty("m_UV7").GetValue(assetObject);
                
                // Get other vertex attributes
                result.m_Normals = (float[])assetType.GetProperty("m_Normals").GetValue(assetObject);
                result.m_Tangents = (float[])assetType.GetProperty("m_Tangents").GetValue(assetObject);
                result.m_Colors = (float[])assetType.GetProperty("m_Colors").GetValue(assetObject);
                
                return result;
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error extracting mesh properties: {ex.Message}");
                return null;
            }
        }
    }

    #endregion
}
