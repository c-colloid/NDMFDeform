using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace MeshModifier.NDMFDeform.Core
{
	/// <summary>
	/// 頂点ごとのスキン行列(LBS: Σ weight × bone.localToWorld × bindpose)。
	/// 変形パイプラインはこれで頂点を「見た目のワールド空間」へ持ち上げてから
	/// デフォーマを適用し、逆行列でメッシュ空間へ書き戻す。
	/// レンダラー Transform のズレはもちろん、Modular Avatar 等で
	/// ボーンがバインド後に個別調整されたアバター・衣装(単一のアフィン行列では
	/// 表せないケース)でも、ワールドのギズモと変形対象が一致する。
	/// 非スキンのレンダラーは全頂点一様に localToWorldMatrix。
	///
	/// Runtime アセンブリに置くのは、参照メッシュ(Body Fit の体など)を
	/// 同じ数式でワールド空間へ持ち上げるためにデフォーマ側からも使うため。
	/// </summary>
	public struct VertexSkinning : System.IDisposable
	{
		public NativeArray<float4x4> ToWorld;
		public NativeArray<float4x4> ToMesh;

		public bool IsCreated => ToWorld.IsCreated;

		public static VertexSkinning Build(Transform rendererTransform, Mesh mesh, Allocator allocator)
		{
			var count = mesh.vertexCount;
			var result = new VertexSkinning
			{
				ToWorld = new NativeArray<float4x4>(count, allocator, NativeArrayOptions.UninitializedMemory),
				ToMesh = new NativeArray<float4x4>(count, allocator, NativeArrayOptions.UninitializedMemory),
			};

			SkinnedMeshRenderer smr = null;
			if (rendererTransform != null)
				rendererTransform.TryGetComponent(out smr);
			var bones = smr != null ? smr.bones : null;
			var bindposes = mesh.bindposes;
			var weights = mesh.GetAllBoneWeights();
			var bonesPerVertex = mesh.GetBonesPerVertex();

			if (smr == null || bones == null || bones.Length == 0 ||
			    bindposes == null || bindposes.Length == 0 ||
			    weights.Length == 0 || bonesPerVertex.Length != count)
			{
				result.FillUniform(rendererTransform != null
					? rendererTransform.localToWorldMatrix
					: Matrix4x4.identity);
				return result;
			}

			// ボーン毎のスキン行列(欠落ボーンは単位行列 = メッシュ空間のまま)
			var boneCount = Mathf.Min(bones.Length, bindposes.Length);
			var skinMatrices = new NativeArray<float4x4>(boneCount, Allocator.TempJob);
			for (var i = 0; i < boneCount; i++)
			{
				skinMatrices[i] = bones[i] != null
					? (float4x4)(bones[i].localToWorldMatrix * bindposes[i])
					: float4x4.identity;
			}

			// 各頂点のウェイト開始オフセット
			var offsets = new NativeArray<int>(count, Allocator.TempJob,
				NativeArrayOptions.UninitializedMemory);
			var acc = 0;
			for (var i = 0; i < count; i++)
			{
				offsets[i] = acc;
				acc += bonesPerVertex[i];
			}

			new BuildJob
			{
				SkinMatrices = skinMatrices,
				Weights = weights,
				BonesPerVertex = bonesPerVertex,
				Offsets = offsets,
				ToWorld = result.ToWorld,
				ToMesh = result.ToMesh,
			}.Schedule(count, 128).Complete();

			skinMatrices.Dispose();
			offsets.Dispose();
			// weights / bonesPerVertex はメッシュ所有のビューなので破棄しない
			return result;
		}

		private void FillUniform(Matrix4x4 matrix)
		{
			var m = (float4x4)matrix;
			var inverse = math.inverse(m);
			for (var i = 0; i < ToWorld.Length; i++)
			{
				ToWorld[i] = m;
				ToMesh[i] = inverse;
			}
		}

		public JobHandle ScheduleToWorld(NativeArray<float3> vertices, JobHandle dependency)
		{
			return new ApplyJob { Matrices = ToWorld, Vertices = vertices }
				.Schedule(vertices.Length, 128, dependency);
		}

		public JobHandle ScheduleToMesh(NativeArray<float3> vertices, JobHandle dependency)
		{
			return new ApplyJob { Matrices = ToMesh, Vertices = vertices }
				.Schedule(vertices.Length, 128, dependency);
		}

		public void Dispose()
		{
			if (ToWorld.IsCreated) ToWorld.Dispose();
			if (ToMesh.IsCreated) ToMesh.Dispose();
		}

		[BurstCompile]
		private struct BuildJob : IJobParallelFor
		{
			[ReadOnly] public NativeArray<float4x4> SkinMatrices;
			[ReadOnly] public NativeArray<BoneWeight1> Weights;
			[ReadOnly] public NativeArray<byte> BonesPerVertex;
			[ReadOnly] public NativeArray<int> Offsets;
			public NativeArray<float4x4> ToWorld;
			public NativeArray<float4x4> ToMesh;

			public void Execute(int index)
			{
				var boneCount = BonesPerVertex[index];
				float4x4 m;
				if (boneCount == 0)
				{
					m = float4x4.identity;
				}
				else
				{
					var offset = Offsets[index];
					m = default;
					var total = 0f;
					for (var i = 0; i < boneCount; i++)
					{
						var w = Weights[offset + i];
						var bone = math.clamp(w.boneIndex, 0, SkinMatrices.Length - 1);
						m += SkinMatrices[bone] * w.weight;
						total += w.weight;
					}
					if (total > 1e-6f)
						m *= 1f / total;
					else
						m = float4x4.identity;
				}
				ToWorld[index] = m;
				ToMesh[index] = math.inverse(m);
			}
		}

		[BurstCompile]
		private struct ApplyJob : IJobParallelFor
		{
			[ReadOnly] public NativeArray<float4x4> Matrices;
			public NativeArray<float3> Vertices;

			public void Execute(int index)
			{
				Vertices[index] = math.mul(Matrices[index], new float4(Vertices[index], 1f)).xyz;
			}
		}
	}
}
