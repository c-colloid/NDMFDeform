using System.Collections.Generic;
using MeshModifier.NDMFDeform.Core;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace MeshModifier.NDMFDeform.Editor
{
	/// <summary>
	/// ヘッドレスなベイクコア。
	/// コンポーネントのライフサイクル副作用に依存せず、
	/// 「元メッシュ + デフォーマスタック → 新メッシュ」の純関数としてベイクする。
	/// ビルド(NDMF Transforming)とプレビュー(IRenderFilter)の両方から呼ばれる。
	/// </summary>
	public static class DeformBakeCore
	{
		/// <summary>
		/// スタックを元メッシュへ適用した新しいメッシュインスタンスを返す。
		/// 有効なデフォーマが 1 つも無い場合は null(呼び出し側は何もしない)。
		/// 返されたメッシュの破棄・アセット登録は呼び出し側の責務。
		/// </summary>
		public static Mesh Bake(DeformStack stack, Mesh source, Transform rendererTransform)
		{
			if (stack == null || source == null || rendererTransform == null)
				return null;

			var deformers = CollectEnabledDeformers(stack);
			if (deformers.Count == 0)
				return null;

			var flags = DeformDataFlags.None;
			foreach (var d in deformers)
				flags |= d.DataFlags;

			// メッシュ全体を参照する解析(UV 島など)を先に済ませる
			foreach (var d in deformers)
				d.PrepareBake(source);

			var buffers = CreateBuffers(source, flags);
			var handle = default(JobHandle);
			try
			{
				foreach (var deformer in deformers)
				{
					var space = new DeformSpace(GetMeshToAxis(deformer.Axis, rendererTransform));
					handle = deformer.Schedule(in buffers, in space, handle);
				}
				handle.Complete();

				var result = Object.Instantiate(source);
				result.name = source.name + " (NDMFDeform)";
				ApplyBuffers(result, buffers, flags);
				result.RecalculateBounds();
				return result;
			}
			finally
			{
				// 例外時もスケジュール済みジョブを完了させてから破棄する
				// (未完了ジョブが参照する NativeArray の Dispose は安全性エラーになる)
				handle.Complete();
				buffers.Dispose();
			}
		}

		public static List<DeformerBase> CollectEnabledDeformers(DeformStack stack)
		{
			var list = new List<DeformerBase>();
			foreach (var entry in stack.Deformers)
			{
				if (entry.enabled && entry.deformer != null)
					list.Add(entry.deformer);
			}
			return list;
		}

		/// <summary>
		/// メッシュ空間 → 軸空間の変換行列
		/// (旧 DeformerUtils.GetMeshToAxisSpace 相当)。
		/// </summary>
		private static float4x4 GetMeshToAxis(Transform axis, Transform rendererTransform)
		{
			var m = axis.worldToLocalMatrix * rendererTransform.localToWorldMatrix;
			return ToFloat4x4(m);
		}

		private static float4x4 ToFloat4x4(Matrix4x4 m)
		{
			return new float4x4(m.GetColumn(0), m.GetColumn(1), m.GetColumn(2), m.GetColumn(3));
		}

		private static MeshBuffers CreateBuffers(Mesh source, DeformDataFlags flags)
		{
			// エディタ実行のため Read/Write 設定に関わらずメッシュを読める前提で実装する。
			// (プレイヤービルドでの制約はベイクには関係しない)
			var buffers = new MeshBuffers { Length = source.vertexCount };

			var vertices = source.vertices;
			buffers.Vertices = new NativeArray<float3>(vertices.Length, Allocator.TempJob,
				NativeArrayOptions.UninitializedMemory);
			for (var i = 0; i < vertices.Length; i++)
				buffers.Vertices[i] = vertices[i];

			if ((flags & DeformDataFlags.Normals) != 0)
			{
				var normals = source.normals;
				if (normals.Length == buffers.Length)
				{
					buffers.Normals = new NativeArray<float3>(normals.Length, Allocator.TempJob,
						NativeArrayOptions.UninitializedMemory);
					for (var i = 0; i < normals.Length; i++)
						buffers.Normals[i] = normals[i];
				}
			}

			if ((flags & DeformDataFlags.Tangents) != 0)
			{
				var tangents = source.tangents;
				if (tangents.Length == buffers.Length)
				{
					buffers.Tangents = new NativeArray<float4>(tangents.Length, Allocator.TempJob,
						NativeArrayOptions.UninitializedMemory);
					for (var i = 0; i < tangents.Length; i++)
						buffers.Tangents[i] = tangents[i];
				}
			}

			if ((flags & DeformDataFlags.UVs) != 0)
			{
				var uvs = source.uv;
				if (uvs.Length == buffers.Length)
				{
					buffers.UVs = new NativeArray<float2>(uvs.Length, Allocator.TempJob,
						NativeArrayOptions.UninitializedMemory);
					for (var i = 0; i < uvs.Length; i++)
						buffers.UVs[i] = uvs[i];
				}
			}

			if ((flags & DeformDataFlags.OriginalVertices) != 0)
			{
				buffers.OriginalVertices = new NativeArray<float3>(buffers.Vertices, Allocator.TempJob);
			}

			return buffers;
		}

		private static void ApplyBuffers(Mesh target, in MeshBuffers buffers, DeformDataFlags flags)
		{
			var vertices = new Vector3[buffers.Length];
			for (var i = 0; i < vertices.Length; i++)
				vertices[i] = buffers.Vertices[i];
			target.vertices = vertices;

			if ((flags & DeformDataFlags.Normals) != 0 && buffers.Normals.IsCreated)
			{
				var normals = new Vector3[buffers.Length];
				for (var i = 0; i < normals.Length; i++)
					normals[i] = buffers.Normals[i];
				target.normals = normals;
			}

			if ((flags & DeformDataFlags.Tangents) != 0 && buffers.Tangents.IsCreated)
			{
				var tangents = new Vector4[buffers.Length];
				for (var i = 0; i < tangents.Length; i++)
					tangents[i] = buffers.Tangents[i];
				target.tangents = tangents;
			}
		}
	}
}
