using MeshModifier.NDMFDeform.Core;
using MeshModifier.NDMFDeform.Editor;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;

namespace MeshModifier.NDMFDeform.Tests
{
	public class LatticeTests
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

		private (DeformStack stack, LatticeDeformer lattice) CreateSetup()
		{
			_root = new GameObject("LatticeTestRoot");
			var stack = _root.AddComponent<DeformStack>();

			var child = new GameObject("Lattice");
			child.transform.SetParent(_root.transform, false);
			var lattice = child.AddComponent<LatticeDeformer>();
			lattice.GenerateControlPoints(new Vector3Int(2, 2, 2));

			_source = new Mesh
			{
				vertices = new[]
				{
					new Vector3(0.5f, 0.5f, 0.5f),   // +コーナー
					new Vector3(0f, 0f, 0f),         // 中心
					new Vector3(-0.5f, -0.5f, -0.5f), // -コーナー
				},
			};

			stack.AddDeformer(lattice);
			return (stack, lattice);
		}

		[Test]
		public void IdentityLattice_KeepsVertices()
		{
			var (stack, _) = CreateSetup();

			_baked = DeformBakeCore.Bake(stack, _source, _root.transform);

			Assert.That(_baked, Is.Not.Null);
			for (var i = 0; i < _source.vertexCount; i++)
			{
				Assert.That(Vector3.Distance(_baked.vertices[i], _source.vertices[i]), Is.LessThan(1e-5f),
					$"vertex {i}");
			}
		}

		[Test]
		public void MovedCorner_InterpolatesTrilinearly()
		{
			var (stack, lattice) = CreateSetup();
			var delta = new float3(0f, 0.4f, 0f);
			var cornerIndex = lattice.GetIndex(1, 1, 1); // (+,+,+) コーナー
			lattice.ControlPoints[cornerIndex] += delta;

			_baked = DeformBakeCore.Bake(stack, _source, _root.transform);

			var v = _baked.vertices;
			// コーナー頂点は制御点と一緒に移動
			Assert.That(Vector3.Distance(v[0], new Vector3(0.5f, 0.9f, 0.5f)), Is.LessThan(1e-4f));
			// 中心頂点はトライリニアで 1/8 だけ移動
			Assert.That(Vector3.Distance(v[1], new Vector3(0f, 0.05f, 0f)), Is.LessThan(1e-4f));
			// 反対側コーナーは不変
			Assert.That(Vector3.Distance(v[2], new Vector3(-0.5f, -0.5f, -0.5f)), Is.LessThan(1e-4f));
		}

		[Test]
		public void ResolutionResample_PreservesDeformation()
		{
			var (_, lattice) = CreateSetup();
			var delta = new float3(0f, 0.4f, 0f);
			var oldPoints = lattice.ControlPoints;
			oldPoints[lattice.GetIndex(1, 1, 1)] += delta; // 2x2x2 の (+,+,+) コーナー

			lattice.GenerateControlPoints(new Vector3Int(3, 3, 3), oldPoints, new Vector3Int(2, 2, 2));

			// 新格子のコーナー (2,2,2) は旧コーナーの変形位置を引き継ぐ
			var corner = lattice.ControlPoints[lattice.GetIndex(2, 2, 2)];
			Assert.That(math.distance(corner, new float3(0.5f, 0.9f, 0.5f)), Is.LessThan(1e-4f));
			// 中心 (1,1,1) はトライリニアで 1/8 だけ動く
			var center = lattice.ControlPoints[lattice.GetIndex(1, 1, 1)];
			Assert.That(math.distance(center, new float3(0f, 0.05f, 0f)), Is.LessThan(1e-4f));
			// 反対側コーナーは恒等のまま
			var opposite = lattice.ControlPoints[lattice.GetIndex(0, 0, 0)];
			Assert.That(math.distance(opposite, new float3(-0.5f, -0.5f, -0.5f)), Is.LessThan(1e-4f));
		}

		[Test]
		public void PointGridUtility_IndexCoordRoundtrip()
		{
			var res = new Vector3Int(3, 4, 5);
			for (var i = 0; i < res.x * res.y * res.z; i++)
			{
				var c = PointGridUtility.GetCoord(res, i);
				Assert.That(PointGridUtility.GetIndex(res, c.x, c.y, c.z), Is.EqualTo(i));
			}
		}

		[Test]
		public void PointGridUtility_LineSelection()
		{
			var res = new Vector3Int(3, 3, 3);
			var line = PointGridUtility.LineIndices(res, new Vector3Int(1, 2, 0), HandleAxis.X);

			Assert.That(line.Count, Is.EqualTo(3));
			foreach (var i in line)
			{
				var c = PointGridUtility.GetCoord(res, i);
				Assert.That(c.y, Is.EqualTo(2));
				Assert.That(c.z, Is.EqualTo(0));
			}
		}

		[Test]
		public void PointGridUtility_SheetSelection()
		{
			var res = new Vector3Int(3, 3, 3);
			var sheet = PointGridUtility.SheetIndices(res, HandleAxis.Z, 1);

			Assert.That(sheet.Count, Is.EqualTo(9));
			foreach (var i in sheet)
				Assert.That(PointGridUtility.GetCoord(res, i).z, Is.EqualTo(1));
		}

		[Test]
		public void PointGridUtility_MirrorIndexAndPosition()
		{
			var res = new Vector3Int(3, 2, 2);

			// X 端は反対端へ、中心列は自分自身へ
			var edge = PointGridUtility.GetIndex(res, 0, 1, 0);
			var mirroredEdge = PointGridUtility.MirrorIndex(res, edge, MirrorAxis.X);
			Assert.That(PointGridUtility.GetCoord(res, mirroredEdge), Is.EqualTo(new Vector3Int(2, 1, 0)));

			var center = PointGridUtility.GetIndex(res, 1, 0, 1);
			Assert.That(PointGridUtility.MirrorIndex(res, center, MirrorAxis.X), Is.EqualTo(center));

			// 位置の鏡像は対象成分のみ符号反転
			var p = new float3(0.3f, -0.1f, 0.2f);
			var m = PointGridUtility.MirrorPosition(p, MirrorAxis.X);
			Assert.That(m.x, Is.EqualTo(-0.3f).Within(1e-6f));
			Assert.That(m.y, Is.EqualTo(-0.1f).Within(1e-6f));
			Assert.That(m.z, Is.EqualTo(0.2f).Within(1e-6f));
		}
	}
}
