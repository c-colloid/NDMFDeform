using System;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace MeshModifier.NDMFDeform.Core
{
	/// <summary>パーツ情報付きで参照表面を構築するための要求(Body Fit のパーツ円柱モード)</summary>
	public sealed class PartRequest
	{
		public HumanoidSkeleton Skeleton;

		/// <summary>衣装アーマチュアの関節をアバターの関節に対応付ける許容距離(m)</summary>
		public float JointTolerance = 0.03f;

		/// <summary>三角形のパーツマスクに含める重みの下限</summary>
		public float MaskThreshold = 0.25f;

		public int Hash()
		{
			unchecked
			{
				var h = Skeleton != null ? Skeleton.StateHash : 0;
				h = h * 31 + JointTolerance.GetHashCode();
				h = h * 31 + MaskThreshold.GetHashCode();
				return h;
			}
		}
	}

	/// <summary>
	/// パーツごとの円柱半径プロファイル R(h, θ)(ジョブから読める読み取り専用ビュー)。
	/// 軸上の点から放射状にレイを飛ばし、そのパーツの三角形との最初の交点までの距離を格子に持つ。
	/// レイが当たらない格子は近傍から補間済み。ヒットが無いパーツは Usable = 0 で、Radius は NaN。
	/// </summary>
	public struct BodyPartProfiles
	{
		public const int HCount = 32;
		public const int ThetaCount = 32;

		/// <summary>h の格子範囲(軸区間 [0, 1] の外側も少し覆う)</summary>
		public const float HStart = -0.25f;
		public const float HEnd = 1.25f;

		[ReadOnly] public NativeArray<PartAxis> Axes;
		[ReadOnly] public NativeArray<float> Radius;
		[ReadOnly] public NativeArray<int> Usable;

		public bool IsCreated => Radius.IsCreated && Radius.Length == HumanoidSkeleton.PartCount * HCount * ThetaCount;

		public static int CellIndex(int part, int hi, int ti)
		{
			return (part * HCount + hi) * ThetaCount + ti;
		}

		/// <summary>格子の連続座標(u: h 方向、v: θ 方向。セル中心が整数)</summary>
		public static void GridCoords(float h, float theta, out float u, out float v)
		{
			u = (h - HStart) / (HEnd - HStart) * HCount - 0.5f;
			var vv = (theta + math.PI) / (2f * math.PI) * ThetaCount - 0.5f;
			vv = vv % ThetaCount;
			if (vv < 0f)
				vv += ThetaCount;
			v = vv;
		}

		/// <summary>格子値の双線形サンプル(h は端でクランプ、θ は周期)</summary>
		public static float SampleGrid(in NativeArray<float> grid, int part, float h, float theta)
		{
			GridCoords(h, theta, out var u, out var v);
			u = math.clamp(u, 0f, HCount - 1f);
			var u0 = (int)math.floor(u);
			var u1 = math.min(u0 + 1, HCount - 1);
			var fu = u - u0;
			var v0 = (int)math.floor(v) % ThetaCount;
			var v1 = (v0 + 1) % ThetaCount;
			var fv = v - math.floor(v);
			var a = grid[CellIndex(part, u0, v0)];
			var b = grid[CellIndex(part, u0, v1)];
			var c = grid[CellIndex(part, u1, v0)];
			var d = grid[CellIndex(part, u1, v1)];
			return math.lerp(math.lerp(a, b, fv), math.lerp(c, d, fv), fu);
		}

		public bool IsUsable(int part)
		{
			return IsCreated && part > 0 && part < HumanoidSkeleton.PartCount && Usable[part] != 0 &&
			       Axes[part].Valid != 0;
		}

		public float SampleRadius(int part, float h, float theta)
		{
			return SampleGrid(in Radius, part, h, theta);
		}
	}

	/// <summary>BodyPartProfiles の所有者</summary>
	public sealed class PartProfileData : IDisposable
	{
		public BodyPartProfiles Data;

		public bool IsCreated => Data.IsCreated;

		public void Dispose()
		{
			if (Data.Axes.IsCreated) Data.Axes.Dispose();
			if (Data.Radius.IsCreated) Data.Radius.Dispose();
			if (Data.Usable.IsCreated) Data.Usable.Dispose();
			Data = default;
		}

		/// <summary>各格子から放射状レイを飛ばして半径を求める</summary>
		[BurstCompile]
		public struct ProfileRayJob : IJobParallelFor
		{
			public MeshSurfaceData surface;
			[ReadOnly] public NativeArray<PartAxis> axes;
			[WriteOnly] public NativeArray<float> radius;
			public float maxDistance;

			public void Execute(int index)
			{
				const int cellsPerPart = BodyPartProfiles.HCount * BodyPartProfiles.ThetaCount;
				var part = index / cellsPerPart;
				var rem = index % cellsPerPart;
				var hi = rem / BodyPartProfiles.ThetaCount;
				var ti = rem % BodyPartProfiles.ThetaCount;
				var axis = axes[part];
				if (part == 0 || axis.Valid == 0)
				{
					radius[index] = float.NaN;
					return;
				}

				var h = BodyPartProfiles.HStart +
				        (hi + 0.5f) / BodyPartProfiles.HCount * (BodyPartProfiles.HEnd - BodyPartProfiles.HStart);
				var theta = -math.PI + (ti + 0.5f) / BodyPartProfiles.ThetaCount * (2f * math.PI);
				axis.RayFrom(h, theta, out var origin, out var direction);
				radius[index] = surface.Raycast(origin, direction, maxDistance, 1 << part, out var t, out _)
					? t
					: float.NaN;
			}
		}

		/// <summary>
		/// 表面データと骨格からプロファイルを構築する。
		/// レイの当たらない格子は近傍平均で埋める(ヒットの無いパーツは NaN のまま・Usable = 0)。
		/// </summary>
		public static PartProfileData Build(in MeshSurfaceData surface, HumanoidSkeleton skeleton, Allocator allocator)
		{
			var result = new PartProfileData();
			var partCount = HumanoidSkeleton.PartCount;
			var cellCount = partCount * BodyPartProfiles.HCount * BodyPartProfiles.ThetaCount;
			result.Data.Axes = new NativeArray<PartAxis>(partCount, allocator);
			for (var p = 0; p < partCount; p++)
				result.Data.Axes[p] = skeleton.Axes[p];
			result.Data.Radius = new NativeArray<float>(cellCount, allocator, NativeArrayOptions.UninitializedMemory);
			result.Data.Usable = new NativeArray<int>(partCount, allocator);

			// レイの最大距離: 骨格の広がりから十分大きく取る
			var extent = 0f;
			foreach (var axis in skeleton.Axes)
			{
				if (axis.Valid != 0)
					extent = math.max(extent, axis.Length);
			}
			new ProfileRayJob
			{
				surface = surface,
				axes = result.Data.Axes,
				radius = result.Data.Radius,
				maxDistance = math.max(extent * 4f, 2f),
			}.Schedule(cellCount, 64).Complete();

			FillMissing(result.Data.Radius, result.Data.Usable);
			return result;
		}

		/// <summary>
		/// NaN の格子を有効な近傍(h 方向はクランプ、θ 方向は周期)の平均で繰り返し埋める。
		/// ヒットが少なすぎるパーツは使えない扱いにして NaN のまま残す。
		/// </summary>
		public static void FillMissing(NativeArray<float> radius, NativeArray<int> usable)
		{
			const int H = BodyPartProfiles.HCount;
			const int T = BodyPartProfiles.ThetaCount;
			const int MinHits = 8;
			var scratch = new float[H * T];
			for (var part = 1; part < HumanoidSkeleton.PartCount; part++)
			{
				var baseIndex = BodyPartProfiles.CellIndex(part, 0, 0);
				var hits = 0;
				for (var i = 0; i < H * T; i++)
				{
					if (!float.IsNaN(radius[baseIndex + i]))
						hits++;
				}
				if (hits < MinHits)
				{
					usable[part] = 0;
					for (var i = 0; i < H * T; i++)
						radius[baseIndex + i] = float.NaN;
					continue;
				}
				usable[part] = 1;

				for (var pass = 0; pass < H + T && hits < H * T; pass++)
				{
					for (var i = 0; i < H * T; i++)
						scratch[i] = radius[baseIndex + i];
					for (var hi = 0; hi < H; hi++)
					for (var ti = 0; ti < T; ti++)
					{
						var i = hi * T + ti;
						if (!float.IsNaN(scratch[i]))
							continue;
						var sum = 0f;
						var count = 0;
						void Add(int hh, int tt)
						{
							var v = scratch[hh * T + ((tt % T) + T) % T];
							if (float.IsNaN(v))
								return;
							sum += v;
							count++;
						}
						if (hi > 0) Add(hi - 1, ti);
						if (hi < H - 1) Add(hi + 1, ti);
						Add(hi, ti - 1);
						Add(hi, ti + 1);
						if (count > 0)
						{
							radius[baseIndex + i] = sum / count;
							hits++;
						}
					}
				}
			}
		}
	}

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
			return TryBuildWorldGeometry(renderer, ResolveMesh(renderer).Mesh, applyBlendShapes, flipNormals,
				out vertices, out triangles);
		}

		/// <summary>解決済みのメッシュを渡す版(呼び出し側で ResolveMesh を 1 回だけ行う)</summary>
		public static bool TryBuildWorldGeometry(Renderer renderer, Mesh mesh, bool applyBlendShapes, bool flipNormals,
			out Vector3[] vertices, out int[] triangles)
		{
			vertices = null;
			triangles = null;
			if (renderer == null || mesh == null || mesh.vertexCount == 0)
				return false;

			vertices = mesh.vertices;
			triangles = CollectTriangles(mesh);
			var mirrored = IsMirrored(renderer, mesh);

			if (renderer is SkinnedMeshRenderer smr)
			{
				if (applyBlendShapes)
					ApplyBlendShapeWeights(mesh, smr, vertices);
				SkinToWorld(smr.transform, mesh, vertices);
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

		/// <summary>
		/// 頂点配列をレンダラーのスキン行列(頂点ごとの LBS)でワールド空間へ変換する(インプレース)。
		/// 非スキンのレンダラーは localToWorldMatrix。
		/// </summary>
		public static void SkinToWorld(Transform rendererTransform, Mesh mesh, Vector3[] vertices)
		{
			var skinning = VertexSkinning.Build(rendererTransform, mesh, Allocator.TempJob);
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
			public PartProfileData Profiles;
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

		private static long KeyOf(Renderer renderer, bool applyBlendShapes, bool flipNormals, bool withParts)
		{
			return ((long)renderer.GetInstanceID() << 3) | (withParts ? 4L : 0L) | (applyBlendShapes ? 2L : 0L) |
			       (flipNormals ? 1L : 0L);
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
			return TryGet(renderer, applyBlendShapes, flipNormals, null, out data, out _);
		}

		/// <summary>
		/// parts を渡すと、三角形のパーツマスク付きの表面データと、パーツごとの半径プロファイルも構築する。
		/// 体がスキンメッシュならボーンウェイトから、そうでなければ連結成分の重心に最も近い
		/// 軸区間からパーツを決める。
		/// </summary>
		public static bool TryGet(Renderer renderer, bool applyBlendShapes, bool flipNormals, PartRequest parts,
			out MeshSurfaceData data, out BodyPartProfiles profiles)
		{
			return TryGet(renderer, applyBlendShapes, flipNormals, parts, out data, out profiles, out _);
		}

		/// <summary>
		/// surfaceHash に表面データの内容を表すハッシュ(メッシュ・ボーン・シェイプ重み・パーツ要求)を返す。
		/// 参照側が表面に依存する派生データ(衣装のパーツ所属など)のキャッシュキーに使う。
		/// </summary>
		public static bool TryGet(Renderer renderer, bool applyBlendShapes, bool flipNormals, PartRequest parts,
			out MeshSurfaceData data, out BodyPartProfiles profiles, out int surfaceHash)
		{
			data = default;
			profiles = default;
			surfaceHash = 0;
			if (renderer == null)
				return false;
			if (parts != null && parts.Skeleton == null)
				parts = null;

			Sweep();

			var withParts = parts != null;
			var key = KeyOf(renderer, applyBlendShapes, flipNormals, withParts);
			Entries.TryGetValue(key, out var entry);
			var bones = entry?.Bones ?? (renderer as SkinnedMeshRenderer)?.bones;
			// 参照メッシュの解決(重ね着では参照先のベイクを伴う)は 1 回だけ行い、ハッシュと構築で共有する
			var info = ReferenceSurfaceUtility.ResolveMesh(renderer);
			var hash = ComputeHash(renderer, info, bones, applyBlendShapes);
			if (withParts)
				hash = unchecked(hash * 31 + parts.Hash());
			if (entry != null && entry.Hash == hash && entry.Surface != null && entry.Surface.IsCreated &&
			    (!withParts || (entry.Profiles != null && entry.Profiles.IsCreated)))
			{
				data = entry.Surface.Data;
				if (withParts)
					profiles = entry.Profiles.Data;
				surfaceHash = hash;
				return true;
			}

			if (!ReferenceSurfaceUtility.TryBuildWorldGeometry(renderer, info.Mesh, applyBlendShapes, flipNormals,
				    out var vertices, out var triangles))
			{
				Evict(key);
				return false;
			}

			BuildCount++;
			int[] masks = null;
			if (withParts)
				masks = BuildPartMasks(renderer, info.Mesh, vertices, triangles, parts);
			var surface = MeshSurface.Build(vertices, triangles, Allocator.Persistent, masks);
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
			entry.Profiles?.Dispose();
			entry.Profiles = withParts ? PartProfileData.Build(in surface.Data, parts.Skeleton, Allocator.Persistent) : null;
			if (withParts)
				profiles = entry.Profiles.Data;
			entry.Hash = hash;
			// ボーン配列は構築時点のものを保持する(差し替えは次回のフルハッシュ不一致で検出されないため、
			// 参照側は bones を差し替えたら Evict すること)
			entry.Bones = (renderer as SkinnedMeshRenderer)?.bones;
			data = surface.Data;
			surfaceHash = hash;
			return true;
		}

		/// <summary>キャッシュ済みエントリ数(テスト用)</summary>
		public static int Count => Entries.Count;

		/// <summary>
		/// 三角形のパーツマスクを求める。スキンメッシュはボーン → パーツ対応とウェイトから、
		/// ウェイトの無いメッシュは連結成分の重心に最も近い軸区間から決める。
		/// </summary>
		private static int[] BuildPartMasks(Renderer renderer, Mesh mesh, Vector3[] vertices, int[] triangles,
			PartRequest parts)
		{
			PartWeights[] weights = null;
			if (renderer is SkinnedMeshRenderer smr && mesh != null && mesh.GetBonesPerVertex().Length == vertices.Length)
			{
				var boneParts = parts.Skeleton.MapBones(smr.bones, parts.JointTolerance);
				weights = PartAssignment.FromBoneWeights(mesh, boneParts);
			}
			else
			{
				weights = new PartWeights[vertices.Length];
				var adjacency = MeshAdjacency.Build(vertices, triangles);
				var groups = PartAssignment.ConnectedComponents(adjacency, triangles, out var groupCount);
				PartAssignment.AssignGroupsBySegment(weights, vertices, groups, groupCount, parts.Skeleton);
			}
			return PartAssignment.TriangleMasks(triangles, weights, parts.MaskThreshold);
		}

		public static void Evict(Renderer renderer)
		{
			if (renderer == null)
				return;
			for (var variant = 0; variant < 8; variant++)
				Evict(KeyOf(renderer, (variant & 2) != 0, (variant & 1) != 0, (variant & 4) != 0));
		}

		private static void Evict(long key)
		{
			if (!Entries.TryGetValue(key, out var entry))
				return;
			entry.Surface?.Dispose();
			entry.Profiles?.Dispose();
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
			{
				entry.Surface?.Dispose();
				entry.Profiles?.Dispose();
			}
			Entries.Clear();
		}

		private static int ComputeHash(Renderer renderer, ReferenceMeshInfo info, Transform[] bones,
			bool applyBlendShapes)
		{
			unchecked
			{
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
