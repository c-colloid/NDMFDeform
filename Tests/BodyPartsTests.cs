using System.Collections.Generic;
using MeshModifier.NDMFDeform.Core;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;

namespace MeshModifier.NDMFDeform.Tests
{
	/// <summary>
	/// パーツ把握(HumanoidSkeleton / PartAssignment)の検証。
	/// 軸の構築、円柱座標の分解、衣装アーマチュアのボーン対応付け(祖先 / 関節一致 / 親探索 / 最寄り区間)、
	/// ボーンウェイトからのパーツ重み、小さな装飾の多数決、連結成分。
	/// </summary>
	public class BodyPartsTests
	{
		private GameObject _root;

		[TearDown]
		public void TearDown()
		{
			if (_root != null) Object.DestroyImmediate(_root);
			_root = null;
		}

		private Transform Bone(string name, Vector3 position, Transform parent = null)
		{
			if (_root == null)
				_root = new GameObject("SkeletonRoot");
			var go = new GameObject(name);
			go.transform.SetParent(parent != null ? parent : _root.transform, false);
			go.transform.position = position;
			return go.transform;
		}

		/// <summary>T ポーズ風の簡易ヒューマノイド(左半身のみ)</summary>
		private (HumanoidSkeleton skeleton, Dictionary<HumanBodyBones, Transform> bones) CreateSkeleton()
		{
			var hips = Bone("Hips", new Vector3(0f, 0.9f, 0f));
			var spine = Bone("Spine", new Vector3(0f, 1.0f, 0f), hips);
			var chest = Bone("Chest", new Vector3(0f, 1.15f, 0f), spine);
			var neck = Bone("Neck", new Vector3(0f, 1.4f, 0f), chest);
			var head = Bone("Head", new Vector3(0f, 1.5f, 0f), neck);
			var upperArm = Bone("LeftUpperArm", new Vector3(0.2f, 1.35f, 0f), chest);
			var lowerArm = Bone("LeftLowerArm", new Vector3(0.45f, 1.35f, 0f), upperArm);
			var hand = Bone("LeftHand", new Vector3(0.7f, 1.35f, 0f), lowerArm);
			var upperLeg = Bone("LeftUpperLeg", new Vector3(0.1f, 0.85f, 0f), hips);
			var lowerLeg = Bone("LeftLowerLeg", new Vector3(0.1f, 0.45f, 0f), upperLeg);
			var foot = Bone("LeftFoot", new Vector3(0.1f, 0.05f, 0f), lowerLeg);

			var bones = new Dictionary<HumanBodyBones, Transform>
			{
				{ HumanBodyBones.Hips, hips },
				{ HumanBodyBones.Spine, spine },
				{ HumanBodyBones.Chest, chest },
				{ HumanBodyBones.Neck, neck },
				{ HumanBodyBones.Head, head },
				{ HumanBodyBones.LeftUpperArm, upperArm },
				{ HumanBodyBones.LeftLowerArm, lowerArm },
				{ HumanBodyBones.LeftHand, hand },
				{ HumanBodyBones.LeftUpperLeg, upperLeg },
				{ HumanBodyBones.LeftLowerLeg, lowerLeg },
				{ HumanBodyBones.LeftFoot, foot },
			};
			return (HumanoidSkeleton.FromBones(bones), bones);
		}

		[Test]
		public void Axes_FollowBoneSegments()
		{
			var (skeleton, _) = CreateSkeleton();

			var torso = skeleton.Axes[(int)BodyPart.Torso];
			Assert.That(torso.Valid, Is.EqualTo(1));
			Assert.That(math.distance(torso.Origin, new float3(0f, 0.9f, 0f)), Is.LessThan(1e-5f));
			Assert.That(math.distance(torso.Direction, new float3(0f, 1f, 0f)), Is.LessThan(1e-5f));
			Assert.That(torso.Length, Is.EqualTo(0.5f).Within(1e-5f), "Hips → Neck");

			var arm = skeleton.Axes[(int)BodyPart.LeftUpperArm];
			Assert.That(arm.Valid, Is.EqualTo(1));
			Assert.That(math.distance(arm.Direction, new float3(1f, 0f, 0f)), Is.LessThan(1e-5f));
			Assert.That(arm.Length, Is.EqualTo(0.25f).Within(1e-5f));
			Assert.That(math.abs(math.dot(arm.Reference, arm.Direction)), Is.LessThan(1e-5f), "基準方向は軸に直交");
			Assert.That(math.abs(math.dot(arm.Binormal, arm.Direction)), Is.LessThan(1e-5f));

			// 右半身は無いので軸なし
			Assert.That(skeleton.HasAxis(BodyPart.RightUpperArm), Is.False);
			Assert.That(skeleton.HasAxis(BodyPart.LeftHand), Is.True, "手は前腕方向へ延長した軸を持つ");
			Assert.That(skeleton.HasAxis(BodyPart.LeftFoot), Is.True, "つま先が無い足は下向きの軸を持つ");
		}

