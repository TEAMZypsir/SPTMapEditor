using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace TransformCacher
{
    /// <summary>
    /// Helper class with utilities for fixing and generating IDs
    /// </summary>
    public static class FixUtility
    {
        // Get the hierarchy path using sibling indices
        public static string GetSiblingIndicesPath(Transform transform)
        {
            if (transform == null) return string.Empty;
            
            // Use List instead of Stack to avoid reference issues
            List<int> indices = new List<int>();
            
            Transform current = transform;
            while (current != null)
            {
                indices.Insert(0, current.GetSiblingIndex());
                current = current.parent;
            }
            
            return string.Join(".", indices.ToArray());
        }
        
        // Get the full path of a transform in the hierarchy
        public static string GetFullPath(Transform transform)
        {
            if (transform == null) return string.Empty;
            
            // Use List instead of Stack to avoid reference issues
            List<string> path = new List<string>();
            
            var current = transform;
            while (current != null)
            {
                path.Insert(0, current.name);
                current = current.parent;
            }
            
            return string.Join("/", path.ToArray());
        }
        
        // Generate a PathID for a transform
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
        
        // Generate an ItemID for a transform
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
        
        // Generate a unique ID for a transform that persists across game sessions
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
}