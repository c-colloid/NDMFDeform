using System.Collections.Generic;
using MeshModifier.NDMFDeform.Core;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;

namespace MeshModifier.NDMFDeform.Editor
{
	/// <summary>
	/// プレビュー専用のホットパスベイクキャッシュ。
	/// DeformBakeCore.Bake は毎回メッシュ全体(全シェイプデータ込み)を複製するため、
	/// ドラッグ編集の 20Hz 再ベイクでは複製と配列コピーが支配的なコストになる。
	/// ここではスタック単位でベイク済みメッシュと NativeArray を常駐させ、
	/// パラメータ変更だけの再ベイクは「ジョブ実行 + SetVertices」のみで済ませる。
	///
	/// シェイプ状態(アクティブ集合・シェイプ設定)が変わった時のみフルベイクし直す。
	/// アクティブなシェイプがある状態でホットパスを使うと、そのシェイプのデルタは
	/// 直前のフルベイク時点のまま(ドラッグ中のみ僅かに古い)になるため、
	/// 編集が落ち着いたタイミングでエディタ更新フックがフルベイクで追いつく。
	/// ビルドはこのキャッシュを使わず、常に DeformBakeCore.Bake(フル)を用いる。
	/// </summary>
	public static class DeformPreviewBakeCache
	{
		/// <summary>ホットパス使用後、シェイプデルタを追いかけ再ベイクするまでの静穏時間(秒)</summary>
		private const double StaleShapeRefreshDelay = 0.35;

		public sealed class Entry
		{
			public Mesh Source;
			public Mesh Baked;
			public int VertexCount;

			/// <summary>プリスキン済み(見た目のワールド空間)のソース頂点</summary>
			public NativeArray<float3> SourceVertices;

			/// <summary>頂点ごとのスキン行列(フルベイク毎に作り直す)</summary>
			internal VertexSkinning Skinning;
			public NativeArray<float2> Uvs;
			public NativeArray<float4> Colors;
			public NativeArray<float3> Work;
			public int ShapeStateHash;
			public bool ShapesStale;
			public double LastBakeTime;

			/// <summary>
			/// ベイク結果が変わるたびに増える通し番号。ホットパスは同じ Mesh インスタンスの頂点だけを
			/// 更新するため、Baked を参照する側(重ね着の参照表面キャッシュ)はこれで更新を検知する。
			/// 呼ばれただけでは増えない(頂点の内容ハッシュが変わった時のみ)。
			/// </summary>
			public int BakeSerial;

			/// <summary>直近のベイク結果(頂点)の内容ハッシュ</summary>
			public int ContentHash;

			// 追いかけフルベイク用に最後の呼び出しコンテキストを保持する
			public DeformStack Stack;
			public Transform RendererTransform;
			public HashSet<string> ActiveShapes;

			public void Dispose()
			{
				if (Baked != null)
					Object.DestroyImmediate(Baked);
				Baked = null;
				if (SourceVertices.IsCreated) SourceVertices.Dispose();
				if (Uvs.IsCreated) Uvs.Dispose();
				if (Colors.IsCreated) Colors.Dispose();
				if (Work.IsCreated) Work.Dispose();
				Skinning.Dispose();
			}
		}

		private static readonly Dictionary<int, Entry> Entries = new Dictionary<int, Entry>();
		private static bool _hooked;

