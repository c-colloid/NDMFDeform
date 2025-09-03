using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor;

namespace ExDeform.Editor
{
    /// <summary>
    /// Validation test for IUIBuilderService implementation
    /// IUIBuilderService実装の検証テスト
    /// </summary>
    public static class UIBuilderServiceValidationTest
    {
        [MenuItem("ExDeform/Test UI Builder Service")]
        public static void RunValidationTest()
        {
            Debug.Log("[UIBuilderService] Starting validation test...");
            
            try
            {
                // Test 1: Service instantiation
                var uiBuilderService = new UIBuilderService();
                Assert(uiBuilderService != null, "UIBuilderService should be instantiable");
                Debug.Log("✓ Service instantiation test passed");
                
                // Test 2: Section creation
                var section = uiBuilderService.CreateSection("Test Section");
                Assert(section != null, "CreateSection should return valid VisualElement");
                Debug.Log("✓ Section creation test passed");
                
                // Test 3: Header creation
                var header = uiBuilderService.CreateHeader();
                Assert(header != null, "CreateHeader should return valid VisualElement");
                Assert(header.childCount > 0, "Header should contain child elements");
                Debug.Log("✓ Header creation test passed");
                
                // Test 4: Status area creation
                var statusComponents = uiBuilderService.CreateStatusArea();
                Assert(statusComponents.container != null, "Status container should not be null");
                Assert(statusComponents.statusLabel != null, "Status label should not be null");
                Debug.Log("✓ Status area creation test passed");
                
                // Test 5: Range selection overlay creation
                var rangeOverlay = uiBuilderService.CreateRangeSelectionOverlay();
                Assert(rangeOverlay != null, "Range selection overlay should not be null");
                Assert(rangeOverlay.style.position.value == Position.Absolute, "Range overlay should be absolutely positioned");
                Debug.Log("✓ Range selection overlay test passed");
                
                // Test 6: Magnifying glass overlay creation
                var magnifyingComponents = uiBuilderService.CreateMagnifyingGlassOverlay();
                Assert(magnifyingComponents.Overlay != null, "Magnifying glass overlay should not be null");
                Assert(magnifyingComponents.ImageElement != null, "Magnifying glass image should not be null");
                Assert(magnifyingComponents.InfoLabel != null, "Magnifying glass label should not be null");
                Debug.Log("✓ Magnifying glass overlay test passed");
                
                // Test 7: Control buttons creation
                bool refreshCalled = false;
                bool clearCalled = false;
                
                var controlButtons = uiBuilderService.CreateControlButtons(
                    () => refreshCalled = true,
                    () => clearCalled = true
                );
                Assert(controlButtons != null, "Control buttons container should not be null");
                Assert(controlButtons.childCount >= 2, "Control buttons should contain at least 2 buttons");
                Debug.Log("✓ Control buttons creation test passed");
                
                // Test 8: Island list creation (with minimal config)
                var listConfig = new IslandListConfig 
                { 
                    Height = 100,
                    MakeItem = () => new Label("Test Item"),
                    BindItem = (element, index) => { },
                    OnSelectionChanged = (items) => { }
                };
                var islandList = uiBuilderService.CreateIslandList(listConfig);
                Assert(islandList != null, "Island list should not be null");
                Assert(islandList.style.height.value.value == 100, "Island list should have correct height");
                Debug.Log("✓ Island list creation test passed");
                
                // Test 9: Localization methods
                var testElement = new Label("Test");
                uiBuilderService.SetLocalizedContent(testElement, "status_ready");
                Assert(!string.IsNullOrEmpty(testElement.text), "Localized content should be set");
                
                uiBuilderService.SetLocalizedTooltip(testElement, "tooltip_test");
                // Note: tooltip might be empty if localization key doesn't exist
                Debug.Log("✓ Localization methods test passed");
                
                Debug.Log("[UIBuilderService] ✅ All validation tests passed successfully!");
                
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[UIBuilderService] ❌ Validation test failed: {ex.Message}");
                Debug.LogException(ex);
            }
        }
        
        private static void Assert(bool condition, string message)
        {
            if (!condition)
            {
                throw new System.Exception($"Assertion failed: {message}");
            }
        }
    }
}