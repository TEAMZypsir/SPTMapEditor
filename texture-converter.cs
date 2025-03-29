using System;
using System.Collections.Generic;
using UnityEngine;

namespace TransformCacher
{
    public static class TextureConverter
    {
        private static Dictionary<Texture, Texture2D> cache = new Dictionary<Texture, Texture2D>();
        
        public static Texture2D Convert(Texture inputTexture, Material mat)
        {
            bool origSRGBWrite = GL.sRGBWrite;
            GL.sRGBWrite = false;
            
            // Create a render texture
            RenderTexture temporary = RenderTexture.GetTemporary(
                inputTexture.width, 
                inputTexture.height, 
                0, 
                RenderTextureFormat.ARGB32, 
                RenderTextureReadWrite.Linear);
            
            // Blit to render texture
            Graphics.Blit(inputTexture, temporary, mat);
            
            // Convert render texture to texture2D
            Texture2D texture2D = ToTexture2D(temporary);
            
            // Release the temporary texture
            RenderTexture.ReleaseTemporary(temporary);
            
            // Restore sRGB write setting
            GL.sRGBWrite = origSRGBWrite;
            
            // Set name and return
            texture2D.name = inputTexture.name;
            return texture2D;
        }
        
        public static Texture2D ConvertAlbedoSpecGlosToSpecGloss(Texture inputTextureAlbedoSpec, Texture inputTextureGloss)
        {
            // Check cache first
            if (cache.ContainsKey(inputTextureAlbedoSpec))
            {
                Texture2D texture2D = cache[inputTextureAlbedoSpec];
                TransformCacherPlugin.Log.LogInfo("Using cached converted texture " + texture2D.name);
                return texture2D;
            }
            
            // Create material for conversion
            Material material = new Material(BundleShaders.Find("Hidden/AlbedoSpecGlosToSpecGloss"));
            material.SetTexture("_AlbedoSpecTex", inputTextureAlbedoSpec);
            material.SetTexture("_GlossinessTex", inputTextureGloss);
            
            // Set up render settings
            bool origSRGBWrite = GL.sRGBWrite;
            GL.sRGBWrite = false;
            
            // Create temporary render texture
            RenderTexture temporary = RenderTexture.GetTemporary(
                inputTextureAlbedoSpec.width, 
                inputTextureAlbedoSpec.height, 
                0, 
                RenderTextureFormat.ARGB32, 
                RenderTextureReadWrite.Linear);
            
            // Blit to render texture
            Graphics.Blit(inputTextureAlbedoSpec, temporary, material);
            
            // Convert render texture to texture2D
            Texture2D texture2D2 = ToTexture2D(temporary);
            
            // Update name
            texture2D2.name = inputTextureAlbedoSpec.name.ReplaceLastWord('_', "SPECGLOS");
            
            // Release temporary resources
            RenderTexture.ReleaseTemporary(temporary);
            GL.sRGBWrite = origSRGBWrite;
            
            // Add to cache
            cache[inputTextureAlbedoSpec] = texture2D2;
            
            return texture2D2;
        }
        
        // Helper to convert RenderTexture to Texture2D
        private static Texture2D ToTexture2D(RenderTexture rTex)
        {
            // Create new texture with RGBA32 format
            Texture2D texture2D = new Texture2D(rTex.width, rTex.height, TextureFormat.RGBA32, false);
            
            // Set the active render texture
            RenderTexture previousRenderTexture = RenderTexture.active;
            RenderTexture.active = rTex;
            
            // Read the pixels
            texture2D.ReadPixels(new Rect(0f, 0f, (float)rTex.width, (float)rTex.height), 0, 0);
            
            // Apply the changes
            texture2D.Apply();
            
            // Restore the previous render texture
            RenderTexture.active = previousRenderTexture;
            
            return texture2D;
        }
        
        // Create a solid color texture
        public static Texture2D CreateSolidColorTexture(int width, int height, float r, float g, float b, float a)
        {
            Texture2D texture2D = new Texture2D(width, height);
            Color[] pixels = new Color[width * height];
            Color color = new Color(r, g, b, a);
            
            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = color;
            }
            
            texture2D.SetPixels(pixels);
            texture2D.Apply();
            
            return texture2D;
        }
        
        // Simplified version for greyscale textures
        public static Texture2D CreateSolidColorTexture(int width, int height, float c, float a)
        {
            return CreateSolidColorTexture(width, height, c, c, c, a);
        }
        
        // Extension method to replace the last word in a string
        public static string ReplaceLastWord(this string input, char separator, string replacement)
        {
            int lastSeparatorIndex = input.LastIndexOf(separator);
            
            if (lastSeparatorIndex == -1)
            {
                return replacement;
            }
            
            return input.Substring(0, lastSeparatorIndex + 1) + replacement;
        }
    }
}