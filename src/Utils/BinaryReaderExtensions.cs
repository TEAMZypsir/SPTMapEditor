// Based on AssetRipper.IO.Files handling
using System.IO;
using UnityEngine;

namespace TransformCacher.AssetRipperIntegration
{
    public static class BinaryReaderExtensions
    {
        public static Vector3 ReadVector3(this BinaryReader reader)
        {
            float x = reader.ReadSingle();
            float y = reader.ReadSingle();
            float z = reader.ReadSingle();
            return new Vector3(x, y, z);
        }
        
        public static Quaternion ReadQuaternion(this BinaryReader reader)
        {
            float x = reader.ReadSingle();
            float y = reader.ReadSingle();
            float z = reader.ReadSingle();
            float w = reader.ReadSingle();
            return new Quaternion(x, y, z, w);
        }
        
        public static string ReadAlignedString(this BinaryReader reader)
        {
            int length = reader.ReadInt32();
            if (length <= 0)
                return string.Empty;
                
            byte[] bytes = reader.ReadBytes(length);
            int alignedLength = (length + 3) & ~3; // Align to 4 bytes
            if (alignedLength > length)
                reader.ReadBytes(alignedLength - length); // Skip alignment bytes
                
            return System.Text.Encoding.UTF8.GetString(bytes);
        }
    }
    
    public static class BinaryWriterExtensions
    {
        public static void Write(this BinaryWriter writer, Vector3 value)
        {
            writer.Write(value.x);
            writer.Write(value.y);
            writer.Write(value.z);
        }
        
        public static void Write(this BinaryWriter writer, Quaternion value)
        {
            writer.Write(value.x);
            writer.Write(value.y);
            writer.Write(value.z);
            writer.Write(value.w);
        }
        
        public static void WriteAlignedString(this BinaryWriter writer, string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                writer.Write(0);
                return;
            }
            
            byte[] bytes = System.Text.Encoding.UTF8.GetBytes(value);
            writer.Write(bytes.Length);
            writer.Write(bytes);
            
            int alignedLength = (bytes.Length + 3) & ~3; // Align to 4 bytes
            int padding = alignedLength - bytes.Length;
            for (int i = 0; i < padding; i++)
                writer.Write((byte)0);
        }
    }
}