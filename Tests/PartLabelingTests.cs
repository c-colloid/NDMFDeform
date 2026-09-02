using System.Collections.Generic;
using MeshModifier.NDMFDeform.Core;
using NUnit.Framework;
using UnityEngine;

namespace MeshModifier.NDMFDeform.Tests
{
	/// <summary>
	/// PartLabeler(グループ単位の投票・信頼度による混合・上書き・縫い目の混合)の単体検証。
	/// 体の形状証拠(PartGeometryJob)は BodyFitDeformerTests の円柱体で検証する。
	/// </summary>
	public class PartLabelingTests
	{
		private static PartWeights Single(BodyPart part) => PartWeights.Single(part);

		private static PartWeights Pair(BodyPart a, float wa, BodyPart b, float wb)
		{
			var total = wa + wb;
			return new PartWeights
			{
				Parts = new Unity.Mathematics.int4((int)a, (int)b, 0, 0),
				Weights = new Unity.Mathematics.float4(wa / total, wb / total, 0f, 0f),
			};
		}

		private static Vector3[] Line(int count, float spacing)
		{
			var v = new Vector3[count];
			for (var i = 0; i < count; i++)
				v[i] = new Vector3(i * spacing, 0f, 0f);
			return v;
		}

		[Test]
		public void Label_ConfidentBoneWeightsUnifyGroup()
		{
			// 3 頂点の小さなグループ: ウェイトは腕 2 : 胴 1、形状は胴 3 → 信頼度 1 のウェイトが勝つ
			var reports = new List<PartGroupReport>();
			var result = PartLabeler.Label(new PartLabelInput
			{
				Vertices = Line(3, 0.01f),
				BoneWeights = new[] { Single(BodyPart.LeftUpperArm), Single(BodyPart.LeftUpperArm), Single(BodyPart.Torso) },
				BoneConfidence = new[] { 1f, 1f, 1f },
				GeometryWeights = new[] { Single(BodyPart.Torso), Single(BodyPart.Torso), Single(BodyPart.Torso) },
				GroupOfVertex = new[] { 0, 0, 0 },
				GroupCount = 1,
				DecorationMaxSize = 0.25f,
				ConfidenceThreshold = 0.7f,
			}, reports);

			for (var i = 0; i < 3; i++)
			{
				Assert.That(result[i].Parts.x, Is.EqualTo((int)BodyPart.LeftUpperArm), $"vertex {i}");
				Assert.That(result[i].Weights.x, Is.EqualTo(1f).Within(1e-5f));
			}
			Assert.That(reports, Has.Count.EqualTo(1));
			var r = reports[0];
			Assert.That(r.Decision, Is.EqualTo(PartDecision.Unified));
			Assert.That(r.Part, Is.EqualTo(BodyPart.LeftUpperArm));
			Assert.That(r.BonePart, Is.EqualTo(BodyPart.LeftUpperArm));
			Assert.That(r.GeometryPart, Is.EqualTo(BodyPart.Torso));
			Assert.That(r.NeedsReview, Is.True, "ウェイトと形状が食い違うので要確認");
			Assert.That(r.BoneMapConfidence, Is.EqualTo(1f).Within(1e-5f));
		}

		[Test]
		public void Label_LowMappingConfidenceLetsGeometryWin()
		{
			// 対応付けが最寄り区間頼み(信頼度 0.25)のウェイト = 胴、形状 = 腕 → 腕 0.75 : 胴 0.25
			var reports = new List<PartGroupReport>();
			var result = PartLabeler.Label(new PartLabelInput
			{
				Vertices = Line(2, 0.01f),
				BoneWeights = new[] { Single(BodyPart.Torso), Single(BodyPart.Torso) },
				BoneConfidence = new[] { 0.25f, 0.25f },
				GeometryWeights = new[] { Single(BodyPart.LeftUpperArm), Single(BodyPart.LeftUpperArm) },
				GroupOfVertex = new[] { 0, 0 },
				GroupCount = 1,
				DecorationMaxSize = 0f, // 大きさによる統一を無効化して比率で判定させる
				ConfidenceThreshold = 0.7f,
			}, reports);

			Assert.That(reports[0].Part, Is.EqualTo(BodyPart.LeftUpperArm));
			Assert.That(reports[0].Confidence, Is.EqualTo(0.75f).Within(1e-4f));
			Assert.That(reports[0].Decision, Is.EqualTo(PartDecision.Unified));
			Assert.That(reports[0].BoneMapConfidence, Is.EqualTo(0.25f).Within(1e-5f));
			Assert.That(result[0].Parts.x, Is.EqualTo((int)BodyPart.LeftUpperArm));
		}