		[Test]
		public void Decompose_AndRayFrom_RoundTrip()
		{
			var (skeleton, _) = CreateSkeleton();
			var arm = skeleton.Axes[(int)BodyPart.LeftUpperArm];

			var point = arm.Origin + arm.Direction * 0.1f + arm.Reference * 0.05f;
			arm.Decompose(point, out var h, out var theta, out var r, out var dir);
			Assert.That(h, Is.EqualTo(0.4f).Within(1e-5f));
			Assert.That(theta, Is.EqualTo(0f).Within(1e-5f));
			Assert.That(r, Is.EqualTo(0.05f).Within(1e-5f));
			Assert.That(math.distance(dir, arm.Reference), Is.LessThan(1e-5f));

			var point2 = arm.Origin + arm.Direction * 0.2f + arm.Binormal * 0.03f;
			arm.Decompose(point2, out h, out theta, out r, out _);
			Assert.That(h, Is.EqualTo(0.8f).Within(1e-5f));
			Assert.That(theta, Is.EqualTo(math.PI / 2f).Within(1e-5f));
			Assert.That(r, Is.EqualTo(0.03f).Within(1e-5f));

			arm.RayFrom(0.4f, 0f, out var origin, out var direction);
			Assert.That(math.distance(origin, arm.Origin + arm.Direction * 0.1f), Is.LessThan(1e-5f));
			Assert.That(math.distance(direction, arm.Reference), Is.LessThan(1e-5f));
		}

		[Test]
		public void MapBones_ResolvesByAncestorJointParentAndSegment()
		{
			var (skeleton, bones) = CreateSkeleton();

			// 体側: ヒューマノイドボーンの子(胸ボーンなど)は祖先のパーツ
			var breast = Bone("Breast", new Vector3(0.05f, 1.2f, 0.1f), bones[HumanBodyBones.Chest]);

			// 衣装側の独自アーマチュア(アバターとは別階層)
			var costumeRoot = Bone("CostumeArmature", Vector3.zero);
			var costumeHips = Bone("Hips", new Vector3(0f, 0.9f, 0f), costumeRoot);          // 関節一致 → Torso
			var costumeArm = Bone("Arm_L", new Vector3(0.2f, 1.35f, 0f), costumeHips);       // 関節一致 → LeftUpperArm
			var ribbon = Bone("Arm_L_ribbon", new Vector3(0.3f, 1.3f, 0.05f), costumeArm);   // 親探索 → LeftUpperArm
			var skirt = Bone("Skirt", new Vector3(0.15f, 0.7f, 0f), costumeHips);            // 親探索 → Torso(脚ではない)
			var floating = Bone("Floating", new Vector3(0.1f, 0.3f, 0f), costumeRoot);       // 最寄り区間 → LeftLowerLeg

			var mapped = skeleton.MapBones(
				new[] { bones[HumanBodyBones.LeftLowerArm], breast, costumeHips, costumeArm, ribbon, skirt, floating },
				0.03f);

			Assert.That(mapped[0], Is.EqualTo(BodyPart.LeftLowerArm));
			Assert.That(mapped[1], Is.EqualTo(BodyPart.Torso), "胸ボーンは Chest の子 → Torso");
			Assert.That(mapped[2], Is.EqualTo(BodyPart.Torso), "衣装 Hips は関節位置の一致");
			Assert.That(mapped[3], Is.EqualTo(BodyPart.LeftUpperArm), "衣装 Arm_L は関節位置の一致");
			Assert.That(mapped[4], Is.EqualTo(BodyPart.LeftUpperArm), "リボンは衣装内の親をたどる");
			Assert.That(mapped[5], Is.EqualTo(BodyPart.Torso), "スカートは衣装内の親(Hips)をたどる");
			Assert.That(mapped[6], Is.EqualTo(BodyPart.LeftLowerLeg), "対応の無いボーンは最寄りの軸区間");
		}