		/// <summary>
		/// スタックのプレビュー用ベイク済みメッシュを返す(キャッシュ所有。破棄しないこと)。
		/// 有効なデフォーマが無い場合は null。
		/// </summary>
		public static Entry Bake(DeformStack stack, Mesh source, Transform rendererTransform,
			HashSet<string> activeShapes)
		{
			if (stack == null || source == null || rendererTransform == null)
				return null;

			EnsureHooks();

			var deformers = DeformBakeCore.CollectEnabledDeformers(stack);
			if (deformers.Count == 0)
			{
				Evict(stack.GetInstanceID());
				return null;
			}

			var flags = DeformDataFlags.None;
			foreach (var d in deformers)
				flags |= d.DataFlags;

			var key = stack.GetInstanceID();
			Entries.TryGetValue(key, out var entry);
			var shapeHash = ComputeShapeStateHash(stack, source, activeShapes);

			// 法線・タンジェントを書き換えるデフォーマがある場合はチャンネル復元が要るためフルベイク
			var fastPathOk = entry != null &&
			                 entry.Baked != null &&
			                 entry.Source == source &&
			                 entry.VertexCount == source.vertexCount &&
			                 entry.ShapeStateHash == shapeHash &&
			                 entry.Skinning.IsCreated &&
			                 (flags & (DeformDataFlags.Normals | DeformDataFlags.Tangents)) == 0;

			if (!fastPathOk)
				return FullBake(key, entry, stack, source, rendererTransform, activeShapes, shapeHash);

			// ---- ホットパス: 頂点のみ更新 ----
			foreach (var d in deformers)
				d.PrepareBake(source);

			NativeArray<float3>.Copy(entry.SourceVertices, entry.Work);
			var buffers = new MeshBuffers
			{
				Vertices = entry.Work,
				Length = entry.VertexCount,
			};
			if ((flags & DeformDataFlags.OriginalVertices) != 0)
				buffers.OriginalVertices = entry.SourceVertices; // 読み取り専用契約
			if ((flags & DeformDataFlags.UVs) != 0)
			{
				EnsureUvs(entry, source);
				if (entry.Uvs.IsCreated)
					buffers.UVs = entry.Uvs;
			}
			if ((flags & DeformDataFlags.Colors) != 0)
			{
				EnsureColors(entry, source);
				if (entry.Colors.IsCreated)
					buffers.Colors = entry.Colors;
			}

			var handle = default(JobHandle);
			try
			{
				// 入力(SourceVertices)はプリスキン済みワールド空間。軸変換は worldToLocal のみ
				foreach (var deformer in deformers)
				{
					var space = new DeformSpace(GetWorldToAxis(deformer.Axis), rendererTransform);
					handle = deformer.Schedule(in buffers, in space, handle);
				}
				// 変形結果をメッシュ空間へ書き戻す(逆スキン行列)
				handle = entry.Skinning.ScheduleToMesh(entry.Work, handle);
			}
			finally
			{
				// 常駐配列なので Dispose はしない(未完了ジョブだけ完了させる)
				handle.Complete();
			}

			entry.Baked.SetVertices(entry.Work.Reinterpret<Vector3>());
			if (stack.Normals == DeformStack.NormalsMode.Recalculate)
			{
				entry.Baked.RecalculateNormals();
				if (source.uv.Length == entry.VertexCount)
					entry.Baked.RecalculateTangents();
			}
			entry.Baked.RecalculateBounds();

			// 内容が変わった時だけ通し番号を進める(参照側のキャッシュが無駄に作り直されないように)
			var contentHash = HashVertices(entry.Work);
			if (contentHash != entry.ContentHash)
			{
				entry.ContentHash = contentHash;
				entry.BakeSerial++;
			}
			entry.LastBakeTime = EditorApplication.timeSinceStartup;
			entry.Stack = stack;
			entry.RendererTransform = rendererTransform;
			entry.ActiveShapes = activeShapes;
			// アクティブなシェイプのデルタは前回フルベイク時点のまま → 静穏後に追いかける
			if (source.blendShapeCount > 0 && (activeShapes == null || activeShapes.Count > 0))
				entry.ShapesStale = true;

			return entry;
		}

