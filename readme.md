### Example Integration

Here’s a simplified example of how you might integrate the `AssetFileUtil` class directly into your project:

1. **Copy the Code**: Copy the `AssetFileUtil` class from the AssetRipper files.

```csharp
using System;
using System.IO;
using System.Security.Cryptography;

namespace YourProjectNamespace
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
                if (overwrite || !File.Exists(destFile))
                {
                    File.Copy(filePath, destFile, overwrite);
                }
            }
        }
    }
}
```

2. **Remove Library References**: If you were previously using AssetRipper as a library, remove any references to it in your project.

3. **Update Usage**: Wherever you were using the AssetRipper library in your code, update those references to use your integrated `AssetFileUtil` class instead.

4. **Test Your Changes**: Run your project and test the functionality to ensure that everything works as expected.

### Additional Considerations

- **Namespace Conflicts**: Ensure that there are no naming conflicts with existing classes in your project.
- **Error Handling**: Review the error handling in the copied code and adjust it to fit your project's error handling strategy.
- **Documentation**: Update any documentation to reflect the changes made during the integration process.

By following these steps, you can effectively integrate the AssetRipper code directly into your project, allowing you to customize and extend its functionality as needed.