using System;
using System.IO;
using System.Security.Cryptography;
using AssetRipper.IO.Files;

namespace TransformCacher.AssetRipperIntegration
{
    public static class AssetFileUtil
    {
        public static bool IsAssetBundle(string filePath)
        {
            if (!File.Exists(filePath))
                return false;
                
            try
            {
                using (FileStream stream = File.OpenRead(filePath))
                using (BinaryReader reader = new BinaryReader(stream))
                {
                    string signature = new string(reader.ReadChars(7));
                    return signature == "UnityFS" || signature == "Unity A";
                }
            }
            catch
            {
                return false;
            }
        }
        
        public static bool IsUnityScene(string filePath)
        {
            return Path.GetExtension(filePath).Equals(".unity", StringComparison.OrdinalIgnoreCase);
        }
        
        public static string CalculateFileHash(string filePath)
        {
            using (FileStream stream = File.OpenRead(filePath))
            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(stream);
                return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
            }
        }
        
        public static void CopyDirectory(string sourceDir, string destDir, bool overwrite = true)
        {
            // Create the destination directory if it doesn't exist
            Directory.CreateDirectory(destDir);
            
            // Get all files and process them
            foreach (string filePath in Directory.GetFiles(sourceDir))
            {
                string fileName = Path.GetFileName(filePath);
                string destFile = Path.Combine(destDir, fileName);
                File.Copy(filePath, destFile, overwrite);
            }
            
            // Process subdirectories recursively
            foreach (string subDir in Directory.GetDirectories(sourceDir))
            {
                string subDirName = Path.GetFileName(subDir);
                string destSubDir = Path.Combine(destDir, subDirName);
                CopyDirectory(subDir, destSubDir, overwrite);
            }
        }
        
        public static void EnsureDirectoryExists(string path)
        {
            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }
        }
        
        public static void BackupFile(string filePath)
        {
            if (File.Exists(filePath))
            {
                string backupPath = filePath + ".backup";
                if (!File.Exists(backupPath))
                {
                    File.Copy(filePath, backupPath);
                }
            }
        }
        
        public static byte[] ReadAllBytes(string filePath)
        {
            return File.ReadAllBytes(filePath);
        }
        
        public static void WriteAllBytes(string filePath, byte[] data)
        {
            File.WriteAllBytes(filePath, data);
        }
    }
}