		private static Entry FullBake(int key, Entry entry, DeformStack stack, Mesh source,
			Transform rendererTransform, HashSet<string> activeShapes, int shapeHash)
		{
			var options = new DeformBakeOptions
			{
				RebakeBlendShapes = true,
				ShapesToRebake = activeShapes,
			};
			var baked = DeformBakeCore.Bake(stack, source, rendererTransform, options);
			if (baked == null)
			{
				Evict(key);
				return null;
			}
			baked.hideFlags = HideFlags.HideAndDontSave;

			if (entry == null)
			{
				entry = new Entry();
				Entries[key] = entry;
			}

			if (entry.Baked != null)
				Object.DestroyImmediate(entry.Baked);
			entry.Baked = baked;

			if (entry.Source != source || entry.VertexCount != source.vertexCount ||
			    !entry.SourceVertices.IsCreated)
			{
				if (entry.SourceVertices.IsCreated) entry.SourceVertices.Dispose();
				if (entry.Uvs.IsCreated) entry.Uvs.Dispose();
				if (entry.Colors.IsCreated) entry.Colors.Dispose();
				if (entry.Work.IsCreated) entry.Work.Dispose();

				entry.SourceVertices = new NativeArray<float3>(source.vertexCount, Allocator.Persistent,
					NativeArrayOptions.UninitializedMemory);
				entry.Work = new NativeArray<float3>(source.vertexCount, Allocator.Persistent,
					NativeArrayOptions.UninitializedMemory);
			}

			// スキン行列とプリスキン済み頂点はフルベイク毎に作り直す
			// (ボーンが動いている可能性があるため)
			entry.Skinning.Dispose();
			entry.Skinning = VertexSkinning.Build(rendererTransform, source, Allocator.Persistent);
			var sourceVertices = source.vertices;
			for (var i = 0; i < sourceVertices.Length; i++)
				entry.SourceVertices[i] = sourceVertices[i];
			entry.Skinning.ScheduleToWorld(entry.SourceVertices, default).Complete();

			entry.Source = source;
			entry.VertexCount = source.vertexCount;
			entry.ShapeStateHash = shapeHash;
			entry.ShapesStale = false;
			entry.BakeSerial++;
			var bakedVertices = new NativeArray<float3>(baked.vertexCount, Allocator.Temp,
				NativeArrayOptions.UninitializedMemory);
			var bakedArray = baked.vertices;
			for (var i = 0; i < bakedArray.Length; i++)
				bakedVertices[i] = bakedArray[i];
			entry.ContentHash = HashVertices(bakedVertices);
			bakedVertices.Dispose();
			entry.LastBakeTime = EditorApplication.timeSinceStartup;
			entry.Stack = stack;
			entry.RendererTransform = rendererTransform;
			entry.ActiveShapes = activeShapes;
			return entry;
		}

		/// <summary>
		/// 現在重みが非 0 のシェイプ名の集合(プレビューで再ベイクする対象)。
		/// NDMF プレビューと、参照先スタックをプレビューと同じ引数でベイクする
		/// DeformedReferenceResolver が同じ判定を使う。
		/// </summary>
		public static HashSet<string> GetActiveShapeNames(SkinnedMeshRenderer smr)
		{
			var names = new HashSet<string>();
			var mesh = smr != null ? smr.sharedMesh : null;
			if (mesh == null)
				return names;

			for (var i = 0; i < mesh.blendShapeCount; i++)
			{
				if (!Mathf.Approximately(smr.GetBlendShapeWeight(i), 0f))
					names.Add(mesh.GetBlendShapeName(i));
			}
			return names;
		}

		/// <summary>頂点配列の内容ハッシュ(ビット表現の合成)</summary>
		private static int HashVertices(NativeArray<float3> vertices)
		{
			unchecked
			{
				var h = 17;
				for (var i = 0; i < vertices.Length; i++)
				{
					var v = vertices[i];
					h = h * 31 + math.asint(v.x);
					h = h * 31 + math.asint(v.y);
					h = h * 31 + math.asint(v.z);
				}
				return h;
			}
		}

		private static void EnsureColors(Entry entry, Mesh source)
		{
			if (entry.Colors.IsCreated)
				return;
			var colors = source.colors;
			if (colors.Length != entry.VertexCount)
				return;
			entry.Colors = new NativeArray<float4>(colors.Length, Allocator.Persistent,
				NativeArrayOptions.UninitializedMemory);
			for (var i = 0; i < colors.Length; i++)
			{
				var c = colors[i];
				entry.Colors[i] = new float4(c.r, c.g, c.b, c.a);
			}
		}

