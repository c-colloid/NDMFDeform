using System.Collections.Generic;
using MeshModifier.NDMFDeform.Core;
using MeshModifier.NDMFDeform.Editor;
using NUnit.Framework;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace MeshModifier.NDMFDeform.Tests
{
	/// <summary>
	/// BodyFitDeformer のベイク検証。
	/// 体 = 原点中心・一辺 1 の立方体(面は ±0.5)、衣装 = 少数の頂点。
	/// 押し出し / 引き寄せ / 帯 / 探索距離 / 二重球 / factor / マスク合成 /
	/// ブレンドシェイプ / 平滑化とシーム / スキン済み参照 / 重ね着 / キャッシュ再利用を確認する。
	/// </summary>
	public class BodyFitDeformerTests
	{
		private GameObject _root;
		private readonly List<Mesh> _meshes = new List<Mesh>();
		private Mesh _baked;

		[TearDown]
		public void TearDown()
		{
			if (_root != null) Object.DestroyImmediate(_root);
			foreach (var mesh in _meshes)
				if (mesh != null) Object.DestroyImmediate(mesh);
			_meshes.Clear();
			if (_baked != null) Object.DestroyImmediate(_baked);
			_baked = null;
			// 重ね着テストで参照先スタックのプレビューキャッシュが作られるため回収する
			DeformPreviewBakeCache.RefreshStaleEntries(0);
		}

		private Mesh Track(Mesh mesh)
		{
			_meshes.Add(mesh);
			return mesh;
		}

		// ---- メッシュ生成 ----

		/// <summary>原点中心・一辺 1 の立方体(8 頂点共有、法線は外向き)</summary>
		private static Mesh MakeCube()
		{
			var vertices = new List<Vector3>();
			var triangles = new List<int>();
			var shared = new Dictionary<Vector3, int>();

			int Add(Vector3 v)
			{
				if (!shared.TryGetValue(v, out var idx))
				{
					idx = vertices.Count;
					vertices.Add(v);
					shared[v] = idx;
				}
				return idx;
			}

			void AddTriangle(Vector3 a, Vector3 b, Vector3 c, Vector3 outward)
			{
				if (Vector3.Dot(Vector3.Cross(b - a, c - a), outward) < 0f)
				{
					var tmp = b;
					b = c;
					c = tmp;
				}
				triangles.Add(Add(a));
				triangles.Add(Add(b));
				triangles.Add(Add(c));
			}

			for (var axis = 0; axis < 3; axis++)
			{
				for (var sign = -1; sign <= 1; sign += 2)
				{
					var normal = Vector3.zero;
					normal[axis] = sign;
					var u = Vector3.zero;
					u[(axis + 1) % 3] = 1f;
					var v = Vector3.zero;
					v[(axis + 2) % 3] = 1f;
					var center = normal * 0.5f;
					var p00 = center - u * 0.5f - v * 0.5f;
					var p10 = center + u * 0.5f - v * 0.5f;
					var p01 = center - u * 0.5f + v * 0.5f;
					var p11 = center + u * 0.5f + v * 0.5f;
					AddTriangle(p00, p10, p11, normal);
					AddTriangle(p00, p11, p01, normal);
				}
			}

			var mesh = new Mesh { vertices = vertices.ToArray(), triangles = triangles.ToArray() };
			mesh.RecalculateBounds();
			return mesh;
		}

		/// <summary>x = x0 の平面に +X を向いた四角形(y, z ∈ [-half, half])</summary>
		private static Mesh MakeQuadFacingX(float x0, float half)
		{
			var a = new Vector3(x0, -half, -half);
			var b = new Vector3(x0, half, -half);
			var c = new Vector3(x0, half, half);
			var d = new Vector3(x0, -half, half);
			var mesh = new Mesh { vertices = new[] { a, b, c, d } };
			var tri = new List<int>();
			void AddTriangle(int i0, int i1, int i2)
			{
				var n = Vector3.Cross(mesh.vertices[i1] - mesh.vertices[i0], mesh.vertices[i2] - mesh.vertices[i0]);
				if (n.x < 0f)
				{
					tri.AddRange(new[] { i0, i2, i1 });
				}
				else
				{
					tri.AddRange(new[] { i0, i1, i2 });
				}
			}
			AddTriangle(0, 1, 2);
			AddTriangle(0, 2, 3);
			mesh.triangles = tri.ToArray();
			mesh.RecalculateBounds();
			return mesh;
		}

		// ---- セットアップ ----

		private struct Setup
		{
			public GameObject BodyGo;
			public Renderer Body;
			public GameObject CostumeGo;
			public DeformStack Stack;
			public BodyFitDeformer Fit;
			public Mesh Source;
		}

		/// <summary>静的な立方体の体と、指定頂点を持つ衣装スタックを作る</summary>
		private Setup CreateSetup(Vector3[] costumeVertices, int[] costumeTriangles = null)
		{
			_root = new GameObject("Avatar");

			var bodyGo = new GameObject("BodyMesh");
			bodyGo.transform.SetParent(_root.transform, false);
			bodyGo.AddComponent<MeshFilter>().sharedMesh = Track(MakeCube());
			var body = bodyGo.AddComponent<MeshRenderer>();

			var costumeGo = new GameObject("Costume");
			costumeGo.transform.SetParent(_root.transform, false);
			costumeGo.AddComponent<MeshFilter>();
			costumeGo.AddComponent<MeshRenderer>();
			var stack = costumeGo.AddComponent<DeformStack>();

			var fitGo = new GameObject("BodyFit");
			fitGo.transform.SetParent(costumeGo.transform, false);
			var fit = fitGo.AddComponent<BodyFitDeformer>();
			fit.Body = body;
			fit.Region = BodyFitDeformer.FitRegion.WholeMesh;
			fit.SmoothIterations = 0;
			fit.SearchDistance = 1f;
			fit.MinGap = 0.02f;
			fit.MaxGap = 0.02f;
			fit.PullIn = true;
			fit.Factor = 1f;
			stack.AddDeformer(fit);

			var source = Track(new Mesh { vertices = costumeVertices });
			if (costumeTriangles != null)
				source.triangles = costumeTriangles;

			return new Setup
			{
				BodyGo = bodyGo, Body = body, CostumeGo = costumeGo, Stack = stack, Fit = fit, Source = source,
			};
		}

		private Vector3[] Bake(in Setup s)
		{
			if (_baked != null)
				Object.DestroyImmediate(_baked);
			_baked = DeformBakeCore.Bake(s.Stack, s.Source, s.CostumeGo.transform);
			Assert.That(_baked, Is.Not.Null);
			return _baked.vertices;
		}

		private static void AssertNear(Vector3 actual, Vector3 expected, string message, float tolerance = 1e-4f)
		{
			Assert.That(Vector3.Distance(actual, expected), Is.LessThan(tolerance),
				$"{message}: expected {expected} but was {actual}");
		}

		// ---- 基本動作 ----

		[Test]
		public void Fit_PushesInsideVertexOutToMinGap()
		{
			var s = CreateSetup(new[] { new Vector3(0.45f, 0f, 0f) });
			var v = Bake(s);
			AssertNear(v[0], new Vector3(0.52f, 0f, 0f), "めり込んだ頂点は minGap まで押し出される");
		}

		[Test]
		public void Fit_PullsFarVertexInToMaxGap()
		{
			var s = CreateSetup(new[] { new Vector3(0.8f, 0f, 0f) });
			var v = Bake(s);
			AssertNear(v[0], new Vector3(0.52f, 0f, 0f), "離れた頂点は maxGap まで引き寄せられる");
		}

		[Test]
		public void Fit_BandKeepsVerticesInsideBand()
		{
			var s = CreateSetup(new[] { new Vector3(0.55f, 0f, 0f), new Vector3(0.8f, 0f, 0f) });
			s.Fit.MinGap = 0.02f;
			s.Fit.MaxGap = 0.1f;
			var v = Bake(s);
			AssertNear(v[0], new Vector3(0.55f, 0f, 0f), "帯の中の頂点は動かない");
			AssertNear(v[1], new Vector3(0.6f, 0f, 0f), "帯の外の頂点は maxGap まで引き寄せられる");
		}

		[Test]
		public void Fit_PullInDisabledOnlyPushesOut()
		{
			var s = CreateSetup(new[] { new Vector3(0.8f, 0f, 0f), new Vector3(0.45f, 0f, 0f) });
			s.Fit.PullIn = false;
			var v = Bake(s);
			AssertNear(v[0], new Vector3(0.8f, 0f, 0f), "pullIn 無効では離れた頂点は動かない");
			AssertNear(v[1], new Vector3(0.52f, 0f, 0f), "めり込みは pullIn 無効でも押し出される");
		}

		[Test]
		public void Fit_RespectsSearchDistance()
		{
			var s = CreateSetup(new[] { new Vector3(0.8f, 0f, 0f), new Vector3(0.78f, 0f, 0f) });
			s.Fit.SearchDistance = 0.1f;
			var v = Bake(s);
			AssertNear(v[0], new Vector3(0.8f, 0f, 0f), "探索距離より遠い頂点は対象外");

			// 上限の 75%〜100% では滑らかに効きが減る:
			// 距離 0.28 / 上限 0.32(減衰開始 0.24)→ t = 0.5 → 1 − smoothstep = 0.5 → 目標 0.52 との中点
			s.Fit.SearchDistance = 0.32f;
			v = Bake(s);
			AssertNear(v[1], new Vector3(0.65f, 0f, 0f), "探索距離の境界付近では部分的に効く", 1e-3f);
		}

		[Test]
		public void Fit_FactorScalesDisplacement()
		{
			var s = CreateSetup(new[] { new Vector3(0.8f, 0f, 0f) });
			s.Fit.Factor = 0.5f;
			var v = Bake(s);
			AssertNear(v[0], new Vector3(0.66f, 0f, 0f), "factor 0.5 で目標までの半分だけ動く");
		}

		[Test]
		public void Fit_SphereRegionExcludesOutsideAndFadesBetween()
		{
			var s = CreateSetup(new[]
			{
				new Vector3(0.55f, 0f, 0f),  // 軸から 0.55 < inner → 100%
				new Vector3(0.8f, 0f, 0f),   // 軸から 0.8 = inner/outer の中点 → 50%
				new Vector3(0f, 0f, 1.2f),   // 軸から 1.2 > outer → 0%
			});
			s.Fit.Region = BodyFitDeformer.FitRegion.Sphere;
			s.Fit.InnerRadius = 0.6f;
			s.Fit.OuterRadius = 1.0f;
			var v = Bake(s);
			AssertNear(v[0], new Vector3(0.52f, 0f, 0f), "内半径の内側は 100%");
			AssertNear(v[1], new Vector3(0.66f, 0f, 0f), "減衰帯の中点は 50%");
			AssertNear(v[2], new Vector3(0f, 0f, 1.2f), "外半径の外側は動かない");
		}

		[Test]
		public void Fit_NoBodyIsNoOp()
		{
			var s = CreateSetup(new[] { new Vector3(0.8f, 0f, 0f) });
			s.Fit.Body = null;
			var v = Bake(s);
			AssertNear(v[0], new Vector3(0.8f, 0f, 0f), "Body 未設定では何もしない");
		}

		[Test]
		public void Fit_SelfReferenceIsNoOp()
		{
			var s = CreateSetup(new[] { new Vector3(0.8f, 0f, 0f) });
			s.Fit.Body = s.CostumeGo.GetComponent<MeshRenderer>();
			var v = Bake(s);
			AssertNear(v[0], new Vector3(0.8f, 0f, 0f), "衣装自身を参照している場合は何もしない");
		}

		[Test]
		public void Fit_MaskAfterRestoresOriginal()
		{
			var s = CreateSetup(new[] { new Vector3(0.8f, 0f, 0f), new Vector3(0f, 0.8f, 0f) });

			// 頂点 0 の位置に球マスク(実効半径 0.1)を置くと、頂点 0 だけ元に戻る
			var maskGo = new GameObject("Mask");
			maskGo.transform.SetParent(s.CostumeGo.transform, false);
			maskGo.transform.position = new Vector3(0.52f, 0f, 0f);
			var mask = maskGo.AddComponent<SphereMaskDeformer>();
			mask.InnerRadius = 0.2f;
			mask.OuterRadius = 0.2f;
			s.Stack.AddDeformer(mask);

			var v = Bake(s);
			AssertNear(v[0], new Vector3(0.8f, 0f, 0f), "後段のマスクで元の形に戻る");
			AssertNear(v[1], new Vector3(0f, 0.52f, 0f), "マスク外はフィットしたまま");
		}

		// ---- ブレンドシェイプ ----

		[Test]
		public void Fit_FixedDisplacementPreservesCostumeBlendShape()
		{
			// 2 頂点目は探索圏外の固定点(バウンズを 0 にしないため。1 頂点だと非線形判定の閾値が 0 になる)
			var s = CreateSetup(new[] { new Vector3(0.8f, 0f, 0f), new Vector3(3f, 0f, 0f) });
			s.Source.AddBlendShapeFrame("puff", 100f, new[] { new Vector3(0.3f, 0f, 0f), Vector3.zero }, null, null);
			s.Fit.BlendShapes = BodyFitDeformer.BlendShapeFitMode.FixedDisplacement;

			var v = Bake(s);
			AssertNear(v[0], new Vector3(0.52f, 0f, 0f), "基本形状はフィットする");

			var last = _baked.GetBlendShapeFrameCount(0) - 1;
			var dv = new Vector3[2];
			_baked.GetBlendShapeFrameVertices(0, last, dv, null, null);
			AssertNear(dv[0], new Vector3(0.3f, 0f, 0f), "FixedDisplacement では衣装のシェイプデルタが維持される");
			Assert.That(_baked.GetBlendShapeFrameCount(0), Is.EqualTo(1), "変位が一定なので中間フレームは増えない");
		}

		[Test]
		public void Fit_RefitEachFrameFlattensCostumeBlendShape()
		{
			var s = CreateSetup(new[] { new Vector3(0.8f, 0f, 0f) });
			s.Source.AddBlendShapeFrame("puff", 100f, new[] { new Vector3(0.3f, 0f, 0f) }, null, null);
			s.Fit.BlendShapes = BodyFitDeformer.BlendShapeFitMode.RefitEachFrame;

			Bake(s);

			var last = _baked.GetBlendShapeFrameCount(0) - 1;
			var dv = new Vector3[1];
			_baked.GetBlendShapeFrameVertices(0, last, dv, null, null);
			Assert.That(dv[0].magnitude, Is.LessThan(1e-4f), "RefitEachFrame ではシェイプ後の形状も体へ再フィットされる");
		}

		// ---- 平滑化 ----

		[Test]
		public void Fit_SmoothingKeepsSeamVerticesCoincident()
		{
			// +X 面の外側に、辺で接する 2 枚の四角形(接する辺の頂点はシームで分割 = 1,2 と 4,5 が同位置)
			var vertices = new[]
			{
				new Vector3(0.60f, -0.1f, -0.1f), new Vector3(0.65f, 0f, -0.1f),
				new Vector3(0.65f, 0f, 0.1f), new Vector3(0.60f, -0.1f, 0.1f),
				new Vector3(0.65f, 0f, -0.1f), new Vector3(0.65f, 0f, 0.1f),
				new Vector3(0.80f, 0.1f, -0.1f), new Vector3(0.80f, 0.1f, 0.1f),
			};
			var triangles = new[] { 0, 2, 1, 0, 3, 2, 4, 5, 6, 5, 7, 6 };

			var s = CreateSetup(vertices, triangles);
			s.Fit.SmoothIterations = 0;
			var plain = (Vector3[])Bake(s).Clone();
			AssertNear(plain[6], new Vector3(0.52f, 0.1f, -0.1f), "平滑化なしでは目標へそのまま移動する");

			s.Fit.SmoothIterations = 1;
			s.Fit.SmoothStrength = 0.5f;
			s.Fit.EnforceMinGap = true;
			var smoothed = Bake(s);

			// 頂点 6 の変位 −0.28 は隣接(−0.13, −0.13, −0.28)の平均 −0.18 へ半分寄って −0.23 になる
			AssertNear(smoothed[6], new Vector3(0.57f, 0.1f, -0.1f), "平滑化で隣接の変位へ寄る", 1e-3f);
			AssertNear(smoothed[1], smoothed[4], "シームで分割された頂点は平滑化後も一致する", 1e-6f);
			AssertNear(smoothed[2], smoothed[5], "シームで分割された頂点は平滑化後も一致する", 1e-6f);

			// 平滑化で minGap を割った頂点は enforce で押し戻される(体の内側には残らない)
			for (var i = 0; i < smoothed.Length; i++)
				Assert.That(smoothed[i].x, Is.GreaterThanOrEqualTo(0.52f - 1e-4f), $"vertex {i} が minGap を割っている");
		}

		// ---- スキン済みの体 ----

		/// <summary>1 ボーンの SkinnedMeshRenderer の立方体を体として作る(バインド時は原点)</summary>
		private SkinnedMeshRenderer CreateSkinnedBody(out Transform bone)
		{
			var boneGo = new GameObject("Hips");
			boneGo.transform.SetParent(_root.transform, false);
			bone = boneGo.transform;

			var bodyGo = new GameObject("Body");
			bodyGo.transform.SetParent(_root.transform, false);
			var smr = bodyGo.AddComponent<SkinnedMeshRenderer>();
			var mesh = Track(MakeCube());
			var weights = new BoneWeight[mesh.vertexCount];
			for (var i = 0; i < weights.Length; i++)
				weights[i] = new BoneWeight { boneIndex0 = 0, weight0 = 1f };
			mesh.boneWeights = weights;
			mesh.bindposes = new[] { Matrix4x4.identity };
			smr.bones = new[] { bone };
			smr.rootBone = bone;
			smr.sharedMesh = mesh;
			return smr;
		}

		[Test]
		public void Fit_SkinnedBodyFollowsBonePose()
		{
			var s = CreateSetup(new[] { new Vector3(0.8f, 0.6f, 0f) });
			var smr = CreateSkinnedBody(out var bone);
			s.Fit.Body = smr;

			// ボーンを +Y 0.3 動かすと体は y ∈ [-0.2, 0.8] に移る → (0.8,0.6,0) の最近接は +X 面上
			bone.position = new Vector3(0f, 0.3f, 0f);
			var v = Bake(s);
			AssertNear(v[0], new Vector3(0.52f, 0.6f, 0f), "スキン後(ボーン移動後)の体にフィットする");
		}

		[Test]
		public void Fit_UsesBodyBlendShapeWeights()
		{
			var s = CreateSetup(new[] { new Vector3(1.0f, 0f, 0f) });
			var smr = CreateSkinnedBody(out _);
			var mesh = smr.sharedMesh;
			// +X 面(x = 0.5)の頂点を +0.2 動かすシェイプ
			var deltas = new Vector3[mesh.vertexCount];
			var vertices = mesh.vertices;
			for (var i = 0; i < deltas.Length; i++)
				deltas[i] = vertices[i].x > 0f ? new Vector3(0.2f, 0f, 0f) : Vector3.zero;
			mesh.AddBlendShapeFrame("grow", 100f, deltas, null, null);
			smr.SetBlendShapeWeight(0, 100f);
			s.Fit.Body = smr;

			s.Fit.UseBodyBlendShapes = true;
			var v = Bake(s);
			AssertNear(v[0], new Vector3(0.72f, 0f, 0f), "体のシェイプ重みを反映した形状にフィットする");

			s.Fit.UseBodyBlendShapes = false;
			v = Bake(s);
			AssertNear(v[0], new Vector3(0.52f, 0f, 0f), "シェイプを無視すると基本形状にフィットする");
		}

		// ---- 重ね着(参照先に DeformStack) ----

		[Test]
		public void Fit_LayeredCostumeFitsToDeformedReference()
		{
			// シャツ: x = 0.6 の四角形を +X 0.5 動かすスタック。ジャケット: シャツを参照する頂点
			var s = CreateSetup(new[] { new Vector3(1.5f, 0f, 0f) });

			var shirtGo = new GameObject("Shirt");
			shirtGo.transform.SetParent(_root.transform, false);
			shirtGo.AddComponent<MeshFilter>().sharedMesh = Track(MakeQuadFacingX(0.6f, 0.5f));
			var shirt = shirtGo.AddComponent<MeshRenderer>();
			var shirtStack = shirtGo.AddComponent<DeformStack>();
			var translateGo = new GameObject("Translate");
			translateGo.transform.SetParent(shirtGo.transform, false);
			shirtStack.AddDeformer(translateGo.AddComponent<TestTranslateXDeformer>());

			s.Fit.Body = shirt;
			s.Fit.SearchDistance = 2f;
			var v = Bake(s);
			AssertNear(v[0], new Vector3(1.12f, 0f, 0f), "参照先の変形後(x = 1.1)の形状へフィットする");

			// 参照先のデフォーマを外す(参照が null になる)と変形前(x = 0.6)へフィットする
			Object.DestroyImmediate(translateGo);
			v = Bake(s);
			AssertNear(v[0], new Vector3(0.62f, 0f, 0f), "参照先に有効なデフォーマが無ければ元の形状へフィットする");
		}

		// ---- キャッシュ ----

		[Test]
		public void Cache_ReusesSurfaceWhileBodyUnchanged()
		{
			var s = CreateSetup(new[] { new Vector3(0.8f, 0f, 0f) });
			var before = ReferenceSurfaceCache.BuildCount;
			Bake(s);
			Bake(s);
			Assert.That(ReferenceSurfaceCache.BuildCount - before, Is.EqualTo(1), "体が変わらなければ表面データは再構築されない");

			// 体の Transform を動かすと作り直される
			s.BodyGo.transform.position = new Vector3(0f, 0f, 0.1f);
			Bake(s);
			Assert.That(ReferenceSurfaceCache.BuildCount - before, Is.EqualTo(2), "体が動いたら再構築される");
		}

		[Test]
		public void Fit_MirroredBodyKeepsOutwardNormals()
		{
			// 負のスケール(鏡映)で巻き順が反転しても、法線は自動で外向きに戻る
			var s = CreateSetup(new[] { new Vector3(0.8f, 0f, 0f), new Vector3(0.45f, 0f, 0f) });
			s.BodyGo.transform.localScale = new Vector3(-1f, 1f, 1f);
			var v = Bake(s);
			AssertNear(v[0], new Vector3(0.52f, 0f, 0f), "鏡映された体でも外側へフィットする");
			AssertNear(v[1], new Vector3(0.52f, 0f, 0f), "鏡映された体でもめり込みは外へ押し出される");
		}

		// ---- パーツ円柱モード ----

		/// <summary>
		/// 底面中心 baseCenter・半径 radius・高さ height の閉じた円柱(法線外向き)を頂点 / 三角形に追加する。
		/// 追加した頂点は全て boneIndex にウェイト 1 で割り当てる。
		/// </summary>
		private static void AppendCylinder(List<Vector3> vertices, List<int> triangles, List<BoneWeight> weights,
			Vector3 baseCenter, float radius, float height, int boneIndex, int segments = 24, int rings = 8)
		{
			var start = vertices.Count;
			for (var ring = 0; ring <= rings; ring++)
			{
				var y = baseCenter.y + height * ring / rings;
				for (var seg = 0; seg < segments; seg++)
				{
					var angle = 2f * Mathf.PI * seg / segments;
					vertices.Add(new Vector3(baseCenter.x + radius * Mathf.Cos(angle), y, baseCenter.z + radius * Mathf.Sin(angle)));
				}
			}
			var bottomCenter = vertices.Count;
			vertices.Add(new Vector3(baseCenter.x, baseCenter.y, baseCenter.z));
			var topCenter = vertices.Count;
			vertices.Add(new Vector3(baseCenter.x, baseCenter.y + height, baseCenter.z));
			for (var i = start; i < vertices.Count; i++)
				weights.Add(new BoneWeight { boneIndex0 = boneIndex, weight0 = 1f });

			void AddTriangle(int a, int b, int c, Vector3 outward)
			{
				var n = Vector3.Cross(vertices[b] - vertices[a], vertices[c] - vertices[a]);
				if (Vector3.Dot(n, outward) < 0f)
				{
					var tmp = b;
					b = c;
					c = tmp;
				}
				triangles.Add(a);
				triangles.Add(b);
				triangles.Add(c);
			}

			for (var ring = 0; ring < rings; ring++)
			for (var seg = 0; seg < segments; seg++)
			{
				var a = start + ring * segments + seg;
				var b = start + ring * segments + (seg + 1) % segments;
				var c = a + segments;
				var d = b + segments;
				var outward = vertices[a] - new Vector3(baseCenter.x, vertices[a].y, baseCenter.z);
				AddTriangle(a, b, d, outward);
				AddTriangle(a, d, c, outward);
			}
			for (var seg = 0; seg < segments; seg++)
			{
				var a = start + seg;
				var b = start + (seg + 1) % segments;
				AddTriangle(bottomCenter, a, b, Vector3.down);
				var ta = start + rings * segments + seg;
				var tb = start + rings * segments + (seg + 1) % segments;
				AddTriangle(topCenter, ta, tb, Vector3.up);
			}
		}

		private struct PartSetup
		{
			public SkinnedMeshRenderer Body;
			public GameObject CostumeGo;
			public DeformStack Stack;
			public BodyFitDeformer Fit;
			public Mesh Source;
		}

		/// <summary>
		/// 体 = 胴の円柱(軸 x=0、半径 0.2、y 0〜1.4)+ 左上腕の円柱(軸 x=0.35、半径 0.06、y 0.6〜1.3)。
		/// 骨格 = Hips(0,0,0) → Neck(0,1.4,0)、LeftUpperArm(0.35,1.3,0) → LeftLowerArm(0.35,0.6,0)。
		/// 衣装 = 指定頂点(ボーン 0 = Hips、1 = LeftUpperArm にウェイト)。両方とも同じボーンを使う。
		/// </summary>
		private PartSetup CreatePartSetup(Vector3[] costumeVertices, int[] costumeBones)
		{
			_root = new GameObject("Avatar");
			var hips = new GameObject("Hips").transform;
			hips.SetParent(_root.transform, false);
			hips.position = new Vector3(0f, 0f, 0f);
			var neck = new GameObject("Neck").transform;
			neck.SetParent(hips, false);
			neck.position = new Vector3(0f, 1.4f, 0f);
			var upperArm = new GameObject("LeftUpperArm").transform;
			upperArm.SetParent(neck, false);
			upperArm.position = new Vector3(0.35f, 1.3f, 0f);
			var lowerArm = new GameObject("LeftLowerArm").transform;
			lowerArm.SetParent(upperArm, false);
			lowerArm.position = new Vector3(0.35f, 0.6f, 0f);
			var bones = new[] { hips, upperArm };
			var bindposes = new[] { hips.worldToLocalMatrix, upperArm.worldToLocalMatrix };

			var skeleton = HumanoidSkeleton.FromBones(new Dictionary<HumanBodyBones, Transform>
			{
				{ HumanBodyBones.Hips, hips },
				{ HumanBodyBones.Neck, neck },
				{ HumanBodyBones.LeftUpperArm, upperArm },
				{ HumanBodyBones.LeftLowerArm, lowerArm },
			});

			// 体
			var bv = new List<Vector3>();
			var bt = new List<int>();
			var bw = new List<BoneWeight>();
			AppendCylinder(bv, bt, bw, new Vector3(0f, 0f, 0f), 0.2f, 1.4f, 0);
			AppendCylinder(bv, bt, bw, new Vector3(0.35f, 0.6f, 0f), 0.06f, 0.7f, 1);
			var bodyMesh = Track(new Mesh { vertices = bv.ToArray(), triangles = bt.ToArray() });
			bodyMesh.boneWeights = bw.ToArray();
			bodyMesh.bindposes = bindposes;
			bodyMesh.RecalculateBounds();
			var bodyGo = new GameObject("Body");
			bodyGo.transform.SetParent(_root.transform, false);
			var body = bodyGo.AddComponent<SkinnedMeshRenderer>();
			body.bones = bones;
			body.rootBone = hips;
			body.sharedMesh = bodyMesh;

			// 衣装
			var source = Track(new Mesh { vertices = costumeVertices });
			var cw = new BoneWeight[costumeVertices.Length];
			for (var i = 0; i < cw.Length; i++)
				cw[i] = new BoneWeight { boneIndex0 = costumeBones[i], weight0 = 1f };
			source.boneWeights = cw;
			source.bindposes = bindposes;
			source.RecalculateBounds();
			var costumeGo = new GameObject("Costume");
			costumeGo.transform.SetParent(_root.transform, false);
			var costume = costumeGo.AddComponent<SkinnedMeshRenderer>();
			costume.bones = bones;
			costume.rootBone = hips;
			costume.sharedMesh = source;
			var stack = costumeGo.AddComponent<DeformStack>();

			var fitGo = new GameObject("BodyFit");
			fitGo.transform.SetParent(costumeGo.transform, false);
			var fit = fitGo.AddComponent<BodyFitDeformer>();
			fit.Body = body;
			fit.SkeletonOverride = skeleton;
			fit.Mode = BodyFitDeformer.FitMode.PartCylinder;
			fit.Region = BodyFitDeformer.FitRegion.WholeMesh;
			fit.SmoothIterations = 0;
			fit.SearchDistance = 1f;
			fit.MinGap = 0.02f;
			fit.MaxGap = 0.02f;
			fit.PullIn = true;
			fit.Factor = 1f;
			fit.DecorationGrouping = BodyFitDeformer.PartGrouping.None;
			stack.AddDeformer(fit);

			return new PartSetup { Body = body, CostumeGo = costumeGo, Stack = stack, Fit = fit, Source = source };
		}

		private Vector3[] BakePart(in PartSetup s)
		{
			if (_baked != null)
				Object.DestroyImmediate(_baked);
			_baked = DeformBakeCore.Bake(s.Stack, s.Source, s.CostumeGo.transform);
			Assert.That(_baked, Is.Not.Null);
			return _baked.vertices;
		}

		[Test]
		public void PartCylinder_MovesRadiallyPerPartAndKeepsDecorationOffset()
		{
			// 0: 袖の内層(腕軸から r=0.10)、1: 袖の装飾(r=0.14、同じ h / θ)、
			// 2: 腕と胴の間の紐(腕軸の −X 側 r=0.14。胴の壁 x=0.2 のすぐ外側)、3: 胴の衣装(x=0.25)
			var s = CreatePartSetup(new[]
			{
				new Vector3(0.45f, 0.95f, 0f), new Vector3(0.49f, 0.95f, 0f),
				new Vector3(0.21f, 0.95f, 0f), new Vector3(0.25f, 0.7f, 0f),
			}, new[] { 1, 1, 1, 0 });

			var v = BakePart(s);
			Assert.That(s.Fit.PartsAvailable, Is.True);
			Assert.That(s.Fit.EffectiveMode, Is.EqualTo(BodyFitDeformer.FitMode.PartCylinder));

			// 腕の半径 0.06 + 隙間 0.02 = 0.08 へ、腕軸(x=0.35)基準で放射状に動く
			AssertNear(v[0], new Vector3(0.43f, 0.95f, 0f), "袖の内層は腕の半径 + 隙間へ", 2e-3f);
			AssertNear(v[1], new Vector3(0.47f, 0.95f, 0f), "装飾は内層との相対オフセット(0.04)を保つ", 2e-3f);
			AssertNear(v[2], new Vector3(0.27f, 0.95f, 0f), "腕の装飾は胴ではなく腕の軸基準で動く", 2e-3f);
			AssertNear(v[3], new Vector3(0.22f, 0.7f, 0f), "胴の衣装は胴の半径 + 隙間へ", 2e-3f);
		}

		[Test]
		public void NearestSurface_PartFilterKeepsArmDecorationOnArm()
		{
			var s = CreatePartSetup(new[]
			{
				new Vector3(0.21f, 0.95f, 0f), new Vector3(0.49f, 0.95f, 0f),
			}, new[] { 1, 1 });
			s.Fit.Mode = BodyFitDeformer.FitMode.NearestSurface;

			// パーツ制限なし: 最寄りの胴の壁(x=0.2)へ押し出され、装飾は腕の表面へ潰れる
			s.Fit.PartFilter = false;
			var v = BakePart(s);
			Assert.That(s.Fit.EffectiveMode, Is.EqualTo(BodyFitDeformer.FitMode.NearestSurface));
			AssertNear(v[0], new Vector3(0.22f, 0.95f, 0f), "制限なしでは胴に吸われる", 2e-3f);
			AssertNear(v[1], new Vector3(0.43f, 0.95f, 0f), "制限なしでは装飾が潰れる", 2e-3f);

			// パーツ制限あり: 腕の三角形だけを探すので腕の表面(x=0.29)基準になる
			s.Fit.PartFilter = true;
			v = BakePart(s);
			AssertNear(v[0], new Vector3(0.27f, 0.95f, 0f), "腕の頂点は腕の表面へ", 2e-3f);
		}

		[Test]
		public void PartCylinder_FallsBackWithoutSkeleton()
		{
			var s = CreatePartSetup(new[] { new Vector3(0.45f, 0.95f, 0f) }, new[] { 1 });
			s.Fit.SkeletonOverride = null; // Animator が無いので骨格なし

			var v = BakePart(s);
			Assert.That(s.Fit.PartsAvailable, Is.False);
			Assert.That(s.Fit.EffectiveMode, Is.EqualTo(BodyFitDeformer.FitMode.NearestSurface));
			// 最近接(腕の壁 x=0.41 から 0.04)→ 隙間 0.02 へ
			AssertNear(v[0], new Vector3(0.43f, 0.95f, 0f), "骨格が無ければ最近接表面でフィットする", 2e-3f);
		}

		[Test]
		public void PartCylinder_DecorationGroupingUnifiesSmallComponents()
		{
			// 紐の 3 頂点(小さな連結成分)のうち 1 つだけ胴にウェイトがあっても、多数決で腕に揃う
			var s = CreatePartSetup(new[]
			{
				new Vector3(0.21f, 0.95f, 0f), new Vector3(0.21f, 0.97f, 0f), new Vector3(0.21f, 0.99f, 0f),
			}, new[] { 1, 1, 0 });
			s.Source.triangles = new[] { 0, 1, 2 };
			s.Fit.DecorationGrouping = BodyFitDeformer.PartGrouping.ConnectedComponents;
			s.Fit.DecorationMaxSize = 0.25f;

			var v = BakePart(s);
			for (var i = 0; i < 3; i++)
				Assert.That(v[i].x, Is.EqualTo(0.27f).Within(3e-3f), $"vertex {i} は腕の軸基準で動く");
		}

		// ---- テスト用デフォーマ ----

		/// <summary>全頂点を +X に 0.5 動かす</summary>
		private class TestTranslateXDeformer : DeformerBase
		{
			public override DeformDataFlags DataFlags => DeformDataFlags.Vertices;

			public override JobHandle Schedule(in MeshBuffers buffers, in DeformSpace space, JobHandle dependency)
			{
				return new TranslateJob { vertices = buffers.Vertices }
					.Schedule(buffers.Length, 64, dependency);
			}

			private struct TranslateJob : IJobParallelFor
			{
				public NativeArray<float3> vertices;

				public void Execute(int index)
				{
					vertices[index] += new float3(0.5f, 0f, 0f);
				}
			}
		}
	}
}
