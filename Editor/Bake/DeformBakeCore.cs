using System.Collections.Generic;
using MeshModifier.NDMFDeform.Core;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace MeshModifier.NDMFDeform.Editor
{
	/// <summary>ベイクの範囲を制御するオプション(既定 = フルベイク)</summary>
	public struct DeformBakeOptions
	{
		/// <summary>ブレンドシェイプを再ベイクするか(ビルドでは常に true)</summary>
		public bool RebakeBlendShapes;

		/// <summary>
		/// 非 null の場合、この集合に含まれる名前のシェイプのみ再ベイクし、
		/// 残りは元のデルタを維持する(プレビューでアクティブなシェイプだけ計算する高速化用)。
		/// </summary>
		public HashSet<string> ShapesToRebake;

		public static DeformBakeOptions Full => new DeformBakeOptions { RebakeBlendShapes = true };
	}

	/// <summary>
	/// ヘッドレスなベイクコア。
	/// コンポーネントのライフサイクル副作用に依存せず、
	/// 「元メッシュ + デフォーマスタック → 新メッシュ」の純関数としてベイクする。
	/// ビルド(NDMF Transforming)とプレビュー(IRenderFilter)の両方から呼ばれる。
	///
	/// ブレンドシェイプは各フレームについて
	/// deformedDelta = Deform(base + delta) − Deform(base) で再ベイクする
	/// (旧実装で変形後もデルタが元メッシュ基準のまま顔などが壊れていた問題の解消)。
	/// 変形が非線形な区間を通るシェイプには中間フレームを自動挿入できる
	/// (途中重みの直線補間による食い込み・行き過ぎの抑制)。
	/// </summary>
	public static class DeformBakeCore
	{
		/// <summary>
		/// 非線形とみなす逸脱量のしきい値(バウンズ対角長に対する比)。
		/// 中間点の真の変形位置が直線補間からこれ以上ずれるシェイプに中間フレームを挿入する。
		/// </summary>
		private const float NonlinearDeviationRatio = 0.002f;

		public static Mesh Bake(DeformStack stack, Mesh source, Transform rendererTransform)
		{
			return Bake(stack, source, rendererTransform, DeformBakeOptions.Full);
		}

		/// <summary>
		/// スタックを元メッシュへ適用した新しいメッシュインスタンスを返す。
		/// 有効なデフォーマが 1 つも無い場合は null(呼び出し側は何もしない)。
		/// 返されたメッシュの破棄・アセット登録は呼び出し側の責務。
		/// </summary>
		public static Mesh Bake(DeformStack stack, Mesh source, Transform rendererTransform,
			DeformBakeOptions options)
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

			// 軸空間はフレーム間で不変なので 1 回だけ計算する
			var spaces = new DeformSpace[deformers.Count];
			for (var i = 0; i < deformers.Count; i++)
				spaces[i] = new DeformSpace(
					GetMeshToAxis(deformers[i].Axis, rendererTransform), rendererTransform);

			// 各パスで共有するソースチャンネル(頂点以外はフレーム間で共通)
			var channels = new SourceChannels
			{
				VertexCount = source.vertexCount,
				Vertices = source.vertices,
			};
			if ((flags & DeformDataFlags.Normals) != 0)
				channels.Normals = source.normals;
			if ((flags & DeformDataFlags.Tangents) != 0)
				channels.Tangents = source.tangents;
			if ((flags & DeformDataFlags.UVs) != 0)
				channels.Uvs = source.uv;

			var result = Object.Instantiate(source);
			result.name = source.name + " (NDMFDeform)";

			// 基本形状のベイク(結果メッシュへ書き戻し)
			var bakedBase = new Vector3[channels.VertexCount];
			RunPipeline(deformers, spaces, in channels, flags, null, bakedBase, result);

			if (options.RebakeBlendShapes)
				RebakeBlendShapes(result, source, stack, deformers, spaces, in channels, flags,
					bakedBase, options.ShapesToRebake);

			// 再計算モードの時のみ法線・タンジェントを変形後の形状から作り直す。
			// 既定(PreserveAuthored)では作り込まれた法線・タンジェント
			// (シーム調整・トゥーンのハイライト等)を保持する。
			if (stack.Normals == DeformStack.NormalsMode.Recalculate)
			{
				result.RecalculateNormals();
				// タンジェント再構築には UV0 と法線が必要
				if (source.uv.Length == channels.VertexCount)
					result.RecalculateTangents();
			}

			result.RecalculateBounds();
			return result;
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

		/// <summary>各パスで共有するソースメッシュのチャンネル配列</summary>
		private struct SourceChannels
		{
			public int VertexCount;
			public Vector3[] Vertices;
			public Vector3[] Normals;
			public Vector4[] Tangents;
			public Vector2[] Uvs;
		}

		/// <summary>
		/// 頂点パイプラインを 1 回実行する。
		/// overrideVertices が null なら channels.Vertices(基本形状)を入力にする。
		/// 結果の頂点を output に書き込み、applyTo が非 null なら全チャンネルを書き戻す。
		/// </summary>
		private static void RunPipeline(List<DeformerBase> deformers, DeformSpace[] spaces,
			in SourceChannels channels, DeformDataFlags flags,
			Vector3[] overrideVertices, Vector3[] output, Mesh applyTo)
		{
			var buffers = CreateBuffers(in channels, flags, overrideVertices);
			var handle = default(JobHandle);
			try
			{
				for (var i = 0; i < deformers.Count; i++)
					handle = deformers[i].Schedule(in buffers, in spaces[i], handle);
				handle.Complete();

				for (var i = 0; i < buffers.Length; i++)
					output[i] = buffers.Vertices[i];

				if (applyTo != null)
					ApplyBuffers(applyTo, buffers, flags);
			}
			finally
			{
				// 例外時もスケジュール済みジョブを完了させてから破棄する
				// (未完了ジョブが参照する NativeArray の Dispose は安全性エラーになる)
				handle.Complete();
				buffers.Dispose();
			}
		}

		/// <summary>
		/// ブレンドシェイプを再ベイクする:
		/// 各フレームの頂点デルタを Deform(base + delta) − Deform(base) で作り直す。
		/// 法線・タンジェントデルタは元の値を引き継ぐ
		/// (現行デフォーマは法線・タンジェントを変更しないため)。
		/// - shapesToRebake が非 null の場合、含まれない名前のシェイプは元のデルタのまま
		/// - KeepAuthoredShape 指定のシェイプは「デルタを持つ頂点のみ base+delta へ戻す」
		/// - 非線形補正が有効な単一フレームシェイプは、中間点の真の変形位置が
		///   直線補間から大きくずれる場合に 25/50/75% の中間フレームを挿入する
		/// </summary>
		private static void RebakeBlendShapes(Mesh result, Mesh source, DeformStack stack,
			List<DeformerBase> deformers, DeformSpace[] spaces,
			in SourceChannels channels, DeformDataFlags flags, Vector3[] bakedBase,
			HashSet<string> shapesToRebake)
		{
			var shapeCount = source.blendShapeCount;
			if (shapeCount == 0)
				return;

			var n = channels.VertexCount;
			var deltaVertices = new Vector3[n];
			var deltaNormals = new Vector3[n];
			var deltaTangents = new Vector3[n];
			var scaledNormals = new Vector3[n];
			var scaledTangents = new Vector3[n];
			var frameInput = new Vector3[n];
			var fullOutput = new Vector3[n];
			var midOutput = new Vector3[n];
			var scratchOutput = new Vector3[n];
			var outputDelta = new Vector3[n];

			var deviationThreshold = NonlinearDeviationRatio * source.bounds.size.magnitude;

			result.ClearBlendShapes();
			for (var s = 0; s < shapeCount; s++)
			{
				var name = source.GetBlendShapeName(s);
				var frameCount = source.GetBlendShapeFrameCount(s);
				var mode = stack.GetBlendShapeMode(name);
				var rebake = shapesToRebake == null || shapesToRebake.Contains(name);

				for (var f = 0; f < frameCount; f++)
				{
					source.GetBlendShapeFrameVertices(s, f, deltaVertices, deltaNormals, deltaTangents);
					var weight = source.GetBlendShapeFrameWeight(s, f);

					if (!rebake)
					{
						result.AddBlendShapeFrame(name, weight, deltaVertices, deltaNormals, deltaTangents);
						continue;
					}

					if (mode == DeformStack.BlendShapeDeltaMode.KeepAuthoredShape)
					{
						// デルタを持つ頂点のみ、作者の作った形状(base+delta)へ戻す
						for (var i = 0; i < n; i++)
						{
							outputDelta[i] = deltaVertices[i].sqrMagnitude > 1e-12f
								? channels.Vertices[i] + deltaVertices[i] - bakedBase[i]
								: Vector3.zero;
						}
						result.AddBlendShapeFrame(name, weight, outputDelta, deltaNormals, deltaTangents);
						continue;
					}

					// 変形に追従(既定): Deform(base + delta) − Deform(base)
					for (var i = 0; i < n; i++)
						frameInput[i] = channels.Vertices[i] + deltaVertices[i];
					RunPipeline(deformers, spaces, in channels, flags, frameInput, fullOutput, null);

					// 中間点の真の変形位置が直線補間からどれだけずれるかで非線形を検出する。
					// 複数フレーム持ちのシェイプは作者が中間を制御しているため対象外
					var curved = false;
					if (stack.NonlinearShapeCorrection && frameCount == 1)
					{
						for (var i = 0; i < n; i++)
							frameInput[i] = channels.Vertices[i] + deltaVertices[i] * 0.5f;
						RunPipeline(deformers, spaces, in channels, flags, frameInput, midOutput, null);

						var thresholdSq = deviationThreshold * deviationThreshold;
						for (var i = 0; i < n; i++)
						{
							var linear = (bakedBase[i] + fullOutput[i]) * 0.5f;
							if ((midOutput[i] - linear).sqrMagnitude > thresholdSq)
							{
								curved = true;
								break;
							}
						}
					}

					if (curved)
					{
						AddIntermediateFrame(result, name, weight, 0.25f, deformers, spaces,
							in channels, flags, deltaVertices, deltaNormals, deltaTangents,
							bakedBase, frameInput, scratchOutput, outputDelta, scaledNormals, scaledTangents);

						for (var i = 0; i < n; i++)
							outputDelta[i] = midOutput[i] - bakedBase[i];
						ScaleDeltas(deltaNormals, deltaTangents, 0.5f, scaledNormals, scaledTangents, n);
						result.AddBlendShapeFrame(name, weight * 0.5f, outputDelta, scaledNormals, scaledTangents);

						AddIntermediateFrame(result, name, weight, 0.75f, deformers, spaces,
							in channels, flags, deltaVertices, deltaNormals, deltaTangents,
							bakedBase, frameInput, scratchOutput, outputDelta, scaledNormals, scaledTangents);
					}

					for (var i = 0; i < n; i++)
						outputDelta[i] = fullOutput[i] - bakedBase[i];
					result.AddBlendShapeFrame(name, weight, outputDelta, deltaNormals, deltaTangents);
				}
			}
		}

		/// <summary>係数 t の中間フレームを 1 枚ベイクして追加する</summary>
		private static void AddIntermediateFrame(Mesh result, string name, float weight, float t,
			List<DeformerBase> deformers, DeformSpace[] spaces,
			in SourceChannels channels, DeformDataFlags flags,
			Vector3[] deltaVertices, Vector3[] deltaNormals, Vector3[] deltaTangents,
			Vector3[] bakedBase, Vector3[] frameInput, Vector3[] frameOutput, Vector3[] outputDelta,
			Vector3[] scaledNormals, Vector3[] scaledTangents)
		{
			var n = channels.VertexCount;
			for (var i = 0; i < n; i++)
				frameInput[i] = channels.Vertices[i] + deltaVertices[i] * t;
			RunPipeline(deformers, spaces, in channels, flags, frameInput, frameOutput, null);

			for (var i = 0; i < n; i++)
				outputDelta[i] = frameOutput[i] - bakedBase[i];
			ScaleDeltas(deltaNormals, deltaTangents, t, scaledNormals, scaledTangents, n);
			result.AddBlendShapeFrame(name, weight * t, outputDelta, scaledNormals, scaledTangents);
		}

		private static void ScaleDeltas(Vector3[] normals, Vector3[] tangents, float t,
			Vector3[] outNormals, Vector3[] outTangents, int count)
		{
			for (var i = 0; i < count; i++)
			{
				outNormals[i] = normals[i] * t;
				outTangents[i] = tangents[i] * t;
			}
		}

		private static MeshBuffers CreateBuffers(in SourceChannels channels, DeformDataFlags flags,
			Vector3[] overrideVertices)
		{
			// エディタ実行のため Read/Write 設定に関わらずメッシュを読める前提で実装する。
			// (プレイヤービルドでの制約はベイクには関係しない)
			var buffers = new MeshBuffers { Length = channels.VertexCount };
			var vertices = overrideVertices ?? channels.Vertices;

			buffers.Vertices = new NativeArray<float3>(channels.VertexCount, Allocator.TempJob,
				NativeArrayOptions.UninitializedMemory);
			for (var i = 0; i < channels.VertexCount; i++)
				buffers.Vertices[i] = vertices[i];

			if (channels.Normals != null && channels.Normals.Length == buffers.Length)
			{
				buffers.Normals = new NativeArray<float3>(channels.Normals.Length, Allocator.TempJob,
					NativeArrayOptions.UninitializedMemory);
				for (var i = 0; i < channels.Normals.Length; i++)
					buffers.Normals[i] = channels.Normals[i];
			}

			if (channels.Tangents != null && channels.Tangents.Length == buffers.Length)
			{
				buffers.Tangents = new NativeArray<float4>(channels.Tangents.Length, Allocator.TempJob,
					NativeArrayOptions.UninitializedMemory);
				for (var i = 0; i < channels.Tangents.Length; i++)
					buffers.Tangents[i] = channels.Tangents[i];
			}

			if (channels.Uvs != null && channels.Uvs.Length == buffers.Length)
			{
				buffers.UVs = new NativeArray<float2>(channels.Uvs.Length, Allocator.TempJob,
					NativeArrayOptions.UninitializedMemory);
				for (var i = 0; i < channels.Uvs.Length; i++)
					buffers.UVs[i] = channels.Uvs[i];
			}

			if ((flags & DeformDataFlags.OriginalVertices) != 0)
			{
				// スナップショットは「そのパスの入力」(フレームなら base + delta)
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