		[Test]
		public void FromBoneWeights_KeepsTopPartsNormalized()
		{
			var mesh = new Mesh
			{
				vertices = new[] { Vector3.zero, Vector3.one, Vector3.up },
			};
			mesh.boneWeights = new[]
			{
				new BoneWeight { boneIndex0 = 0, weight0 = 0.7f, boneIndex1 = 1, weight1 = 0.3f },
				new BoneWeight { boneIndex0 = 2, weight0 = 1f },
				new BoneWeight { boneIndex0 = 0, weight0 = 0.5f, boneIndex1 = 3, weight1 = 0.5f },
			};
			try
			{
				var boneParts = new[] { BodyPart.LeftUpperArm, BodyPart.LeftLowerArm, BodyPart.None, BodyPart.LeftUpperArm };
				var weights = PartAssignment.FromBoneWeights(mesh, boneParts);

				Assert.That(weights[0].Parts.x, Is.EqualTo((int)BodyPart.LeftUpperArm));
				Assert.That(weights[0].Weights.x, Is.EqualTo(0.7f).Within(1e-4f));
				Assert.That(weights[0].Parts.y, Is.EqualTo((int)BodyPart.LeftLowerArm));
				Assert.That(weights[0].Weights.y, Is.EqualTo(0.3f).Within(1e-4f));
				Assert.That(weights[1].Parts.x, Is.EqualTo(0), "対応の無いボーンだけの頂点は所属なし");
				Assert.That(weights[1].Weights.x, Is.EqualTo(0f));
				// 同じパーツの 2 ボーンは合算される
				Assert.That(weights[2].Parts.x, Is.EqualTo((int)BodyPart.LeftUpperArm));
				Assert.That(weights[2].Weights.x, Is.EqualTo(1f).Within(1e-4f));
				Assert.That(weights[2].Parts.y, Is.EqualTo(0));

				var mask = BodyFitDeformer.PartMaskOf(in weights[0]);
				Assert.That(mask & (1 << (int)BodyPart.LeftUpperArm), Is.Not.EqualTo(0));
				Assert.That(mask & (1 << (int)BodyPart.LeftLowerArm), Is.Not.EqualTo(0));
				Assert.That(BodyFitDeformer.PartMaskOf(in weights[1]), Is.EqualTo(0), "所属なしはマスク 0(絞らない)");
			}
			finally
			{
				Object.DestroyImmediate(mesh);
			}
		}

		[Test]
		public void ConsolidateGroups_UnifiesOnlySmallGroups()
		{
			var vertices = new[]
			{
				// 小さな装飾(対角 0.05): 混合所属 → 多数決で 1 パーツに
				new Vector3(0f, 0f, 0f), new Vector3(0.05f, 0f, 0f),
				// 大きな成分(対角 1): そのまま
				new Vector3(1f, 0f, 0f), new Vector3(2f, 0f, 0f),
			};
			var weights = new[]
			{
				PartAssignment.TopWeights(new float[HumanoidSkeleton.PartCount]
					{ 0, 0.4f, 0, 0, 0, 0.6f, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }),
				PartAssignment.TopWeights(new float[HumanoidSkeleton.PartCount]
					{ 0, 0.9f, 0, 0, 0, 0.1f, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }),
				PartWeights.Single(BodyPart.Torso),
				PartWeights.Single(BodyPart.LeftUpperLeg),
			};
			var groups = new[] { 0, 0, 1, 1 };

			PartAssignment.ConsolidateGroups(weights, vertices, groups, 2, 0.25f);

			// 装飾: Torso 0.4 + 0.9 = 1.3 > LeftUpperArm 0.7 → Torso に統一
			Assert.That(weights[0].Parts.x, Is.EqualTo((int)BodyPart.Torso));
			Assert.That(weights[0].Weights.x, Is.EqualTo(1f));
			Assert.That(weights[0].Parts.y, Is.EqualTo(0));
			Assert.That(weights[1].Parts.x, Is.EqualTo((int)BodyPart.Torso));
			// 大きな成分は変わらない
			Assert.That(weights[2].Parts.x, Is.EqualTo((int)BodyPart.Torso));
			Assert.That(weights[3].Parts.x, Is.EqualTo((int)BodyPart.LeftUpperLeg));
		}