		[Test]
		public void Label_NoGroupingMixesPerVertex()
		{
			var result = PartLabeler.Label(new PartLabelInput
			{
				Vertices = Line(1, 0.01f),
				BoneWeights = new[] { Single(BodyPart.Torso) },
				BoneConfidence = new[] { 0.5f },
				GeometryWeights = new[] { Single(BodyPart.LeftUpperArm) },
			}, null);

			// 0.5 : 0.5 の混合(上位はどちらでもよいが、両方が半分ずつ)
			Assert.That(result[0].Weights.x, Is.EqualTo(0.5f).Within(1e-5f));
			Assert.That(result[0].Weights.y, Is.EqualTo(0.5f).Within(1e-5f));
			Assert.That(result[0].Mask(0.4f), Is.EqualTo((1 << (int)BodyPart.Torso) | (1 << (int)BodyPart.LeftUpperArm)));
		}

		[Test]
		public void Label_LargeMixedGroupStaysPerVertexAndNeedsReview()
		{
			// 腕 2 頂点 + 胴 2 頂点(対角 0.4 m > 0.25)→ 比率 0.5 で頂点ごとのまま
			var reports = new List<PartGroupReport>();
			var result = PartLabeler.Label(new PartLabelInput
			{
				Vertices = new[] { new Vector3(0f, 0f, 0f), new Vector3(0.1f, 0f, 0f), new Vector3(0.3f, 0f, 0f), new Vector3(0.4f, 0f, 0f) },
				BoneWeights = new[] { Single(BodyPart.LeftUpperArm), Single(BodyPart.LeftUpperArm), Single(BodyPart.Torso), Single(BodyPart.Torso) },
				GroupOfVertex = new[] { 0, 0, 0, 0 },
				GroupCount = 1,
				DecorationMaxSize = 0.25f,
				ConfidenceThreshold = 0.7f,
			}, reports);

			Assert.That(reports[0].Decision, Is.EqualTo(PartDecision.PerVertex));
			Assert.That(reports[0].NeedsReview, Is.True);
			Assert.That(reports[0].Confidence, Is.EqualTo(0.5f).Within(1e-5f));
			Assert.That(result[0].Parts.x, Is.EqualTo((int)BodyPart.LeftUpperArm));
			Assert.That(result[3].Parts.x, Is.EqualTo((int)BodyPart.Torso));
		}

		[Test]
		public void Label_SmallGroupIsUnifiedRegardlessOfRatio()
		{
			var reports = new List<PartGroupReport>();
			var result = PartLabeler.Label(new PartLabelInput
			{
				Vertices = Line(2, 0.01f),
				BoneWeights = new[] { Single(BodyPart.LeftUpperArm), Single(BodyPart.Torso) },
				GroupOfVertex = new[] { 0, 0 },
				GroupCount = 1,
				DecorationMaxSize = 0.25f,
				ConfidenceThreshold = 0.9f,
			}, reports);

			Assert.That(reports[0].Decision, Is.EqualTo(PartDecision.Unified));
			Assert.That(result[0].Parts.x, Is.EqualTo(result[1].Parts.x), "小さな装飾は 1 パーツに揃う");
		}

		[Test]
		public void Label_OverrideWinsAndIsNotFlagged()
		{
			var reports = new List<PartGroupReport>();
			var result = PartLabeler.Label(new PartLabelInput
			{
				Vertices = Line(2, 0.01f),
				BoneWeights = new[] { Single(BodyPart.LeftUpperArm), Single(BodyPart.LeftUpperArm) },
				GeometryWeights = new[] { Single(BodyPart.Torso), Single(BodyPart.Torso) },
				BoneConfidence = new[] { 1f, 1f },
				GroupOfVertex = new[] { 0, 0 },
				GroupCount = 1,
				Overrides = new Dictionary<int, BodyPart> { { 0, BodyPart.RightHand } },
			}, reports);

			Assert.That(reports[0].Decision, Is.EqualTo(PartDecision.Override));
			Assert.That(reports[0].Part, Is.EqualTo(BodyPart.RightHand));
			Assert.That(reports[0].NeedsReview, Is.False);
			Assert.That(result[0].Parts.x, Is.EqualTo((int)BodyPart.RightHand));
			Assert.That(result[1].Weights.x, Is.EqualTo(1f).Within(1e-5f));
		}

