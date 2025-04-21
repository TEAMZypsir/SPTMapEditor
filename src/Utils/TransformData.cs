using System;
using System.IO;
using AssetRipper.IO.Endian;
using UnityEngine;

namespace TransformCacher.AssetRipperIntegration
{
    public class SerializedTransform
    {
        // Essential properties for transform serialization
        public long PathID { get; set; }
        public string Name { get; set; }
        public Vector3 LocalPosition { get; set; }
        public Quaternion LocalRotation { get; set; }
        public Vector3 LocalScale { get; set; }
        public long ParentPathID { get; set; }
        public bool IsActive { get; set; }
        
        // Added properties for better scene management
        public string HierarchyPath { get; set; }
        public int InstanceID { get; set; }
        public string SceneName { get; set; }
        
        public static SerializedTransform FromTransform(Transform transform, long pathID)
        {
            if (transform == null) throw new ArgumentNullException(nameof(transform));
            
            return new SerializedTransform
            {
                PathID = pathID,
                Name = transform.name,
                LocalPosition = transform.localPosition,
                LocalRotation = transform.localRotation,
                LocalScale = transform.localScale,
                ParentPathID = transform.parent != null ? GetPathID(transform.parent) : 0,
                IsActive = transform.gameObject.activeSelf,
                HierarchyPath = GetHierarchyPath(transform),
                InstanceID = transform.gameObject.GetInstanceID(),
                SceneName = transform.gameObject.scene.name
            };
        }
        
        private static string GetHierarchyPath(Transform transform)
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
        
        private static long GetPathID(Transform transform)
        {
            // In a real implementation, we would lookup the path ID from asset data
            // For now, we'll use a simple hashing approach as a placeholder
            string path = GetHierarchyPath(transform);
            return Math.Abs(path.GetHashCode()) * 1000 + transform.GetInstanceID();
        }
        
        // Binary serialization methods for writing to asset files
        public void Write(EndianWriter writer)
        {
            writer.Write(PathID);
            writer.Write(Name);
            writer.Write(LocalPosition.x);
            writer.Write(LocalPosition.y);
            writer.Write(LocalPosition.z);
            writer.Write(LocalRotation.x);
            writer.Write(LocalRotation.y);
            writer.Write(LocalRotation.z);
            writer.Write(LocalRotation.w);
            writer.Write(LocalScale.x);
            writer.Write(LocalScale.y);
            writer.Write(LocalScale.z);
            writer.Write(ParentPathID);
            writer.Write(IsActive);
            writer.Write(HierarchyPath);
            writer.Write(InstanceID);
            writer.Write(SceneName);
        }
        
        public static SerializedTransform Read(EndianReader reader)
        {
            return new SerializedTransform
            {
                PathID = reader.ReadInt64(),
                Name = reader.ReadString(),
                LocalPosition = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle()),
                LocalRotation = new Quaternion(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle()),
                LocalScale = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle()),
                ParentPathID = reader.ReadInt64(),
                IsActive = reader.ReadBoolean(),
                HierarchyPath = reader.ReadString(),
                InstanceID = reader.ReadInt32(),
                SceneName = reader.ReadString()
            };
        }
    }
}