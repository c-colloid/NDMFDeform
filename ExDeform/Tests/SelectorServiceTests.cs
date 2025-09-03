using UnityEngine;
using UnityEditor;
using NUnit.Framework;
using System.Collections.Generic;

namespace ExDeform.Editor
{
    /// <summary>
    /// Unit tests for SelectorService implementation
    /// SelectorService実装のユニットテスト
    /// </summary>
    public class SelectorServiceTests
    {
        private ISelectorService _selectorService;
        private Mesh _testMesh;

        [SetUp]
        public void SetUp()
        {
            _selectorService = SelectorService.Instance;
            _testMesh = CreateTestMesh();
        }

        [TearDown]
        public void TearDown()
        {
            _selectorService.ClearCache();
        }

        [Test]
        public void GetOrCreateSelector_WithValidMesh_ReturnsSelector()
        {
            // Act
            var selector = _selectorService.GetOrCreateSelector(_testMesh);

            // Assert
            Assert.IsNotNull(selector);
            Assert.AreEqual(_testMesh, selector.TargetMesh);
        }

        [Test]
        public void GetOrCreateSelector_WithSameMesh_ReturnsCachedSelector()
        {
            // Arrange
            var selector1 = _selectorService.GetOrCreateSelector(_testMesh);
            
            // Act
            var selector2 = _selectorService.GetOrCreateSelector(_testMesh);

            // Assert
            Assert.AreSame(selector1, selector2);
        }

        [Test]
        public void GetOrCreateSelector_WithConfig_AppliesConfiguration()
        {
            // Arrange
            var config = new SelectorConfig
            {
                TargetMesh = _testMesh,
                UseAdaptiveVertexSize = false,
                ManualVertexSphereSize = 0.02f,
                AutoUpdatePreview = false,
                SelectedIslandIDs = new List<int> { 0, 1 }
            };

            // Act
            var selector = _selectorService.GetOrCreateSelector(config);

            // Assert
            Assert.IsFalse(selector.UseAdaptiveVertexSize);
            Assert.AreEqual(0.02f, selector.ManualVertexSphereSize, 0.001f);
            Assert.IsFalse(selector.AutoUpdatePreview);
            Assert.AreEqual(2, selector.SelectedIslandIDs.Count);
        }

        [Test]
        public void GenerateCacheKey_WithValidMesh_ReturnsConsistentKey()
        {
            // Act
            var key1 = _selectorService.GenerateCacheKey(_testMesh);
            var key2 = _selectorService.GenerateCacheKey(_testMesh);

            // Assert
            Assert.IsNotNull(key1);
            Assert.AreEqual(key1, key2);
            Assert.IsTrue(key1.StartsWith("SelectorService_"));
        }

        [Test]
        public void GenerateCacheKey_WithSuffix_IncludesSuffix()
        {
            // Arrange
            var suffix = "test_suffix";

            // Act
            var key = _selectorService.GenerateCacheKey(_testMesh, suffix);

            // Assert
            Assert.IsTrue(key.EndsWith($"_{suffix}"));
        }

        [Test]
        public void HasCachedSelector_WithExistingSelector_ReturnsTrue()
        {
            // Arrange
            var cacheKey = _selectorService.GenerateCacheKey(_testMesh);
            _selectorService.GetOrCreateSelector(_testMesh, cacheKey);

            // Act
            var result = _selectorService.HasCachedSelector(cacheKey);

            // Assert
            Assert.IsTrue(result);
        }

        [Test]
        public void HasCachedSelector_WithNonExistentSelector_ReturnsFalse()
        {
            // Act
            var result = _selectorService.HasCachedSelector("non_existent_key");

            // Assert
            Assert.IsFalse(result);
        }

        [Test]
        public void DisposeSelector_RemovesFromCache()
        {
            // Arrange
            var cacheKey = _selectorService.GenerateCacheKey(_testMesh);
            _selectorService.GetOrCreateSelector(_testMesh, cacheKey);

            // Act
            _selectorService.DisposeSelector(cacheKey);

            // Assert
            Assert.IsFalse(_selectorService.HasCachedSelector(cacheKey));
        }

        [Test]
        public void GetStatistics_ReturnsValidStatistics()
        {
            // Arrange
            _selectorService.GetOrCreateSelector(_testMesh);

            // Act
            var stats = _selectorService.GetStatistics();

            // Assert
            Assert.IsTrue(stats.totalCachedSelectors > 0);
            Assert.IsTrue(stats.totalSelectorsCreated > 0);
            Assert.IsTrue(stats.activeSelectors >= 0);
        }

        [Test]
        public void ClearCache_RemovesAllSelectors()
        {
            // Arrange
            _selectorService.GetOrCreateSelector(_testMesh);
            var statsBefore = _selectorService.GetStatistics();

            // Act
            _selectorService.ClearCache();
            var statsAfter = _selectorService.GetStatistics();

            // Assert
            Assert.IsTrue(statsBefore.totalCachedSelectors > 0);
            Assert.AreEqual(0, statsAfter.totalCachedSelectors);
        }

        [Test]
        public void InitializeSelector_WithValidParams_InitializesCorrectly()
        {
            // Arrange
            var selector = new UVIslandSelector(null);
            var config = new SelectorConfig
            {
                TargetMesh = _testMesh,
                UseAdaptiveVertexSize = false
            };

            // Act
            _selectorService.InitializeSelector(selector, _testMesh, config);

            // Assert
            Assert.AreEqual(_testMesh, selector.TargetMesh);
            Assert.IsFalse(selector.UseAdaptiveVertexSize);
        }

        private Mesh CreateTestMesh()
        {
            var mesh = new Mesh();
            mesh.vertices = new Vector3[]
            {
                new Vector3(0, 0, 0),
                new Vector3(1, 0, 0),
                new Vector3(0, 1, 0),
                new Vector3(1, 1, 0)
            };
            mesh.triangles = new int[] { 0, 1, 2, 1, 3, 2 };
            mesh.uv = new Vector2[]
            {
                new Vector2(0, 0),
                new Vector2(1, 0),
                new Vector2(0, 1),
                new Vector2(1, 1)
            };
            mesh.RecalculateNormals();
            return mesh;
        }
    }
}