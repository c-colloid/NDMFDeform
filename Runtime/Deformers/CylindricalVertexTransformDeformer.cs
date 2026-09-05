// 移植元: dev ブランチ ExDeform/CylindricalVertexTransformDefomer.cs(自作コード)
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using static Unity.Mathematics.math;

namespace MeshModifier.NDMFDeform.Core
{
	/// <summary>
	/// Cylindrical Vertex Transform の断ち切りライン計算(Burst ジョブ・テストから使う純粋関数)。
	/// </summary>
	public static class CylindricalCutLine
	{
		/// <summary>
		/// 折り返し防止で保証する放射方向の写像の最小勾配。
		/// 押し出し時、断ち切りラインの外側の帯は最悪でもこの比率まで圧縮されるに留まり、
		/// 内側の頂点に追い越されない(smoothstep の最大勾配 1.5 に対し幅 = 2 × 変位で 0.25)。
		/// </summary>
		public const float MinRadialSlope = 0.25f;

		/// <summary>押し出し量に対する折り返し防止帯幅の倍率(1.5 / (1 - MinRadialSlope) = 2)</summary>
		public const float FoldGuardBandScale = 2f;

		/// <summary>隣接拡散 1 回あたりの混合率</summary>
		public const float SmoothingLambda = 0.5f;

		/// <summary>
		/// 断ち切りラインからの距離 outside(内側は負)を重みへ。
		/// 帯幅 band の外側で smoothstep で 1 → 0。band が 0 なら境界で打ち切る。
		/// </summary>
		public static float Fade(float outside, float band)
		{
			if (outside <= 0f)
				return 1f;
			if (band <= 0f || outside >= band)
				return 0f;
			return 1f - smoothstep(0f, band, outside);
		}

		/// <summary>軸空間の点に対する変位重み(0..1)。scope 円筒と top / bottom 平面の帯を掛け合わせる</summary>
		public static float Weight(float3 point, float scope, float top, float bottom,
			float radialBand, float axialBand)
		{
			var d = length(point.xy);
			// 旧実装の境界条件(d < scope, bottom <= z <= top)を band = 0 で再現する
			if (d >= scope && radialBand <= 0f)
				return 0f;
			var w = Fade(d - scope, radialBand);
			w *= Fade(point.z - top, axialBand);
			w *= Fade(bottom - point.z, axialBand);
			return w;
		}

		/// <summary>
		/// 重み付き変位を軸空間の点へ適用する。
		/// 引き込みが軸を横切る場合は、写像の勾配が MinRadialSlope を下回らない位置で止める
		/// (d' = max(d + delta, MinRadialSlope × d))。
		/// </summary>
		public static float3 Apply(float3 point, float weight, float shift)
		{
			var delta = weight * shift;
			if (delta == 0f)
				return point;
			var d = length(point.xy);
			delta = max(delta, -(1f - MinRadialSlope) * d);
			point.xy += normalizesafe(point.xy) * delta;
			return point;
		}
	}

	/// <summary>
	/// 円柱コントローラによる放射状頂点移動。
	/// 軸空間で半径 scope・区間 [bottom, top] 内の頂点を、
	/// XY 放射方向へ (radius - scope) × factor だけ押し出す。
	///
	/// 断ち切りライン(scope 円筒と top / bottom 平面)の扱い:
	/// 内側だけを一律に動かすと境界をまたぐ面が段差になり、押し出し量が
	/// 境界外の頂点を追い越して面が交差する。服の裾上げになぞらえ、
	/// (1) 境界の外側 falloff 幅で変位を滑らかに 0 へ落とし、
	/// (2) 押し出し量が幅を超えて折り返す場合は幅を自動的に広げて
	///     「余った布」を追い越さずに圧縮し、
	/// (3) 軸を横切る引き込みは軸の手前で止め、
	/// (4) 任意でメッシュの隣接関係に沿って重みを拡散(平滑化)する。
	/// </summary>
	[DeformerMeta(Name = "Cylindrical Vertex Transform", Category = DeformerCategory.Shape,
	              Description = "円柱コントローラで頂点を放射状に移動する")]
	[AddComponentMenu("NDMF Deform/Deformers/Cylindrical Vertex Transform")]
	public class CylindricalVertexTransformDeformer : DeformerBase
	{
		[SerializeField, Range(0f, 1f)] private float factor = 0f;
		[SerializeField, Tooltip("移動先の半径(シーンではシアン実線)。scope との差分だけ放射方向へ押し出される")]
		private float radius = 1f;
		[SerializeField, Tooltip("影響範囲の半径(シーンではオレンジ点線)。この内側の頂点だけが変形される")]
		private float scope = 1f;
		[SerializeField] private float top = 0.5f;
		[SerializeField] private float bottom = -0.5f;
		[SerializeField, Min(0f), Tooltip("断ち切りライン(scope 円筒・top / bottom 平面)の外側で変位を 0 へ落とす幅。" +
			"0 なら境界で打ち切る。押し出し時は面が折り返さない幅まで自動で広がる")]
		private float falloff = 0f;
		[SerializeField, Range(0, 64), Tooltip("メッシュの隣接関係に沿って変位の重みを拡散する回数。" +
			"断ち切りラインの段差をメッシュの繋がりに沿ってならす(0 で無効)")]
		private int smoothIterations = 0;
		[SerializeField] private Transform axisOverride;

