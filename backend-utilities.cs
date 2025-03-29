using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Logging;
using UnityEngine;

namespace TransformCacher
{
    #region Logging

    /// <summary>
    /// BepInEx logger implementation for external libraries
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
    /// Progress logger for async operations
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

    #region Unity Extensions

    /// <summary>
    /// Extension methods for Unity transforms
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

    #region LoggerEvent Enum

    /// <summary>
    /// Logger event types for BepInEx logger
    /// </summary>
    public enum LoggerEvent
    {
        Verbose,
        Debug,
        Info,
        Warning,
        Error
    }

    #endregion

    #region ILogger Interface

    /// <summary>
    /// Interface for loggers that can be used by external libraries
    /// </summary>
    public interface ILogger
    {
        void Log(LoggerEvent loggerEvent, string message, bool ignoreLevel = false);
    }

    #endregion

    #region Asset Studio Integration

    /// <summary>
    /// Static class for helping with AssetStudio integration
    /// </summary>
    public static class AssetStudioHelper
    {
        /// <summary>
        /// Initialize AssetStudio logging with BepInEx logger
        /// </summary>
        public static void InitializeAssetStudioLogging(ManualLogSource logSource)
        {
            try
            {
                // Find AssetStudio Logger class via reflection
                var assetStudioAssembly = AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(a => a.GetName().Name.Contains("AssetStudio"));
                    
                if (assetStudioAssembly != null)
                {
                    var loggerType = assetStudioAssembly.GetType("AssetStudio.Logger");
                    if (loggerType != null)
                    {
                        var defaultField = loggerType.GetField("Default", BindingFlags.Public | BindingFlags.Static);
                        if (defaultField != null)
                        {
                            // Create our logger and assign it to AssetStudio's Logger.Default
                            var bepinexLogger = new BepinexLogger(logSource);
                            defaultField.SetValue(null, bepinexLogger);
                        }
                    }
                    
                    var progressType = assetStudioAssembly.GetType("AssetStudio.Progress");
                    if (progressType != null)
                    {
                        var defaultField = progressType.GetField("Default", BindingFlags.Public | BindingFlags.Static);
                        if (defaultField != null)
                        {
                            // Create progress logger
                            var progressLogger = new ProgressLogger();
                            defaultField.SetValue(null, progressLogger);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                logSource.LogError($"Failed to initialize AssetStudio logging: {ex.Message}");
            }
        }
    }

    #endregion
}