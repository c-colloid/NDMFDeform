using MeshModifier.NDMFDeform.Core;
using MeshModifier.NDMFDeform.Editor;
using NUnit.Framework;
using UnityEngine;

namespace MeshModifier.NDMFDeform.Tests
{
	public class SimpleDeformerTests
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

		private DeformStack CreateStack()
		{
			_root = new GameObject("SimpleDeformerTestRoot");
			return _root.AddComponent<DeformStack>();
		}

		[Test]
		public void Scale_ScalesAlongAxis()
		{
			var stack = CreateStack();
			var axisGo = new GameObject("Axis");
			axisGo.transform.SetParent(_root.transform, false);
			axisGo.transform.localScale = new Vector3(2f, 1f, 1f);
			var deformer = axisGo.AddComponent<ScaleDeformer>();
			stack.AddDeformer(deformer);

			_source = new Mesh
			{
				vertices = new[] { new Vector3(1f, 0f, 0f), new Vector3(0f, 1f, 0f) },
			};

			_baked = DeformBakeCore.Bake(stack, _source, _root.transform);

			var v = _baked.vertices;
			Assert.That(Vector3.Distance(v[0], new Vector3(2f, 0f, 0f)), Is.LessThan(1e-4f));
			Assert.That(Vector3.Distance(v[1], new Vector3(0f, 1f, 0f)), Is.LessThan(1e-4f));
		}

		[Test]
		public void Transform_LerpsTowardTarget()
		{
			var stack = CreateStack();
			var deformerGo = new GameObject("Deformer");
			deformerGo.transform.SetParent(_root.transform, false);
			var deformer = deformerGo.AddComponent<TransformDeformer>();
			var targetGo = new GameObject("Target");
			targetGo.transform.SetParent(_root.transform, false);
			targetGo.transform.position = new Vector3(0f, 2f, 0f);
			deformer.Target = targetGo.transform;
			deformer.Factor = 1f;
			stack.AddDeformer(deformer);

			_source = new Mesh
			{
				vertices = new[] { new Vector3(0f, 0f, 0f), new Vector3(1f, 0f, 0f) },
			};

			_baked = DeformBakeCore.Bake(stack, _source, _root.transform);

			var v = _baked.vertices;
			Assert.That(Vector3.Distance(v[0], new Vector3(0f, 2f, 0f)), Is.LessThan(1e-4f));
			Assert.That(Vector3.Distance(v[1], new Vector3(1f, 2f, 0f)), Is.LessThan(1e-4f));

			// factor 0.5 では半分だけ移動する
			Object.DestroyImmediate(_baked);
			deformer.Factor = 0.5f;
			_baked = DeformBakeCore.Bake(stack, _source, _root.transform);
			Assert.That(Vector3.Distance(_baked.vertices[0], new Vector3(0f, 1f, 0f)), Is.LessThan(1e-4f));
		}
	}
}
