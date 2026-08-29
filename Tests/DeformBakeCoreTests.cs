using MeshModifier.NDMFDeform.Core;
using MeshModifier.NDMFDeform.Editor;
using NUnit.Framework;
using UnityEngine;

namespace MeshModifier.NDMFDeform.Tests
{
	public class DeformBakeCoreTests
	{
		private GameObject _root;
		private Mesh _source;
		private Mesh _baked;

		[TearDown]
		public void TearDown()
		{
			if (_root != null) Object.DestroyImmediate(_root);
			if (_source != null) Object.DestroyImmediate(_source);
			if (_baked != null) Object.DestroyImmediate(_baked);
		}

		/// <summary>
		/// ルート(レンダラー相当)+ 子(デフォーマ軸)+ 4頂点メッシュを作る。
		/// 頂点 0,1 は円柱(scope=1, z∈(-0.5,0.5))の内側、頂点 2,3 は外側。
		/// </summary>
		private (DeformStack stack, CylindricalScaleDeformer deformer) CreateSetup()
		{
			_root = new GameObject("BakeTestRoot");
			var stack = _root.AddComponent<DeformStack>();

			var child = new GameObject("Deformer");
			child.transform.SetParent(_root.transform, false);
			var deformer = child.AddComponent<CylindricalScaleDeformer>();

			_source = new Mesh
			{
				vertices = new[]
				{
					new Vector3(0.5f, 0f, 0f),
					new Vector3(0f, 0.5f, 0f),
					new Vector3(5f, 0f, 0f),
					new Vector3(0f, 5f, 0f),
				},
			};

			return (stack, deformer);
		}

		[Test]
		public void Bake_ScalesVerticesInsideCylinder()
		{
			var (stack, deformer) = CreateSetup();
			deformer.Factor = 1f;
			deformer.Radius = 2f;
			deformer.Scope = 1f;
			stack.AddDeformer(deformer);

			_baked = DeformBakeCore.Bake(stack, _source, _root.transform);

			Assert.That(_baked, Is.Not.Null);
			var v = _baked.vertices;
			// 内側: radius/scope = 2 倍に XY スケール
			Assert.That(Vector3.Distance(v[0], new Vector3(1f, 0f, 0f)), Is.LessThan(1e-4f));
			Assert.That(Vector3.Distance(v[1], new Vector3(0f, 1f, 0f)), Is.LessThan(1e-4f));
			// 外側: 変化なし
			Assert.That(Vector3.Distance(v[2], new Vector3(5f, 0f, 0f)), Is.LessThan(1e-4f));
			Assert.That(Vector3.Distance(v[3], new Vector3(0f, 5f, 0f)), Is.LessThan(1e-4f));
		}

		[Test]
		public void Bake_DoesNotModifySourceMesh()
		{
			var (stack, deformer) = CreateSetup();
			deformer.Factor = 1f;
			deformer.Radius = 2f;
			stack.AddDeformer(deformer);

			_baked = DeformBakeCore.Bake(stack, _source, _root.transform);

			Assert.That(_baked, Is.Not.SameAs(_source));
			Assert.That(Vector3.Distance(_source.vertices[0], new Vector3(0.5f, 0f, 0f)), Is.LessThan(1e-6f));
		}

		[Test]
		public void Bake_ReturnsNullWithoutDeformers()
		{
			var (stack, _) = CreateSetup();

			_baked = DeformBakeCore.Bake(stack, _source, _root.transform);

			Assert.That(_baked, Is.Null);
		}

		[Test]
		public void Bake_ReturnsNullWhenAllDeformersDisabled()
		{
			var (stack, deformer) = CreateSetup();
			deformer.Factor = 1f;
			stack.AddDeformer(deformer, enabled: false);

			_baked = DeformBakeCore.Bake(stack, _source, _root.transform);

			Assert.That(_baked, Is.Null);
		}

		[Test]
		public void Bake_FactorZeroKeepsVertices()
		{
			var (stack, deformer) = CreateSetup();
			deformer.Factor = 0f;
			stack.AddDeformer(deformer);

			_baked = DeformBakeCore.Bake(stack, _source, _root.transform);

			Assert.That(_baked, Is.Not.Null);
			for (var i = 0; i < _source.vertexCount; i++)
			{
				Assert.That(Vector3.Distance(_baked.vertices[i], _source.vertices[i]), Is.LessThan(1e-6f));
			}
		}

		[Test]
		public void Bake_VertexTransformPushesRadially()
		{
			var (stack, _) = CreateSetup();
			var deformer = _root.transform.GetChild(0).gameObject
				.AddComponent<CylindricalVertexTransformDeformer>();
			deformer.Factor = 1f;
			deformer.Radius = 2f;
			deformer.Scope = 1f;
			stack.AddDeformer(deformer);

			_baked = DeformBakeCore.Bake(stack, _source, _root.transform);

			var v = _baked.vertices;
			// 内側: 放射方向へ (radius - scope) = 1 押し出し
			Assert.That(Vector3.Distance(v[0], new Vector3(1.5f, 0f, 0f)), Is.LessThan(1e-4f));
			Assert.That(Vector3.Distance(v[1], new Vector3(0f, 1.5f, 0f)), Is.LessThan(1e-4f));
			// 外側: 変化なし
			Assert.That(Vector3.Distance(v[2], new Vector3(5f, 0f, 0f)), Is.LessThan(1e-4f));
		}

		[Test]
		public void Bake_RespectsAxisTransform()
		{
			var (stack, deformer) = CreateSetup();
			deformer.Factor = 1f;
			deformer.Radius = 2f;
			// 軸を X 方向へ 5 移動 → 頂点 2 (5,0,0) が軸空間の中心に入る
			deformer.transform.localPosition = new Vector3(5f, 0f, 0f);
			stack.AddDeformer(deformer);

			_baked = DeformBakeCore.Bake(stack, _source, _root.transform);

			var v = _baked.vertices;
			// 頂点 2 は軸空間で原点 → スケールしても位置不変
			Assert.That(Vector3.Distance(v[2], new Vector3(5f, 0f, 0f)), Is.LessThan(1e-4f));
			// 頂点 0 (0.5,0,0) は軸空間で (-4.5,0,0) → scope=1 の外 → 不変
			Assert.That(Vector3.Distance(v[0], new Vector3(0.5f, 0f, 0f)), Is.LessThan(1e-4f));
		}
	}
}
