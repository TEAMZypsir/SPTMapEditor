using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TransformCacher
{
    public class UIManager : MonoBehaviour
    {
        // UI State
        
        // Reference to the bundle loader
        
        // UI Positions
        
        // References
        private TransformCacher _transformCacher;
        
        public void Initialize()
        {
            _transformCacher = GetComponent<TransformCacher>();
            
            TransformCacherPlugin.Log.LogInfo("UIManager initialized");
        }
        
        private void Update()
        {
            // No hotkey handling needed after removing export functionality
        }
        
        private void OnGUI()
        {
            // Add a small button in the corner
            if (GUI.Button(new Rect(Screen.width - 120, 10, 110, 30), "Transform Cacher"))
            {
                TransformCacherPlugin.Log.LogInfo("Transform Cacher button clicked");
            }
        }
    }
}