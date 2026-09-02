using System;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace MeshModifier.NDMFDeform.Core
{
	/// <summary>
	/// パーツ所属の手動上書き(グループ = UV 島 / 連結成分 ごと)。
	/// UV 島グループでは島の代表 UV(シード)で、連結成分グループでは代表点(メッシュ空間)で
	/// グループを再特定する(メッシュの頂点順が変わっても追従できるように)。
	/// </summary>
	[Serializable]
	public struct PartOverride
	{
		/// <summary>島シードで参照する(UV 島グループ)。偽なら代表点で参照する</summary>
		public bool useIsland;

		/// <summary>UV 島グループのときの島シード(代表 UV + サブメッシュ)</summary>
		public IslandSeed island;

		/// <summary>連結成分グループのときの代表点(メッシュ空間)。最も近い頂点の成分を指す</summary>
		public Vector3 point;

		/// <summary>割り当てるパーツ</summary>
		public BodyPart part;
	}

	/// <summary>グループ(UV 島 / 連結成分)の所属判定の結果</summary>
	public enum PartDecision
	{
		/// <summary>証拠が無く所属なし(頂点は None のまま)</summary>
		Unlabeled = 0,

		/// <summary>グループ全体を 1 パーツに揃えた</summary>
		Unified = 1,

		/// <summary>グループが複数パーツにまたがるため頂点ごとの所属を使う</summary>
		PerVertex = 2,

		/// <summary>手動上書き</summary>
		Override = 3,
	}

	/// <summary>
	/// グループごとの判定内容(インスペクタの一覧・要確認表示用)。
	/// ウェイト由来と形状由来の投票をそれぞれ持ち、食い違いを「要確認」として示す。
	/// </summary>
	public sealed class PartGroupReport
	{
		public int Group;
		public int VertexCount;

		/// <summary>バウンズ対角(メッシュ空間、m)</summary>
		public float Size;

		/// <summary>代表点(メッシュ空間。連結成分グループの上書きに使う)</summary>
		public Vector3 Point;

		/// <summary>UV 島グループのときのシード</summary>
		public IslandSeed Island;
		public bool IsIsland;

		/// <summary>ボーンウェイト由来の投票の上位と比率(証拠なしは None / 0)</summary>
		public BodyPart BonePart;
		public float BoneConfidence;

		/// <summary>ボーン対応付けの信頼度の平均(名前 / ヒューマノイド = 1、関節位置 = 0.5、最寄り区間 = 0.25)</summary>
		public float BoneMapConfidence;

		/// <summary>体の形状(パーツ表面からの隙間)由来の投票の上位と比率</summary>
		public BodyPart GeometryPart;
		public float GeometryConfidence;

		/// <summary>採用したパーツ(PerVertex のときは合算投票の上位)と合算投票の比率</summary>
		public BodyPart Part;
		public float Confidence;

		public PartDecision Decision;

		/// <summary>ウェイトと形状の判定が食い違う、または大きなグループが頂点ごとの所属に落ちた</summary>
		public bool NeedsReview;
	}

	/// <summary>PartLabeler.Label の入力</summary>
	public sealed class PartLabelInput
	{
		/// <summary>頂点(メッシュ空間。大きさと代表点に使う)</summary>
		public Vector3[] Vertices;

		/// <summary>ボーンウェイト由来のパーツ重み(無ければ null)</summary>
		public PartWeights[] BoneWeights;

		/// <summary>頂点ごとのボーン対応付けの信頼度 0〜1(BoneWeights と対。無ければ 1 扱い)</summary>
		public float[] BoneConfidence;

		/// <summary>体の形状由来のパーツ重み(無ければ null)</summary>
		public PartWeights[] GeometryWeights;

		/// <summary>頂点 → グループ番号(-1 = 所属なし)。null ならグループ化しない</summary>
		public int[] GroupOfVertex;
		public int GroupCount;

		/// <summary>UV 島グループのときのシード(グループ番号順)。連結成分では null</summary>
		public IslandSeed[] GroupSeeds;

		/// <summary>この大きさ(バウンズ対角)以下のグループは投票の比率に関わらず 1 パーツに揃える</summary>
		public float DecorationMaxSize = 0.25f;

		/// <summary>大きなグループを 1 パーツに揃えるのに必要な投票の比率</summary>
		public float ConfidenceThreshold = 0.7f;

		/// <summary>グループ番号 → 上書きパーツ</summary>
		public Dictionary<int, BodyPart> Overrides;
	}

	/// <summary>
	/// 衣装頂点のパーツ所属を決める(UV 島 / 連結成分を単位にした投票)。
	///
	/// 証拠は 2 種類:
	/// - ボーンウェイト: 作者の意図そのもの。ただし衣装ボーン → パーツの対応付けが位置頼み
	///   (関節一致・最寄り区間)のときは信頼度を下げる
	/// - 形状: 体の各パーツの半径プロファイルからの隙間。ウェイトの無い衣装でも使え、
	///   対応付けの誤りを検出できる。装飾が隣のパーツに近い場合は間違えるので単独では使わない
	/// 頂点ごとの所属は信頼度で両者を混ぜ、グループ単位で投票して比率が高ければ揃える。
	/// 比率が低い(ボディスーツ・ロングコートなど複数パーツにまたがる)グループは頂点ごとの所属のまま。
	/// </summary>
	public static class PartLabeler
	{
		/// <summary>形状証拠を求める h の許容範囲(軸区間 [0, 1] からのはみ出し)</summary>
		public const float HMargin = 0.15f;

		/// <summary>
		/// 頂点ごとに、体の各パーツ表面からの放射方向の隙間を比べてパーツ重みを求める。
		/// 隙間が最小のパーツを 1、2 番目は差が小さいほど(隙間の差 / τ、τ = max(2 cm, 半径の 1/4))重みを持つ。
		/// 体の内側(隙間が負)は半分の重さで数える(袖が腕に食い込んでいても腕とみなす)。
		/// </summary>
		[BurstCompile]
		public struct PartGeometryJob : IJobParallelFor
		{
			[ReadOnly] public NativeArray<float3> vertices;
			public BodyPartProfiles profiles;
			public float hMargin;
			[WriteOnly] public NativeArray<PartWeights> weights;

			public void Execute(int index)
			{
				var p = vertices[index];
				var best1 = 0;
				var best2 = 0;
				var s1 = float.MaxValue;
				var s2 = float.MaxValue;
				var radius1 = 0f;
				for (var part = 1; part < HumanoidSkeleton.PartCount; part++)
				{
					if (!profiles.IsUsable(part))
						continue;
					var axis = profiles.Axes[part];
					axis.Decompose(p, out var h, out var theta, out var r, out _);
					if (h < -hMargin || h > 1f + hMargin)
						continue;
					var radius = profiles.SampleRadius(part, h, theta);
					if (math.isnan(radius))
						continue;
					var gap = r - radius;
					var score = gap >= 0f ? gap : -gap * 0.5f;
					if (score < s1)
					{
						best2 = best1;
						s2 = s1;
						best1 = part;
						s1 = score;
						radius1 = radius;
					}
					else if (score < s2)
					{
						best2 = part;
						s2 = score;
					}
				}

				var pw = new PartWeights();
				if (best1 != 0)
				{
					var tau = math.max(0.02f, 0.25f * radius1);
					var w2 = best2 != 0 ? math.saturate(1f - (s2 - s1) / tau) : 0f;
					if (w2 <= 0f)
						best2 = 0;
					var total = 1f + w2;
					pw.Parts = new int4(best1, best2, 0, 0);
					pw.Weights = new float4(1f / total, w2 / total, 0f, 0f);
				}
				weights[index] = pw;
			}
		}

		/// <summary>形状証拠(ワールド空間の頂点 × 体のプロファイル)を求める。プロファイルが無ければ null</summary>
		public static PartWeights[] EvaluateGeometry(Vector3[] worldVertices, in BodyPartProfiles profiles)
		{
			if (worldVertices == null || !profiles.IsCreated)
				return null;
			var n = worldVertices.Length;
			var result = new PartWeights[n];
			if (n == 0)
				return result;

			var input = new NativeArray<float3>(n, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
			var output = new NativeArray<PartWeights>(n, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
			for (var i = 0; i < n; i++)
				input[i] = worldVertices[i];
			try
			{
				new PartGeometryJob
				{
					vertices = input,
					profiles = profiles,
					hMargin = HMargin,
					weights = output,
				}.Schedule(n, 64).Complete();
				output.CopyTo(result);
			}
			finally
			{
				input.Dispose();
				output.Dispose();
			}
			return result;
		}

		/// <summary>パーツ重みを累積配列(長さ PartCount)へ足す</summary>
		public static void Accumulate(float[] accum, in PartWeights pw, float scale)
		{
			if (scale <= 0f)
				return;
			for (var s = 0; s < 4; s++)
			{
				var part = pw.Parts[s];
				if (part > 0 && part < accum.Length)
					accum[part] += pw.Weights[s] * scale;
			}
		}

		/// <summary>累積配列の上位パーツと比率(合計 0 なら None / 0)</summary>
		public static BodyPart Top(float[] accum, out float confidence)
		{
			var total = 0f;
			var best = 0;
			var bestValue = 0f;
			for (var p = 1; p < accum.Length; p++)
			{
				total += accum[p];
				if (accum[p] > bestValue)
				{
					bestValue = accum[p];
					best = p;
				}
			}
			confidence = total > 0f ? bestValue / total : 0f;
			return (BodyPart)best;
		}

		/// <summary>
		/// 頂点ごとの所属を決め、グループ単位で投票して揃える。reports にグループごとの判定を追加する(null 可)。
		/// </summary>
		public static PartWeights[] Label(PartLabelInput input, List<PartGroupReport> reports)
		{
			var n = input.Vertices?.Length ?? 0;
			var result = new PartWeights[n];
			var bone = input.BoneWeights != null && input.BoneWeights.Length == n ? input.BoneWeights : null;
			var geo = input.GeometryWeights != null && input.GeometryWeights.Length == n ? input.GeometryWeights : null;
			var boneConf = bone != null && input.BoneConfidence != null && input.BoneConfidence.Length == n
				? input.BoneConfidence
				: null;
			var partCount = HumanoidSkeleton.PartCount;
			var accum = new float[partCount];

			// 頂点ごとの所属: ウェイト(信頼度 c)と形状(1 − c)を混ぜる
			for (var v = 0; v < n; v++)
			{
				Array.Clear(accum, 0, partCount);
				if (bone != null && geo != null)
				{
					var c = boneConf != null ? Mathf.Clamp01(boneConf[v]) : 1f;
					if (bone[v].Parts.x == 0)
						c = 0f; // ウェイトの無い頂点は形状だけ
					Accumulate(accum, bone[v], c);
					Accumulate(accum, geo[v], 1f - c);
				}
				else if (bone != null)
				{
					Accumulate(accum, bone[v], 1f);
				}
				else if (geo != null)
				{
					Accumulate(accum, geo[v], 1f);
				}
				result[v] = PartAssignment.TopWeights(accum);
			}

			var groups = input.GroupOfVertex;
			var groupCount = input.GroupCount;
			if (groups == null || groups.Length != n || groupCount <= 0)
				return result;

			// グループごとの集計
			var boneSum = new float[groupCount * partCount];
			var geoSum = new float[groupCount * partCount];
			var allSum = new float[groupCount * partCount];
			var confSum = new float[groupCount];
			var count = new int[groupCount];
			var min = new Vector3[groupCount];
			var max = new Vector3[groupCount];
			var first = new Vector3[groupCount];
			var scratch = new PartWeights();
			for (var v = 0; v < n; v++)
			{
				var g = groups[v];
				if (g < 0 || g >= groupCount)
					continue;
				var p = input.Vertices[v];
				if (count[g] == 0)
				{
					min[g] = p;
					max[g] = p;
					first[g] = p;
				}
				else
				{
					min[g] = Vector3.Min(min[g], p);
					max[g] = Vector3.Max(max[g], p);
				}
				count[g]++;
				if (bone != null)
				{
					AccumulateInto(boneSum, g * partCount, bone[v]);
					confSum[g] += boneConf != null ? Mathf.Clamp01(boneConf[v]) : 1f;
				}
				if (geo != null)
					AccumulateInto(geoSum, g * partCount, geo[v]);
				AccumulateInto(allSum, g * partCount, result[v]);
			}

			var slice = new float[partCount];
			for (var g = 0; g < groupCount; g++)
			{
				if (count[g] == 0)
					continue;

				var bonePart = BodyPart.None;
				var boneConfidence = 0f;
				if (bone != null)
				{
					Array.Copy(boneSum, g * partCount, slice, 0, partCount);
					bonePart = Top(slice, out boneConfidence);
				}
				var geoPart = BodyPart.None;
				var geoConfidence = 0f;
				if (geo != null)
				{
					Array.Copy(geoSum, g * partCount, slice, 0, partCount);
					geoPart = Top(slice, out geoConfidence);
				}
				Array.Copy(allSum, g * partCount, slice, 0, partCount);
				var top = Top(slice, out var confidence);

				var size = (max[g] - min[g]).magnitude;
				var decision = PartDecision.Unlabeled;
				var part = top;
				if (input.Overrides != null && input.Overrides.TryGetValue(g, out var overridePart) &&
				    overridePart != BodyPart.None)
				{
					decision = PartDecision.Override;
					part = overridePart;
				}
				else if (top == BodyPart.None)
				{
					decision = PartDecision.Unlabeled;
				}
				else if (size <= input.DecorationMaxSize || confidence >= input.ConfidenceThreshold)
				{
					decision = PartDecision.Unified;
				}
				else
				{
					decision = PartDecision.PerVertex;
				}

				if (decision == PartDecision.Unified || decision == PartDecision.Override)
				{
					scratch = PartWeights.Single(part);
					for (var v = 0; v < n; v++)
					{
						if (groups[v] == g)
							result[v] = scratch;
					}
				}

				if (reports == null)
					continue;
				var disagree = bonePart != BodyPart.None && geoPart != BodyPart.None && bonePart != geoPart &&
				               boneConfidence >= 0.6f && geoConfidence >= 0.6f;
				reports.Add(new PartGroupReport
				{
					Group = g,
					VertexCount = count[g],
					Size = size,
					Point = first[g],
					Island = input.GroupSeeds != null && g < input.GroupSeeds.Length ? input.GroupSeeds[g] : default,
					IsIsland = input.GroupSeeds != null && g < input.GroupSeeds.Length,
					BonePart = bonePart,
					BoneConfidence = boneConfidence,
					BoneMapConfidence = bone != null ? confSum[g] / count[g] : 0f,
					GeometryPart = geoPart,
					GeometryConfidence = geoConfidence,
					Part = part,
					Confidence = confidence,
					Decision = decision,
					NeedsReview = decision != PartDecision.Override &&
					              (disagree || (decision == PartDecision.PerVertex && size > input.DecorationMaxSize)),
				});
			}
			return result;
		}

		private static void AccumulateInto(float[] sums, int offset, in PartWeights pw)
		{
			for (var s = 0; s < 4; s++)
			{
				var part = pw.Parts[s];
				if (part > 0 && part < HumanoidSkeleton.PartCount)
					sums[offset + part] += pw.Weights[s];
			}
		}

		/// <summary>
		/// グループ境界(袖付けなどの縫い目)で所属を混ぜる。
		/// まず同じ位置の頂点(UV シームで分割された頂点)の所属を平均して縫い目の裂けを防ぎ、
		/// 次に所属の異なる頂点に接する頂点を iterations 回だけ隣接平均へ寄せて遷移を広げる。
		/// </summary>
		public static void BlendSeams(PartWeights[] weights, MeshAdjacency adjacency, int iterations)
		{
			if (weights == null || adjacency == null || adjacency.VertexCount != weights.Length)
				return;
			var n = weights.Length;
			var groupOf = adjacency.GroupOf;
			var groupCount = adjacency.Representative.Length;
			var partCount = HumanoidSkeleton.PartCount;

			// 1. 同位置の頂点を平均
			var sums = new float[groupCount * partCount];
			var members = new int[groupCount];
			for (var v = 0; v < n; v++)
			{
				AccumulateInto(sums, groupOf[v] * partCount, weights[v]);
				members[groupOf[v]]++;
			}
			var rep = new PartWeights[groupCount];
			var slice = new float[partCount];
			for (var g = 0; g < groupCount; g++)
			{
				Array.Copy(sums, g * partCount, slice, 0, partCount);
				rep[g] = PartAssignment.TopWeights(slice);
			}

			// 2. 境界の拡散(代表頂点の隣接 = 代表頂点)
			if (iterations > 0 && adjacency.HasEdges)
			{
				var start = adjacency.Start;
				var neighbors = adjacency.Neighbors;
				var frontier = new bool[groupCount];
				var anyFrontier = false;
				for (var v = 0; v < n; v++)
				{
					var g = groupOf[v];
					if (frontier[g])
						continue;
					var dominant = rep[g].Parts.x;
					for (var i = start[v]; i < start[v + 1]; i++)
					{
						var ng = groupOf[neighbors[i]];
						if (rep[ng].Parts.x != dominant)
						{
							frontier[g] = true;
							anyFrontier = true;
							break;
						}
					}
				}

				var next = new PartWeights[groupCount];
				var nextFrontier = new bool[groupCount];
				for (var it = 0; it < iterations && anyFrontier; it++)
				{
					Array.Copy(rep, next, groupCount);
					Array.Copy(frontier, nextFrontier, groupCount);
					for (var v = 0; v < n; v++)
					{
						var g = groupOf[v];
						if (!frontier[g] || adjacency.Representative[g] != v)
							continue;
						Array.Clear(slice, 0, partCount);
						var degree = 0;
						for (var i = start[v]; i < start[v + 1]; i++)
						{
							var ng = groupOf[neighbors[i]];
							Accumulate(slice, rep[ng], 1f);
							nextFrontier[ng] = true;
							degree++;
						}
						if (degree == 0)
							continue;
						for (var p = 1; p < partCount; p++)
							slice[p] /= degree;
						Accumulate(slice, rep[g], 1f);
						next[g] = PartAssignment.TopWeights(slice);
					}
					var tmp = rep;
					rep = next;
					next = tmp;
					var tmpF = frontier;
					frontier = nextFrontier;
					nextFrontier = tmpF;
				}
			}

			for (var v = 0; v < n; v++)
				weights[v] = rep[groupOf[v]];
		}
	}
}
