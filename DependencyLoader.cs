using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace TransformCacher
{
    /// <summary>
    /// Handles loading of dependencies from the libs directory
    /// </summary>
    public static class DependencyLoader
    {
        private static string _libsDirectory;
        private static Dictionary<string, Assembly> _loadedAssemblies = new Dictionary<string, Assembly>();
        private static bool _initialized = false;
        
        /// <summary>
        /// Initialize the dependency loader
        /// </summary>
        public static void Initialize()
        {
            if (_initialized) return;
            
            try
            {
                // Set up the libs directory path
                string assemblyLocation = Assembly.GetExecutingAssembly().Location;
                string assemblyDirectory = Path.GetDirectoryName(assemblyLocation);
                _libsDirectory = Path.Combine(assemblyDirectory, "libs");
                
                TransformCacherPlugin.Log.LogInfo($"DependencyLoader initialized with libs directory: {_libsDirectory}");
                
                // Register assembly resolve event to load assemblies from libs directory
                AppDomain.CurrentDomain.AssemblyResolve += OnAssemblyResolve;
                
                // Create directory if it doesn't exist
                if (!Directory.Exists(_libsDirectory))
                {
                    Directory.CreateDirectory(_libsDirectory);
                    TransformCacherPlugin.Log.LogInfo($"Created libs directory: {_libsDirectory}");
                }
                else
                {
                    // Log the existing DLLs
                    LogAvailableDependencies();
                }
                
                _initialized = true;
            }
            catch (Exception ex)
            {
                TransformCacherPlugin.Log.LogError($"Error initializing DependencyLoader: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Log available dependencies in the libs directory
        /// </summary>
        private static void LogAvailableDependencies()
        {
            try
            {
                if (!Directory.Exists(_libsDirectory)) return;
                
                var dlls = Directory.GetFiles(_libsDirectory, "*.dll");
                TransformCacherPlugin.Log.LogInfo($"Found {dlls.Length} dependencies in libs directory:");
                
                foreach (var dll in dlls)
                {
                    TransformCacherPlugin.Log.LogInfo($"  - {Path.GetFileName(dll)}");
                }
            }
            catch (Exception ex)
            {
                TransformCacherPlugin.Log.LogError($"Error listing dependencies: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Get full path to the dependency in the libs directory
        /// </summary>
        public static string GetDependencyPath(string dependencyName)
        {
            if (!_initialized)
            {
                Initialize();
            }
            
            // Add .dll extension if not present
            if (!dependencyName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            {
                dependencyName += ".dll";
            }
            
            return Path.Combine(_libsDirectory, dependencyName);
        }
        
        /// <summary>
        /// Load a specific assembly from the libs directory
        /// </summary>
        public static Assembly LoadAssembly(string assemblyName)
        {
            if (!_initialized)
            {
                Initialize();
            }
            
            // Check if already loaded
            if (_loadedAssemblies.TryGetValue(assemblyName, out Assembly loadedAssembly))
            {
                return loadedAssembly;
            }
            
            try
            {
                string assemblyPath = GetDependencyPath(assemblyName);
                
                if (!File.Exists(assemblyPath))
                {
                    TransformCacherPlugin.Log.LogWarning($"Assembly not found: {assemblyPath}");
                    return null;
                }
                
                Assembly assembly = Assembly.LoadFrom(assemblyPath);
                
                if (assembly != null)
                {
                    _loadedAssemblies[assemblyName] = assembly;
                    TransformCacherPlugin.Log.LogInfo($"Successfully loaded assembly: {assemblyName}");
                    return assembly;
                }
            }
            catch (Exception ex)
            {
                TransformCacherPlugin.Log.LogError($"Error loading assembly {assemblyName}: {ex.Message}");
            }
            
            return null;
        }
        
        /// <summary>
        /// Assembly resolve event handler to load assemblies from libs directory
        /// </summary>
        private static Assembly OnAssemblyResolve(object sender, ResolveEventArgs args)
        {
            try
            {
                // Get assembly name without version, culture, etc.
                string assemblyName = new AssemblyName(args.Name).Name;
                
                // Check if we've already loaded this assembly
                if (_loadedAssemblies.TryGetValue(assemblyName, out Assembly loadedAssembly))
                {
                    return loadedAssembly;
                }
                
                // Try to load from libs directory
                string assemblyPath = GetDependencyPath(assemblyName);
                
                if (File.Exists(assemblyPath))
                {
                    Assembly assembly = Assembly.LoadFrom(assemblyPath);
                    if (assembly != null)
                    {
                        _loadedAssemblies[assemblyName] = assembly;
                        TransformCacherPlugin.Log.LogInfo($"Automatically loaded assembly: {assemblyName}");
                        return assembly;
                    }
                }
            }
            catch (Exception ex)
            {
                TransformCacherPlugin.Log.LogError($"Error in OnAssemblyResolve for {args.Name}: {ex.Message}");
            }
            
            return null;
        }
        
        /// <summary>
        /// Load native libraries from the libs directory
        /// </summary>
        public static bool LoadNativeLibrary(string libraryName)
        {
            if (!_initialized)
            {
                Initialize();
            }
            
            try
            {
                // Add proper extension based on platform
                string extension;
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    extension = ".dll";
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    extension = ".so";
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    extension = ".dylib";
                }
                else
                {
                    TransformCacherPlugin.Log.LogError("Unsupported platform for native library loading");
                    return false;
                }
                
                // Add extension if not present
                if (!libraryName.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
                {
                    libraryName += extension;
                }
                
                string libraryPath = Path.Combine(_libsDirectory, libraryName);
                
                if (!File.Exists(libraryPath))
                {
                    TransformCacherPlugin.Log.LogWarning($"Native library not found: {libraryPath}");
                    return false;
                }
                
                // Load the native library
                IntPtr handle;
                
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    handle = LoadLibrary(libraryPath);
                }
                else
                {
                    handle = dlopen(libraryPath, 2); // RTLD_NOW = 2
                }
                
                bool success = handle != IntPtr.Zero;
                
                if (success)
                {
                    TransformCacherPlugin.Log.LogInfo($"Successfully loaded native library: {libraryName}");
                }
                else
                {
                    TransformCacherPlugin.Log.LogError($"Failed to load native library: {libraryName}");
                }
                
                return success;
            }
            catch (Exception ex)
            {
                TransformCacherPlugin.Log.LogError($"Error loading native library {libraryName}: {ex.Message}");
                return false;
            }
        }
        
        // P/Invoke declarations for loading native libraries
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Ansi)]
        private static extern IntPtr LoadLibrary([MarshalAs(UnmanagedType.LPStr)] string lpFileName);
        
        [DllImport("libdl.so", SetLastError = true)]
        private static extern IntPtr dlopen([MarshalAs(UnmanagedType.LPStr)] string filename, int flags);
    }
}