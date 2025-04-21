using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TransformCacher
{
    public class FixUtility
    {
        /// <summary>
        /// Generate a simple unique ID for an object based on its path and scene
        /// </summary>
        public static string GenerateUniqueId(Transform transform)
        {
            string path = GetFullPath(transform);
            string scene = SceneManager.GetActiveScene().name;
            return $"{scene}_{path.GetHashCode():X8}";
        }
        
        /// <summary>
        /// Get the full path of a transform in the hierarchy
        /// </summary>
        public static string GetFullPath(Transform transform)
        {
            if (transform == null) return string.Empty;
            
            string path = transform.name;
            Transform parent = transform.parent;
            
            while (parent != null)
            {
                path = parent.name + "/" + path;
                parent = parent.parent;
            }
            
            return path;
        }
        
        /// <summary>
        /// For backward compatibility with old code - replaced with simplified implementation
        /// </summary>
        public static string GeneratePathID(Transform transform)
        {
            // Simplify to just use path hash
            string path = GetFullPath(transform);
            return path.GetHashCode().ToString("X8");
        }
        
        /// <summary>
        /// For backward compatibility with old code - replaced with simplified implementation
        /// </summary>
        public static string GenerateItemID(Transform transform)
        {
            // Simplify to use name hash + position
            Vector3 position = transform.position;
            string uniqueString = $"{transform.name}_{position.x}_{position.y}_{position.z}";
            return uniqueString.GetHashCode().ToString("X8");
        }

        /// <summary>
        /// Gets a path of sibling indices for a transform for alternate identification
        /// </summary>
        public static string GetSiblingIndicesPath(Transform transform)
        {
            if (transform == null) return string.Empty;

            List<int> indices = new List<int>();
            Transform current = transform;
            
            while (current != null)
            {
                indices.Add(current.GetSiblingIndex());
                current = current.parent;
            }
            
            // Reverse so we go from root to target
            indices.Reverse();
            
            return string.Join(".", indices.Select(i => i.ToString()).ToArray());
        }
    }
}