		private static void EnsureUvs(Entry entry, Mesh source)
		{
			if (entry.Uvs.IsCreated)
				return;
			var uvs = source.uv;
			if (uvs.Length != entry.VertexCount)
				return;
			entry.Uvs = new NativeArray<float2>(uvs.Length, Allocator.Persistent,
				NativeArrayOptions.UninitializedMemory);
			for (var i = 0; i < uvs.Length; i++)
				entry.Uvs[i] = uvs[i];
		}

		private static int ComputeShapeStateHash(DeformStack stack, Mesh source,
			HashSet<string> activeShapes)
		{
			unchecked
			{
				var h = 17;
				h = h * 31 + source.blendShapeCount;
				h = h * 31 + (stack.NonlinearShapeCorrection ? 1 : 0);
				h = h * 31 + (int)stack.Normals;
				foreach (var entry in stack.BlendShapeOverrides)
				{
					h = h * 31 + (entry.shapeName?.GetHashCode() ?? 0);
					h = h * 31 + (int)entry.mode;
				}
				if (activeShapes == null)
				{
					h = h * 31 - 1;
				}
				else
				{
					// 集合なので順序に依存しない合成にする
					var setHash = 0;
					foreach (var name in activeShapes)
						setHash ^= name.GetHashCode();
					h = h * 31 + setHash;
					h = h * 31 + activeShapes.Count;
				}
				return h;
			}
		}

		private static float4x4 GetWorldToAxis(Transform axis)
		{
			var m = axis.worldToLocalMatrix;
			return new float4x4(m.GetColumn(0), m.GetColumn(1), m.GetColumn(2), m.GetColumn(3));
		}

		// ---- ライフサイクル ----

		private static void EnsureHooks()
		{
			if (_hooked)
				return;
			_hooked = true;
			EditorApplication.update += OnEditorUpdate;
			AssemblyReloadEvents.beforeAssemblyReload += DisposeAll;
			EditorApplication.quitting += DisposeAll;
		}

		private static void OnEditorUpdate()
		{
			RefreshStaleEntries(EditorApplication.timeSinceStartup);
		}

		/// <summary>
		/// ホットパスで古くなったシェイプデルタを、編集が落ち着いた後にフルベイクで追いかける。
		/// 破棄済みスタックのエントリはここで回収する(テストからも直接呼べる)。
		/// </summary>
		public static void RefreshStaleEntries(double now)
		{
			List<int> toEvict = null;
			List<int> toRefresh = null;
			foreach (var pair in Entries)
			{
				var entry = pair.Value;
				if (entry.Stack == null || entry.Source == null)
				{
					(toEvict ??= new List<int>()).Add(pair.Key);
					continue;
				}
				if (entry.ShapesStale && entry.RendererTransform != null &&
				    now - entry.LastBakeTime > StaleShapeRefreshDelay)
				{
					(toRefresh ??= new List<int>()).Add(pair.Key);
				}
			}

			if (toEvict != null)
			{
				foreach (var key in toEvict)
					Evict(key);
			}

			if (toRefresh != null)
			{
				foreach (var key in toRefresh)
				{
					if (!Entries.TryGetValue(key, out var entry))
						continue;
					var shapeHash = ComputeShapeStateHash(entry.Stack, entry.Source, entry.ActiveShapes);
					FullBake(key, entry, entry.Stack, entry.Source, entry.RendererTransform,
						entry.ActiveShapes, shapeHash);
				}
				SceneView.RepaintAll();
			}
		}

		private static void Evict(int key)
		{
			if (!Entries.TryGetValue(key, out var entry))
				return;
			entry.Dispose();
			Entries.Remove(key);
		}

		private static void DisposeAll()
		{
			foreach (var entry in Entries.Values)
				entry.Dispose();
			Entries.Clear();
		}
	}
}