		public float Factor { get => factor; set => factor = Mathf.Clamp01(value); }
		public float Radius { get => radius; set => radius = value; }
		public float Scope { get => scope; set => scope = value; }
		public float Top { get => top; set => top = value; }
		public float Bottom { get => bottom; set => bottom = value; }
		public float Falloff { get => falloff; set => falloff = Mathf.Max(0f, value); }
		public int SmoothIterations { get => smoothIterations; set => smoothIterations = Mathf.Clamp(value, 0, 64); }

		public override Transform Axis => axisOverride != null ? axisOverride : transform;

		public override DeformDataFlags DataFlags => DeformDataFlags.Vertices;

		// ---- 隣接情報キャッシュ(シリアライズ対象外) ----
		[System.NonSerialized] private MeshAdjacency _adjacency;
		[System.NonSerialized] private Mesh _adjacencyMesh;
		[System.NonSerialized] private int _adjacencyVertexCount;

#if UNITY_EDITOR
		public override void DescribeHandles(IHandleBuilder h)
		{
			// キャップは top リングの縁に載せる(中空に浮かせない)。
			// radius/scope はペア宣言し、ホバー矢印が互いのリングを指すようにする
			h.RadiusSlider(nameof(radius), HandleAxis.Y, HandleLineStyle.Solid, 1f,
				nameof(top), HandleAxis.Z, nameof(scope));
			h.RadiusSlider(nameof(scope), HandleAxis.Y, HandleLineStyle.Dotted, 1f,
				nameof(top), HandleAxis.Z, nameof(radius));
			h.AxisSlider(nameof(top), HandleAxis.Z);
			h.AxisSlider(nameof(bottom), HandleAxis.Z);
			h.Circle(HandleAxis.Z, nameof(top), nameof(radius));
			h.Circle(HandleAxis.Z, nameof(bottom), nameof(radius));
			h.Circle(HandleAxis.Z, nameof(top), nameof(scope), HandleLineStyle.Dotted);
			h.Circle(HandleAxis.Z, nameof(bottom), nameof(scope), HandleLineStyle.Dotted);
		}
#endif

		/// <summary>放射方向の変位量(軸空間)。正で押し出し、負で引き込み</summary>
		public float Shift => (radius - scope) * factor;

		/// <summary>
		/// scope 円筒の外側に取る帯幅。押し出し時は折り返さない幅
		/// (CylindricalCutLine.FoldGuardBandScale × 押し出し量)を下限にする。
		/// </summary>
		public float RadialBand => max(falloff, CylindricalCutLine.FoldGuardBandScale * max(0f, Shift));

		public override void PrepareBake(Mesh source)
		{
			if (smoothIterations <= 0 || source == null)
				return;
			if (_adjacency == null || _adjacencyMesh != source || _adjacencyVertexCount != source.vertexCount)
			{
				_adjacency = MeshAdjacency.Build(source);
				_adjacencyMesh = source;
				_adjacencyVertexCount = source.vertexCount;
			}
		}

