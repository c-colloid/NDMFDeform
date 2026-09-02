using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace MeshModifier.NDMFDeform.Core
{
	/// <summary>
	/// 参照レンダラー(体など)の「変形後メッシュ」の解決結果。
	/// Editor 側のフックが、参照レンダラーに DeformStack がある場合に
	/// プレビューでベイク済みのメッシュを返す(重ね着: 体 → 下着 → 服 の連鎖用)。
	/// Version はメッシュインスタンスが同じまま中身が更新された場合(プレビューの
	/// ホットパスは同一 Mesh の頂点だけを更新する)にキャッシュを無効化するための通し番号。
	/// </summary>
	public struct ReferenceMeshInfo
	{
		public Mesh Mesh;
		public int Version;
	}

	/// <summary>
	/// 参照レンダラーのメッシュを「見た目のワールド空間」の頂点配列へ展開する補助。
	/// スキンメッシュは衣装側のパイプラインと同じ LBS(ボーン × バインドポーズ)で持ち上げるため、
	/// 同じアバター上の体と衣装が同じ空間で比較できる。
	/// </summary>
	public static class ReferenceSurfaceUtility
	{
		/// <summary>
		/// Editor 側が設定するフック。参照レンダラーに DeformStack があり、
		/// その変形後メッシュを使うべき場合に非 null の Mesh を返す。
		/// 未設定・該当なしの場合はレンダラーの sharedMesh を使う。
		/// </summary>
		public static Func<Renderer, ReferenceMeshInfo> DeformedMeshResolver;

		/// <summary>参照レンダラーのメッシュ(変形後があればそれ)を返す</summary>
		public static ReferenceMeshInfo ResolveMesh(Renderer renderer)
		{
			if (renderer == null)
				return default;

			var resolver = DeformedMeshResolver;
			if (resolver != null)
			{
				var info = resolver(renderer);
				if (info.Mesh != null)
					return info;
			}

			if (renderer is SkinnedMeshRenderer smr)
				return new ReferenceMeshInfo { Mesh = smr.sharedMesh };
			if (renderer.TryGetComponent<MeshFilter>(out var filter))
				return new ReferenceMeshInfo { Mesh = filter.sharedMesh };
			return default;
		}

		/// <summary>三角形トポロジのサブメッシュのインデックスを連結して返す(その他は無視)</summary>
		public static int[] CollectTriangles(Mesh mesh)
		{
			if (mesh == null)
				return Array.Empty<int>();

			var total = 0;
			var subCount = mesh.subMeshCount;
			var parts = new int[subCount][];
			for (var s = 0; s < subCount; s++)
			{
				if (mesh.GetTopology(s) != MeshTopology.Triangles)
					continue;
				parts[s] = mesh.GetTriangles(s);
				total += parts[s].Length;
			}

			if (subCount == 1 && parts[0] != null)
				return parts[0];

			var result = new int[total];
			var offset = 0;
			for (var s = 0; s < subCount; s++)
			{
				if (parts[s] == null)
					continue;
				Array.Copy(parts[s], 0, result, offset, parts[s].Length);
				offset += parts[s].Length;
			}
			return result;
		}

		/// <summary>
		/// 参照レンダラーの頂点をワールド空間へ展開する。
		/// applyBlendShapes が真ならレンダラーに設定されている現在のブレンドシェイプ重みを
		/// 適用してから(区分線形補間。最終フレームを超える重みは最終フレームで頭打ち)、
		/// スキン行列で持ち上げる。非スキンは localToWorldMatrix。
		/// </summary>
		public static bool TryBuildWorldGeometry(Renderer renderer, bool applyBlendShapes,
			out Vector3[] vertices, out int[] triangles)
		{
			return TryBuildWorldGeometry(renderer, applyBlendShapes, false, out vertices, out triangles);
		}

		/// <summary>
		/// <see cref="TryBuildWorldGeometry(Renderer, bool, out Vector3[], out int[])"/> に加えて表裏を制御する。
		/// ワールド変換が鏡映(行列式が負。負のスケールによるミラー)なら三角形の巻き順が反転して
		/// 法線が内向きになるため自動で巻き順を戻す。flipNormals はそれに加えて明示的に反転する。
		/// </summary>
		public static bool TryBuildWorldGeometry(Renderer renderer, bool applyBlendShapes, bool flipNormals,
			out Vector3[] vertices, out int[] triangles)
		{
			vertices = null;
			triangles = null;
			if (renderer == null)
				return false;

			var mesh = ResolveMesh(renderer).Mesh;
			if (mesh == null || mesh.vertexCount == 0)
				return false;

			vertices = mesh.vertices;
			triangles = CollectTriangles(mesh);
			var mirrored = IsMirrored(renderer, mesh);

			if (renderer is SkinnedMeshRenderer smr)
			{
				if (applyBlendShapes)
					ApplyBlendShapeWeights(mesh, smr, vertices);

				var skinning = VertexSkinning.Build(smr.transform, mesh, Allocator.TempJob);
				var native = new NativeArray<float3>(vertices.Length, Allocator.TempJob,
					NativeArrayOptions.UninitializedMemory);
				try
				{
					for (var i = 0; i < vertices.Length; i++)
						native[i] = vertices[i];
					skinning.ScheduleToWorld(native, default).Complete();
					for (var i = 0; i < vertices.Length; i++)
						vertices[i] = native[i];
				}
				finally
				{
					native.Dispose();
					skinning.Dispose();
				}
			}
			else
			{
				var m = renderer.transform.localToWorldMatrix;
				for (var i = 0; i < vertices.Length; i++)
					vertices[i] = m.MultiplyPoint3x4(vertices[i]);
			}

			if (mirrored != flipNormals)
			{
				// GetTriangles はコピーを返すためインプレースで反転してよい
				for (var t = 0; t + 2 < triangles.Length; t += 3)
				{
					var tmp = triangles[t + 1];
					triangles[t + 1] = triangles[t + 2];
					triangles[t + 2] = tmp;
				}
			}
			return true;
		}

		/// <summary>メッシュ空間 → ワールドの写像が鏡映(行列式が負)かどうか</summary>
		public static bool IsMirrored(Renderer renderer, Mesh mesh)
		{
			if (renderer is SkinnedMeshRenderer smr)
			{
				var bones = smr.bones;
				var bindposes = mesh != null ? mesh.bindposes : null;
				if (bones != null && bindposes != null)
				{
					var count = Mathf.Min(bones.Length, bindposes.Length);
					for (var i = 0; i < count; i++)
					{
						if (bones[i] == null)
							continue;
						return (bones[i].localToWorldMatrix * bindposes[i]).determinant < 0f;
					}
				}
			}
			return renderer.transform.localToWorldMatrix.determinant < 0f;
		}

		/// <summary>
		/// レンダラーの現在のブレンドシェイプ重みを vertices に適用する(インプレース)。
		/// Unity と同じ区分線形補間。負の重みは 0 として扱う。
		/// </summary>
		public static void ApplyBlendShapeWeights(Mesh mesh, SkinnedMeshRenderer smr, Vector3[] vertices)
		{
			var shapeCount = mesh.blendShapeCount;
			var rendererMesh = smr.sharedMesh;
			if (shapeCount == 0 || rendererMesh == null)
				return;
			// 変形後メッシュ(シェイプ名・順序は元と同じ)にも対応できるよう、番号で対応付ける
			shapeCount = Mathf.Min(shapeCount, rendererMesh.blendShapeCount);

			var n = vertices.Length;
			if (mesh.vertexCount != n)
				return;

			Vector3[] delta = null;
			Vector3[] previous = null;
			for (var s = 0; s < shapeCount; s++)
			{
				var weight = Mathf.Max(0f, smr.GetBlendShapeWeight(s));
				if (weight <= 0f)
					continue;

				delta ??= new Vector3[n];
				var frameCount = mesh.GetBlendShapeFrameCount(s);
				var previousWeight = 0f;
				var previousValid = false;
				var applied = false;
				for (var f = 0; f < frameCount; f++)
				{
					var frameWeight = mesh.GetBlendShapeFrameWeight(s, f);
					mesh.GetBlendShapeFrameVertices(s, f, delta, null, null);

					if (weight <= frameWeight)
					{
						var span = frameWeight - previousWeight;
						var t = span > 1e-6f ? (weight - previousWeight) / span : 1f;
						for (var i = 0; i < n; i++)
						{
							var from = previousValid ? previous[i] : Vector3.zero;
							vertices[i] += from + (delta[i] - from) * t;
						}
						applied = true;
						break;
					}

					previous ??= new Vector3[n];
					Array.Copy(delta, previous, n);
					previousWeight = frameWeight;
					previousValid = true;
				}

				// 最終フレームを超える重みは最終フレームで頭打ち
				if (!applied && previousValid)
				{
					for (var i = 0; i < n; i++)
						vertices[i] += previous[i];
				}
			}
		}
	}

	/// <summary>
	/// 参照レンダラーごとの最近接点クエリ構造(MeshSurface)のキャッシュ。
	/// 同じ体を参照する複数の Body Fit デフォーマ、およびベイク・プレビューの
	/// 繰り返しで BVH 構築を共有する。メッシュ・ボーン姿勢・シェイプ重み・Transform の
	/// いずれかが変わるとハッシュが変わり、次回取得時に作り直す。
	/// </summary>
	public static class ReferenceSurfaceCache
	{
		private sealed class Entry
		{
			public Renderer Renderer;
			public MeshSurface Surface;
			public int Hash;

			/// <summary>
			/// SkinnedMeshRenderer.bones はアクセスごとに配列を複製するため、
			/// ホットパス(約 20Hz)のハッシュ計算用に保持する(構築時に取り直す)
			/// </summary>
			public Transform[] Bones;
		}

		// キー = レンダラー ID とシェイプ適用有無の組(同じ体を別設定で参照するデフォーマが
		// 互いのエントリを作り直して、取得済みの MeshSurfaceData を無効化しないようにする)
		private static readonly Dictionary<long, Entry> Entries = new Dictionary<long, Entry>();

		private static long KeyOf(Renderer renderer, bool applyBlendShapes, bool flipNormals)
		{
			return ((long)renderer.GetInstanceID() << 2) | (applyBlendShapes ? 2L : 0L) | (flipNormals ? 1L : 0L);
		}

		/// <summary>表面データを構築した回数(キャッシュの再利用を検証するテスト用)</summary>
		public static int BuildCount { get; private set; }

		/// <summary>
		/// 参照レンダラーの表面データを返す。必要なら構築する。
		/// 三角形が無い・メッシュが無い場合は false。
		/// 返した MeshSurfaceData は次回の TryGet / Dispose まで有効(ジョブ完了後に再取得すること)。
		/// </summary>
		public static bool TryGet(Renderer renderer, bool applyBlendShapes, out MeshSurfaceData data)
		{
			return TryGet(renderer, applyBlendShapes, false, out data);
		}

		public static bool TryGet(Renderer renderer, bool applyBlendShapes, bool flipNormals,
			out MeshSurfaceData data)
		{
			data = default;
			if (renderer == null)
				return false;

			Sweep();

			var key = KeyOf(renderer, applyBlendShapes, flipNormals);
			Entries.TryGetValue(key, out var entry);
			var bones = entry?.Bones ?? (renderer as SkinnedMeshRenderer)?.bones;
			var hash = ComputeHash(renderer, bones, applyBlendShapes);
			if (entry != null && entry.Hash == hash && entry.Surface != null && entry.Surface.IsCreated)
			{
				data = entry.Surface.Data;
				return true;
			}

			if (!ReferenceSurfaceUtility.TryBuildWorldGeometry(renderer, applyBlendShapes, flipNormals,
				    out var vertices, out var triangles))
			{
				Evict(key);
				return false;
			}

			BuildCount++;
			var surface = MeshSurface.Build(vertices, triangles, Allocator.Persistent);
			if (!surface.IsCreated)
			{
				surface.Dispose();
				Evict(key);
				return false;
			}

			if (entry == null)
			{
				entry = new Entry { Renderer = renderer };
				Entries[key] = entry;
			}
			entry.Surface?.Dispose();
			entry.Surface = surface;
			entry.Hash = hash;
			// ボーン配列は構築時点のものを保持する(差し替えは次回のフルハッシュ不一致で検出されないため、
			// 参照側は bones を差し替えたら Evict すること)
			entry.Bones = (renderer as SkinnedMeshRenderer)?.bones;
			data = surface.Data;
			return true;
		}

		/// <summary>キャッシュ済みエントリ数(テスト用)</summary>
		public static int Count => Entries.Count;

		public static void Evict(Renderer renderer)
		{
			if (renderer == null)
				return;
			for (var variant = 0; variant < 4; variant++)
				Evict(KeyOf(renderer, (variant & 2) != 0, (variant & 1) != 0));
		}

		private static void Evict(long key)
		{
			if (!Entries.TryGetValue(key, out var entry))
				return;
			entry.Surface?.Dispose();
			Entries.Remove(key);
		}

		/// <summary>破棄されたレンダラーのエントリを回収する</summary>
		private static void Sweep()
		{
			List<long> dead = null;
			foreach (var pair in Entries)
			{
				if (pair.Value.Renderer == null)
					(dead ??= new List<long>()).Add(pair.Key);
			}
			if (dead == null)
				return;
			foreach (var key in dead)
				Evict(key);
		}

		public static void DisposeAll()
		{
			foreach (var entry in Entries.Values)
				entry.Surface?.Dispose();
			Entries.Clear();
		}

		private static int ComputeHash(Renderer renderer, Transform[] bones, bool applyBlendShapes)
		{
			unchecked
			{
				var info = ReferenceSurfaceUtility.ResolveMesh(renderer);
				var mesh = info.Mesh;
				var h = 17;
				h = h * 31 + (mesh != null ? mesh.GetInstanceID() : 0);
				h = h * 31 + (mesh != null ? mesh.vertexCount : 0);
				h = h * 31 + info.Version;
				h = h * 31 + (applyBlendShapes ? 1 : 0);

				if (renderer is SkinnedMeshRenderer smr)
				{
					if (bones != null && bones.Length > 0)
					{
						foreach (var bone in bones)
							h = h * 31 + (bone != null ? bone.localToWorldMatrix.GetHashCode() : 0);
					}
					else
					{
						h = h * 31 + smr.transform.localToWorldMatrix.GetHashCode();
					}

					if (applyBlendShapes && smr.sharedMesh != null)
					{
						var count = smr.sharedMesh.blendShapeCount;
						for (var s = 0; s < count; s++)
						{
							var w = smr.GetBlendShapeWeight(s);
							if (w != 0f)
								h = h * 31 + s * 7919 + w.GetHashCode();
						}
					}
				}
				else
				{
					h = h * 31 + renderer.transform.localToWorldMatrix.GetHashCode();
				}
				return h;
			}
		}

#if UNITY_EDITOR
		// ドメインリロード・エディタ終了時に常駐 NativeArray を回収する
		[UnityEditor.InitializeOnLoadMethod]
		private static void HookEditorLifecycle()
		{
			UnityEditor.AssemblyReloadEvents.beforeAssemblyReload += DisposeAll;
			UnityEditor.EditorApplication.quitting += DisposeAll;
		}
#endif
	}
}
