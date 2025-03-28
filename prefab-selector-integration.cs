using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace TarkinItemExporter.UI
{
    /// <summary>
    /// Integrates bundle prefabs into the existing prefab selector UI.
    /// This class should be attached to your existing prefab selector panel.
    /// </summary>
    public class PrefabSelectorIntegration : MonoBehaviour
    {
        // References to UI elements
        private Dropdown bundleCategoryDropdown;  // Changed from TMP_Dropdown to standard Dropdown
        private Transform prefabListContainer;
        private GameObject prefabButtonTemplate;
        
        // Reference to the active prefab selection panel
        [SerializeField] private GameObject prefabSelectionPanel;
        
        private void Awake()
        {
            // Find needed UI references
            InitializeUIReferences();
            
            // Subscribe to events
            if (SimpleBundleLoader.Instance != null)  // Changed from BundleLoader to SimpleBundleLoader
            {
                // Load bundles
                SimpleBundleLoader.Instance.LoadAllBundles();  // Changed from BundleLoader to SimpleBundleLoader
            }
            
            // Add a refresh button to reload bundles
            CreateRefreshButton();
        }
        
        private void InitializeUIReferences()
        {
            // Find the category dropdown in your existing UI
            bundleCategoryDropdown = transform.Find("CategoryDropdown")?.GetComponent<Dropdown>();  // Changed from TMP_Dropdown to standard Dropdown
            
            // Find or create a bundle category in the dropdown
            if (bundleCategoryDropdown != null)
            {
                // Add the "Bundles" category if it doesn't exist
                List<Dropdown.OptionData> options = new List<Dropdown.OptionData>(bundleCategoryDropdown.options);  // Changed from TMP_Dropdown to standard Dropdown
                if (!options.Any(o => o.text == "Bundles"))
                {
                    options.Add(new Dropdown.OptionData("Bundles"));  // Changed from TMP_Dropdown to standard Dropdown
                    bundleCategoryDropdown.options = options;
                }
                
                // Add listener to dropdown value change
                bundleCategoryDropdown.onValueChanged.AddListener(OnCategoryChanged);
            }
            
            // Find the prefab list container in your existing UI
            prefabListContainer = transform.Find("PrefabListContainer");
            
            // Find a prefab button template that can be cloned
            prefabButtonTemplate = transform.Find("PrefabListContainer/PrefabButtonTemplate")?.gameObject;
        }
        
        private void CreateRefreshButton()
        {
            // Create a refresh button to reload bundles
            GameObject refreshButton = new GameObject("RefreshBundlesButton");
            refreshButton.transform.SetParent(transform, false);
            
            RectTransform refreshButtonRect = refreshButton.AddComponent<RectTransform>();
            refreshButtonRect.anchorMin = new Vector2(1, 1);
            refreshButtonRect.anchorMax = new Vector2(1, 1);
            refreshButtonRect.pivot = new Vector2(1, 1);
            refreshButtonRect.sizeDelta = new Vector2(80, 30);
            refreshButtonRect.anchoredPosition = new Vector2(-10, -10);
            
            Image refreshButtonImage = refreshButton.AddComponent<Image>();
            refreshButtonImage.color = new Color(0.2f, 0.6f, 0.8f, 1);
            
            Button refreshButtonComponent = refreshButton.AddComponent<Button>();
            refreshButtonComponent.targetGraphic = refreshButtonImage;
            refreshButtonComponent.onClick.AddListener(RefreshBundles);
            
            GameObject refreshButtonText = new GameObject("Text");
            refreshButtonText.transform.SetParent(refreshButton.transform, false);
            
            RectTransform refreshButtonTextRect = refreshButtonText.AddComponent<RectTransform>();
            refreshButtonTextRect.anchorMin = Vector2.zero;
            refreshButtonTextRect.anchorMax = Vector2.one;
            refreshButtonTextRect.sizeDelta = Vector2.zero;
            
            // Use regular Text component instead of TextMeshProUGUI
            Text refreshButtonTextComponent = refreshButtonText.AddComponent<Text>();
            refreshButtonTextComponent.text = "Refresh";
            refreshButtonTextComponent.fontSize = 14;
            refreshButtonTextComponent.alignment = TextAnchor.MiddleCenter;  // Changed from TextAlignmentOptions to TextAnchor
            refreshButtonTextComponent.color = Color.white;
        }
        
        private void OnCategoryChanged(int index)
        {
            // Check if the "Bundles" category was selected
            if (bundleCategoryDropdown != null && bundleCategoryDropdown.options[index].text == "Bundles")
            {
                DisplayBundlePrefabs();
            }
        }
        
        private void DisplayBundlePrefabs()
        {
            // Clear existing prefabs in the list
            foreach (Transform child in prefabListContainer)
            {
                if (child.gameObject != prefabButtonTemplate)
                {
                    Destroy(child.gameObject);
                }
            }
            
            // Get all available prefabs from the bundle loader
            var bundlePrefabs = SimpleBundleLoader.Instance.GetAvailablePrefabs();  // Changed from BundleLoader to SimpleBundleLoader
            
            // Group prefabs by path (subdirectory)
            var groupedPrefabs = bundlePrefabs
                .GroupBy(p => p.Path)
                .OrderBy(g => g.Key);
            
            // Create prefab buttons for each group
            foreach (var group in groupedPrefabs)
            {
                // Create a header for the group
                CreateGroupHeader(group.Key);
                
                // Create buttons for each prefab in the group
                foreach (var prefab in group.OrderBy(p => p.DisplayName))
                {
                    CreatePrefabButton(prefab);
                }
            }
        }
        
        private void CreateGroupHeader(string groupName)
        {
            if (string.IsNullOrEmpty(groupName))
            {
                groupName = "Root";
            }
            
            GameObject header = new GameObject("Header_" + groupName);
            header.transform.SetParent(prefabListContainer, false);
            
            RectTransform headerRect = header.AddComponent<RectTransform>();
            headerRect.anchorMin = new Vector2(0, 1);
            headerRect.anchorMax = new Vector2(1, 1);
            headerRect.pivot = new Vector2(0.5f, 1);
            headerRect.sizeDelta = new Vector2(0, 25);
            
            // Add background image
            Image headerImage = header.AddComponent<Image>();
            headerImage.color = new Color(0.3f, 0.3f, 0.3f, 0.7f);
            
            // Add text
            GameObject textObj = new GameObject("Text");
            textObj.transform.SetParent(header.transform, false);
            
            RectTransform textRect = textObj.AddComponent<RectTransform>();
            textRect.anchorMin = new Vector2(0, 0);
            textRect.anchorMax = new Vector2(1, 1);
            textRect.offsetMin = new Vector2(10, 0);
            textRect.offsetMax = new Vector2(-10, 0);
            
            // Use regular Text component instead of TextMeshProUGUI
            Text text = textObj.AddComponent<Text>();
            text.text = groupName;
            text.fontSize = 14;
            text.fontStyle = FontStyle.Bold;
            text.alignment = TextAnchor.MiddleLeft;  // Changed from TextAlignmentOptions to TextAnchor
            text.color = Color.white;
        }
        
        private void CreatePrefabButton(SimpleBundleLoader.BundlePrefabInfo prefabInfo)  // Changed from BundleLoader to SimpleBundleLoader
        {
            if (prefabButtonTemplate == null)
            {
                Debug.LogError("Prefab button template not found!");
                return;
            }
            
            // Clone the button template
            GameObject buttonObj = Instantiate(prefabButtonTemplate, prefabListContainer);
            buttonObj.name = "PrefabButton_" + prefabInfo.DisplayName;
            buttonObj.SetActive(true);
            
            // Find the text component and set the prefab name
            Text buttonText = buttonObj.GetComponentInChildren<Text>();  // Changed from TextMeshProUGUI to Text
            if (buttonText != null)
            {
                buttonText.text = prefabInfo.DisplayName;
            }
            
            // Set up the button's click handler
            Button button = buttonObj.GetComponent<Button>();
            if (button != null)
            {
                button.onClick.AddListener(() => OnPrefabSelected(prefabInfo));
            }
        }
        
        private void OnPrefabSelected(SimpleBundleLoader.BundlePrefabInfo prefabInfo)  // Changed from BundleLoader to SimpleBundleLoader
        {
            // Close the prefab selection panel
            if (prefabSelectionPanel != null)
            {
                prefabSelectionPanel.SetActive(false);
            }
            
            // Spawn the selected prefab at a default position
            Vector3 spawnPosition = Vector3.zero;
            Quaternion spawnRotation = Quaternion.identity;
            
            if (Camera.main != null)
            {
                // Spawn in front of the camera
                spawnPosition = Camera.main.transform.position + Camera.main.transform.forward * 5f;
            }
            
            // Spawn the prefab
            GameObject spawnedObject = SimpleBundleLoader.Instance.SpawnPrefabInScene(  // Changed from BundleLoader to SimpleBundleLoader
                prefabInfo.BundleName,
                prefabInfo.AssetName,
                spawnPosition,
                spawnRotation
            );
        }
        
        private void RefreshBundles()
        {
            if (SimpleBundleLoader.Instance != null)  // Changed from BundleLoader to SimpleBundleLoader
            {
                SimpleBundleLoader.Instance.RefreshBundles();  // Changed from BundleLoader to SimpleBundleLoader
                
                // If the bundles category is currently active, refresh the display
                if (bundleCategoryDropdown != null && 
                    bundleCategoryDropdown.options[bundleCategoryDropdown.value].text == "Bundles")
                {
                    DisplayBundlePrefabs();
                }
            }
        }
    }
}