using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace TransformCacher
{
    public class SelectionHighlighter : MonoBehaviour
    {
        // The color of the highlight
        private Color _highlightColor = new Color(0.2f, 0.4f, 1f, 0.5f);
        
        // Pulse parameters
        private float _pulseSpeed = 1.5f;  // Pulses per second
        private float _pulseMin = 0.3f;    // Minimum intensity (0-1)
        private float _pulseMax = 0.7f;    // Maximum intensity (0-1)
        
        // List of renderers to highlight
        private List<Renderer> _renderers = new List<Renderer>();
        
        // Original materials
        private Dictionary<Renderer, Material[]> _originalMaterials = new Dictionary<Renderer, Material[]>();
        
        // Highlight materials
        private Dictionary<Renderer, Material[]> _highlightMaterials = new Dictionary<Renderer, Material[]>();
        
        // Whether highlighting is active
        private bool _isHighlighting = false;
        
        void Start()
        {
            // Gather all renderers attached to this object and its children
            _renderers.AddRange(GetComponentsInChildren<Renderer>(true));
            
            if (_renderers.Count == 0)
            {
                // If no renderers, add a temporary one for visualization
                AddTemporaryVisualizer();
            }
            
            // Set up materials
            foreach (var renderer in _renderers)
            {
                // Cache original materials
                _originalMaterials[renderer] = renderer.materials;
                
                // Create highlight materials
                Material[] highlightMats = new Material[renderer.materials.Length];
                for (int i = 0; i < renderer.materials.Length; i++)
                {
                    highlightMats[i] = CreateHighlightMaterial(renderer.materials[i]);
                }
                _highlightMaterials[renderer] = highlightMats;
            }
            
            // Start the pulse coroutine
            StartCoroutine(PulseEffect());
        }
        
        void OnDestroy()
        {
            // Restore original materials
            if (_isHighlighting)
            {
                foreach (var renderer in _renderers)
                {
                    if (renderer && _originalMaterials.ContainsKey(renderer))
                    {
                        renderer.materials = _originalMaterials[renderer];
                    }
                }
            }
            
            // Clean up highlight materials
            foreach (var materialArray in _highlightMaterials.Values)
            {
                foreach (var material in materialArray)
                {
                    Destroy(material);
                }
            }
        }
        
        private void AddTemporaryVisualizer()
        {
            // Don't add a visualizer - objects without renderers will have no highlight
            // Just log this for debugging
            Debug.Log("[SelectionHighlighter] Object has no renderers - no highlight visualizer added");
            
            // If we really need a fallback highlighter, add an empty GameObject that doesn't have a visible mesh
            GameObject visualizer = new GameObject("InvisibleHighlighter");
            visualizer.transform.SetParent(transform, false);
            visualizer.transform.localPosition = Vector3.zero;
            
            // Add a mesh renderer but don't set a mesh
            MeshRenderer renderer = visualizer.AddComponent<MeshRenderer>();
            
            // Add to renderers list to ensure highlighting still works for objects with renderers
            _renderers.Add(renderer);
        }
        
        private Bounds CalculateObjectBounds()
        {
            Bounds bounds = new Bounds(Vector3.zero, Vector3.one);
            Renderer[] renderers = GetComponentsInChildren<Renderer>();
            
            if (renderers.Length > 0)
            {
                bounds = renderers[0].bounds;
                for (int i = 1; i < renderers.Length; i++)
                {
                    bounds.Encapsulate(renderers[i].bounds);
                }
            }
            
            return bounds;
        }
        
        private Mesh CreateSphereMesh()
        {
            // Create a simple sphere mesh for visualization
            return Resources.GetBuiltinResource<Mesh>("Sphere.fbx");
        }
        
        private Material CreateHighlightMaterial(Material baseMaterial)
        {
            // Create a new material based on the original
            Material highlightMaterial = new Material(Shader.Find("Standard"));
            
            // Set to transparent mode
            highlightMaterial.SetFloat("_Mode", 3); // Transparent mode
            highlightMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            highlightMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            highlightMaterial.SetInt("_ZWrite", 0);
            highlightMaterial.DisableKeyword("_ALPHATEST_ON");
            highlightMaterial.EnableKeyword("_ALPHABLEND_ON");
            highlightMaterial.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            highlightMaterial.renderQueue = 3000;
            
            // Set color with initial alpha
            highlightMaterial.color = new Color(
                _highlightColor.r, 
                _highlightColor.g, 
                _highlightColor.b, 
                _highlightColor.a * _pulseMin
            );
            
            // Add emission for glow effect
            highlightMaterial.EnableKeyword("_EMISSION");
            highlightMaterial.SetColor("_EmissionColor", new Color(
                _highlightColor.r * 0.7f, 
                _highlightColor.g * 0.7f, 
                _highlightColor.b * 0.7f, 
                1.0f
            ));
            
            return highlightMaterial;
        }
        
        private IEnumerator PulseEffect()
        {
            float time = 0f;
            
            while (true)
            {
                // Calculate pulse alpha based on sine wave
                float pulseAlpha = Mathf.Lerp(_pulseMin, _pulseMax, 
                    (Mathf.Sin(time * _pulseSpeed * 2 * Mathf.PI) + 1) / 2);
                
                // Calculate emission intensity (slightly higher than alpha)
                float emissionIntensity = Mathf.Lerp(0.5f, 1.2f, pulseAlpha);
                
                // Update all highlight materials
                foreach (var kvp in _highlightMaterials)
                {
                    foreach (var material in kvp.Value)
                    {
                        // Update alpha
                        Color color = material.color;
                        color.a = _highlightColor.a * pulseAlpha;
                        material.color = color;
                        
                        // Update emission
                        Color emissionColor = new Color(
                            _highlightColor.r * emissionIntensity,
                            _highlightColor.g * emissionIntensity,
                            _highlightColor.b * emissionIntensity,
                            1.0f
                        );
                        material.SetColor("_EmissionColor", emissionColor);
                    }
                }
                
                // Toggle highlight materials
                if (!_isHighlighting)
                {
                    ApplyHighlightMaterials();
                    _isHighlighting = true;
                }
                
                time += Time.deltaTime;
                yield return null;
            }
        }
        
        private void ApplyHighlightMaterials()
        {
            foreach (var renderer in _renderers)
            {
                if (renderer && _highlightMaterials.ContainsKey(renderer))
                {
                    renderer.materials = _highlightMaterials[renderer];
                }
            }
        }
    }
}