		[Test]
		public void ConnectedComponents_SeparatesShellsAndWeldsSeams()
		{
			var vertices = new[]
			{
				new Vector3(0f, 0f, 0f), new Vector3(1f, 0f, 0f), new Vector3(0f, 1f, 0f),
				new Vector3(1f, 0f, 0f), new Vector3(1f, 1f, 0f), new Vector3(0f, 1f, 0f), // シームで分割された隣の三角形
				new Vector3(5f, 0f, 0f), new Vector3(6f, 0f, 0f), new Vector3(5f, 1f, 0f), // 離れた三角形
			};
			var triangles = new[] { 0, 2, 1, 3, 5, 4, 6, 8, 7 };
			var adjacency = MeshAdjacency.Build(vertices, triangles);

			var groups = PartAssignment.ConnectedComponents(adjacency, triangles, out var count);

			Assert.That(count, Is.EqualTo(2));
			Assert.That(groups[0], Is.EqualTo(groups[3]), "シームをまたいで同じ成分");
			Assert.That(groups[0], Is.EqualTo(groups[5]));
			Assert.That(groups[6], Is.Not.EqualTo(groups[0]));
			Assert.That(groups[6], Is.EqualTo(groups[8]));
		}

		[Test]
		public void Refresh_TracksBonePositionChanges()
		{
			var (skeleton, bones) = CreateSkeleton();
			var hash = skeleton.StateHash;

			// 変化が無ければ false で、軸もハッシュもそのまま
			Assert.That(skeleton.Refresh(), Is.False);
			Assert.That(skeleton.StateHash, Is.EqualTo(hash));

			// 前腕の関節を動かすと上腕の軸長と関節位置が追従し、ハッシュが変わる
			bones[HumanBodyBones.LeftLowerArm].position = new Vector3(0.55f, 1.35f, 0f);
			Assert.That(skeleton.Refresh(), Is.True);
			Assert.That(skeleton.StateHash, Is.Not.EqualTo(hash));
			Assert.That(skeleton.Axes[(int)BodyPart.LeftUpperArm].Length, Is.EqualTo(0.35f).Within(1e-5f));
			Assert.That(skeleton.ResolveByJoint(new float3(0.55f, 1.35f, 0f), 0.01f), Is.EqualTo(BodyPart.LeftLowerArm));
			Assert.That(skeleton.ResolveByJoint(new float3(0.45f, 1.35f, 0f), 0.01f), Is.EqualTo(BodyPart.None), "古い関節位置には一致しない");
		}

		[Test]
		public void AssignGroupsBySegment_UsesNearestAxis()
		{
			var (skeleton, _) = CreateSkeleton();
			var vertices = new[]
			{
				new Vector3(0.3f, 1.4f, 0f), new Vector3(0.35f, 1.3f, 0f), // 上腕の近く
				new Vector3(0.1f, 0.3f, 0f),                               // 下腿の近く(膝 y=0.45 と足首 y=0.05 の間)
			};
			var weights = new PartWeights[3];
			var groups = new[] { 0, 0, 1 };

			PartAssignment.AssignGroupsBySegment(weights, vertices, groups, 2, skeleton);

			Assert.That(weights[0].Parts.x, Is.EqualTo((int)BodyPart.LeftUpperArm));
			Assert.That(weights[1].Parts.x, Is.EqualTo((int)BodyPart.LeftUpperArm));
			Assert.That(weights[2].Parts.x, Is.EqualTo((int)BodyPart.LeftLowerLeg));
		}
	}
}