		public override JobHandle Schedule(in MeshBuffers buffers, in DeformSpace space, JobHandle dependency)
		{
			if (Mathf.Approximately(factor, 0f))
				return dependency;

			var shift = Shift;
			var radialBand = RadialBand;
			var canSmooth = smoothIterations > 0 && _adjacency != null &&
			                _adjacency.VertexCount == buffers.Length && _adjacency.Neighbors.Length > 0;

			if (!canSmooth)
			{
				return new CylindricalVertexTransformJob
				{
					shift = shift,
					scope = scope,
					top = top,
					bottom = bottom,
					radialBand = radialBand,
					axialBand = falloff,
					meshToAxis = space.MeshToAxis,
					axisToMesh = space.AxisToMesh,
					vertices = buffers.Vertices,
				}.Schedule(buffers.Length, 64, dependency);
			}

			// 平滑化あり: 重み → 隣接拡散 × N → 適用 の 3 段
			var n = buffers.Length;
			var weights = new NativeArray<float>(n, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
			var scratch = new NativeArray<float>(n, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
			var offsets = new NativeArray<int>(_adjacency.Offsets, Allocator.TempJob);
			var neighbors = new NativeArray<int>(_adjacency.Neighbors, Allocator.TempJob);

			var handle = new WeightJob
			{
				scope = scope,
				top = top,
				bottom = bottom,
				radialBand = radialBand,
				axialBand = falloff,
				meshToAxis = space.MeshToAxis,
				vertices = buffers.Vertices,
				weights = weights,
			}.Schedule(n, 128, dependency);

			var src = weights;
			var dst = scratch;
			for (var i = 0; i < smoothIterations; i++)
			{
				handle = new DiffuseWeightJob
				{
					lambda = CylindricalCutLine.SmoothingLambda,
					offsets = offsets,
					neighbors = neighbors,
					source = src,
					result = dst,
				}.Schedule(n, 128, handle);
				(src, dst) = (dst, src);
			}

			handle = new ApplyJob
			{
				shift = shift,
				meshToAxis = space.MeshToAxis,
				axisToMesh = space.AxisToMesh,
				weights = src,
				vertices = buffers.Vertices,
			}.Schedule(n, 64, handle);

			handle = weights.Dispose(handle);
			handle = scratch.Dispose(handle);
			handle = offsets.Dispose(handle);
			handle = neighbors.Dispose(handle);
			return handle;
		}

		// ---- ジョブ ----

		/// <summary>平滑化なしの 1 パス(重み計算と適用を同時に行う)</summary>
		[BurstCompile]
		public struct CylindricalVertexTransformJob : IJobParallelFor
		{
			public float shift;
			public float scope;
			public float top;
			public float bottom;
			public float radialBand;
			public float axialBand;
			public float4x4 meshToAxis;
			public float4x4 axisToMesh;
			public NativeArray<float3> vertices;

			public void Execute(int index)
			{
				var point = mul(meshToAxis, float4(vertices[index], 1f)).xyz;
				var w = CylindricalCutLine.Weight(point, scope, top, bottom, radialBand, axialBand);
				if (w <= 0f)
					return;
				point = CylindricalCutLine.Apply(point, w, shift);
				vertices[index] = mul(axisToMesh, float4(point, 1f)).xyz;
			}
		}

		[BurstCompile]
		public struct WeightJob : IJobParallelFor
		{
			public float scope;
			public float top;
			public float bottom;
			public float radialBand;
			public float axialBand;
			public float4x4 meshToAxis;
			[ReadOnly] public NativeArray<float3> vertices;
			[WriteOnly] public NativeArray<float> weights;

			public void Execute(int index)
			{
				var point = mul(meshToAxis, float4(vertices[index], 1f)).xyz;
				weights[index] = CylindricalCutLine.Weight(point, scope, top, bottom, radialBand, axialBand);
			}
		}

		/// <summary>隣接頂点の平均へ lambda だけ寄せる(ラプラシアン拡散 1 回分)</summary>
		[BurstCompile]
		public struct DiffuseWeightJob : IJobParallelFor
		{
			public float lambda;
			[ReadOnly] public NativeArray<int> offsets;
			[ReadOnly] public NativeArray<int> neighbors;
			[ReadOnly] public NativeArray<float> source;
			[WriteOnly] public NativeArray<float> result;

			public void Execute(int index)
			{
				var begin = offsets[index];
				var end = offsets[index + 1];
				var value = source[index];
				if (end <= begin)
				{
					result[index] = value;
					return;
				}
				var sum = 0f;
				for (var i = begin; i < end; i++)
					sum += source[neighbors[i]];
				result[index] = lerp(value, sum / (end - begin), lambda);
			}
		}

		[BurstCompile]
		public struct ApplyJob : IJobParallelFor
		{
			public float shift;
			public float4x4 meshToAxis;
			public float4x4 axisToMesh;
			[ReadOnly] public NativeArray<float> weights;
			public NativeArray<float3> vertices;

			public void Execute(int index)
			{
				var w = weights[index];
				if (w <= 0f)
					return;
				var point = mul(meshToAxis, float4(vertices[index], 1f)).xyz;
				point = CylindricalCutLine.Apply(point, w, shift);
				vertices[index] = mul(axisToMesh, float4(point, 1f)).xyz;
			}
		}
	}
}
