using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEditor;
using EFT;

namespace TransformCacher
{
    public class AssetManager : MonoBehaviour
    {
        private static AssetManager _instance;
        public static AssetManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    GameObject go = new GameObject("AssetManager");
                    DontDestroyOnLoad(go);
                    _instance = go.AddComponent<AssetManager>();
                }
                return _instance;
            }
        }
        
        // Logger reference
        private BepInEx.Logging.ManualLogSource Logger;
        
        // Asset paths
        private string _modDirectory;
        private string _originalAssetsPath;
        private string _modifiedAssetsPath;
        private string _gameLevelFilesPath;

        // Maps scene names to their actual level filenames
        private Dictionary<string, string> _sceneToLevelFileMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        
        // Maps scene names to their actual scene file paths
        private Dictionary<string, string> _sceneToSceneFileMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        
        public void Initialize()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            
            _instance = this;
            
            // Get logger
            Logger = BepInEx.Logging.Logger.CreateLogSource("AssetManager");
            
            // Set up asset paths
            string pluginPath = Path.GetDirectoryName(typeof(TransformCacherPlugin).Assembly.Location);
            _modDirectory = Path.Combine(pluginPath, "ModifiedAssets");
            _originalAssetsPath = Application.dataPath;
            _modifiedAssetsPath = Path.Combine(_modDirectory, "Assets");
            
            // Path to game level files (EscapeFromTarkov_Data/)
            _gameLevelFilesPath = Path.GetFullPath(Application.dataPath);
            Logger.LogInfo($"Game level files path: {_gameLevelFilesPath}");
            
            // Create mod directory if it doesn't exist
            if (!Directory.Exists(_modDirectory))
            {
                Directory.CreateDirectory(_modDirectory);
            }
            
            if (!Directory.Exists(_modifiedAssetsPath))
            {
                Directory.CreateDirectory(_modifiedAssetsPath);
            }

            // Create directory for scenes
            string sceneDirectory = Path.Combine(_modifiedAssetsPath, "Scenes");
            if (!Directory.Exists(sceneDirectory))
            {
                Directory.CreateDirectory(sceneDirectory);
            }
            
            // Initialize scene file mappings
            InitializeFileMappings();
            
            Logger.LogInfo("AssetManager initialized");
        }

        /// <summary>
        /// Initialize mappings between scene names and actual level files
        /// </summary>
        private void InitializeFileMappings()
        {
            try
            {
                Logger.LogInfo("Initializing scene file mappings...");
                
                // Clear existing mappings
                _sceneToLevelFileMap.Clear();
                _sceneToSceneFileMap.Clear();
                
                // Add known standard mappings
                AddKnownMappings();
                
                // Scan level directories for level files
                ScanForLevelFiles();
                
                // Scan for scene files
                ScanForSceneFiles();
                
                Logger.LogInfo($"Initialized file mappings: found {_sceneToLevelFileMap.Count} level files and {_sceneToSceneFileMap.Count} scene files");
            }
            catch (Exception ex)
            {
                Logger.LogError($"Error initializing file mappings: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Add known standard level name mappings
        /// </summary>
        private void AddKnownMappings()
        {
            // Add standard scene to level name mappings
            Dictionary<string, string> sceneToLevelMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "Factory", "factory4_day" },
                { "FactoryNight", "factory4_night" },
                { "Woods", "woods" },
                { "Customs", "customs" },
                { "Interchange", "interchange" },
                { "Laboratory", "laboratory" },
                { "Reserve", "rezervbase" },
                { "Shoreline", "shoreline" },
                { "Lighthouse", "lighthouse" },
                { "Streets", "tarkovstreets" },
                { "Factory_Rework_Day_Scripts", "level528" }, // Add the specific mapping mentioned in the issue
            };
            
            foreach (var mapping in sceneToLevelMap)
            {
                _sceneToLevelFileMap[mapping.Key] = mapping.Value;
            }
        }
        
        /// <summary>
        /// Scan the game directory for level files to build mappings
        /// </summary>
        private void ScanForLevelFiles()
        {
            try
            {
                // Scan the main level directory
                string levelDirPath = _gameLevelFilesPath;
                if (Directory.Exists(levelDirPath))
                {
                    foreach (var directory in Directory.GetDirectories(levelDirPath))
                    {
                        string dirName = Path.GetFileName(directory);
                        if (!string.IsNullOrEmpty(dirName) && !_sceneToLevelFileMap.ContainsValue(dirName))
                        {
                            // Look for any config files that might indicate the scene name
                            string[] configFiles = Directory.GetFiles(directory, "*.json", SearchOption.AllDirectories);
                            foreach (string configFile in configFiles)
                            {
                                try
                                {
                                    string configContent = File.ReadAllText(configFile);
                                    // Look for any scene names in the config content
                                    foreach (string potentialSceneName in ExtractPotentialSceneNames(configContent))
                                    {
                                        if (!_sceneToLevelFileMap.ContainsKey(potentialSceneName))
                                        {
                                            _sceneToLevelFileMap[potentialSceneName] = dirName;
                                            Logger.LogInfo($"Mapped scene '{potentialSceneName}' to level '{dirName}' (from config)");
                                        }
                                    }
                                }
                                catch (Exception ex)
                                {
                                    Logger.LogWarning($"Error reading config file {configFile}: {ex.Message}");
                                }
                            }
                        }
                    }
                }
                
                
                // Scan for level files with numeric names (like level528)
                string[] levelFiles = Directory.GetDirectories(levelDirPath, "level*", SearchOption.TopDirectoryOnly);
                foreach (var levelFile in levelFiles)
                {
                    string fileName = Path.GetFileName(levelFile);
                    Logger.LogInfo($"Found level file: {fileName}");
                    _sceneToLevelFileMap[fileName] = fileName;
                }
            }
            catch (Exception ex)
            {
                Logger.LogError($"Error scanning for level files: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Extract potential scene names from a config file content
        /// </summary>
        private List<string> ExtractPotentialSceneNames(string configContent)
        {
            List<string> potentialNames = new List<string>();
            
            // Look for "name", "scene", "sceneName" fields in JSON
            string[] propertyMarkers = new string[] { "\"Name\":", "\"name\":", "\"Scene\":", "\"scene\":", "\"SceneName\":", "\"sceneName\":" };
            foreach (var marker in propertyMarkers)
            {
                int index = 0;
                while ((index = configContent.IndexOf(marker, index)) != -1)
                {
                    index += marker.Length;
                    
                    // Skip whitespace
                    while (index < configContent.Length && char.IsWhiteSpace(configContent[index]))
                    {
                        index++;
                    }
                    
                    // Check if it's a string value
                    if (index < configContent.Length && configContent[index] == '"')
                    {
                        index++; // Skip the opening quote
                        int endIndex = configContent.IndexOf('"', index);
                        if (endIndex != -1)
                        {
                            string value = configContent.Substring(index, endIndex - index);
                            if (!string.IsNullOrEmpty(value) && value.Length > 3)
                            {
                                potentialNames.Add(value);
                            }
                        }
                    }
                }
            }
            
            return potentialNames;
        }
        
        /// <summary>
        /// Scan for Unity scene files
        /// </summary>
        private void ScanForSceneFiles()
        {
            try
            {
                // Search in the standard game scenes directory
                string scenesDir = Path.Combine(_gameLevelFilesPath, "Scenes");
                if (Directory.Exists(scenesDir))
                {
                    foreach (string sceneFile in Directory.GetFiles(scenesDir, "*.*", SearchOption.AllDirectories))
                    {
                        string fileName = Path.GetFileNameWithoutExtension(sceneFile);
                        _sceneToSceneFileMap[fileName] = sceneFile;
                        
                        // Also try to map using the scene content
                        try
                        {
                            // Read first few KB of the scene file to look for recognizable names
                            byte[] buffer = new byte[8192]; // 8KB
                            using (FileStream fs = new FileStream(sceneFile, FileMode.Open, FileAccess.Read))
                            {
                                int bytesRead = fs.Read(buffer, 0, buffer.Length);
                                string content = System.Text.Encoding.UTF8.GetString(buffer, 0, bytesRead);
                                
                                foreach (string potentialName in ExtractPotentialSceneNames(content))
                                {
                                    if (!_sceneToSceneFileMap.ContainsKey(potentialName))
                                    {
                                        _sceneToSceneFileMap[potentialName] = sceneFile;
                                        Logger.LogInfo($"Mapped scene name '{potentialName}' to scene file '{fileName}' (from scene content)");
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Logger.LogWarning($"Error analyzing scene file {sceneFile}: {ex.Message}");
                        }
                    }
                }
                
                // Also look for level-specific scene files in the Resources folder
                string resourcesDir = Path.Combine(_gameLevelFilesPath, "Resources");
                if (Directory.Exists(resourcesDir))
                {
                    foreach (string sceneFile in Directory.GetFiles(resourcesDir, "*.*", SearchOption.AllDirectories))
                    {
                        string fileName = Path.GetFileNameWithoutExtension(sceneFile);
                        _sceneToSceneFileMap[fileName] = sceneFile;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogError($"Error scanning for scene files: {ex.Message}");
            }
        }

        /// <summary>
        /// Copy a scene file to the mod directory if it doesn't exist
        /// </summary>
        public void CopySceneIfNeeded(string sceneName)
        {
            try
            {
                // Find the original scene file
                string originalScenePath = FindOriginalScenePath(sceneName);
                if (string.IsNullOrEmpty(originalScenePath))
                {
                    Logger.LogError($"Could not find original scene file for {sceneName}");
                    return;
                }
                
                Logger.LogInfo($"Found original scene file: {originalScenePath} for scene {sceneName}");
                
                // Determine destination path
                string fileName = Path.GetFileName(originalScenePath);
                string destinationPath = Path.Combine(_modifiedAssetsPath, "Scenes", fileName);
                
                // Create destination directory if needed
                Directory.CreateDirectory(Path.GetDirectoryName(destinationPath));
                
                // Copy file if it doesn't exist or is outdated
                if (!File.Exists(destinationPath) || 
                    File.GetLastWriteTime(originalScenePath) > File.GetLastWriteTime(destinationPath))
                {
                    File.Copy(originalScenePath, destinationPath, true);
                    Logger.LogInfo($"Copied scene file: {originalScenePath} -> {destinationPath}");
                }
                
                // Store mapping from scene name to scene file for future reference
                string filenameWithoutExtension = Path.GetFileNameWithoutExtension(fileName);
                _sceneToSceneFileMap[sceneName] = filenameWithoutExtension;
                
                // Also copy level files from the game directory
                CopyLevelFiles(sceneName);
                
                // Also copy any streaming assets referenced by the scene
                CopyStreamingAssets(sceneName);
            }
            catch (Exception ex)
            {
                Logger.LogError($"Error copying scene {sceneName}: {ex.Message}");
            }
        }

        /// <summary>
        /// Copy level files from game directory
        /// </summary>
        private void CopyLevelFiles(string sceneName)
        {
            try
            {
                // Get the current level name (might be different from scene name)
                string levelName = GetLevelNameForScene(sceneName);
                if (string.IsNullOrEmpty(levelName))
                {
                    levelName = sceneName; // Fallback to scene name if level name not found
                }

                Logger.LogInfo($"Copying level files for {levelName}");

                // Check for level files directly in the EscapeFromTarkov_Data folder first
                // This is important for files like level528 that may be at root level
                string directLevelPath = Path.Combine(_gameLevelFilesPath, levelName);
                if (Directory.Exists(directLevelPath))
                {
                    // Create level directory in mod folder
                    string modLevelPath = Path.Combine(_modDirectory, levelName);
                    Directory.CreateDirectory(Path.GetDirectoryName(modLevelPath));
                    
                    Logger.LogInfo($"Found level files at: {directLevelPath}");

                    // Copy all files in the directory
                    foreach (string filePath in Directory.GetFiles(directLevelPath, "*", SearchOption.AllDirectories))
                    {
                        string relativePath = GetRelativePath(filePath, directLevelPath);
                        string destinationPath = Path.Combine(modLevelPath, relativePath);
                        
                        // Create destination directory
                        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath));
                        
                        // Only copy if file doesn't exist or is newer
                        if (!File.Exists(destinationPath) || 
                            File.GetLastWriteTime(filePath) > File.GetLastWriteTime(destinationPath))
                        {
                            File.Copy(filePath, destinationPath, true);
                            Logger.LogInfo($"Copied level file: {relativePath}");
                        }
                    }
                    return; // We found and copied the files, so we can exit the method
                }

                // Check both conventional level data and bundles subfolders if direct path wasn't found
                string[] possibleLevelPaths = new string[]
                {
                    Path.Combine(_gameLevelFilesPath, "level", levelName),
                    Path.Combine(_gameLevelFilesPath, "bundles", levelName)
                };

                bool foundAnyFiles = false;
                foreach (string levelPath in possibleLevelPaths)
                {
                    if (Directory.Exists(levelPath))
                    {
                        // Create level directory in mod folder
                        string modLevelPath = Path.Combine(_modDirectory, "level", levelName);
                        Directory.CreateDirectory(Path.GetDirectoryName(modLevelPath));
                        
                        Logger.LogInfo($"Found level files at: {levelPath}");
                        foundAnyFiles = true;

                        // Copy all files in the directory
                        foreach (string filePath in Directory.GetFiles(levelPath, "*", SearchOption.AllDirectories))
                        {
                            string relativePath = GetRelativePath(filePath, levelPath);
                            string destinationPath = Path.Combine(modLevelPath, relativePath);
                            
                            // Create destination directory
                            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath));
                            
                            // Only copy if file doesn't exist or is newer
                            if (!File.Exists(destinationPath) || 
                                File.GetLastWriteTime(filePath) > File.GetLastWriteTime(destinationPath))
                            {
                                File.Copy(filePath, destinationPath, true);
                                Logger.LogInfo($"Copied level file: {relativePath}");
                            }
                        }
                    }
                }
                
                if (!foundAnyFiles)
                {
                    Logger.LogInfo($"No level files found at: {_gameLevelFilesPath}{Path.DirectorySeparatorChar}level528");
                }
            }
            catch (Exception ex)
            {
                Logger.LogError($"Error copying level files: {ex.Message}");
            }
        }

        /// <summary>
        /// Copy streaming assets referenced by a scene
        /// </summary>
        private void CopyStreamingAssets(string sceneName)
        {
            try
            {
                // Get the level name for more accurate searches
                string levelName = GetLevelNameForScene(sceneName);
                if (string.IsNullOrEmpty(levelName))
                {
                    levelName = sceneName; // Fallback to scene name
                }
                
                // Check in the game's StreamingAssets folder first
                string gameStreamingAssetsPath = Path.Combine(_gameLevelFilesPath, "StreamingAssets", levelName);
                string moddedStreamingAssetsPath = Path.Combine(_modifiedAssetsPath, "StreamingAssets", levelName);
                
                if (Directory.Exists(gameStreamingAssetsPath))
                {
                    // Create destination directory
                    Directory.CreateDirectory(moddedStreamingAssetsPath);
                    
                    Logger.LogInfo($"Found streaming assets at: {gameStreamingAssetsPath}");
                    
                    // Copy all files
                    foreach (string filePath in Directory.GetFiles(gameStreamingAssetsPath, "*", SearchOption.AllDirectories))
                    {
                        string relativePath = GetRelativePath(filePath, gameStreamingAssetsPath);
                        string destinationPath = Path.Combine(moddedStreamingAssetsPath, relativePath);
                        
                        // Create destination directory
                        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath));
                        
                        // Copy file
                        File.Copy(filePath, destinationPath, true);
                        Logger.LogInfo($"Copied streaming asset: {relativePath}");
                    }
                }
                else
                {
                    Logger.LogInfo($"No streaming assets found for level: {levelName}");
                    
                    // Try original method as fallback
                    string originalStreamingAssetsPath = Path.Combine(_originalAssetsPath, "StreamingAssets", sceneName);
                    
                    if (Directory.Exists(originalStreamingAssetsPath))
                    {
                        // Create destination directory
                        Directory.CreateDirectory(moddedStreamingAssetsPath);
                        
                        // Copy all files
                        foreach (string filePath in Directory.GetFiles(originalStreamingAssetsPath, "*", SearchOption.AllDirectories))
                        {
                            string relativePath = GetRelativePath(filePath, originalStreamingAssetsPath);
                            string destinationPath = Path.Combine(moddedStreamingAssetsPath, relativePath);
                            
                            // Create destination directory
                            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath));
                            
                            // Copy file
                            File.Copy(filePath, destinationPath, true);
                        }
                        
                        Logger.LogInfo($"Copied streaming assets (fallback method) for {sceneName}");
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogError($"Error copying streaming assets for {sceneName}: {ex.Message}");
            }
        }

        /// <summary>
        /// Get the level name associated with a scene
        /// </summary>
        private string GetLevelNameForScene(string sceneName)
        {
            try
            {
                // First check our existing mapping dictionary
                if (_sceneToLevelFileMap.TryGetValue(sceneName, out string mappedLevel))
                {
                    Logger.LogInfo($"Mapped scene {sceneName} to level {mappedLevel} using known mappings");
                    return mappedLevel;
                }
                
                // Try to get level name from LocationScene objects
                string locationSceneLevelName = GetLevelNameFromLocationScene(sceneName);
                if (!string.IsNullOrEmpty(locationSceneLevelName))
                {
                    // Cache this for future use
                    _sceneToLevelFileMap[sceneName] = locationSceneLevelName;
                    return locationSceneLevelName;
                }
                
                // Check if the scene name directly matches the level naming pattern "level123"
                string sceneNameLower = sceneName.ToLower();
                if (sceneNameLower.StartsWith("level") && int.TryParse(sceneNameLower.Substring(5), out _))
                {
                    Logger.LogInfo($"Scene name {sceneName} directly matches level naming pattern");
                    _sceneToLevelFileMap[sceneName] = sceneName;
                    return sceneName;
                }
                
                // Try to find level by scanning directories
                string scannedLevelName = FindLevelByScanning(sceneName);
                if (!string.IsNullOrEmpty(scannedLevelName))
                {
                    // Cache the discovered mapping
                    _sceneToLevelFileMap[sceneName] = scannedLevelName;
                    return scannedLevelName;
                }
                
                // Try to get the current level name from the active scene
                var activeScene = SceneManager.GetActiveScene();
                if (activeScene.name != null && !string.IsNullOrEmpty(activeScene.name))
                {
                    Logger.LogInfo($"Using active scene name: {activeScene.name}");
                    
                    // Check if active scene name matches level naming pattern
                    string activeNameLower = activeScene.name.ToLower();
                    if (activeNameLower.StartsWith("level") && int.TryParse(activeNameLower.Substring(5), out _))
                    {
                        Logger.LogInfo($"Active scene name {activeScene.name} matches level naming pattern");
                        _sceneToLevelFileMap[sceneName] = activeScene.name;
                        return activeScene.name;
                    }
                }

                // If all else fails, just return the scene name
                Logger.LogInfo($"Could not determine level name for scene {sceneName}, using scene name as fallback");
                return sceneName;
            }
            catch (Exception ex)
            {
                Logger.LogError($"Error getting level name for scene {sceneName}: {ex.Message}");
                return sceneName; // Return scene name as fallback
            }
        }

        /// <summary>
        /// Get level name from loaded LocationScene objects
        /// </summary>
        private string GetLevelNameFromLocationScene(string sceneName)
        {
            try
            {
                // Try to find a loaded LocationScene component that might have information about our scene
                var locationScenes = UnityEngine.Object.FindObjectsOfType<LocationScene>();
                if (locationScenes != null && locationScenes.Length > 0)
                {
                    Logger.LogInfo($"Found {locationScenes.Length} LocationScene objects to check for level info");
                    
                    // First try to find an exact match by scene name
                    foreach (var locationScene in locationScenes)
                    {
                        Scene scene = locationScene.gameObject.scene;
                        if (scene.name.Equals(sceneName, StringComparison.OrdinalIgnoreCase))
                        {
                            // Check if LocationOrigins has information
                            if (locationScene.LocationOrigins != null && locationScene.LocationOrigins.Length > 0)
                            {
                                // Check for any properties or fields that might contain location name
                                var locationOrigin = locationScene.LocationOrigins[0];
                                string locationName = null;
                                
                                // Try to get location name from object name or parent name
                                if (locationOrigin != null)
                                {
                                    // The location name might be part of the GameObject name
                                    string objName = locationOrigin.gameObject.name;
                                    if (!string.IsNullOrEmpty(objName) && objName.Contains("_"))
                                    {
                                        // Extract potential level name from object name
                                        string[] nameParts = objName.Split('_');
                                        foreach (string part in nameParts)
                                        {
                                            if (part.ToLower().StartsWith("level") && part.Length > 5)
                                            {
                                                locationName = part;
                                                Logger.LogInfo($"Extracted level name '{locationName}' from LocationOrigin object name");
                                                return locationName;
                                            }
                                        }
                                    }
                                }
                            }

                            // Extract level name from TransitPoints if available
                            if (locationScene.TransitPoints != null)
                            {
                                foreach (var transitPoint in locationScene.TransitPoints)
                                {
                                    if (transitPoint != null)
                                    {
                                        // Try to get level name from object name or path
                                        string objName = transitPoint.gameObject.name;
                                        if (!string.IsNullOrEmpty(objName))
                                        {
                                            // Check if the object name contains a level reference
                                            if (objName.ToLower().Contains("level") && objName.Length > 7)
                                            {
                                                int levelIndex = objName.ToLower().IndexOf("level");
                                                if (levelIndex >= 0 && levelIndex + 5 < objName.Length)
                                                {
                                                    // Extract level name
                                                    string levelPart = objName.Substring(levelIndex);
                                                    // Take just the level part with potential number
                                                    if (levelPart.Length > 5)
                                                    {
                                                        string levelName = null;
                                                        // Extract just "level123" from the name
                                                        for (int i = 5; i < levelPart.Length; i++)
                                                        {
                                                            if (!char.IsDigit(levelPart[i]))
                                                            {
                                                                levelName = levelPart.Substring(0, i);
                                                                break;
                                                            }
                                                        }
                                                        
                                                        if (levelName == null && levelPart.Length > 5)
                                                        {
                                                            // If we didn't find a non-digit, take the full part
                                                            levelName = levelPart;
                                                        }
                                                        
                                                        if (!string.IsNullOrEmpty(levelName))
                                                        {
                                                            Logger.LogInfo($"Found level name '{levelName}' from TransitPoint object name");
                                                            return levelName;
                                                        }
                                                    }
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                            
                            // Try to extract level name from scene path if it follows the "level123" pattern
                            string sceneNameLower = scene.name.ToLower();
                            if (sceneNameLower.StartsWith("level") && sceneNameLower.Length > 5)
                            {
                                string levelNumber = sceneNameLower.Substring(5);
                                if (int.TryParse(levelNumber, out _))
                                {
                                    Logger.LogInfo($"Extracted level name 'level{levelNumber}' from scene name {sceneName}");
                                    return $"level{levelNumber}";
                                }
                            }
                        }
                    }
                    
                    // If we didn't find an exact match, check for any helpful information
                    foreach (var locationScene in locationScenes)
                    {
                        // Try to find references to our scene name in any object in the scene
                        if (locationScene.EventObjects != null)
                        {
                            foreach (var eventObj in locationScene.EventObjects)
                            {
                                if (eventObj != null && eventObj.name.Contains(sceneName))
                                {
                                    // The scene where this event object is located might be related to our target scene
                                    string hostScene = locationScene.gameObject.scene.name;
                                    Logger.LogInfo($"Found event object related to {sceneName} in scene {hostScene}");
                                    
                                    // Check if the host scene name follows the "level123" pattern
                                    string hostSceneLower = hostScene.ToLower();
                                    if (hostSceneLower.StartsWith("level") && hostSceneLower.Length > 5)
                                    {
                                        string levelNumber = hostSceneLower.Substring(5);
                                        if (int.TryParse(levelNumber, out _))
                                        {
                                            Logger.LogInfo($"Extracted level name 'level{levelNumber}' from related scene {hostScene}");
                                            return $"level{levelNumber}";
                                        }
                                    }
                                }
                            }
                        }
                    }
                }

                // If we get here, we couldn't find any level information from LocationScene objects
                return null;
            }
            catch (Exception ex)
            {
                Logger.LogError($"Error getting level name from LocationScene for {sceneName}: {ex.Message}");
                return null;
            }
        }
        
        /// <summary>
        /// Apply transform changes to a scene file
        /// </summary>
        public bool ApplyChangesToScene(string sceneName, List<TransformData> changes)
        {
            try
            {
                // Find the actual scene file name from our mappings
                string sceneFileName = sceneName;
                string levelName = null;
                
                // Log the changes for debugging
                Logger.LogInfo($"[TRANSFORM] Applying {changes.Count} transform changes to scene {sceneName}");
                foreach (var change in changes)
                {
                    Logger.LogInfo($"[TRANSFORM] Object: {change.ObjectName}, Path: {change.ObjectPath}");
                    Logger.LogInfo($"[TRANSFORM] Position: {change.Position}, Rotation: {change.Rotation}, Scale: {change.Scale}");
                    Logger.LogInfo($"[TRANSFORM] PathID: {change.PathID}, IsDestroyed: {change.IsDestroyed}");
                }
                
                // First check if we have a direct mapping to a scene file
                if (_sceneToSceneFileMap.TryGetValue(sceneName, out string mappedSceneFile))
                {
                    if (!string.IsNullOrEmpty(mappedSceneFile))
                    {
                        if (File.Exists(mappedSceneFile))
                        {
                            sceneFileName = Path.GetFileNameWithoutExtension(mappedSceneFile);
                            Logger.LogInfo($"Using mapped scene file: {sceneFileName} for scene {sceneName}");
                        }
                        else
                        {
                            sceneFileName = mappedSceneFile;
                            Logger.LogInfo($"Using scene file name from mapping: {sceneFileName}");
                        }
                    }
                }
                // If no direct scene mapping, check if we have a level name mapping
                else if (_sceneToLevelFileMap.TryGetValue(sceneName, out levelName))
                {
                    sceneFileName = levelName; // Use level name as scene file name
                    Logger.LogInfo($"Using level name as scene file: {sceneFileName} for scene {sceneName}");
                }
                
                // Build path to the modified scene file
                string modifiedScenePath = Path.Combine(_modifiedAssetsPath, "Scenes", $"{sceneFileName}");
                Logger.LogInfo($"[TRANSFORM] Target file path: {modifiedScenePath}");
                
                // Use the same bundle file name as the scene name if dealing with custom scenes
                if (sceneName.EndsWith("_Scripts", StringComparison.OrdinalIgnoreCase) ||
                    sceneName.Contains("custom", StringComparison.OrdinalIgnoreCase))
                {
                    modifiedScenePath = Path.Combine(_modifiedAssetsPath, "Scenes", $"{sceneName}.bundle");
                    Logger.LogInfo($"Using scene name as bundle name for custom scene: {modifiedScenePath}");
                }
                
                // If the file doesn't exist, try with different extensions or names
                if (!File.Exists(modifiedScenePath))
                {
                    Logger.LogWarning($"Scene file not found at: {modifiedScenePath}, searching for alternatives...");
                    
                    // Try to find a matching scene file
                    string[] sceneFiles = Directory.GetFiles(Path.Combine(_modifiedAssetsPath, "Scenes"));
                    bool foundMatch = false;
                    
                    foreach (string sceneFile in sceneFiles)
                    {
                        // Try to analyze the file to see if it contains the scene name
                        try
                        {
                            Logger.LogInfo($"Checking if {sceneFile} is a match for {sceneName}...");
                            
                            // Read the first portion of the file
                            byte[] buffer = new byte[8192]; // 8KB should be enough to check the header
                            using (FileStream fs = new FileStream(sceneFile, FileMode.Open, FileAccess.Read))
                            {
                                fs.Read(buffer, 0, buffer.Length);
                            }
                            
                            string content = System.Text.Encoding.UTF8.GetString(buffer);
                            
                            // Check if the file contains the scene name
                            if (content.Contains(sceneName) || (levelName != null && content.Contains(levelName)))
                            {
                                Logger.LogInfo($"Found matching scene file: {sceneFile} for scene {sceneName}");
                                modifiedScenePath = sceneFile;
                                foundMatch = true;
                                
                                // Update our mapping for future use
                                _sceneToSceneFileMap[sceneName] = sceneFile;
                                break;
                            }
                        }
                        catch (Exception ex)
                        {
                            Logger.LogWarning($"Error analyzing scene file {sceneFile}: {ex.Message}");
                        }
                    }
                    
                    if (!foundMatch)
                    {
                        // If we have a level name mapping, try to find that file specifically
                        if (levelName != null)
                        {
                            string levelScenePath = Path.Combine(_modifiedAssetsPath, "Scenes", $"{levelName}");
                            if (File.Exists(levelScenePath))
                            {
                                Logger.LogInfo($"Using mapped level scene file: {levelScenePath}");
                                modifiedScenePath = levelScenePath;
                            }
                            else
                            {
                                // Create a new bundle file for this scene
                                string bundlePath = Path.Combine(_modifiedAssetsPath, "Scenes", $"{sceneName}.bundle");
                                try
                                {
                                    Directory.CreateDirectory(Path.GetDirectoryName(bundlePath));
                                    File.WriteAllText(bundlePath, $"# Scene bundle for {sceneName}");
                                    Logger.LogInfo($"Created new bundle file: {bundlePath}");
                                    modifiedScenePath = bundlePath;
                                }
                                catch (Exception ex)
                                {
                                    Logger.LogError($"Failed to create bundle file: {ex.Message}");
                                    return false;
                                }
                            }
                        }
                        else
                        {
                            // Create a new bundle file for this scene
                            string bundlePath = Path.Combine(_modifiedAssetsPath, "Scenes", $"{sceneName}.bundle");
                            try
                            {
                                Directory.CreateDirectory(Path.GetDirectoryName(bundlePath));
                                File.WriteAllText(bundlePath, $"# Scene bundle for {sceneName}");
                                Logger.LogInfo($"Created new bundle file: {bundlePath}");
                                modifiedScenePath = bundlePath;
                            }
                            catch (Exception ex)
                            {
                                Logger.LogError($"Failed to create bundle file: {ex.Message}");
                                return false;
                            }
                        }
                    }
                }
                
                Logger.LogInfo($"[TRANSFORM] Scene file found: {modifiedScenePath}");
                Logger.LogInfo($"[TRANSFORM] File exists: {File.Exists(modifiedScenePath)}");
                if (File.Exists(modifiedScenePath))
                {
                    long fileSize = new FileInfo(modifiedScenePath).Length;
                    Logger.LogInfo($"[TRANSFORM] File size: {fileSize} bytes");
                }
                
                // Use our direct integration instead of AssetRipper libraries
                var sceneParser = new AssetRipperIntegration.SceneParser(Logger);
                
                // Convert our TransformData to SerializedTransform
                var serializedTransforms = changes.Select(t => new AssetRipperIntegration.SerializedTransform
                {
                    // Add safe parsing for PathID to handle invalid or null values
                    PathID = !string.IsNullOrEmpty(t.PathID) && long.TryParse(t.PathID, out long pathId) ? pathId : 0,
                    Name = t.ObjectName,
                    LocalPosition = t.Position,
                    LocalRotation = Quaternion.Euler(t.Rotation),
                    LocalScale = t.Scale,
                    IsActive = !t.IsDestroyed
                }).ToList();
                
                // Log the serialized transforms we're going to apply
                foreach (var st in serializedTransforms)
                {
                    Logger.LogInfo($"[TRANSFORM] Serialized: {st.Name}, PathID: {st.PathID}");
                    Logger.LogInfo($"[TRANSFORM] Position: {st.LocalPosition}, Rotation: {st.LocalRotation.eulerAngles}, Scale: {st.LocalScale}");
                    Logger.LogInfo($"[TRANSFORM] IsActive: {st.IsActive}");
                }
                
                try
                {
                    // Apply changes
                    sceneParser.ApplyTransforms(modifiedScenePath, serializedTransforms);
                    
                    Logger.LogInfo($"Successfully applied {changes.Count} changes to {sceneName} using scene file {Path.GetFileName(modifiedScenePath)}");
                    return true;
                }
                catch (Exception ex)
                {
                    Logger.LogError($"Error applying transforms to file: {ex.Message}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Logger.LogError($"Error applying changes to scene {sceneName}: {ex.Message}");
                return false;
            }
        }
        
        /// <summary>
        /// Apply destruction changes to scene assets
        /// </summary>
        public void RemoveDestroyedObjectsFromAsset(string sceneName, List<TransformData> destroyedObjects)
        {
            try
            {
                // Find the actual scene file name using our mapping system
                string sceneFileName = sceneName;
                string levelName = null;
                
                // Check mappings for the correct file name
                if (_sceneToSceneFileMap.TryGetValue(sceneName, out string mappedSceneFile))
                {
                    if (!string.IsNullOrEmpty(mappedSceneFile))
                    {
                        if (File.Exists(mappedSceneFile))
                        {
                            sceneFileName = Path.GetFileNameWithoutExtension(mappedSceneFile);
                        }
                        else
                        {
                            sceneFileName = mappedSceneFile;
                        }
                    }
                }
                else if (_sceneToLevelFileMap.TryGetValue(sceneName, out levelName))
                {
                    sceneFileName = levelName;
                }
                
                string modifiedScenePath = Path.Combine(_modifiedAssetsPath, "Scenes", $"{sceneFileName}");
                
                // If file doesn't exist, try to find alternatives
                if (!File.Exists(modifiedScenePath))
                {
                    // Look for any potential matching scene files
                    string[] sceneFiles = Directory.GetFiles(Path.Combine(_modifiedAssetsPath, "Scenes"));
                    bool foundMatch = false;
                    
                    foreach (string sceneFile in sceneFiles)
                    {
                        try
                        {
                            // Check if the file might contain the scene name or level name
                            byte[] buffer = new byte[8192];
                            using (FileStream fs = new FileStream(sceneFile, FileMode.Open, FileAccess.Read))
                            {
                                fs.Read(buffer, 0, buffer.Length);
                            }
                            
                            string content = System.Text.Encoding.UTF8.GetString(buffer);
                            if (content.Contains(sceneName) || (levelName != null && content.Contains(levelName)))
                            {
                                Logger.LogInfo($"Found scene file for destruction: {sceneFile}");
                                modifiedScenePath = sceneFile;
                                foundMatch = true;
                                break;
                            }
                        }
                        catch (Exception ex)
                        {
                            Logger.LogWarning($"Error checking scene file {sceneFile}: {ex.Message}");
                        }
                    }
                    
                    if (!foundMatch)
                    {
                        Logger.LogError($"Modified scene file not found for removal: {modifiedScenePath}");
                        return;
                    }
                }
                
                Logger.LogInfo($"Removing {destroyedObjects.Count} objects from scene: {sceneName} using file {modifiedScenePath}");
                
                // Use our direct integration or AssetRipper
                var sceneParser = new AssetRipperIntegration.SceneParser(Logger);
                
                // Convert TransformData to SerializedTransform with IsActive = false
                var serializedTransforms = destroyedObjects.Select(t => new AssetRipperIntegration.SerializedTransform
                {
                    // Add safe parsing for PathID to handle invalid or null values
                    PathID = !string.IsNullOrEmpty(t.PathID) && long.TryParse(t.PathID, out long pathId) ? pathId : 0,
                    Name = t.ObjectName,
                    LocalPosition = t.Position,
                    LocalRotation = Quaternion.Euler(t.Rotation),
                    LocalScale = t.Scale,
                    IsActive = false // This will ensure the object is removed or deactivated
                }).ToList();
                
                // Apply changes - this will mark objects as inactive in the asset file
                sceneParser.ApplyTransforms(modifiedScenePath, serializedTransforms);
                
                Logger.LogInfo($"Successfully removed {destroyedObjects.Count} objects from scene: {sceneName}");
            }
            catch (Exception ex)
            {
                Logger.LogError($"Error removing destroyed objects from scene {sceneName}: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Apply changes using AssetRipper
        /// </summary>
        private void ApplyAssetRipperChanges(string filePath, List<TransformData> changes)
        {
            // Note: This is pseudocode that would use AssetRipper libraries
            // In a real implementation, you'd include and use AssetRipper's Unity asset serialization code
            
            /* Pseudocode for AssetRipper integration:
            
            // Load the asset file
            AssetBundle bundle = AssetRipper.LoadFile(filePath);
            
            // Process each change
            foreach (TransformData change in changes)
            {
                // Find the GameObject by path/id
                GameObject gameObject = bundle.FindObject(change.PathID);
                
                if (gameObject != null)
                {
                    // Apply transform changes
                    gameObject.transform.position = change.Position;
                    gameObject.transform.rotation = Quaternion.Euler(change.Rotation);
                    gameObject.transform.localScale = change.Scale;
                    
                    // Handle destruction if needed
                    if (change.IsDestroyed)
                    {
                        gameObject.SetActive(false);
                    }
                }
            }
            
            // Save the modified asset file
            bundle.Save(filePath);
            */
            
            Logger.LogInfo($"Applied changes to {filePath} using AssetRipper (simulated)");
        }
        
        /// <summary>
        /// Find the original scene file path
        /// </summary>
        private string FindOriginalScenePath(string sceneName)
        {
            // First check if we have a direct mapping to a scene file
            if (_sceneToSceneFileMap.TryGetValue(sceneName, out string sceneFilePath))
            {
                if (File.Exists(sceneFilePath))
                {
                    Logger.LogInfo($"Found scene file from mapping: {sceneFilePath} for {sceneName}");
                    return sceneFilePath;
                }
            }
            
            // Special handling for custom maps/scenes with "*_Scripts" naming pattern
            if (sceneName.EndsWith("_Scripts", StringComparison.OrdinalIgnoreCase) || 
                sceneName.Contains("custom", StringComparison.OrdinalIgnoreCase))
            {
                Logger.LogInfo($"Detected custom scene name: {sceneName}. Looking for specific file match...");

                // For custom scenes, generate a unique file in our mod directory if it doesn't exist
                string customScenePath = Path.Combine(_modifiedAssetsPath, "Scenes", $"{sceneName}.bundle");
                if (!File.Exists(customScenePath))
                {
                    // Create a minimal bundle file for this custom scene to avoid using other maps' bundles
                    try {
                        Directory.CreateDirectory(Path.GetDirectoryName(customScenePath));
                        File.WriteAllText(customScenePath, $"# Custom scene file for {sceneName}");
                        Logger.LogInfo($"Created custom scene file: {customScenePath} for scene {sceneName}");
                        
                        // Add to our mapping
                        _sceneToSceneFileMap[sceneName] = customScenePath;
                        return customScenePath;
                    }
                    catch (Exception ex) {
                        Logger.LogError($"Failed to create custom scene file: {ex.Message}");
                    }
                }
                else
                {
                    Logger.LogInfo($"Using existing custom scene file: {customScenePath} for scene {sceneName}");
                    return customScenePath;
                }
            }
            
            // Then check if we have a level name mapping
            string levelFileName = null;
            if (_sceneToLevelFileMap.TryGetValue(sceneName, out levelFileName))
            {
                // First check directly in EscapeFromTarkov_Data folder for the level file
                string directLevelPath = Path.Combine(_gameLevelFilesPath, levelFileName);
                if (File.Exists(directLevelPath))
                {
                    Logger.LogInfo($"Found scene file directly in game data folder: {directLevelPath}");
                    return directLevelPath;
                }
                
                // Then check StreamingAssets folders for files like factory_day_preset.bundle
                string streamingAssetsFolder = Path.Combine(_gameLevelFilesPath, "StreamingAssets");
                if (Directory.Exists(streamingAssetsFolder))
                {
                    // First look for a perfect name match before checking content
                    string windowsMapsFolder = Path.Combine(streamingAssetsFolder, "Windows", "maps");
                    if (Directory.Exists(windowsMapsFolder))
                    {
                        // Check for exact match first - higher priority than content matching
                        string exactMatch = Path.Combine(windowsMapsFolder, $"{sceneName}.bundle");
                        if (File.Exists(exactMatch))
                        {
                            Logger.LogInfo($"Found exact match bundle file: {exactMatch}");
                            _sceneToSceneFileMap[sceneName] = exactMatch;
                            return exactMatch;
                        }
                        
                        exactMatch = Path.Combine(windowsMapsFolder, $"{levelFileName}.bundle");
                        if (File.Exists(exactMatch))
                        {
                            Logger.LogInfo($"Found exact match bundle file using level name: {exactMatch}");
                            _sceneToSceneFileMap[sceneName] = exactMatch;
                            return exactMatch;
                        }
                        
                        // Check for <scene>_preset.bundle
                        exactMatch = Path.Combine(windowsMapsFolder, $"{sceneName}_preset.bundle");
                        if (File.Exists(exactMatch))
                        {
                            Logger.LogInfo($"Found exact match preset bundle: {exactMatch}");
                            _sceneToSceneFileMap[sceneName] = exactMatch;
                            return exactMatch;
                        }
                        
                        // Check for <level>_preset.bundle
                        if (levelFileName != null)
                        {
                            exactMatch = Path.Combine(windowsMapsFolder, $"{levelFileName}_preset.bundle");
                            if (File.Exists(exactMatch))
                            {
                                Logger.LogInfo($"Found exact match level preset bundle: {exactMatch}");
                                _sceneToSceneFileMap[sceneName] = exactMatch;
                                return exactMatch;
                            }
                        }
                        
                        // Now look for bundle files with potential content matches
                        string[] bundleFiles = Directory.GetFiles(windowsMapsFolder, "*.bundle", SearchOption.TopDirectoryOnly);
                        foreach (string bundleFile in bundleFiles)
                        {
                            // Skip files that don't contain the scene or level name in their filename
                            string bundleFileName = Path.GetFileNameWithoutExtension(bundleFile).ToLower();
                            string sceneNameLower = sceneName.ToLower();
                            string levelFileNameLower = levelFileName?.ToLower() ?? "";
                            
                            if (!bundleFileName.Contains(sceneNameLower) && !bundleFileName.Contains(levelFileNameLower))
                            {
                                // Skip detailed checking for files that don't match by name at all
                                continue;
                            }
                            
                            // Try to check content of bundle files
                            try
                            {
                                byte[] buffer = new byte[8192]; // Read first 8KB
                                using (FileStream fs = new FileStream(bundleFile, FileMode.Open, FileAccess.Read))
                                {
                                    fs.Read(buffer, 0, buffer.Length);
                                }
                                
                                string content = System.Text.Encoding.UTF8.GetString(buffer);
                                if (content.Contains(sceneName) || content.Contains(levelFileName))
                                {
                                    Logger.LogInfo($"Found bundle file in StreamingAssets: {bundleFile}");
                                    return bundleFile;
                                }
                            }
                            catch (Exception ex)
                            {
                                Logger.LogWarning($"Error checking bundle file {bundleFile}: {ex.Message}");
                            }
                        }
                    }
                    
                    // Check the general StreamingAssets folder
                    string[] assetFiles = Directory.GetFiles(streamingAssetsFolder, "*.bundle", SearchOption.AllDirectories);
                    foreach (string assetFile in assetFiles)
                    {
                        // Skip files that don't contain the scene or level name in their filename
                        string assetFileName = Path.GetFileNameWithoutExtension(assetFile).ToLower();
                        string sceneNameLower = sceneName.ToLower();
                        string levelFileNameLower = levelFileName?.ToLower() ?? "";
                        
                        if (!assetFileName.Contains(sceneNameLower) && !assetFileName.Contains(levelFileNameLower))
                        {
                            // Skip detailed checking for files that don't match by name at all
                            continue;
                        }
                        
                        // Check content for references to our scene or level name
                        try
                        {
                            byte[] buffer = new byte[8192];
                            using (FileStream fs = new FileStream(assetFile, FileMode.Open, FileAccess.Read))
                            {
                                fs.Read(buffer, 0, buffer.Length);
                            }
                            
                            string content = System.Text.Encoding.UTF8.GetString(buffer);
                            if (content.Contains(sceneName) || content.Contains(levelFileName))
                            {
                                Logger.LogInfo($"Found asset file with scene reference: {assetFile}");
                                return assetFile;
                            }
                        }
                        catch (Exception ex)
                        {
                            Logger.LogWarning($"Error checking asset file {assetFile}: {ex.Message}");
                        }
                    }
                }
                
                // Try standard locations after that
                string[] possibleLevelPaths = new string[]
                {
                    Path.Combine(_gameLevelFilesPath, "Scenes", levelFileName),
                    Path.Combine(_gameLevelFilesPath, "Scenes", levelFileName, levelFileName),
                    Path.Combine(_gameLevelFilesPath, "level", levelFileName, levelFileName),
                };
                
                foreach (string path in possibleLevelPaths)
                {
                    if (File.Exists(path))
                    {
                        Logger.LogInfo($"Found scene file using level mapping: {path} for {sceneName}");
                        return path;
                    }
                }
                
                // Also check the general scenes folder for any file with this name
                string scenesFolder = Path.Combine(_gameLevelFilesPath, "Scenes");
                if (Directory.Exists(scenesFolder))
                {
                    string[] sceneFiles = Directory.GetFiles(scenesFolder, "*.*", SearchOption.TopDirectoryOnly);
                    foreach (string sceneFile in sceneFiles)
                    {
                        if (Path.GetFileNameWithoutExtension(sceneFile).Equals(levelFileName, StringComparison.OrdinalIgnoreCase))
                        {
                            Logger.LogInfo($"Found matching scene file: {sceneFile} for level {levelFileName}");
                            return sceneFile;
                        }
                    }
                }
            }
            
            // Try direct scene path checks
            string[] searchLocations = new string[] 
            {
                Path.Combine(_gameLevelFilesPath, $"{sceneName}"),
                Path.Combine(_gameLevelFilesPath, "Scenes", $"{sceneName}"),
                Path.Combine(_gameLevelFilesPath, "Resources", $"{sceneName}"),
                Path.Combine(_originalAssetsPath, "Scenes", $"{sceneName}"),
            };
            
            foreach (string path in searchLocations)
            {
                if (File.Exists(path))
                {
                    Logger.LogInfo($"Found scene file at standard location: {path}");
                    return path;
                }
            }
            
            // Create a custom file for this scene if all else fails
            string customFile = Path.Combine(_modifiedAssetsPath, "Scenes", $"{sceneName}.bundle");
            Directory.CreateDirectory(Path.GetDirectoryName(customFile));
            
            // Create empty file if it doesn't exist
            if (!File.Exists(customFile))
            {
                File.WriteAllText(customFile, $"# Custom scene file for {sceneName}");
                Logger.LogInfo($"Created empty scene file for unknown scene {sceneName}: {customFile}");
            }
            
            return customFile;
        }
        
        /// <summary>
        /// Get a path relative to a base directory
        /// </summary>
        private string GetRelativePath(string fullPath, string basePath)
        {
            if (!basePath.EndsWith(Path.DirectorySeparatorChar.ToString()))
            {
                basePath += Path.DirectorySeparatorChar;
            }
            
            Uri baseUri = new Uri(basePath);
            Uri fullUri = new Uri(fullPath);
            
            return Uri.UnescapeDataString(baseUri.MakeRelativeUri(fullUri).ToString())
                .Replace('/', Path.DirectorySeparatorChar);
        }
        
        /// <summary>
        /// Find level name by scanning the level directories
        /// </summary>
        private string FindLevelByScanning(string sceneName)
        {
            try
            {
                // Try to extract level number if the scene name follows pattern "Factory_Rework_Day_Scripts" and look for level files
                string[] sceneParts = sceneName.Split('_');
                
                // Look for all level* directories
                string levelDir = Path.Combine(_gameLevelFilesPath, "level");
                if (Directory.Exists(levelDir))
                {
                    // Get all level directories
                    string[] levelDirs = Directory.GetDirectories(levelDir, "level*");
                    
                    if (levelDirs.Length > 0)
                    {
                        Logger.LogInfo($"Found {levelDirs.Length} level directories to scan");
                        
                        // First, try to match by looking for sceneName references in level files
                        foreach (string dir in levelDirs)
                        {
                            string dirName = Path.GetFileName(dir);
                            
                            // Look for config/json files in this level directory
                            string[] configFiles = Directory.GetFiles(dir, "*.json", SearchOption.AllDirectories);
                            foreach (string configFile in configFiles)
                            {
                                try
                                {
                                    // Read the file and look for the scene name
                                    string content = File.ReadAllText(configFile);
                                    if (content.Contains(sceneName))
                                    {
                                        Logger.LogInfo($"Found reference to {sceneName} in level directory {dirName}");
                                        return dirName;
                                    }
                                    
                                    // Also check individual parts of the scene name
                                    foreach (string part in sceneParts)
                                    {
                                        if (part.Length > 3 && content.Contains($"\"{part}\""))
                                        {
                                            Logger.LogInfo($"Found reference to scene part '{part}' in level directory {dirName}");
                                            return dirName;
                                        }
                                    }
                                }
                                catch (Exception ex)
                                {
                                    Logger.LogWarning($"Error reading config file {configFile}: {ex.Message}");
                                }
                            }
                        }
                        
                        // If no match found, try to match by partial name
                        foreach (string dir in levelDirs)
                        {
                            string dirName = Path.GetFileName(dir);
                            
                            // Extract the level number
                            if (dirName.StartsWith("level") && int.TryParse(dirName.Substring(5), out int levelNum))
                            {
                                // Check if the level number appears in the scene name
                                if (sceneName.Contains(levelNum.ToString()))
                                {
                                    Logger.LogInfo($"Found potential level match by number: {dirName} for {sceneName}");
                                    return dirName;
                                }
                            }
                        }
                    }
                }
                
                return null;
            }
            catch (Exception ex)
            {
                Logger.LogError($"Error scanning for level files: {ex.Message}");
                return null;
            }
        }
        
        /// <summary>
        /// Find level files directly in EscapeFromTarkov_Data or subdirectories
        /// </summary>
        private string[] FindLevelFiles(string levelName)
        {
            List<string> foundFiles = new List<string>();
            
            try
            {
                // Check directly in EscapeFromTarkov_Data folder first
                string directPath = Path.Combine(_gameLevelFilesPath, levelName);
                if (Directory.Exists(directPath))
                {
                    Logger.LogInfo($"Found level directory directly at: {directPath}");
                    foundFiles.Add(directPath);
                }
                
                // Check for files that match the level name pattern
                string[] topLevelFiles = Directory.GetFiles(_gameLevelFilesPath, levelName + "*.*", SearchOption.TopDirectoryOnly);
                foreach (string file in topLevelFiles)
                {
                    Logger.LogInfo($"Found level-related file directly at root: {file}");
                    foundFiles.Add(file);
                }
                
                // Check for sharedassets files
                string[] sharedAssetFiles = Directory.GetFiles(_gameLevelFilesPath, "sharedassets*", SearchOption.TopDirectoryOnly)
                    .Where(f => Path.GetFileName(f).Contains(levelName))
                    .ToArray();
                
                foreach (string file in sharedAssetFiles)
                {
                    Logger.LogInfo($"Found shared asset file for level: {file}");
                    foundFiles.Add(file);
                }
                
                // Check in standard level subdirectory
                string levelSubdir = Path.Combine(_gameLevelFilesPath, "level", levelName);
                if (Directory.Exists(levelSubdir))
                {
                    Logger.LogInfo($"Found level in standard subdirectory: {levelSubdir}");
                    foundFiles.Add(levelSubdir);
                }
            }
            catch (Exception ex)
            {
                Logger.LogError($"Error searching for level files: {ex.Message}");
            }
            
            return foundFiles.ToArray();
        }

        /// <summary>
        /// Get a list of all scenes that have bundle files
        /// </summary>
        public List<string> GetScenesThatHaveBundleFiles()
        {
            List<string> scenesWithBundles = new List<string>();
            
            try
            {
                string scenesDir = Path.Combine(_modifiedAssetsPath, "Scenes");
                if (!Directory.Exists(scenesDir))
                {
                    Logger.LogWarning("Scenes directory does not exist");
                    return scenesWithBundles;
                }
                
                // Get all bundle files in the scenes directory
                string[] bundleFiles = Directory.GetFiles(scenesDir, "*.bundle", SearchOption.TopDirectoryOnly);
                Logger.LogInfo($"Found {bundleFiles.Length} bundle files in scenes directory");
                
                foreach (string bundlePath in bundleFiles)
                {
                    string sceneName = Path.GetFileNameWithoutExtension(bundlePath);
                    scenesWithBundles.Add(sceneName);
                    
                    // Also get any mappings from scene names to this bundle file
                    foreach (var mapping in _sceneToSceneFileMap)
                    {
                        if (mapping.Value.Equals(bundlePath, StringComparison.OrdinalIgnoreCase))
                        {
                            if (!scenesWithBundles.Contains(mapping.Key))
                            {
                                scenesWithBundles.Add(mapping.Key);
                            }
                        }
                    }
                }
                
                Logger.LogInfo($"Found {scenesWithBundles.Count} scenes with bundle files");
            }
            catch (Exception ex)
            {
                Logger.LogError($"Error getting scenes with bundle files: {ex.Message}");
            }
            
            return scenesWithBundles;
        }
    }
}