		[Test]
		public void Label_WithoutEvidenceLeavesNone()
		{
			var reports = new List<PartGroupReport>();
			var result = PartLabeler.Label(new PartLabelInput
			{
				Vertices = Line(2, 0.01f),
				GroupOfVertex = new[] { 0, 0 },
				GroupCount = 1,
			}, reports);

			Assert.That(result[0].Parts.x, Is.EqualTo(0));
			Assert.That(reports[0].Decision, Is.EqualTo(PartDecision.Unlabeled));
		}

		[Test]
		public void BlendSeams_UnifiesCoincidentVerticesAndSpreadsAcrossBoundary()
		{
			// 2 三角形: 0-1-2(腕)と 1-2-3(胴)。頂点 4 は頂点 1 と同位置(シーム分割)で胴
			var vertices = new[]
			{
				new Vector3(0f, 0f, 0f), new Vector3(1f, 0f, 0f), new Vector3(0f, 1f, 0f), new Vector3(1f, 1f, 0f),
				new Vector3(1f, 0f, 0f),
			};
			var triangles = new[] { 0, 1, 2, 4, 3, 2 };
			var adjacency = MeshAdjacency.Build(vertices, triangles);
			var weights = new[]
			{
				Single(BodyPart.LeftUpperArm), Single(BodyPart.LeftUpperArm), Single(BodyPart.LeftUpperArm),
				Single(BodyPart.Torso), Single(BodyPart.Torso),
			};

			// 反復 0: 同位置の頂点だけ揃う(1 と 4 は腕 / 胴の平均)
			var once = (PartWeights[])weights.Clone();
			PartLabeler.BlendSeams(once, adjacency, 0);
			Assert.That(once[1].Weights.x, Is.EqualTo(0.5f).Within(1e-5f));
			Assert.That(once[1].Parts.x, Is.EqualTo(once[4].Parts.x));
			Assert.That(once[1].Weights.x, Is.EqualTo(once[4].Weights.x).Within(1e-6f));
			Assert.That(once[0].Weights.x, Is.EqualTo(1f).Within(1e-5f), "境界に接しない… ただし 0 は 1 に接する");

			// 反復 1: 境界に接する頂点が隣接の平均へ寄る(0 は 1(混合)と 2(腕)に接する → 腕が主だが胴も混ざる)
			var blended = (PartWeights[])weights.Clone();
			PartLabeler.BlendSeams(blended, adjacency, 1);
			Assert.That(blended[0].Parts.x, Is.EqualTo((int)BodyPart.LeftUpperArm));
			Assert.That(blended[0].Mask(0.05f) & (1 << (int)BodyPart.Torso), Is.Not.EqualTo(0), "胴の成分が混ざる");
			Assert.That(blended[3].Parts.x, Is.EqualTo((int)BodyPart.Torso));
			Assert.That(blended[3].Mask(0.05f) & (1 << (int)BodyPart.LeftUpperArm), Is.Not.EqualTo(0));
		}

		[Test]
		public void FromBoneWeights_AveragesMappingConfidence()
		{
			var mesh = new Mesh
			{
				vertices = new[] { Vector3.zero, Vector3.right },
				boneWeights = new[]
				{
					new BoneWeight { boneIndex0 = 0, weight0 = 0.5f, boneIndex1 = 1, weight1 = 0.5f },
					new BoneWeight { boneIndex0 = 1, weight0 = 1f },
				},
				bindposes = new[] { Matrix4x4.identity, Matrix4x4.identity },
			};
			try
			{
				var parts = new[] { BodyPart.Torso, BodyPart.LeftUpperArm };
				var result = PartAssignment.FromBoneWeights(mesh, parts, new[] { 1f, 0.25f }, out var confidence);
				Assert.That(confidence[0], Is.EqualTo(0.625f).Within(1e-4f));
				Assert.That(confidence[1], Is.EqualTo(0.25f).Within(1e-4f));
				Assert.That(result[0].Mask(0.4f), Is.EqualTo((1 << (int)BodyPart.Torso) | (1 << (int)BodyPart.LeftUpperArm)));
				Assert.That(result[1].Parts.x, Is.EqualTo((int)BodyPart.LeftUpperArm));
			}
			finally
			{
				Object.DestroyImmediate(mesh);
			}
		}
	}
}
