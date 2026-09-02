using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace MeshModifier.NDMFDeform.Core
{
	/// <summary>
	/// 体のパーツ(ヒューマノイド骨格を 17 パーツに集約)。
	/// Body Fit のパーツ円柱モードで「どの軸を基準に放射状へ動かすか」「どの三角形を検索対象にするか」を決める。
	/// 32 ビットのマスクに収まるよう 18 種(None 含む)に抑えている。
	/// </summary>
	public enum BodyPart : byte
	{
		None = 0,
		Torso = 1,
		Neck = 2,
		Head = 3,
		LeftShoulder = 4,
		LeftUpperArm = 5,
		LeftLowerArm = 6,
		LeftHand = 7,
		RightShoulder = 8,
		RightUpperArm = 9,
		RightLowerArm = 10,
		RightHand = 11,
		LeftUpperLeg = 12,
		LeftLowerLeg = 13,
		LeftFoot = 14,
		RightUpperLeg = 15,
		RightLowerLeg = 16,
		RightFoot = 17,
	}

	/// <summary>
	/// パーツの円柱軸(ワールド空間)。h = 軸方向の位置(0 = 根元、1 = 先端)、
	/// θ = Reference / Binormal 平面での周角、r = 軸からの距離 で頂点を表す。
	/// </summary>
	public struct PartAxis
	{
		public int Valid;
		public float3 Origin;

		/// <summary>単位ベクトル</summary>
		public float3 Direction;

		public float Length;

		/// <summary>θ = 0 の方向(Direction に直交する単位ベクトル)</summary>
		public float3 Reference;

		/// <summary>cross(Direction, Reference)</summary>
		public float3 Binormal;

		/// <summary>ワールド点を円柱座標 (h, θ, r) と放射方向へ分解する</summary>
		public void Decompose(float3 point, out float h, out float theta, out float r, out float3 radialDirection)
		{
			var v = point - Origin;
			var along = math.dot(v, Direction);
			h = Length > 1e-6f ? along / Length : 0f;
			var radial = v - Direction * along;
			r = math.length(radial);
			if (r > 1e-6f)
			{
				radialDirection = radial / r;
				theta = math.atan2(math.dot(radial, Binormal), math.dot(radial, Reference));
			}
			else
			{
				radialDirection = Reference;
				theta = 0f;
			}
		}

		/// <summary>円柱座標 (h, θ) の軸上の点と放射方向</summary>
		public void RayFrom(float h, float theta, out float3 origin, out float3 direction)
		{
			origin = Origin + Direction * (h * Length);
			direction = Reference * math.cos(theta) + Binormal * math.sin(theta);
		}
	}

	/// <summary>頂点のパーツ所属(重み上位 4 つ、正規化済み。未使用スロットは None / 0)</summary>
	public struct PartWeights
	{
		public int4 Parts;
		public float4 Weights;

		public static PartWeights Single(BodyPart part)
		{
			return new PartWeights
			{
				Parts = new int4((int)part, 0, 0, 0),
				Weights = new float4(part == BodyPart.None ? 0f : 1f, 0f, 0f, 0f),
			};
		}

		/// <summary>重みが threshold 以上のパーツのビットマスク(None は含めない)</summary>
		public int Mask(float threshold)
		{
			var mask = 0;
			for (var i = 0; i < 4; i++)
			{
				var p = Parts[i];
				if (p != 0 && Weights[i] >= threshold)
					mask |= 1 << p;
			}
			return mask;
		}
	}

	/// <summary>
	/// ヒューマノイド骨格からのパーツ情報。
	/// Animator(ヒューマノイド)から作るのが基本で、テスト用にボーン辞書からも作れる。
	/// - 各パーツの円柱軸(ボーン区間)
	/// - Transform → パーツの対応(ヒューマノイドボーンと、その祖先探索)
	/// - 衣装側の独自アーマチュアのボーンを、関節位置の一致 → 親探索 → 最寄り区間 の順で対応付ける
	/// </summary>
	public sealed class HumanoidSkeleton
	{
		public const int PartCount = 18;

		public readonly PartAxis[] Axes = new PartAxis[PartCount];

		/// <summary>ヒューマノイドボーン Transform → パーツ</summary>
		public readonly Dictionary<Transform, BodyPart> BoneParts = new Dictionary<Transform, BodyPart>();

		/// <summary>関節位置(ワールド)とパーツ。衣装ボーンの位置一致に使う</summary>
		public readonly List<(float3 position, BodyPart part)> Joints = new List<(float3, BodyPart)>();

		/// <summary>骨格の状態ハッシュ(関節位置)。キャッシュの無効化に使う</summary>
		public int StateHash { get; private set; }

		/// <summary>構築に使ったヒューマノイドボーン(Refresh で現在位置を読み直す)</summary>
		private readonly Dictionary<HumanBodyBones, Transform> _bones = new Dictionary<HumanBodyBones, Transform>();

		/// <summary>この骨格の元になった Animator(FromAnimator のとき。衣装側の Animator と区別する)</summary>
		public Animator SourceAnimator { get; private set; }

		/// <summary>ボーン対応付けの信頼度: ヒューマノイド / 祖先 / 名前 / 対応済みの親</summary>
		public const float ConfidenceStructural = 1f;

		/// <summary>ボーン対応付けの信頼度: 関節位置の一致</summary>
		public const float ConfidenceJoint = 0.5f;

		/// <summary>ボーン対応付けの信頼度: 最寄りの軸区間</summary>
		public const float ConfidenceSegment = 0.25f;

		public static HumanoidSkeleton FromAnimator(Animator animator)
		{
			if (animator == null || !animator.isHuman)
				return null;

			var bones = CollectHumanoidBones(animator);
			var skeleton = FromBones(bones);
			if (skeleton != null)
				skeleton.SourceAnimator = animator;
			return skeleton;
		}

		/// <summary>Animator のヒューマノイド対応(存在するボーンのみ)</summary>
		public static Dictionary<HumanBodyBones, Transform> CollectHumanoidBones(Animator animator)
		{
			var bones = new Dictionary<HumanBodyBones, Transform>();
			if (animator == null || !animator.isHuman)
				return bones;
			foreach (HumanBodyBones bone in Enum.GetValues(typeof(HumanBodyBones)))
			{
				if (bone == HumanBodyBones.LastBone)
					continue;
				var t = animator.GetBoneTransform(bone);
				if (t != null)
					bones[bone] = t;
			}
			return bones;
		}

		public static HumanoidSkeleton FromBones(IReadOnlyDictionary<HumanBodyBones, Transform> bones)
		{
			if (bones == null)
				return null;

			var skeleton = new HumanoidSkeleton();
			foreach (var pair in bones)
			{
				if (pair.Value == null)
					continue;
				skeleton._bones[pair.Key] = pair.Value;
				skeleton.BoneParts[pair.Value] = PartOf(pair.Key);
			}
			skeleton.Refresh();
			return skeleton;
		}

		/// <summary>
		/// 現在のボーン位置から関節・軸・状態ハッシュを再計算する(ポーズ変更の追従用)。
		/// Animator の再列挙(GetBoneTransform × 全ボーン)を伴わないので、プレビューの
		/// ホットパスから毎回呼べる。位置が変わっていれば true。
		/// </summary>
		public bool Refresh()
		{
			var previous = StateHash;
			Joints.Clear();
			Array.Clear(Axes, 0, Axes.Length);
			var hash = 17;
			foreach (var pair in _bones)
			{
				if (pair.Value == null)
					continue;
				var part = PartOf(pair.Key);
				var position = (float3)pair.Value.position;
				Joints.Add((position, part));
				unchecked
				{
					hash = hash * 31 + (int)pair.Key;
					hash = hash * 31 + pair.Value.position.GetHashCode();
				}
			}
			StateHash = hash;
			BuildAxes(_bones);
			return hash != previous;
		}

		/// <summary>ヒューマノイドボーン → パーツ</summary>
		public static BodyPart PartOf(HumanBodyBones bone)
		{
			switch (bone)
			{
				case HumanBodyBones.Hips:
				case HumanBodyBones.Spine:
				case HumanBodyBones.Chest:
				case HumanBodyBones.UpperChest:
					return BodyPart.Torso;
				case HumanBodyBones.Neck:
					return BodyPart.Neck;
				case HumanBodyBones.Head:
				case HumanBodyBones.Jaw:
				case HumanBodyBones.LeftEye:
				case HumanBodyBones.RightEye:
					return BodyPart.Head;
				case HumanBodyBones.LeftShoulder: return BodyPart.LeftShoulder;
				case HumanBodyBones.LeftUpperArm: return BodyPart.LeftUpperArm;
				case HumanBodyBones.LeftLowerArm: return BodyPart.LeftLowerArm;
				case HumanBodyBones.LeftHand: return BodyPart.LeftHand;
				case HumanBodyBones.RightShoulder: return BodyPart.RightShoulder;
				case HumanBodyBones.RightUpperArm: return BodyPart.RightUpperArm;
				case HumanBodyBones.RightLowerArm: return BodyPart.RightLowerArm;
				case HumanBodyBones.RightHand: return BodyPart.RightHand;
				case HumanBodyBones.LeftUpperLeg: return BodyPart.LeftUpperLeg;
				case HumanBodyBones.LeftLowerLeg: return BodyPart.LeftLowerLeg;
				case HumanBodyBones.LeftFoot:
				case HumanBodyBones.LeftToes:
					return BodyPart.LeftFoot;
				case HumanBodyBones.RightUpperLeg: return BodyPart.RightUpperLeg;
				case HumanBodyBones.RightLowerLeg: return BodyPart.RightLowerLeg;
				case HumanBodyBones.RightFoot:
				case HumanBodyBones.RightToes:
					return BodyPart.RightFoot;
			}

			// 指のボーンは手に含める
			var name = bone.ToString();
			if (name.StartsWith("Left", StringComparison.Ordinal))
				return BodyPart.LeftHand;
			if (name.StartsWith("Right", StringComparison.Ordinal))
				return BodyPart.RightHand;
			return BodyPart.None;
		}

		private void BuildAxes(IReadOnlyDictionary<HumanBodyBones, Transform> bones)
		{
			float3? Pos(HumanBodyBones b)
			{
				return bones.TryGetValue(b, out var t) && t != null ? (float3?)(float3)t.position : null;
			}

			void Segment(BodyPart part, float3? from, float3? to)
			{
				if (from == null || to == null)
					return;
				SetAxis(part, from.Value, to.Value);
			}

			void Extended(BodyPart part, float3? from, float3? previous, float scale, float fallbackLength)
			{
				if (from == null)
					return;
				float3 direction;
				float length;
				if (previous != null && math.lengthsq(from.Value - previous.Value) > 1e-8f)
				{
					direction = math.normalize(from.Value - previous.Value);
					length = math.length(from.Value - previous.Value) * scale;
				}
				else
				{
					direction = new float3(0f, 1f, 0f);
					length = fallbackLength;
				}
				SetAxis(part, from.Value, from.Value + direction * length);
			}

			var hips = Pos(HumanBodyBones.Hips);
			var neck = Pos(HumanBodyBones.Neck);
			var head = Pos(HumanBodyBones.Head);
			var torsoTop = neck ?? head ?? Pos(HumanBodyBones.UpperChest) ?? Pos(HumanBodyBones.Chest) ?? Pos(HumanBodyBones.Spine);
			Segment(BodyPart.Torso, hips, torsoTop);
			Segment(BodyPart.Neck, neck, head);
			Extended(BodyPart.Head, head, neck ?? Pos(HumanBodyBones.Chest) ?? hips, 2f, 0.2f);

			Segment(BodyPart.LeftShoulder, Pos(HumanBodyBones.LeftShoulder), Pos(HumanBodyBones.LeftUpperArm));
			Segment(BodyPart.LeftUpperArm, Pos(HumanBodyBones.LeftUpperArm), Pos(HumanBodyBones.LeftLowerArm));
			Segment(BodyPart.LeftLowerArm, Pos(HumanBodyBones.LeftLowerArm), Pos(HumanBodyBones.LeftHand));
			var leftMiddle = Pos(HumanBodyBones.LeftMiddleProximal);
			if (leftMiddle != null && Pos(HumanBodyBones.LeftHand) != null)
				SetAxis(BodyPart.LeftHand, Pos(HumanBodyBones.LeftHand).Value,
					Pos(HumanBodyBones.LeftHand).Value + (leftMiddle.Value - Pos(HumanBodyBones.LeftHand).Value) * 2.5f);
			else
				Extended(BodyPart.LeftHand, Pos(HumanBodyBones.LeftHand), Pos(HumanBodyBones.LeftLowerArm), 0.6f, 0.15f);

			Segment(BodyPart.RightShoulder, Pos(HumanBodyBones.RightShoulder), Pos(HumanBodyBones.RightUpperArm));
			Segment(BodyPart.RightUpperArm, Pos(HumanBodyBones.RightUpperArm), Pos(HumanBodyBones.RightLowerArm));
			Segment(BodyPart.RightLowerArm, Pos(HumanBodyBones.RightLowerArm), Pos(HumanBodyBones.RightHand));
			var rightMiddle = Pos(HumanBodyBones.RightMiddleProximal);
			if (rightMiddle != null && Pos(HumanBodyBones.RightHand) != null)
				SetAxis(BodyPart.RightHand, Pos(HumanBodyBones.RightHand).Value,
					Pos(HumanBodyBones.RightHand).Value + (rightMiddle.Value - Pos(HumanBodyBones.RightHand).Value) * 2.5f);
			else
				Extended(BodyPart.RightHand, Pos(HumanBodyBones.RightHand), Pos(HumanBodyBones.RightLowerArm), 0.6f, 0.15f);

			Segment(BodyPart.LeftUpperLeg, Pos(HumanBodyBones.LeftUpperLeg), Pos(HumanBodyBones.LeftLowerLeg));
			Segment(BodyPart.LeftLowerLeg, Pos(HumanBodyBones.LeftLowerLeg), Pos(HumanBodyBones.LeftFoot));
			FootAxis(BodyPart.LeftFoot, Pos(HumanBodyBones.LeftFoot), Pos(HumanBodyBones.LeftToes), Pos(HumanBodyBones.LeftLowerLeg));
			Segment(BodyPart.RightUpperLeg, Pos(HumanBodyBones.RightUpperLeg), Pos(HumanBodyBones.RightLowerLeg));
			Segment(BodyPart.RightLowerLeg, Pos(HumanBodyBones.RightLowerLeg), Pos(HumanBodyBones.RightFoot));
			FootAxis(BodyPart.RightFoot, Pos(HumanBodyBones.RightFoot), Pos(HumanBodyBones.RightToes), Pos(HumanBodyBones.RightLowerLeg));
		}

		private void FootAxis(BodyPart part, float3? foot, float3? toes, float3? lowerLeg)
		{
			if (foot == null)
				return;
			if (toes != null && math.lengthsq(toes.Value - foot.Value) > 1e-8f)
			{
				// つま先方向を 1.3 倍に伸ばして靴先まで覆う
				SetAxis(part, foot.Value, foot.Value + (toes.Value - foot.Value) * 1.3f);
				return;
			}
			// つま先が無ければ足首から下向き(すね方向の延長)
			var down = lowerLeg != null && math.lengthsq(foot.Value - lowerLeg.Value) > 1e-8f
				? math.normalize(foot.Value - lowerLeg.Value)
				: new float3(0f, -1f, 0f);
			SetAxis(part, foot.Value, foot.Value + down * 0.12f);
		}

		private void SetAxis(BodyPart part, float3 from, float3 to)
		{
			var delta = to - from;
			var length = math.length(delta);
			if (length <= 1e-6f)
				return;
			var direction = delta / length;
			// θ = 0 の基準は軸に直交する決定的な方向(体と衣装で同じ軸を使うので任意でよい)
			var helper = math.abs(direction.y) < 0.9f ? new float3(0f, 1f, 0f) : new float3(0f, 0f, 1f);
			var reference = math.normalize(math.cross(direction, helper));
			Axes[(int)part] = new PartAxis
			{
				Valid = 1,
				Origin = from,
				Direction = direction,
				Length = length,
				Reference = reference,
				Binormal = math.cross(direction, reference),
			};
		}

		public bool HasAxis(BodyPart part)
		{
			return Axes[(int)part].Valid != 0;
		}

		/// <summary>
		/// 軸の無いパーツを、軸のある近いパーツへ寄せる。
		/// ヒューマノイドの任意ボーン(Shoulder / Neck / UpperChest など)は有無がアバターごとに違うため、
		/// 衣装ボーンの名前やヒューマノイド対応がそれらを指しても、この骨格に軸が無ければ
		/// Shoulder → UpperArm、Neck → Torso、Head → Neck → Torso に丸める。
		/// </summary>
		public BodyPart Canonical(BodyPart part)
		{
			if (part == BodyPart.None || HasAxis(part))
				return part;
			switch (part)
			{
				case BodyPart.LeftShoulder:
					return HasAxis(BodyPart.LeftUpperArm) ? BodyPart.LeftUpperArm : part;
				case BodyPart.RightShoulder:
					return HasAxis(BodyPart.RightUpperArm) ? BodyPart.RightUpperArm : part;
				case BodyPart.Neck:
					return HasAxis(BodyPart.Torso) ? BodyPart.Torso : part;
				case BodyPart.Head:
					if (HasAxis(BodyPart.Neck)) return BodyPart.Neck;
					return HasAxis(BodyPart.Torso) ? BodyPart.Torso : part;
				default:
					return part;
			}
		}

		/// <summary>ボーン(とその祖先)がヒューマノイドボーンに対応していればそのパーツ</summary>
		public BodyPart ResolveByAncestor(Transform bone)
		{
			for (var t = bone; t != null; t = t.parent)
			{
				if (BoneParts.TryGetValue(t, out var part))
					return part;
			}
			return BodyPart.None;
		}

		/// <summary>関節位置が tolerance 以内で一致するパーツ(衣装アーマチュアの関節合わせ)</summary>
		public BodyPart ResolveByJoint(float3 position, float tolerance)
		{
			var best = BodyPart.None;
			var bestDist = tolerance * tolerance;
			foreach (var (jointPosition, part) in Joints)
			{
				var d = math.distancesq(jointPosition, position);
				if (d < bestDist)
				{
					bestDist = d;
					best = part;
				}
			}
			return best;
		}

		/// <summary>最寄りの軸区間のパーツ(最終フォールバック)</summary>
		public BodyPart ResolveBySegment(float3 position)
		{
			var best = BodyPart.None;
			var bestDist = float.MaxValue;
			for (var p = 1; p < PartCount; p++)
			{
				var axis = Axes[p];
				if (axis.Valid == 0)
					continue;
				var v = position - axis.Origin;
				var t = math.clamp(math.dot(v, axis.Direction), 0f, axis.Length);
				var d = math.distancesq(v, axis.Direction * t);
				if (d < bestDist)
				{
					bestDist = d;
					best = (BodyPart)p;
				}
			}
			return best;
		}

		public BodyPart[] MapBones(Transform[] bones, float jointTolerance)
		{
			return MapBones(bones, jointTolerance, null, null);
		}

		/// <summary>
		/// レンダラーのボーン配列をパーツへ対応付ける。位置に頼る段は最後に回す:
		/// 1. ヒューマノイドボーンとその子孫(この骨格の Animator、または衣装自身のヒューマノイド Animator。
		///    体、マージ済みの衣装、胸・尻尾などの補助ボーン)
		/// 2. ボーン名(UpperArm_L / 左腕 など。<see cref="BoneNameMatcher"/>)
		/// 3. 衣装アーマチュア内で対応済みの祖先(スカート・リボンなどの補助ボーン)。親の信頼度を引き継ぐ
		/// 4. 関節位置の一致(信頼度 0.5。体と衣装の骨格の比率が違うと外れる)
		/// 5. 最寄りの軸区間(信頼度 0.25)
		/// 任意ボーン(Shoulder / Neck)はこの骨格に軸が無ければ近いパーツへ丸める(<see cref="Canonical"/>)。
		/// confidence(null 可)にボーンごとの信頼度を書く。
		/// </summary>
		public BodyPart[] MapBones(Transform[] bones, float jointTolerance, Animator costumeAnimator, float[] confidence)
		{
			var count = bones?.Length ?? 0;
			var result = new BodyPart[count];
			if (count == 0)
				return result;

			// 衣装自身のヒューマノイド対応(この骨格の Animator と別のもののみ)
			Dictionary<Transform, BodyPart> costumeParts = null;
			if (costumeAnimator != null && costumeAnimator.isHuman && costumeAnimator != SourceAnimator)
			{
				costumeParts = new Dictionary<Transform, BodyPart>();
				foreach (var pair in CollectHumanoidBones(costumeAnimator))
				{
					if (!costumeParts.ContainsKey(pair.Value))
						costumeParts[pair.Value] = PartOf(pair.Key);
				}
			}

			var mapped = new Dictionary<Transform, (BodyPart part, float confidence)>();
			var remaining = new List<int>();
			for (var i = 0; i < count; i++)
			{
				var bone = bones[i];
				if (bone == null)
					continue;
				var part = BodyPart.None;
				for (var t = bone; t != null; t = t.parent)
				{
					if (costumeParts != null && costumeParts.TryGetValue(t, out part))
						break;
					if (BoneParts.TryGetValue(t, out part))
						break;
				}
				if (part == BodyPart.None)
					part = BoneNameMatcher.Match(bone.name);
				part = Canonical(part);
				if (part == BodyPart.None)
				{
					remaining.Add(i);
					continue;
				}
				result[i] = part;
				mapped[bone] = (part, ConfidenceStructural);
				if (confidence != null)
					confidence[i] = ConfidenceStructural;
			}

			// 残りは階層の浅い順に(親の結果を子が引き継げるように)
			remaining.Sort((a, b) => Depth(bones[a]).CompareTo(Depth(bones[b])));
			foreach (var i in remaining)
			{
				var bone = bones[i];
				var part = BodyPart.None;
				var conf = 0f;
				for (var t = bone.parent; t != null; t = t.parent)
				{
					if (mapped.TryGetValue(t, out var parentEntry))
					{
						part = parentEntry.part;
						conf = parentEntry.confidence;
						break;
					}
				}
				if (part == BodyPart.None)
				{
					part = Canonical(ResolveByJoint(bone.position, jointTolerance));
					conf = ConfidenceJoint;
				}
				if (part == BodyPart.None)
				{
					part = ResolveBySegment(bone.position);
					conf = ConfidenceSegment;
				}
				result[i] = part;
				if (part != BodyPart.None)
					mapped[bone] = (part, conf);
				if (confidence != null)
					confidence[i] = part != BodyPart.None ? conf : 0f;
			}
			return result;
		}

		private static int Depth(Transform t)
		{
			var depth = 0;
			for (var p = t != null ? t.parent : null; p != null; p = p.parent)
				depth++;
			return depth;
		}
	}

	/// <summary>頂点のパーツ所属を求める補助(体・衣装共通)</summary>
	public static class PartAssignment
	{
		/// <summary>
		/// ボーンウェイトからパーツ重みを求める。ウェイトの無いメッシュでは全頂点 None。
		/// </summary>
		public static PartWeights[] FromBoneWeights(Mesh mesh, BodyPart[] boneParts)
		{
			return FromBoneWeights(mesh, boneParts, null, out _);
		}

		/// <summary>
		/// ボーンウェイトからパーツ重みを求める。boneConfidence(ボーンごとの対応付けの信頼度、null 可)を
		/// ウェイトで平均した頂点ごとの信頼度も返す(対応の無いボーンのウェイトは信頼度 0)。
		/// </summary>
		public static PartWeights[] FromBoneWeights(Mesh mesh, BodyPart[] boneParts, float[] boneConfidence,
			out float[] vertexConfidence)
		{
			var n = mesh.vertexCount;
			var result = new PartWeights[n];
			vertexConfidence = new float[n];
			var weights = mesh.GetAllBoneWeights();
			var perVertex = mesh.GetBonesPerVertex();
			if (boneParts == null || boneParts.Length == 0 || weights.Length == 0 || perVertex.Length != n)
				return result;

			var accum = new float[HumanoidSkeleton.PartCount];
			var offset = 0;
			for (var v = 0; v < n; v++)
			{
				Array.Clear(accum, 0, accum.Length);
				var count = perVertex[v];
				var weightSum = 0f;
				var confidenceSum = 0f;
				for (var k = 0; k < count; k++)
				{
					var w = weights[offset + k];
					var valid = w.boneIndex >= 0 && w.boneIndex < boneParts.Length;
					var part = valid ? boneParts[w.boneIndex] : BodyPart.None;
					accum[(int)part] += w.weight;
					weightSum += w.weight;
					if (part != BodyPart.None)
						confidenceSum += w.weight * (boneConfidence != null && w.boneIndex < boneConfidence.Length
							? boneConfidence[w.boneIndex]
							: 1f);
				}
				offset += count;
				result[v] = TopWeights(accum);
				vertexConfidence[v] = weightSum > 0f ? confidenceSum / weightSum : 0f;
			}
			return result;
		}

		/// <summary>累積重みから上位 4 パーツ(None を除く)を正規化して取り出す</summary>
		public static PartWeights TopWeights(float[] accum)
		{
			var pw = new PartWeights();
			var total = 0f;
			for (var slot = 0; slot < 4; slot++)
			{
				var bestPart = 0;
				var best = 0f;
				for (var p = 1; p < HumanoidSkeleton.PartCount; p++)
				{
					if (accum[p] > best)
					{
						best = accum[p];
						bestPart = p;
					}
				}
				if (bestPart == 0 || best <= 0f)
					break;
				pw.Parts[slot] = bestPart;
				pw.Weights[slot] = best;
				total += best;
				accum[bestPart] = 0f;
			}
			if (total > 0f)
				pw.Weights /= total;
			return pw;
		}

		/// <summary>
		/// 小さな連結成分(装飾)をひとつのパーツ所属に揃える。
		/// groupOfVertex は頂点 → グループ番号(連結成分または UV 島)。
		/// 対角長が maxSize 以下のグループのみ、頂点の重みを合算した多数決で統一する。
		/// </summary>
		public static void ConsolidateGroups(PartWeights[] partWeights, Vector3[] vertices, int[] groupOfVertex,
			int groupCount, float maxSize)
		{
			if (partWeights == null || groupOfVertex == null || groupCount <= 0)
				return;

			var n = partWeights.Length;
			var min = new Vector3[groupCount];
			var max = new Vector3[groupCount];
			var seen = new bool[groupCount];
			var sums = new float[groupCount, HumanoidSkeleton.PartCount];
			for (var v = 0; v < n; v++)
			{
				var g = groupOfVertex[v];
				if (g < 0 || g >= groupCount)
					continue;
				var p = vertices[v];
				if (!seen[g])
				{
					seen[g] = true;
					min[g] = p;
					max[g] = p;
				}
				else
				{
					min[g] = Vector3.Min(min[g], p);
					max[g] = Vector3.Max(max[g], p);
				}
				var pw = partWeights[v];
				for (var s = 0; s < 4; s++)
				{
					if (pw.Parts[s] != 0)
						sums[g, pw.Parts[s]] += pw.Weights[s];
				}
			}

			var consolidated = new PartWeights[groupCount];
			var apply = new bool[groupCount];
			var accum = new float[HumanoidSkeleton.PartCount];
			for (var g = 0; g < groupCount; g++)
			{
				if (!seen[g] || (max[g] - min[g]).magnitude > maxSize)
					continue;
				var any = false;
				for (var p = 1; p < HumanoidSkeleton.PartCount; p++)
				{
					accum[p] = sums[g, p];
					any |= accum[p] > 0f;
				}
				if (!any)
					continue;
				// 装飾は単一パーツに揃える(多数決の最上位のみ)
				var top = TopWeights(accum);
				consolidated[g] = PartWeights.Single((BodyPart)top.Parts.x);
				apply[g] = true;
			}

			for (var v = 0; v < n; v++)
			{
				var g = groupOfVertex[v];
				if (g >= 0 && g < groupCount && apply[g])
					partWeights[v] = consolidated[g];
			}
		}

		/// <summary>位置で溶接した三角形連結成分(頂点 → 成分番号)</summary>
		public static int[] ConnectedComponents(MeshAdjacency adjacency, int[] triangles, out int componentCount)
		{
			var n = adjacency.VertexCount;
			var groupOf = adjacency.GroupOf;
			var groupCount = adjacency.Representative.Length;
			var parent = new int[groupCount];
			for (var i = 0; i < groupCount; i++)
				parent[i] = i;

			int Find(int x)
			{
				while (parent[x] != x)
				{
					parent[x] = parent[parent[x]];
					x = parent[x];
				}
				return x;
			}

			if (triangles != null)
			{
				for (var t = 0; t + 2 < triangles.Length; t += 3)
				{
					var a = triangles[t];
					var b = triangles[t + 1];
					var c = triangles[t + 2];
					if (a < 0 || b < 0 || c < 0 || a >= n || b >= n || c >= n)
						continue;
					var ra = Find(groupOf[a]);
					var rb = Find(groupOf[b]);
					var rc = Find(groupOf[c]);
					if (ra != rb) parent[ra] = rb;
					var rb2 = Find(rb);
					if (rb2 != Find(rc)) parent[rb2] = Find(rc);
				}
			}

			var componentOfRoot = new Dictionary<int, int>();
			var result = new int[n];
			for (var v = 0; v < n; v++)
			{
				var root = Find(groupOf[v]);
				if (!componentOfRoot.TryGetValue(root, out var id))
				{
					id = componentOfRoot.Count;
					componentOfRoot[root] = id;
				}
				result[v] = id;
			}
			componentCount = componentOfRoot.Count;
			return result;
		}

		/// <summary>UV 島(頂点 → 島番号。島に属さない頂点は -1)</summary>
		public static int[] UVIslandGroups(Mesh mesh, out int islandCount)
		{
			var analysis = UVIslandAnalysis.Analyze(mesh);
			var result = new int[mesh.vertexCount];
			for (var i = 0; i < result.Length; i++)
				result[i] = -1;
			foreach (var island in analysis.Islands)
			{
				foreach (var v in island.Vertices)
					result[v] = island.Id;
			}
			islandCount = analysis.Islands.Count;
			return result;
		}

		/// <summary>
		/// ウェイトの無いメッシュ向け: グループ(連結成分 / UV 島)の重心に最も近い軸区間のパーツを割り当てる。
		/// </summary>
		public static void AssignGroupsBySegment(PartWeights[] partWeights, Vector3[] vertices, int[] groupOfVertex,
			int groupCount, HumanoidSkeleton skeleton)
		{
			if (groupCount <= 0)
				return;
			var sum = new Vector3[groupCount];
			var count = new int[groupCount];
			for (var v = 0; v < vertices.Length; v++)
			{
				var g = groupOfVertex[v];
				if (g < 0 || g >= groupCount)
					continue;
				sum[g] += vertices[v];
				count[g]++;
			}
			var partOfGroup = new BodyPart[groupCount];
			for (var g = 0; g < groupCount; g++)
			{
				if (count[g] == 0)
					continue;
				partOfGroup[g] = skeleton.ResolveBySegment(sum[g] / count[g]);
			}
			for (var v = 0; v < vertices.Length; v++)
			{
				var g = groupOfVertex[v];
				if (g >= 0 && g < groupCount)
					partWeights[v] = PartWeights.Single(partOfGroup[g]);
			}
		}

		/// <summary>三角形ごとのパーツマスク(頂点の重み threshold 以上のパーツの OR。空なら全パーツ)</summary>
		public static int[] TriangleMasks(int[] triangles, PartWeights[] partWeights, float threshold)
		{
			var triCount = triangles.Length / 3;
			var masks = new int[triCount];
			for (var t = 0; t < triCount; t++)
			{
				var mask = 0;
				for (var e = 0; e < 3; e++)
				{
					var v = triangles[t * 3 + e];
					if (v >= 0 && v < partWeights.Length)
					{
						mask |= partWeights[v].Mask(threshold);
						// 支配パーツは閾値に関わらず含める
						var dominant = partWeights[v].Parts.x;
						if (dominant != 0)
							mask |= 1 << dominant;
					}
				}
				masks[t] = mask;
			}
			return masks;
		}
	}
}
