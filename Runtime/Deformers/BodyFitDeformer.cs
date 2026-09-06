using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Serialization;
using static Unity.Mathematics.math;
using float3 = Unity.Mathematics.float3;
using float4 = Unity.Mathematics.float4;
using float4x4 = Unity.Mathematics.float4x4;

namespace MeshModifier.NDMFDeform.Core
{
	/// <summary>
	/// 参照した体のメッシュ(Body)に沿って衣装を寄せる / 離すデフォーマ(非対応衣装の体合わせ)。
	///
	/// 各頂点について体表面の最近接点と符号付き距離 d(外側が正)を求め、
	/// 体との隙間が [minGap, maxGap] の帯に収まるよう法線方向へ動かす:
	/// - d &lt; minGap(めり込み・近すぎ)→ minGap まで押し出す
	/// - d &gt; maxGap(離れすぎ)→ maxGap まで引き寄せる(pullIn が真のときのみ)
	/// - 帯の中はそのまま
	/// ぴったりした衣装は minGap = maxGap(帯幅 0)、ブカッとした衣装は pullIn を切るか
	/// maxGap を大きく取り「めり込みだけ直す」設定にする。
	///
	/// 適用範囲は二重球(innerRadius の内側で 100%、outerRadius まで滑らかに減衰)で指定する。
	/// 最近接点写像は凹んだ部位(脇・股)で隣り合う頂点が離れた体表面へ写り布が折れるため、
	/// 変位ベクトルを衣装メッシュの隣接で平滑化してから適用し、最後にもう一度
	/// minGap を保証する(enforceMinGap)。
	///
	/// ブレンドシェイプ: 既定(FixedDisplacement)では基本形状で求めた変位をそのまま
	/// 各シェイプフレームにも足す(衣装のシェイプ形状を維持し、ベイクも軽い)。
	/// RefitEachFrame はフレームごとに体へ再フィットする(衣装のシェイプが体の基本形状へ潰れる)。
	/// 体側のブレンドシェイプは useBodyBlendShapes でレンダラーの現在の重みを反映する。
	///
	/// ボーンウェイトは変更しない(衣装の元のウェイトのまま)。ウェイト転写は別機能。
	/// </summary>
	[DeformerMeta(Name = "Body Fit", Category = DeformerCategory.Shape,
	              Description = "参照した体のメッシュに沿って衣装を寄せる / 離す(非対応衣装の体合わせ)")]
	[AddComponentMenu("NDMF Deform/Deformers/Body Fit")]
	public class BodyFitDeformer : DeformerBase, IRendererReferences
	{
		public enum FitRegion
		{
			/// <summary>二重球の内側にのみ適用(内半径で 100%、外半径まで減衰)</summary>
			Sphere = 0,

			/// <summary>メッシュ全体に適用</summary>
			WholeMesh = 1,
		}

		public enum BlendShapeFitMode
		{
			/// <summary>基本形状で求めた変位を各シェイプフレームにもそのまま足す(シェイプ形状を維持)</summary>
			FixedDisplacement = 0,

			/// <summary>シェイプフレームごとに体へ再フィットする</summary>
			RefitEachFrame = 1,
		}

		public enum FitMode
		{
			/// <summary>
			/// パーツ円柱: ヒューマノイド骨格のパーツごとに、ボーン軸からの放射方向へ移動する。
			/// 装飾は下地との相対オフセットを保ち、腕の装飾が胴へ吸われない(推奨。ヒューマノイド必須)
			/// </summary>
			PartCylinder = 0,

			/// <summary>最近接表面: 体表面の最近接点へ向けて移動する(骨格が無い場合のフォールバック)</summary>
			NearestSurface = 1,
		}

		public enum PartGrouping
		{
			/// <summary>グループ化しない(頂点ごとの所属)</summary>
			None = 0,

			/// <summary>3D の連結成分(位置で溶接)を単位に投票して所属パーツを揃える</summary>
			ConnectedComponents = 1,

			/// <summary>UV 島を単位に投票して所属パーツを揃える(推奨。UV が無ければ連結成分)</summary>
			UVIslands = 2,
		}

		public enum PartSource
		{
			/// <summary>ボーンウェイトと体の形状を、ボーン対応付けの信頼度で混ぜて投票する(推奨)</summary>
			Auto = 0,

			/// <summary>ボーンウェイトのみ(ウェイトの無い衣装は所属なし)</summary>
			BoneWeights = 1,

			/// <summary>体の形状(パーツ表面からの隙間)のみ。ウェイトを無視する</summary>
			Geometry = 2,
		}

		[SerializeField, Tooltip("沿わせる体のレンダラー(SkinnedMeshRenderer / MeshRenderer)。衣装と同じアバター上のものを指定する")]
		private Renderer body;

		[SerializeField, Tooltip("追加で参照する体のレンダラー(首から上が別メッシュのアバターの頭など)。Body と合わせて 1 つの表面として扱う")]
		private List<Renderer> additionalBodies = new List<Renderer>();

		[SerializeField, Tooltip("体のレンダラーに設定されている現在のブレンドシェイプ重みを体の形状に反映する")]
		private bool useBodyBlendShapes = true;

		[SerializeField, Range(0f, 1f), Tooltip("全体の効き。0.5 なら目標位置までの半分だけ動く")]
		private float factor = 1f;

		[SerializeField, Tooltip("適用範囲。Sphere は二重球の内側のみ、WholeMesh はメッシュ全体")]
		private FitRegion region = FitRegion.Sphere;

		[SerializeField, Min(0f), Tooltip("100% 適用する球の半径(シーンでは実線)")]
		private float innerRadius = 0.15f;

		[SerializeField, Min(0f), Tooltip("適用が 0 になる球の半径(シーンでは点線)。内半径との間で滑らかに減衰する")]
		private float outerRadius = 0.25f;

		[SerializeField, Tooltip("体との最小の隙間(m)。これより近い / めり込んでいる頂点を押し出す")]
		private float minGap = 0.005f;

		[SerializeField, Tooltip("離れすぎた頂点を体へ引き寄せる。切るとめり込みの解消だけを行う(ブカッとした衣装向け)")]
		private bool pullIn = true;

		[SerializeField, Tooltip("体との最大の隙間(m)。これより遠い頂点を引き寄せる(pullIn が有効なとき)。既定の 2 cm は「帯」設定(5 mm〜2 cm は動かさない)。ぴったりにするなら minGap と同じ値にする")]
		private float maxGap = 0.02f;

		[SerializeField, Min(0f), Tooltip("体表面を探す距離の上限(m)。これより体から離れた頂点は対象外(上限の 75% から滑らかに効きが減る)")]
		private float searchDistance = 0.1f;

		[SerializeField, Range(0, 30), Tooltip("変位の平滑化回数。凹んだ部位で布が折れるのを抑える(0 で無効)")]
		private int smoothIterations = 4;

		[SerializeField, Range(0f, 1f), Tooltip("平滑化 1 回あたりの強さ")]
		private float smoothStrength = 0.5f;

		[SerializeField, Tooltip("平滑化後にもう一度 minGap を保証する(めり込みを残さない)")]
		private bool enforceMinGap = true;

		[SerializeField, Tooltip("ブレンドシェイプの扱い。FixedDisplacement は衣装のシェイプ形状を維持、RefitEachFrame はフレームごとに再フィット")]
		private BlendShapeFitMode blendShapes = BlendShapeFitMode.FixedDisplacement;

		[SerializeField, Tooltip("体の表裏を反転する(法線が内向きのメッシュ用。負のスケールによる反転は自動で補正される)")]
		private bool flipBodyNormals;

		[SerializeField, Tooltip("フィット方式。PartCylinder はヒューマノイド骨格のパーツ軸から放射状に動かす(推奨)。骨格が無ければ NearestSurface に自動で切り替わる")]
		private FitMode fitMode = FitMode.PartCylinder;

		[SerializeField, Tooltip("パーツ所属の根拠。Auto はボーンウェイトと体の形状を、ボーン対応付けの信頼度で混ぜて投票する")]
		private PartSource partSource = PartSource.Auto;

		[SerializeField, FormerlySerializedAs("decorationGrouping"),
		 Tooltip("所属を揃える単位。UV 島(推奨)/ 3D の連結成分 / なし(頂点ごと)")]
		private PartGrouping partGrouping = PartGrouping.UVIslands;

		[SerializeField, Min(0f), Tooltip("この大きさ(バウンズ対角、m)以下のグループ(紐・リボンなどの装飾)は投票の比率に関わらず 1 パーツに揃える")]
		private float decorationMaxSize = 0.25f;

		[SerializeField, Range(0.5f, 1f), Tooltip("大きなグループ(袖・身頃)を 1 パーツに揃えるのに必要な投票の比率。届かないグループ(ボディスーツなど)は頂点ごとの所属になる")]
		private float islandConfidence = 0.7f;

		[SerializeField, Range(0, 10), Tooltip("グループ境界(縫い目)で所属を混ぜる回数。0 でも同じ位置の頂点は揃える")]
		private int seamBlend = 3;

		[SerializeField, HideInInspector] private List<PartOverride> partOverrides = new List<PartOverride>();

		[SerializeField, Tooltip("NearestSurface でも、自分のパーツの体表面だけを探す(腕の装飾が胴へ吸われるのを防ぐ)")]
		private bool partFilter = true;

		[SerializeField, Min(0f), Tooltip("衣装アーマチュアの関節をアバターの関節へ対応付ける許容距離(m)")]
		private float jointTolerance = 0.03f;

		[SerializeField, Range(0, 10), Tooltip("パーツ円柱: 縫い目(所属が混ざる頂点とその隣)の変位ベクトルを衣装の隣接で平滑化する回数(強さは smoothStrength)。0 で無効")]
		private int seamSmoothIterations;

		[SerializeField, Tooltip("パーツ円柱: 縫い目だけでなく全頂点の変位ベクトルを平滑化する")]
		private bool seamSmoothAll;

		[SerializeField, Tooltip("パーツ円柱: 所属が混ざる頂点は、所属パーツの軸(線分)の最寄り点から放射する(折れ線軸の近似。関節で放射方向を扇状につなぐ)")]
		private bool jointFan;

		[SerializeField, Tooltip("肩の軸を脊椎(胴の軸)から上腕関節までの線分にし、胴の上端(肩甲帯: 上腕関節の高さより上)の胴所属を肩へ移す")]
		private bool shoulderAxis;

		[SerializeField, Range(0f, 0.3f), Tooltip("肩甲帯の下端。上腕関節の高さ(胴の軸の h)からこれだけ下までを肩の軸で扱う")]
		private float shoulderCapMargin = 0.1f;

		[SerializeField, Tooltip("首の関節より上にある胴所属(襟など)を首パーツへ移し、首・頭のプロファイルで動かす(首から上が別レンダラーなら additionalBodies に入れる)。実験的")]
		private bool neckCap;

		[SerializeField, Tooltip("パーツ円柱: 小さなグループ(紐・ボタンなど独立した UV 島 / 連結成分の装飾)は頂点ごとに放射させず、付け根(他のグループに接する頂点)の変位に追従させて輪郭を保つ。実験的")]
		private bool rigidDecorations;

		[SerializeField, Min(0f), Tooltip("付け根に追従させるグループの大きさ(バウンズ対角、m)の上限")]
		private float rigidMaxSize = 0.2f;

		[SerializeField, Range(0f, 1f), Tooltip("パーツ円柱: 引き寄せで軸からの距離を縮める率の上限。0 で無制限。既定の 0.15 は元の 85% までしか寄せず、残りは隙間として残す(布の潰れと輪郭の縮みを抑える)")]
		private float maxShrink = 0.15f;

		[SerializeField, Tooltip("同じ体を参照して連続する Body Fit と 1 つのグループとして合成する(各 Body Fit の変位を同じ入力から求め、重なりでは重み付き平均。順序に依らず、縮み率の上限や factor が累積しない)")]
		private bool fitGroup = true;

		[SerializeField] private Transform axisOverride;

		public Renderer Body { get => body; set => body = value; }

		/// <summary>追加で参照する体のレンダラー(Body と結合して 1 つの参照表面にする)</summary>
		public List<Renderer> AdditionalBodies => additionalBodies;
		public bool UseBodyBlendShapes { get => useBodyBlendShapes; set => useBodyBlendShapes = value; }
		public float Factor { get => factor; set => factor = Mathf.Clamp01(value); }
		public FitRegion Region { get => region; set => region = value; }
		public float InnerRadius { get => innerRadius; set => innerRadius = Mathf.Max(0f, value); }
		public float OuterRadius { get => outerRadius; set => outerRadius = Mathf.Max(0f, value); }
		public float MinGap { get => minGap; set => minGap = value; }
		public bool PullIn { get => pullIn; set => pullIn = value; }
		public float MaxGap { get => maxGap; set => maxGap = value; }
		public float SearchDistance { get => searchDistance; set => searchDistance = Mathf.Max(0f, value); }
		public int SmoothIterations { get => smoothIterations; set => smoothIterations = Mathf.Clamp(value, 0, 30); }
		public float SmoothStrength { get => smoothStrength; set => smoothStrength = Mathf.Clamp01(value); }
		public bool EnforceMinGap { get => enforceMinGap; set => enforceMinGap = value; }
		public BlendShapeFitMode BlendShapes { get => blendShapes; set => blendShapes = value; }
		public bool FlipBodyNormals { get => flipBodyNormals; set => flipBodyNormals = value; }
		public FitMode Mode { get => fitMode; set => fitMode = value; }
		public PartSource Source { get => partSource; set => partSource = value; }
		public PartGrouping Grouping { get => partGrouping; set => partGrouping = value; }
		public float DecorationMaxSize { get => decorationMaxSize; set => decorationMaxSize = Mathf.Max(0f, value); }
		public float IslandConfidence { get => islandConfidence; set => islandConfidence = Mathf.Clamp(value, 0.5f, 1f); }
		public int SeamBlend { get => seamBlend; set => seamBlend = Mathf.Clamp(value, 0, 10); }

		/// <summary>グループ(UV 島 / 連結成分)ごとのパーツ所属の手動上書き</summary>
		public List<PartOverride> PartOverrides => partOverrides;

		/// <summary>直近のパーツ所属計算のグループごとの判定(インスペクタ用。グループ化なしでは空)</summary>
		public IReadOnlyList<PartGroupReport> PartReports => _partReports;

		/// <summary>
		/// 直近のパーツ所属計算の頂点ごとの所属(縫い目の混合・軸区間外の差し替え後)をコピーして返す。
		/// 未計算なら null(インスペクタの可視化・検証用)。
		/// </summary>
		public PartWeights[] GetCostumeParts()
		{
			if (!_costumeParts.IsCreated)
				return null;
			var copy = new PartWeights[_costumeParts.Length];
			_costumeParts.CopyTo(copy);
			return copy;
		}
		public bool PartFilter { get => partFilter; set => partFilter = value; }
		public float JointTolerance { get => jointTolerance; set => jointTolerance = Mathf.Max(0f, value); }
		public int SeamSmoothIterations { get => seamSmoothIterations; set => seamSmoothIterations = Mathf.Clamp(value, 0, 10); }
		public bool SeamSmoothAll { get => seamSmoothAll; set => seamSmoothAll = value; }
		public bool JointFan { get => jointFan; set => jointFan = value; }
		public bool ShoulderAxis { get => shoulderAxis; set => shoulderAxis = value; }
		public float ShoulderCapMargin { get => shoulderCapMargin; set => shoulderCapMargin = Mathf.Clamp(value, 0f, 0.3f); }
		public bool NeckCap { get => neckCap; set => neckCap = value; }
		public bool RigidDecorations { get => rigidDecorations; set => rigidDecorations = value; }
		public bool FitGroup { get => fitGroup; set => fitGroup = value; }

		/// <summary>直近の PrepareBake で決めたフィットグループの大きさ(1 = 単独)</summary>
		public int FitGroupSize => _groupSize;
		public float RigidMaxSize { get => rigidMaxSize; set => rigidMaxSize = Mathf.Max(0f, value); }
		public float MaxShrink { get => maxShrink; set => maxShrink = Mathf.Clamp01(value); }

		/// <summary>直近のパーツ所属計算のグループ分け(頂点 → グループ番号、-1 = 所属なし)のコピー。未計算・グループ化なしなら null</summary>
		public int[] GetPartGroups()
		{
			return _lastGroups == null ? null : (int[])_lastGroups.Clone();
		}

		/// <summary>骨格の差し替え(テスト用。null なら体 / 衣装の親の Animator から作る)</summary>
		[System.NonSerialized] public HumanoidSkeleton SkeletonOverride;

		/// <summary>直近の PrepareBake でパーツ情報が使えたか</summary>
		public bool PartsAvailable => _partsReady;

		/// <summary>直近の PrepareBake で実際に使う方式</summary>
		public FitMode EffectiveMode => _effectiveMode;

		public override Transform Axis => axisOverride != null ? axisOverride : transform;

		public override DeformDataFlags DataFlags => DeformDataFlags.Vertices;

		// ---- ベイク用キャッシュ(シリアライズ対象外) ----

		// 参照表面(ReferenceSurfaceCache が所有。PrepareBake で取得し直す)
		[System.NonSerialized] private MeshSurfaceData _surface;
		[System.NonSerialized] private bool _surfaceReady;

		// 衣装メッシュの隣接(平滑化用。ソースメッシュが変わらない限り再利用)
		[System.NonSerialized] private Mesh _adjacencyMesh;
		[System.NonSerialized] private int _adjacencyVertexCount;
		[System.NonSerialized] private NativeArray<int> _adjStart;
		[System.NonSerialized] private NativeArray<int> _adjList;

		// 基本形状パスで求めた変位(重み込み・factor 抜き)。FixedDisplacement のシェイプフレームで使う
		[System.NonSerialized] private NativeArray<float3> _baseDisplacement;
		[System.NonSerialized] private int _passIndex;
		[System.NonSerialized] private int _vertexCount;

		// パーツ情報(骨格が使える場合)。_costumeParts は骨格が無くても頂点数分確保する(全 None)
		[System.NonSerialized] private BodyPartProfiles _profiles;
		[System.NonSerialized] private bool _partsReady;
		[System.NonSerialized] private FitMode _effectiveMode = FitMode.NearestSurface;
		[System.NonSerialized] private NativeArray<PartWeights> _costumeParts;

		// 装飾の追従: 頂点 → 変位を写す元の頂点(-1 = 自分の放射変位)。_costumeParts と同じ寿命
		[System.NonSerialized] private NativeArray<int> _follow;
		[System.NonSerialized] private int[] _followManaged;
		[System.NonSerialized] private int _costumePartsKey;
		[System.NonSerialized] private int _surfaceHash;
		[System.NonSerialized] private readonly List<PartGroupReport> _partReports = new List<PartGroupReport>();

		// 直近のパーツ所属計算のグループ分け(上書きの参照をベイクと同じ規則でグループへ解決するため)
		[System.NonSerialized] private int[] _lastGroups;
		[System.NonSerialized] private int _lastGroupCount;
		[System.NonSerialized] private UVIslandAnalysis _lastAnalysis;
		[System.NonSerialized] private Vector3[] _lastVertices;
		[System.NonSerialized] private MeshAdjacency _adjacencyManaged;
		[System.NonSerialized] private readonly List<Renderer> _bodyList = new List<Renderer>();

		// フィットグループ(§14.3): 同じ体を参照して連続する Body Fit。先頭がグループの共有バッファを持つ
		[System.NonSerialized] private BodyFitDeformer _groupLeader;
		[System.NonSerialized] private bool _groupLast;
		[System.NonSerialized] private int _groupSize = 1;
		[System.NonSerialized] private FitGroupBuffers _group;
		[System.NonSerialized] private HumanoidSkeleton _cachedSkeleton;
		[System.NonSerialized] private Animator _cachedAnimator;
		[System.NonSerialized] private Avatar _cachedAvatar;

		private void OnValidate()
		{
			innerRadius = Mathf.Max(0f, innerRadius);
			outerRadius = Mathf.Max(innerRadius, outerRadius);
			maxGap = Mathf.Max(minGap, maxGap);
			searchDistance = Mathf.Max(0f, searchDistance);
		}

		protected virtual void Reset()
		{
			AutoDetectBody();
			FitSphereToParentStack();
		}

		private void OnDisable()
		{
			DisposeNative();
		}

		private void OnDestroy()
		{
			DisposeNative();
		}

		private void DisposeNative()
		{
			if (_adjStart.IsCreated) _adjStart.Dispose();
			if (_adjList.IsCreated) _adjList.Dispose();
			if (_baseDisplacement.IsCreated) _baseDisplacement.Dispose();
			if (_costumeParts.IsCreated) _costumeParts.Dispose();
			if (_follow.IsCreated) _follow.Dispose();
			_group?.DisposeNow();
			_group = null;
			_groupLeader = null;
			_adjacencyMesh = null;
			_adjacencyManaged = null;
			_adjacencyVertexCount = 0;
			_cachedSkeleton = null;
			_cachedAnimator = null;
			_cachedAvatar = null;
			_costumePartsKey = 0;
			_surfaceReady = false;
			ResetPartsState();
		}

		/// <summary>
		/// パーツ情報が使えない結果(Body 未設定・自己参照・表面の構築失敗)を反映する。
		/// PrepareBake の途中終了でも PartsAvailable / EffectiveMode / PartReports が
		/// 「直近の呼び出し」を表すように、各早期 return の直前で呼ぶ。
		/// (EnsureCostumeParts のキャッシュ命中ではレポートを保つため、成功経路の先頭では呼ばない)
		/// </summary>
		private void ResetPartsState()
		{
			_partsReady = false;
			_effectiveMode = FitMode.NearestSurface;
			ClearPartReports();
		}

		private void ClearPartReports()
		{
			_partReports.Clear();
			_lastGroups = null;
			_lastGroupCount = 0;
			_lastAnalysis = null;
			_lastVertices = null;
		}

#if UNITY_EDITOR
		// ドメインリロード前に、生きているインスタンスの常駐 NativeArray を回収する
		[UnityEditor.InitializeOnLoadMethod]
		private static void HookEditorLifecycle()
		{
			UnityEditor.AssemblyReloadEvents.beforeAssemblyReload += DisposeAllInstances;
			UnityEditor.EditorApplication.quitting += DisposeAllInstances;
		}

		private static void DisposeAllInstances()
		{
			foreach (var deformer in Resources.FindObjectsOfTypeAll<BodyFitDeformer>())
				deformer.DisposeNative();
		}
#endif

		public void CollectReferencedRenderers(List<Renderer> results)
		{
			if (body != null)
				results.Add(body);
			foreach (var r in additionalBodies)
			{
				if (r != null && !results.Contains(r))
					results.Add(r);
			}
		}

		/// <summary>参照する体のレンダラー一覧(Body + 追加。null・衣装自身・重複を除く)を _bodyList に集める</summary>
		private List<Renderer> CollectBodies()
		{
			_bodyList.Clear();
			var own = GetOwnRenderer();
			if (body != null && body != own)
				_bodyList.Add(body);
			foreach (var r in additionalBodies)
			{
				if (r != null && r != own && !_bodyList.Contains(r))
					_bodyList.Add(r);
			}
			return _bodyList;
		}

		/// <summary>親の DeformStack が付いているレンダラー(衣装自身)</summary>
		public Renderer GetOwnRenderer()
		{
			var stack = GetComponentInParent<DeformStack>();
			if (stack == null)
				return null;
			stack.TryGetComponent<Renderer>(out var renderer);
			return renderer;
		}

		/// <summary>
		/// 同じアバター(Transform ルート)配下から体のレンダラーを推定して設定する。
		/// VRChat の慣例に従い、名前が "Body" の SkinnedMeshRenderer を優先し、
		/// 無ければ名前に "body" を含むもの、それも無ければ最も頂点数の多い
		/// SkinnedMeshRenderer を使う。衣装自身は除外する。
		/// </summary>
		public bool AutoDetectBody()
		{
			var own = GetOwnRenderer();
			var root = transform.root;
			SkinnedMeshRenderer exact = null;
			SkinnedMeshRenderer partial = null;
			SkinnedMeshRenderer largest = null;
			var largestCount = 0;
			foreach (var smr in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
			{
				if (smr == own || smr.sharedMesh == null)
					continue;
				var n = smr.name;
				if (string.Equals(n, "Body", System.StringComparison.OrdinalIgnoreCase))
				{
					exact = smr;
					break;
				}
				if (partial == null && n.IndexOf("body", System.StringComparison.OrdinalIgnoreCase) >= 0)
					partial = smr;
				if (smr.sharedMesh.vertexCount > largestCount)
				{
					largest = smr;
					largestCount = smr.sharedMesh.vertexCount;
				}
			}
			var found = exact != null ? exact : (partial != null ? partial : largest);
			if (found == null)
				return false;
			body = found;
			// 名前に body を含む他のレンダラー(Body / Body2 のように体が分かれているアバター)は追加の体にする
			additionalBodies.Clear();
			foreach (var smr in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
			{
				if (smr == own || smr == found || smr.sharedMesh == null)
					continue;
				if (smr.name.IndexOf("body", System.StringComparison.OrdinalIgnoreCase) >= 0)
					additionalBodies.Add(smr);
			}
			return true;
		}

		/// <summary>
		/// 二重球を衣装(親スタックのレンダラー)全体を覆う大きさ・位置に合わせる。
		/// 内半径は見た目のバウンズの半対角、外半径はその 1.25 倍。
		/// </summary>
		public bool FitSphereToParentStack()
		{
			var stack = GetComponentInParent<DeformStack>();
			if (stack == null)
				return false;

			Bounds bounds;
			Matrix4x4 meshToWorld;
			if (stack.TryGetComponent<SkinnedMeshRenderer>(out var smr) && smr.sharedMesh != null)
			{
				var baked = new Mesh();
				smr.BakeMesh(baked, true);
				baked.RecalculateBounds();
				bounds = baked.bounds;
				DestroyImmediate(baked);
				meshToWorld = stack.transform.localToWorldMatrix;
			}
			else if (stack.TryGetComponent<MeshFilter>(out var mf) && mf.sharedMesh != null)
			{
				bounds = mf.sharedMesh.bounds;
				meshToWorld = stack.transform.localToWorldMatrix;
			}
			else
			{
				return false;
			}

			var worldCenter = meshToWorld.MultiplyPoint3x4(bounds.center);
			var worldExtents = Vector3.Scale(meshToWorld.lossyScale, bounds.extents);
			var radius = Mathf.Max(worldExtents.magnitude, 0.01f);

			if (axisOverride == null)
				transform.position = worldCenter;

			// 半径は軸空間で解釈されるため、軸のスケールで割ってワールド半径に合わせる
			var axisScale = Axis.lossyScale;
			var scale = Mathf.Max(Mathf.Abs(axisScale.x), Mathf.Max(Mathf.Abs(axisScale.y), Mathf.Abs(axisScale.z)));
			if (scale > 1e-6f)
				radius /= scale;
			innerRadius = radius;
			outerRadius = radius * 1.25f;
			return true;
		}

#if UNITY_EDITOR
		public override void DescribeHandles(IHandleBuilder h)
		{
			if (region != FitRegion.Sphere)
				return;

			// SphereMask と同じ二重球。inner/outer をペア宣言し、ホバー矢印が互いのリングを指すようにする
			h.RadiusSlider(nameof(innerRadius), HandleAxis.Y, HandleLineStyle.Solid, 1f,
				pairProperty: nameof(outerRadius));
			h.RadiusSlider(nameof(outerRadius), HandleAxis.Y, HandleLineStyle.Dotted, 1f,
				pairProperty: nameof(innerRadius));
			h.Circle(HandleAxis.X, 0f, nameof(innerRadius));
			h.Circle(HandleAxis.Y, 0f, nameof(innerRadius));
			h.Circle(HandleAxis.Z, 0f, nameof(innerRadius));
			h.Circle(HandleAxis.X, 0f, nameof(outerRadius), HandleLineStyle.Dotted);
			h.Circle(HandleAxis.Y, 0f, nameof(outerRadius), HandleLineStyle.Dotted);
			h.Circle(HandleAxis.Z, 0f, nameof(outerRadius), HandleLineStyle.Dotted);
		}
#endif

		public override void PrepareBake(Mesh source)
		{
			_passIndex = 0;
			_surfaceReady = false;
			_vertexCount = source != null ? source.vertexCount : 0;
			DetermineGroup();

			if (body == null || source == null || _vertexCount == 0)
			{
				ResetPartsState();
				return;
			}

			// 自分自身(衣装のレンダラー)を参照している場合は何もしない
			if (body == GetOwnRenderer())
			{
				ResetPartsState();
				return;
			}

			// パーツ情報: ヒューマノイド骨格が見つかれば、体の表面にパーツマスクと半径プロファイルを付ける
			var skeleton = ResolveSkeleton();
			var request = skeleton != null
				? new PartRequest { Skeleton = skeleton, JointTolerance = jointTolerance }
				: null;
			_surfaceReady = ReferenceSurfaceCache.TryGet(CollectBodies(), useBodyBlendShapes, flipBodyNormals, request,
				out _surface, out _profiles, out _surfaceHash);
			if (!_surfaceReady)
			{
				ResetPartsState();
				return;
			}
			_partsReady = request != null && _profiles.IsCreated;
			_effectiveMode = fitMode == FitMode.PartCylinder && _partsReady
				? FitMode.PartCylinder
				: FitMode.NearestSurface;

			// パーツ所属(連結成分・縫い目の混合)には隣接が要る
			EnsureAdjacency(source, _partsReady);
			EnsureCostumeParts(source, _partsReady ? skeleton : null);

			if (!_baseDisplacement.IsCreated || _baseDisplacement.Length != _vertexCount)
			{
				if (_baseDisplacement.IsCreated)
					_baseDisplacement.Dispose();
				_baseDisplacement = new NativeArray<float3>(_vertexCount, Allocator.Persistent,
					NativeArrayOptions.UninitializedMemory);
			}
		}

		/// <summary>
		/// パーツ軸に使うヒューマノイド Animator: 体の親 → 衣装(親スタック)の親 の順に探す。
		/// 見つからなければ null(インスペクタの状態表示も同じ判定を使う)。
		/// </summary>
		public Animator FindHumanoidAnimator()
		{
			var animator = body != null ? body.GetComponentInParent<Animator>() : null;
			if (animator == null || !animator.isHuman)
			{
				var own = GetOwnRenderer();
				animator = own != null ? own.GetComponentInParent<Animator>() : null;
			}
			return animator != null && animator.isHuman ? animator : null;
		}

		/// <summary>
		/// 骨格の解決: 差し替え → FindHumanoidAnimator。
		/// 同じ Animator(と Avatar)の間は骨格を再利用し、現在のボーン位置だけ読み直す
		/// (PrepareBake はプレビューのホットパスから毎回呼ばれるため、全ボーンの再列挙は避ける)。
		/// </summary>
		private HumanoidSkeleton ResolveSkeleton()
		{
			if (SkeletonOverride != null)
			{
				if (SkeletonOverride.ShoulderFromSpine != shoulderAxis)
				{
					SkeletonOverride.ShoulderFromSpine = shoulderAxis;
					SkeletonOverride.Refresh();
				}
				return SkeletonOverride;
			}
			var animator = FindHumanoidAnimator();
			if (animator == null)
			{
				_cachedSkeleton = null;
				_cachedAnimator = null;
				_cachedAvatar = null;
				return null;
			}
			if (_cachedSkeleton != null && _cachedAnimator == animator && _cachedAvatar == animator.avatar)
			{
				_cachedSkeleton.ShoulderFromSpine = shoulderAxis;
				_cachedSkeleton.Refresh();
				return _cachedSkeleton;
			}
			_cachedSkeleton = HumanoidSkeleton.FromAnimator(animator);
			_cachedAnimator = animator;
			_cachedAvatar = animator.avatar;
			if (_cachedSkeleton != null && _cachedSkeleton.ShoulderFromSpine != shoulderAxis)
			{
				_cachedSkeleton.ShoulderFromSpine = shoulderAxis;
				_cachedSkeleton.Refresh();
			}
			return _cachedSkeleton;
		}

		private void EnsureAdjacency(Mesh source, bool force)
		{
			if (smoothIterations <= 0 && !force)
				return;
			if (_adjStart.IsCreated && _adjacencyManaged != null && _adjacencyMesh == source &&
			    _adjacencyVertexCount == source.vertexCount)
				return;

			if (_adjStart.IsCreated) _adjStart.Dispose();
			if (_adjList.IsCreated) _adjList.Dispose();

			var adjacency = MeshAdjacency.Build(source.vertices, ReferenceSurfaceUtility.CollectTriangles(source));
			_adjStart = new NativeArray<int>(adjacency.Start, Allocator.Persistent);
			_adjList = new NativeArray<int>(adjacency.Neighbors, Allocator.Persistent);
			_adjacencyManaged = adjacency;
			_adjacencyMesh = source;
			_adjacencyVertexCount = source.vertexCount;
		}

		/// <summary>
		/// 衣装頂点のパーツ所属を用意する。骨格が無ければ全頂点 None(マスク 0 = 絞り込みなし)。
		/// ソースメッシュ・骨格・体の表面・設定・上書きが変わらない限り再利用する。
		/// </summary>
		private void EnsureCostumeParts(Mesh source, HumanoidSkeleton skeleton)
		{
			var n = source.vertexCount;
			int key;
			unchecked
			{
				key = 17;
				key = key * 31 + source.GetInstanceID();
				key = key * 31 + n;
				key = key * 31 + (skeleton != null ? skeleton.StateHash : 0);
				key = key * 31 + (skeleton != null ? _surfaceHash : 0);
				key = key * 31 + (int)partSource;
				key = key * 31 + (int)partGrouping;
				key = key * 31 + decorationMaxSize.GetHashCode();
				key = key * 31 + islandConfidence.GetHashCode();
				key = key * 31 + seamBlend;
				key = key * 31 + jointTolerance.GetHashCode();
				key = key * 31 + (shoulderAxis ? 1 : 0);
				key = key * 31 + shoulderCapMargin.GetHashCode();
				key = key * 31 + (neckCap ? 1 : 0);
				key = key * 31 + (rigidDecorations ? 1 : 0);
				key = key * 31 + rigidMaxSize.GetHashCode();
				key = key * 31 + OverridesHash();
				var own = GetOwnRenderer();
				key = key * 31 + (own != null ? own.GetInstanceID() : 0);
			}
			if (_costumeParts.IsCreated && _costumeParts.Length == n && _costumePartsKey == key)
				return;

			if (_costumeParts.IsCreated)
				_costumeParts.Dispose();
			if (_follow.IsCreated)
				_follow.Dispose();
			ClearPartReports();
			_followManaged = null;
			var weights = skeleton != null ? BuildCostumePartWeights(source, skeleton, _partReports) : new PartWeights[n];
			_costumeParts = new NativeArray<PartWeights>(weights, Allocator.Persistent);
			if (_followManaged != null && _followManaged.Length == n)
				_follow = new NativeArray<int>(_followManaged, Allocator.Persistent);
			_costumePartsKey = key;
		}

		private int OverridesHash()
		{
			unchecked
			{
				var h = partOverrides.Count;
				foreach (var o in partOverrides)
				{
					h = h * 31 + (o.useIsland ? 1 : 0);
					h = h * 31 + o.island.uv.GetHashCode();
					h = h * 31 + o.island.subMesh;
					h = h * 31 + o.island.index;
					h = h * 31 + o.point.GetHashCode();
					h = h * 31 + (int)o.part;
				}
				return h;
			}
		}

		/// <summary>
		/// 衣装頂点のパーツ所属を決める(<see cref="PartLabeler"/>):
		/// 証拠 = ボーンウェイト(対応付けの信頼度付き)+ 体の形状(パーツ表面からの隙間)、
		/// 単位 = UV 島 / 連結成分、投票で揃え、手動上書きを適用し、縫い目で所属を混ぜる。
		/// </summary>
		private PartWeights[] BuildCostumePartWeights(Mesh source, HumanoidSkeleton skeleton, List<PartGroupReport> reports)
		{
			var n = source.vertexCount;
			var own = GetOwnRenderer();
			var triangles = ReferenceSurfaceUtility.CollectTriangles(source);
			var local = source.vertices;
			var adjacency = _adjacencyManaged != null && _adjacencyManaged.VertexCount == n
				? _adjacencyManaged
				: MeshAdjacency.Build(local, triangles);

			// 証拠 1: ボーンウェイト(衣装ボーン → パーツの対応付けと、その信頼度)
			PartWeights[] boneWeights = null;
			float[] boneConfidence = null;
			if (partSource != PartSource.Geometry && own is SkinnedMeshRenderer smr && smr.bones != null &&
			    smr.bones.Length > 0 && source.GetBonesPerVertex().Length == n)
			{
				var mapConfidence = new float[smr.bones.Length];
				var boneParts = skeleton.MapBones(smr.bones, jointTolerance, own.GetComponentInParent<Animator>(), mapConfidence);
				boneWeights = PartAssignment.FromBoneWeights(source, boneParts, mapConfidence, out boneConfidence);
			}

			// 証拠 2: 体の形状(ワールド空間の頂点と体のプロファイル)
			PartWeights[] geometryWeights = null;
			Vector3[] world = null;
			if (partSource != PartSource.BoneWeights && _profiles.IsCreated)
			{
				world = (Vector3[])local.Clone();
				if (own != null)
					ReferenceSurfaceUtility.SkinToWorld(own.transform, source, world);
				geometryWeights = PartLabeler.EvaluateGeometry(world, in _profiles);
			}

			// 単位: UV 島(無ければ連結成分)/ 連結成分 / なし
			int[] groups = null;
			var groupCount = 0;
			IslandSeed[] seeds = null;
			UVIslandAnalysis analysis = null;
			if (partGrouping == PartGrouping.UVIslands)
			{
				analysis = UVIslandAnalysis.Analyze(source);
				if (analysis.Islands.Count > 0)
				{
					groupCount = analysis.Islands.Count;
					groups = new int[n];
					for (var i = 0; i < n; i++)
						groups[i] = -1;
					seeds = new IslandSeed[groupCount];
					var overlapping = new List<UVIslandAnalysis.Island>();
					foreach (var island in analysis.Islands)
					{
						foreach (var v in island.Vertices)
						{
							if (v >= 0 && v < n)
								groups[v] = island.Id;
						}
						overlapping.Clear();
						analysis.FindIslandsAt(island.Seed, island.SubMesh, overlapping);
						var ordinal = Mathf.Max(0, overlapping.IndexOf(island));
						seeds[island.Id] = new IslandSeed(island.Seed, island.SubMesh, ordinal);
					}
				}
				else
				{
					analysis = null;
				}
			}
			if (groups == null && partGrouping != PartGrouping.None)
				groups = PartAssignment.ConnectedComponents(adjacency, triangles, out groupCount);

			// インスペクタの上書き操作がベイクと同じ規則でグループを特定できるよう、分け方を保持する
			_lastGroups = groups;
			_lastGroupCount = groupCount;
			_lastAnalysis = analysis;
			_lastVertices = local;

			// 装飾の追従: 小さなグループの頂点は付け根の頂点の変位を写す
			_followManaged = rigidDecorations && groups != null
				? BuildFollowSources(local, groups, groupCount, adjacency, rigidMaxSize)
				: null;

			// 手動上書き → グループ番号
			Dictionary<int, BodyPart> overrides = null;
			if (groups != null && partOverrides.Count > 0)
			{
				overrides = new Dictionary<int, BodyPart>();
				foreach (var o in partOverrides)
				{
					var g = ResolveOverrideGroup(o, analysis, local, groups, groupCount);
					if (g >= 0 && o.part != BodyPart.None)
						overrides[g] = o.part;
				}
			}

			var weights = PartLabeler.Label(new PartLabelInput
			{
				Vertices = local,
				BoneWeights = boneWeights,
				BoneConfidence = boneConfidence,
				GeometryWeights = geometryWeights,
				GroupOfVertex = groups,
				GroupCount = groupCount,
				GroupSeeds = seeds,
				DecorationMaxSize = decorationMaxSize,
				ConfidenceThreshold = islandConfidence,
				Overrides = overrides,
			}, reports);

			// 首の帽子領域(首の関節より上の襟など)は首パーツで動かす。肩甲帯(肩の軸)より先に適用する
			if ((neckCap || shoulderAxis) && _profiles.IsCreated && world == null)
			{
				world = (Vector3[])local.Clone();
				if (own != null)
					ReferenceSurfaceUtility.SkinToWorld(own.transform, source, world);
			}
			if (neckCap && _profiles.IsCreated)
				PartLabeler.ApplyNeckCap(weights, world, in _profiles);

			// 肩の帽子領域(胴の上端)は脊椎から上腕関節へ伸びる肩の軸で放射させる
			if (shoulderAxis && _profiles.IsCreated)
				PartLabeler.ApplyShoulderCap(weights, world, in _profiles, shoulderCapMargin);

			// 軸区間の外(腰より下に垂れる裾、首の軸より上の襟の先など)は所属パーツのプロファイルに根拠が無いので、
			// 形状証拠(最寄りのパーツ)へ差し替える
			if (world != null && geometryWeights != null)
			{
				var fallback = PartLabeler.ApplyAxisRangeFallback(weights, geometryWeights, world, in _profiles);
				if (fallback != null && groups != null && reports != null)
				{
					var counts = new int[groupCount];
					for (var v = 0; v < n; v++)
					{
						var g = groups[v];
						if (fallback[v] && g >= 0 && g < groupCount)
							counts[g]++;
					}
					foreach (var report in reports)
					{
						if (report.Group >= 0 && report.Group < groupCount)
							report.OutOfRangeCount = counts[report.Group];
					}
				}
			}

			// 縫い目: 同位置の頂点を揃え、境界で所属を混ぜる
			PartLabeler.BlendSeams(weights, adjacency, seamBlend);
			return weights;
		}

		/// <summary>
		/// 装飾の追従の写し元を求める。バウンズ対角が maxSize 以下のグループ(襟・紐・ボタン)について、
		/// 「付け根」= 他のグループの頂点と同じ位置にある / 隣接している / 1 cm 以内にある頂点 を探し、
		/// グループの各頂点に最も近い付け根の頂点番号を写し元にする(付け根自身は自分)。
		/// 付け根の無い(どこにも付いていない)グループと大きなグループは -1(頂点ごとの放射変位のまま)。
		/// 襟のような部品が、傾いた軸の放射や軸区間の切り替わりで輪郭を崩さず、付け根の布と一緒に動く。
		/// </summary>
		public static int[] BuildFollowSources(Vector3[] vertices, int[] groups, int groupCount, MeshAdjacency adjacency,
			float maxSize)
		{
			var n = vertices.Length;
			if (groups == null || groups.Length != n || groupCount <= 0)
				return null;

			var min = new Vector3[groupCount];
			var max = new Vector3[groupCount];
			var seen = new bool[groupCount];
			for (var v = 0; v < n; v++)
			{
				var g = groups[v];
				if (g < 0 || g >= groupCount)
					continue;
				if (!seen[g])
				{
					seen[g] = true;
					min[g] = vertices[v];
					max[g] = vertices[v];
				}
				else
				{
					min[g] = Vector3.Min(min[g], vertices[v]);
					max[g] = Vector3.Max(max[g], vertices[v]);
				}
			}
			var small = new bool[groupCount];
			var anySmall = false;
			for (var g = 0; g < groupCount; g++)
			{
				small[g] = seen[g] && (max[g] - min[g]).magnitude <= maxSize;
				anySmall |= small[g];
			}
			if (!anySmall)
				return null;

			// 溶接グループ(同じ位置の頂点)に含まれるグループ: UV シームで分かれた同位置の頂点を付け根とみなす
			var weldOf = adjacency != null && adjacency.VertexCount == n ? adjacency.GroupOf : null;
			var weldCount = weldOf != null ? adjacency.Representative.Length : 0;
			var weldFirst = new int[weldCount];
			var weldMixed = new bool[weldCount];
			for (var w = 0; w < weldCount; w++)
				weldFirst[w] = -1;
			if (weldOf != null)
			{
				for (var v = 0; v < n; v++)
				{
					var w = weldOf[v];
					var g = groups[v];
					if (g < 0)
						continue;
					if (weldFirst[w] < 0)
						weldFirst[w] = g;
					else if (weldFirst[w] != g)
						weldMixed[w] = true;
				}
			}

			// 近接(1 cm)の判定用の均一グリッド
			const float attach = 0.01f;
			var cells = new Dictionary<long, List<int>>();
			long Key(Vector3 p)
			{
				var x = (long)Mathf.Floor(p.x / attach);
				var y = (long)Mathf.Floor(p.y / attach);
				var z = (long)Mathf.Floor(p.z / attach);
				return ((x & 0x1FFFFF) << 42) ^ ((y & 0x1FFFFF) << 21) ^ (z & 0x1FFFFF);
			}
			for (var v = 0; v < n; v++)
			{
				var key = Key(vertices[v]);
				if (!cells.TryGetValue(key, out var list))
					cells[key] = list = new List<int>();
				list.Add(v);
			}

			bool OtherGroupNear(int v, int g)
			{
				var p = vertices[v];
				for (var dx = -1; dx <= 1; dx++)
				for (var dy = -1; dy <= 1; dy++)
				for (var dz = -1; dz <= 1; dz++)
				{
					if (!cells.TryGetValue(Key(p + new Vector3(dx, dy, dz) * attach), out var list))
						continue;
					foreach (var u in list)
					{
						if (groups[u] != g && groups[u] >= 0 && (vertices[u] - p).sqrMagnitude <= attach * attach)
							return true;
					}
				}
				return false;
			}

			var isBase = new bool[n];
			var members = new List<int>[groupCount];
			for (var v = 0; v < n; v++)
			{
				var g = groups[v];
				if (g < 0 || !small[g])
					continue;
				(members[g] ??= new List<int>()).Add(v);
				var isSeam = false;
				if (weldOf != null)
				{
					var w = weldOf[v];
					isSeam = weldMixed[w] || (weldFirst[w] >= 0 && weldFirst[w] != g);
					if (!isSeam && adjacency.HasEdges)
					{
						for (var i = adjacency.Start[v]; i < adjacency.Start[v + 1] && !isSeam; i++)
						{
							var nw = weldOf[adjacency.Neighbors[i]];
							isSeam = weldMixed[nw] || (weldFirst[nw] >= 0 && weldFirst[nw] != g);
						}
					}
				}
				isBase[v] = isSeam || OtherGroupNear(v, g);
			}

			var result = new int[n];
			for (var v = 0; v < n; v++)
				result[v] = -1;
			var bases = new List<int>();
			for (var g = 0; g < groupCount; g++)
			{
				if (members[g] == null)
					continue;
				bases.Clear();
				foreach (var v in members[g])
				{
					if (isBase[v])
						bases.Add(v);
				}
				if (bases.Count == 0)
					continue;
				foreach (var v in members[g])
				{
					if (isBase[v])
					{
						result[v] = v;
						continue;
					}
					var best = -1;
					var bestDist = float.MaxValue;
					var p = vertices[v];
					foreach (var b in bases)
					{
						var d = (vertices[b] - p).sqrMagnitude;
						if (d < bestDist)
						{
							bestDist = d;
							best = b;
						}
					}
					result[v] = best;
				}
			}
			return result;
		}

		/// <summary>上書きの参照(島シード / 代表点)を現在のグループ番号へ解決する。見つからなければ -1</summary>
		private static int ResolveOverrideGroup(in PartOverride o, UVIslandAnalysis analysis, Vector3[] vertices,
			int[] groups, int groupCount)
		{
			if (o.useIsland && analysis != null)
			{
				var island = analysis.ResolveSeed(o.island);
				return island != null && island.Id < groupCount ? island.Id : -1;
			}
			var best = -1;
			var bestDist = float.MaxValue;
			for (var v = 0; v < vertices.Length; v++)
			{
				var d = (vertices[v] - o.point).sqrMagnitude;
				if (d < bestDist)
				{
					bestDist = d;
					best = v;
				}
			}
			if (best < 0)
				return -1;
			var g = groups[best];
			return g >= 0 && g < groupCount ? g : -1;
		}

		/// <summary>
		/// インスペクタ用: 衣装メッシュに対してパーツ所属を計算し、<see cref="PartReports"/> を更新する。
		/// 体・骨格が使えなければ false。
		/// </summary>
		public bool AnalyzeParts()
		{
			var own = GetOwnRenderer();
			Mesh mesh = null;
			if (own is SkinnedMeshRenderer smr)
				mesh = smr.sharedMesh;
			else if (own != null && own.TryGetComponent<MeshFilter>(out var filter))
				mesh = filter.sharedMesh;
			if (mesh == null)
				return false;
			PrepareBake(mesh);
			return _partsReady;
		}

		/// <summary>
		/// 上書きの参照を、直近のパーツ所属計算のグループ番号へ解決する(ベイクと同じ規則)。
		/// グループ分けの方式(UV 島 / 連結成分)を切り替えても、島シードで記録した上書きは
		/// 代表点で、代表点で記録した上書きは最寄り頂点の島で追従する。解決できなければ -1
		/// </summary>
		public int ResolveOverrideGroup(in PartOverride o)
		{
			if (_lastGroups == null || _lastVertices == null)
				return -1;
			return ResolveOverrideGroup(o, _lastAnalysis, _lastVertices, _lastGroups, _lastGroupCount);
		}

		/// <summary>グループの現在の上書き(無ければ None)</summary>
		public BodyPart GetPartOverride(PartGroupReport report)
		{
			if (report == null)
				return BodyPart.None;
			foreach (var o in partOverrides)
			{
				if (ResolveOverrideGroup(o) == report.Group)
					return o.part;
			}
			return BodyPart.None;
		}

		/// <summary>
		/// グループの上書きを設定する(part = None で解除)。同じグループへ解決される既存の上書きは置き換える。
		/// UV 島グループは島シードで、連結成分グループは代表点で記録する(頂点順が変わっても追従する)。
		/// </summary>
		public void SetPartOverride(PartGroupReport report, BodyPart part)
		{
			if (report == null)
				return;
			for (var i = partOverrides.Count - 1; i >= 0; i--)
			{
				if (ResolveOverrideGroup(partOverrides[i]) == report.Group)
					partOverrides.RemoveAt(i);
			}
			if (part != BodyPart.None)
			{
				partOverrides.Add(new PartOverride
				{
					useIsland = report.IsIsland,
					island = report.Island,
					point = report.Point,
					part = part,
				});
			}
		}

		public override JobHandle Schedule(in MeshBuffers buffers, in DeformSpace space, JobHandle dependency)
		{
			// フィットグループのメンバーは、自分が寄与できなくても(末尾なら合成のために)グループの手順を踏む
			var group = _groupLeader != null ? _groupLeader._group : null;
			if (group != null && buffers.Length == _vertexCount)
				return ScheduleGrouped(group, in buffers, in space, dependency);

			if (factor <= 0f || !_surfaceReady || !_surface.IsCreated)
				return dependency;
			if (buffers.Length != _vertexCount || !_baseDisplacement.IsCreated)
				return dependency;

			var pass = _passIndex++;
			var n = buffers.Length;

			// シェイプフレーム(2 パス目以降)は、FixedDisplacement なら基本形状の変位を足すだけ
			if (pass > 0 && blendShapes == BlendShapeFitMode.FixedDisplacement)
			{
				return new AddDisplacementJob
				{
					displacement = _baseDisplacement,
					factor = factor,
					vertices = buffers.Vertices,
				}.Schedule(n, 128, dependency);
			}

			if (!_costumeParts.IsCreated || _costumeParts.Length != n)
				return dependency;
			return ScheduleCore(in buffers, in space, buffers.Vertices, null, dependency);
		}

		/// <summary>
		/// 基本形状のジョブチェーン。input = 隙間を測る頂点位置(単独では buffers.Vertices、グループでは
		/// スナップショット)。group が null なら結果を buffers.Vertices へ適用して minGap を再保証し、
		/// 非 null ならグループの累積へ足すだけ(合成と再保証は末尾メンバーが行う)。
		/// </summary>
		private JobHandle ScheduleCore(in MeshBuffers buffers, in DeformSpace space, NativeArray<float3> input,
			FitGroupBuffers group, JobHandle dependency)
		{
			var n = buffers.Length;
			if (_effectiveMode == FitMode.PartCylinder)
				return SchedulePartCylinder(in buffers, in space, input, group, dependency);

			var delta = new NativeArray<float3>(n, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
			var weight = new NativeArray<float>(n, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
			var valid = new NativeArray<byte>(n, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
			var usePartFilter = partFilter && _partsReady ? 1 : 0;

			var handle = new QueryJob
			{
				surface = _surface,
				vertices = input,
				parts = _costumeParts,
				usePartFilter = usePartFilter,
				meshToAxis = space.MeshToAxis,
				wholeMesh = region == FitRegion.WholeMesh ? 1 : 0,
				innerRadius = innerRadius,
				outerRadius = outerRadius,
				minGap = minGap,
				maxGap = pullIn ? Mathf.Max(maxGap, minGap) : float.MaxValue,
				searchDistance = searchDistance,
				delta = delta,
				weight = weight,
				valid = valid,
			}.Schedule(n, 32, dependency);

			// 変位の平滑化(ピンポンバッファ)
			var current = delta;
			var scratch = default(NativeArray<float3>);
			var smoothed = false;
			if (smoothIterations > 0 && smoothStrength > 0f && _adjList.IsCreated && _adjList.Length > 0 &&
			    _adjStart.Length == n + 1)
			{
				scratch = new NativeArray<float3>(n, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
				var other = scratch;
				for (var i = 0; i < smoothIterations; i++)
				{
					handle = new SmoothJob
					{
						adjStart = _adjStart,
						adjList = _adjList,
						valid = valid,
						input = current,
						output = other,
						strength = smoothStrength,
					}.Schedule(n, 64, handle);
					var tmp = current;
					current = other;
					other = tmp;
				}
				smoothed = true;
			}

			if (group != null)
			{
				handle = new AccumulateJob
				{
					delta = current,
					weight = weight,
					factor = factor,
					sumD = group.SumD,
					sumW = group.SumW,
				}.Schedule(n, 128, handle);
			}
			else
			{
				handle = new ApplyJob
				{
					delta = current,
					weight = weight,
					factor = factor,
					vertices = buffers.Vertices,
					displacement = _baseDisplacement,
				}.Schedule(n, 128, handle);

				if (smoothed && enforceMinGap)
				{
					handle = new EnforceMinGapJob
					{
						surface = _surface,
						parts = _costumeParts,
						usePartFilter = usePartFilter,
						weight = weight,
						factor = factor,
						minGap = minGap,
						searchDistance = searchDistance,
						vertices = buffers.Vertices,
						displacement = _baseDisplacement,
					}.Schedule(n, 32, handle);
				}
			}

			handle = delta.Dispose(handle);
			handle = weight.Dispose(handle);
			handle = valid.Dispose(handle);
			if (scratch.IsCreated)
				handle = scratch.Dispose(handle);
			return handle;
		}

		/// <summary>
		/// パーツ円柱モードのジョブチェーン:
		/// CylinderCoordJob(頂点をパーツ軸の円柱座標へ)→ RadialFieldJob(格子ごとの最内層半径から
		/// 放射変位場 Δr(h, θ) を作り、補間・平滑化)→ RadialApplyJob(各頂点の所属パーツで Δr をサンプル)
		/// → ApplyJob → EnforceMinGapJob(パーツ制限付きの最近接点で minGap を保証)
		/// </summary>
		private JobHandle SchedulePartCylinder(in MeshBuffers buffers, in DeformSpace space, NativeArray<float3> input,
			FitGroupBuffers group, JobHandle dependency)
		{
			var n = buffers.Length;
			var cellCount = HumanoidSkeleton.PartCount * BodyPartProfiles.HCount * BodyPartProfiles.ThetaCount;
			var coords = new NativeArray<float4>(n * 4, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
			var radialDirs = new NativeArray<float3>(n * 4, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
			var binPart = new NativeArray<int>(n, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
			var weight = new NativeArray<float>(n, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
			var validRadial = new NativeArray<byte>(n, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
			var valid = validRadial;
			var delta = new NativeArray<float3>(n, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
			var grid = new NativeArray<float>(cellCount, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
			var gridScratch = new NativeArray<float>(cellCount, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);

			var handle = new CylinderCoordJob
			{
				vertices = input,
				parts = _costumeParts,
				profiles = _profiles,
				meshToAxis = space.MeshToAxis,
				wholeMesh = region == FitRegion.WholeMesh ? 1 : 0,
				innerRadius = innerRadius,
				outerRadius = outerRadius,
				coords = coords,
				radialDirs = radialDirs,
				binPart = binPart,
				weight = weight,
			}.Schedule(n, 64, dependency);

			handle = new RadialFieldJob
			{
				coords = coords,
				binPart = binPart,
				weight = weight,
				profiles = _profiles,
				minGap = minGap,
				maxGap = pullIn ? Mathf.Max(maxGap, minGap) : float.MaxValue,
				smoothIterations = smoothIterations,
				smoothStrength = smoothStrength,
				maxShrink = maxShrink,
				grid = grid,
				scratch = gridScratch,
			}.Schedule(handle);

			handle = new RadialApplyJob
			{
				coords = coords,
				radialDirs = radialDirs,
				parts = _costumeParts,
				grid = grid,
				vertices = input,
				profiles = _profiles,
				jointFan = jointFan ? 1 : 0,
				delta = delta,
				valid = valid,
			}.Schedule(n, 64, handle);

			var current = delta;
			var followBuffer = default(NativeArray<float3>);
			var scratch = default(NativeArray<float3>);
			var target = default(NativeArray<byte>);
			var validFollow = default(NativeArray<byte>);

			// 装飾の追従: 小さなグループの頂点は付け根の頂点の変位を写す(輪郭を保つ)
			if (rigidDecorations && _follow.IsCreated && _follow.Length == n)
			{
				followBuffer = new NativeArray<float3>(n, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
				validFollow = new NativeArray<byte>(n, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
				handle = new FollowJob
				{
					follow = _follow,
					input = current,
					validIn = valid,
					output = followBuffer,
					validOut = validFollow,
				}.Schedule(n, 128, handle);
				current = followBuffer;
				valid = validFollow;
			}

			// 縫い目(所属が混ざる頂点とその隣)の変位ベクトルを頂点隣接で平滑化する(放射方向の食い違いをならす)
			if (seamSmoothIterations > 0 && smoothStrength > 0f && _adjList.IsCreated && _adjList.Length > 0 &&
			    _adjStart.Length == n + 1)
			{
				target = new NativeArray<byte>(n, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
				scratch = new NativeArray<float3>(n, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
				handle = new SeamMaskJob
				{
					adjStart = _adjStart,
					adjList = _adjList,
					parts = _costumeParts,
					valid = valid,
					all = seamSmoothAll ? 1 : 0,
					target = target,
				}.Schedule(n, 64, handle);
				var other = scratch;
				for (var i = 0; i < seamSmoothIterations; i++)
				{
					handle = new MaskedSmoothJob
					{
						adjStart = _adjStart,
						adjList = _adjList,
						valid = valid,
						target = target,
						input = current,
						output = other,
						strength = smoothStrength,
					}.Schedule(n, 64, handle);
					var tmp = current;
					current = other;
					other = tmp;
				}
			}

			if (group != null)
			{
				handle = new AccumulateJob
				{
					delta = current,
					weight = weight,
					factor = factor,
					sumD = group.SumD,
					sumW = group.SumW,
				}.Schedule(n, 128, handle);
			}
			else
			{
				handle = new ApplyJob
				{
					delta = current,
					weight = weight,
					factor = factor,
					vertices = buffers.Vertices,
					displacement = _baseDisplacement,
				}.Schedule(n, 128, handle);

				if (enforceMinGap)
				{
					handle = new EnforceMinGapJob
					{
						surface = _surface,
						parts = _costumeParts,
						usePartFilter = 1,
						weight = weight,
						factor = factor,
						minGap = minGap,
						searchDistance = searchDistance,
						vertices = buffers.Vertices,
						displacement = _baseDisplacement,
					}.Schedule(n, 32, handle);
				}
			}

			handle = coords.Dispose(handle);
			handle = radialDirs.Dispose(handle);
			handle = binPart.Dispose(handle);
			handle = weight.Dispose(handle);
			handle = validRadial.Dispose(handle);
			handle = delta.Dispose(handle);
			handle = grid.Dispose(handle);
			handle = gridScratch.Dispose(handle);
			if (followBuffer.IsCreated)
				handle = followBuffer.Dispose(handle);
			if (scratch.IsCreated)
				handle = scratch.Dispose(handle);
			if (target.IsCreated)
				handle = target.Dispose(handle);
			if (validFollow.IsCreated)
				handle = validFollow.Dispose(handle);
			return handle;
		}

		/// <summary>
		/// フィットグループの共有バッファ(先頭メンバーが所有、パスごとに確保・末尾メンバーが解放)。
		/// Snapshot = グループ直前の頂点位置(全メンバーの入力)、SumD = Σ wᵢ fᵢ dᵢ、SumW = Σ wᵢ
		/// </summary>
		private sealed class FitGroupBuffers
		{
			public NativeArray<float3> Snapshot;
			public NativeArray<float3> SumD;
			public NativeArray<float> SumW;
			public int Pass = -1;

			public bool IsCreated => Snapshot.IsCreated;

			public void Allocate(int n)
			{
				DisposeNow();
				Snapshot = new NativeArray<float3>(n, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
				SumD = new NativeArray<float3>(n, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
				SumW = new NativeArray<float>(n, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
			}

			public JobHandle Dispose(JobHandle handle)
			{
				if (Snapshot.IsCreated) handle = Snapshot.Dispose(handle);
				if (SumD.IsCreated) handle = SumD.Dispose(handle);
				if (SumW.IsCreated) handle = SumW.Dispose(handle);
				Pass = -1;
				return handle;
			}

			public void DisposeNow()
			{
				if (Snapshot.IsCreated) Snapshot.Dispose();
				if (SumD.IsCreated) SumD.Dispose();
				if (SumW.IsCreated) SumW.Dispose();
				Pass = -1;
			}
		}

		/// <summary>
		/// フィットグループを決める: 親スタックの有効なデフォーマの並びで、この Body Fit と同じ体を参照する
		/// Body Fit(fitGroup が真のもの)が連続する範囲。先頭が共有バッファを持ち、末尾が合成して書き戻す。
		/// 参照する体が違う(重ね着)、間に他のデフォーマが挟まる、fitGroup が偽、のいずれかで切れる。
		/// </summary>
		private void DetermineGroup()
		{
			_groupLeader = null;
			_groupLast = false;
			_groupSize = 1;
			_group?.DisposeNow();
			if (!fitGroup)
			{
				_group = null;
				return;
			}
			var stack = GetComponentInParent<DeformStack>();
			if (stack == null)
			{
				_group = null;
				return;
			}
			var ordered = new List<DeformerBase>();
			foreach (var entry in stack.Deformers)
			{
				if (entry.enabled && entry.deformer != null)
					ordered.Add(entry.deformer);
			}
			var index = ordered.IndexOf(this);
			if (index < 0)
			{
				_group = null;
				return;
			}
			var first = index;
			while (first > 0 && IsGroupMate(ordered[first - 1]))
				first--;
			var last = index;
			while (last + 1 < ordered.Count && IsGroupMate(ordered[last + 1]))
				last++;
			if (last == first)
			{
				_group = null;
				return;
			}
			_groupLeader = (BodyFitDeformer)ordered[first];
			_groupLast = index == last;
			_groupSize = last - first + 1;
			if (_groupLeader == this)
				_group ??= new FitGroupBuffers();
			else
				_group = null;
		}

		private bool IsGroupMate(DeformerBase other)
		{
			return other is BodyFitDeformer fit && fit != this && fit.fitGroup && SameBodies(fit);
		}

		/// <summary>参照する体(body + additionalBodies。null は無視)が同じか</summary>
		private bool SameBodies(BodyFitDeformer other)
		{
			if (body != other.body)
				return false;
			foreach (var r in additionalBodies)
			{
				if (r != null && !other.additionalBodies.Contains(r))
					return false;
			}
			foreach (var r in other.additionalBodies)
			{
				if (r != null && !additionalBodies.Contains(r))
					return false;
			}
			return true;
		}

		private bool CanContribute(int n)
		{
			return factor > 0f && _surfaceReady && _surface.IsCreated && n == _vertexCount &&
			       _baseDisplacement.IsCreated && _costumeParts.IsCreated && _costumeParts.Length == n;
		}

		/// <summary>
		/// フィットグループのメンバーとしてのスケジュール(§14.3)。
		/// パスの最初に走ったメンバーが共有バッファを確保して入力を写し、各メンバーは自分のジョブチェーンを
		/// 入力のスナップショットに対して回して Σ w f d と Σ w に足す。末尾メンバーが
		/// p' = p + Σ w f d / max(1, Σ w) を書き戻し、minGap を再保証して共有バッファを解放する。
		/// シェイプフレーム(FixedDisplacement)は末尾メンバーだけが合成済みの変位を足す。
		/// </summary>
		private JobHandle ScheduleGrouped(FitGroupBuffers group, in MeshBuffers buffers, in DeformSpace space,
			JobHandle dependency)
		{
			var pass = _passIndex++;
			var n = buffers.Length;
			var handle = dependency;

			if (pass > 0 && _groupLeader.blendShapes == BlendShapeFitMode.FixedDisplacement)
			{
				if (!_groupLast || !_baseDisplacement.IsCreated || _baseDisplacement.Length != n)
					return dependency;
				return new AddDisplacementJob
				{
					displacement = _baseDisplacement,
					factor = 1f,
					vertices = buffers.Vertices,
				}.Schedule(n, 128, dependency);
			}

			if (group.Pass != pass || !group.IsCreated || group.Snapshot.Length != n)
			{
				group.Allocate(n);
				group.Pass = pass;
				handle = new InitGroupJob
				{
					vertices = buffers.Vertices,
					snapshot = group.Snapshot,
					sumD = group.SumD,
					sumW = group.SumW,
				}.Schedule(n, 128, handle);
			}

			if (CanContribute(n))
				handle = ScheduleCore(in buffers, in space, group.Snapshot, group, handle);

			if (!_groupLast)
				return handle;

			if (!_baseDisplacement.IsCreated || _baseDisplacement.Length != n)
			{
				if (_baseDisplacement.IsCreated)
					_baseDisplacement.Dispose();
				_baseDisplacement = new NativeArray<float3>(n, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
			}
			handle = new CombineJob
			{
				snapshot = group.Snapshot,
				sumD = group.SumD,
				sumW = group.SumW,
				vertices = buffers.Vertices,
				displacement = _baseDisplacement,
			}.Schedule(n, 128, handle);

			if (enforceMinGap && _surfaceReady && _surface.IsCreated && _costumeParts.IsCreated && _costumeParts.Length == n)
			{
				handle = new EnforceMinGapJob
				{
					surface = _surface,
					parts = _costumeParts,
					usePartFilter = (_effectiveMode == FitMode.PartCylinder || partFilter) && _partsReady ? 1 : 0,
					weight = group.SumW,
					clampWeight = 1,
					factor = 1f,
					minGap = minGap,
					searchDistance = searchDistance,
					vertices = buffers.Vertices,
					displacement = _baseDisplacement,
				}.Schedule(n, 32, handle);
			}
			return group.Dispose(handle);
		}

		// ---- ジョブ ----

		/// <summary>フィットグループの共有バッファを初期化する(入力の写し、累積 0)</summary>
		[BurstCompile]
		public struct InitGroupJob : IJobParallelFor
		{
			[ReadOnly] public NativeArray<float3> vertices;
			[WriteOnly] public NativeArray<float3> snapshot;
			[WriteOnly] public NativeArray<float3> sumD;
			[WriteOnly] public NativeArray<float> sumW;

			public void Execute(int index)
			{
				snapshot[index] = vertices[index];
				sumD[index] = float3.zero;
				sumW[index] = 0f;
			}
		}

		/// <summary>フィットグループへの寄与: Σ w f d と Σ w に足す(w = 領域重み、f = factor)</summary>
		[BurstCompile]
		public struct AccumulateJob : IJobParallelFor
		{
			[ReadOnly] public NativeArray<float3> delta;
			[ReadOnly] public NativeArray<float> weight;
			public float factor;
			public NativeArray<float3> sumD;
			public NativeArray<float> sumW;

			public void Execute(int index)
			{
				var w = weight[index];
				if (w <= 0f)
					return;
				sumD[index] += delta[index] * (w * factor);
				sumW[index] += w;
			}
		}

		/// <summary>フィットグループの合成: p' = snapshot + Σ w f d / max(1, Σ w)。合成した変位も記録する</summary>
		[BurstCompile]
		public struct CombineJob : IJobParallelFor
		{
			[ReadOnly] public NativeArray<float3> snapshot;
			[ReadOnly] public NativeArray<float3> sumD;
			[ReadOnly] public NativeArray<float> sumW;
			[WriteOnly] public NativeArray<float3> vertices;
			[WriteOnly] public NativeArray<float3> displacement;

			public void Execute(int index)
			{
				var w = sumW[index];
				var d = w > 1f ? sumD[index] / w : sumD[index];
				vertices[index] = snapshot[index] + d;
				displacement[index] = d;
			}
		}

		/// <summary>
		/// 頂点ごとに領域重みと目標変位を求める。
		/// delta = 「体表面から法線方向に目標の隙間だけ離れた点」− 現在位置(重み・factor は掛けない)。
		/// 対象外(領域外・探索距離外)は delta = 0、valid = 0。帯の中は delta = 0 だが valid = 1
		/// (平滑化で「動かない頂点」として周囲の変位をなだらかにする)。
		/// </summary>
		[BurstCompile]
		public struct QueryJob : IJobParallelFor
		{
			// 内包する NativeArray は MeshSurfaceData 側で [ReadOnly] 宣言済み
			public MeshSurfaceData surface;
			[ReadOnly] public NativeArray<float3> vertices;
			[ReadOnly] public NativeArray<PartWeights> parts;
			public int usePartFilter;
			public float4x4 meshToAxis;
			public int wholeMesh;
			public float innerRadius;
			public float outerRadius;
			public float minGap;
			public float maxGap;
			public float searchDistance;
			[WriteOnly] public NativeArray<float3> delta;
			[WriteOnly] public NativeArray<float> weight;
			[WriteOnly] public NativeArray<byte> valid;

			public void Execute(int index)
			{
				var p = vertices[index];
				var w = RegionWeight(p, meshToAxis, wholeMesh, innerRadius, outerRadius);
				weight[index] = w;
				if (w <= 0f)
				{
					delta[index] = float3.zero;
					valid[index] = 0;
					return;
				}

				var mask = usePartFilter != 0 ? PartMaskOf(parts[index]) : 0;
				if (!surface.FindClosest(p, searchDistance, mask, out var hit))
				{
					delta[index] = float3.zero;
					valid[index] = 0;
					return;
				}

				// 探索距離の境界で段差が出ないよう、上限の 75% から 100% にかけて効きを減衰させる
				w *= SearchFalloff(hit.Distance, searchDistance);
				weight[index] = w;
				if (w <= 0f)
				{
					delta[index] = float3.zero;
					valid[index] = 0;
					return;
				}

				valid[index] = 1;
				var d = hit.SignedDistance;
				if (d < minGap && !ResolvePushOut(in surface, p, searchDistance, mask, minGap, ref hit))
				{
					// 全身で見れば隙間が足りている(パーツ境界の面による「内側」の誤判定)
					delta[index] = float3.zero;
					return;
				}
				d = hit.SignedDistance;
				var target = clamp(d, minGap, maxGap);
				if (target == d)
				{
					delta[index] = float3.zero;
					return;
				}

				var dir = OutwardDirection(p, hit);
				delta[index] = hit.Point + dir * target - p;
			}
		}

		/// <summary>
		/// 変位ベクトルの平滑化 1 回分(有効な隣接の平均への補間)。
		/// 対象外の頂点(valid = 0)は変位 0 のまま動かさず、平均にも含めない
		/// (領域外・探索圏外の頂点が境界の変位を引きずらないようにする)。
		/// 隣接リストは位置で溶接した代表頂点を指すため、シーム分割された頂点でも結果が一致する。
		/// </summary>
		[BurstCompile]
		public struct SmoothJob : IJobParallelFor
		{
			[ReadOnly] public NativeArray<int> adjStart;
			[ReadOnly] public NativeArray<int> adjList;
			[ReadOnly] public NativeArray<byte> valid;
			[ReadOnly] public NativeArray<float3> input;
			[WriteOnly] public NativeArray<float3> output;
			public float strength;

			public void Execute(int index)
			{
				var value = input[index];
				if (valid[index] == 0)
				{
					output[index] = value;
					return;
				}

				var start = adjStart[index];
				var end = adjStart[index + 1];
				var sum = float3.zero;
				var count = 0;
				for (var i = start; i < end; i++)
				{
					var j = adjList[i];
					if (valid[j] == 0)
						continue;
					sum += input[j];
					count++;
				}
				if (count == 0)
				{
					output[index] = value;
					return;
				}
				output[index] = lerp(value, sum / count, strength);
			}
		}

		/// <summary>変位を重み・factor 付きで適用し、重み込みの変位を記録する</summary>
		[BurstCompile]
		public struct ApplyJob : IJobParallelFor
		{
			[ReadOnly] public NativeArray<float3> delta;
			[ReadOnly] public NativeArray<float> weight;
			public float factor;
			public NativeArray<float3> vertices;
			[WriteOnly] public NativeArray<float3> displacement;

			public void Execute(int index)
			{
				var d = delta[index] * weight[index];
				displacement[index] = d;
				vertices[index] += d * factor;
			}
		}

		/// <summary>
		/// 平滑化で崩れた minGap をもう一度保証する(めり込み・近すぎのみ押し出す。平滑化なし)。
		/// </summary>
		[BurstCompile]
		public struct EnforceMinGapJob : IJobParallelFor
		{
			public MeshSurfaceData surface;
			[ReadOnly] public NativeArray<PartWeights> parts;
			public int usePartFilter;
			[ReadOnly] public NativeArray<float> weight;

			/// <summary>重みを 1 で頭打ちにする(フィットグループの Σ w は 1 を超える)</summary>
			public int clampWeight;
			public float factor;
			public float minGap;
			public float searchDistance;
			public NativeArray<float3> vertices;
			public NativeArray<float3> displacement;

			public void Execute(int index)
			{
				var w = weight[index];
				if (w <= 0f)
					return;
				if (clampWeight != 0)
					w = min(w, 1f);

				var p = vertices[index];
				var mask = usePartFilter != 0 ? PartMaskOf(parts[index]) : 0;
				var found = surface.FindClosest(p, searchDistance, mask, out var hit);
				if (mask != 0 && surface.HasPartMasks && (!found || hit.SignedDistance >= minGap) &&
				    surface.FindClosest(p, searchDistance, 0, out var whole) && whole.SignedDistance < 0f)
				{
					// 自分のパーツの三角形が遠い(襟元と肩など)と「十分外側」に見えるが、全身で見て体の内側なら
					// めり込みなので押し出す。パーツ制限は引き寄せ先の選択のためで、めり込みを許す意図ではない
					hit = whole;
					found = true;
					mask = 0;
				}
				if (!found)
					return;
				if (!ResolvePushOut(in surface, p, searchDistance, mask, minGap, ref hit))
					return;

				var d = hit.SignedDistance;
				var correction = OutwardDirection(p, hit) * (minGap - d) * w;
				vertices[index] = p + correction * factor;
				displacement[index] += correction;
			}
		}

		/// <summary>FixedDisplacement のシェイプフレーム用: 基本形状の変位をそのまま足す</summary>
		[BurstCompile]
		public struct AddDisplacementJob : IJobParallelFor
		{
			[ReadOnly] public NativeArray<float3> displacement;
			public float factor;
			public NativeArray<float3> vertices;

			public void Execute(int index)
			{
				vertices[index] += displacement[index] * factor;
			}
		}

		/// <summary>頂点をパーツ軸の円柱座標へ分解する(所属スロットごと)。領域重みと最内層判定用の支配パーツも出す</summary>
		[BurstCompile]
		public struct CylinderCoordJob : IJobParallelFor
		{
			[ReadOnly] public NativeArray<float3> vertices;
			[ReadOnly] public NativeArray<PartWeights> parts;
			public BodyPartProfiles profiles;
			public float4x4 meshToAxis;
			public int wholeMesh;
			public float innerRadius;
			public float outerRadius;

			/// <summary>
			/// 頂点 × 4 スロット: (h, θ, r, スロット重み)。使わないスロットは重み 0。
			/// 要素 index*4 〜 index*4+3 へ書くため、並列ジョブの「自分の index のみ」制限を外す
			/// (各頂点が書く範囲は重ならない)。
			/// </summary>
			[WriteOnly, NativeDisableParallelForRestriction] public NativeArray<float4> coords;

			[WriteOnly, NativeDisableParallelForRestriction] public NativeArray<float3> radialDirs;

			/// <summary>最内層の集計に使う支配パーツ(重み 0.5 以上・軸が使える場合のみ。それ以外は 0)</summary>
			[WriteOnly] public NativeArray<int> binPart;

			[WriteOnly] public NativeArray<float> weight;

			public void Execute(int index)
			{
				var p = vertices[index];
				weight[index] = RegionWeight(p, meshToAxis, wholeMesh, innerRadius, outerRadius);
				var pw = parts[index];
				var dominant = 0;
				for (var s = 0; s < 4; s++)
				{
					var part = pw.Parts[s];
					var sw = pw.Weights[s];
					if (part == 0 || sw <= 0f || !profiles.IsUsable(part))
					{
						coords[index * 4 + s] = float4.zero;
						radialDirs[index * 4 + s] = float3.zero;
						continue;
					}
					var axis = profiles.Axes[part];
					axis.Decompose(p, out var h, out var theta, out var r, out var dir);
					coords[index * 4 + s] = new float4(h, theta, r, sw);
					radialDirs[index * 4 + s] = dir;
					if (s == 0 && sw >= 0.5f)
						dominant = part;
				}
				binPart[index] = dominant;
			}
		}

		/// <summary>
		/// パーツごとの放射変位場 Δr(h, θ) を作る(単一スレッド):
		/// 1. 格子ごとに衣装の最内層半径 r_min(支配パーツの頂点の最小 r)
		/// 2. Δr = clamp(r_min, R + minGap, R + maxGap) − r_min(R は体の半径プロファイル)
		/// 3. 値の無い格子を近傍平均で 2 周だけ埋め(双線形補間の縁取り)、残りは 0
		/// 4. 3×3 平均への補間で平滑化(θ 方向は周期)
		/// </summary>
		[BurstCompile]
		public struct RadialFieldJob : IJob
		{
			[ReadOnly] public NativeArray<float4> coords;
			[ReadOnly] public NativeArray<int> binPart;
			[ReadOnly] public NativeArray<float> weight;
			public BodyPartProfiles profiles;
			public float minGap;
			public float maxGap;
			public int smoothIterations;
			public float smoothStrength;

			/// <summary>引き寄せで軸からの距離を縮める率の上限(0 で無制限)</summary>
			public float maxShrink;
			public NativeArray<float> grid;
			public NativeArray<float> scratch;

			public void Execute()
			{
				const int H = BodyPartProfiles.HCount;
				const int T = BodyPartProfiles.ThetaCount;
				const int P = HumanoidSkeleton.PartCount;
				var cellCount = P * H * T;

				for (var c = 0; c < cellCount; c++)
					grid[c] = float.PositiveInfinity;

				// 1. 最内層半径。領域(二重球)の重みに関わらず全頂点から取る: 領域が格子の一部しか覆わないとき、
				//    最内層が領域の外にあると領域内の外側の層が最内層として体へ潰されるため(§14)。
				//    領域の重みは変位の適用にだけ効く
				var n = binPart.Length;
				for (var i = 0; i < n; i++)
				{
					var part = binPart[i];
					if (part == 0)
						continue;
					var c = coords[i * 4];
					var hi = (int)floor((c.x - BodyPartProfiles.HStart) / (BodyPartProfiles.HEnd - BodyPartProfiles.HStart) * H);
					if (hi < 0 || hi >= H)
						continue;
					var ti = (int)floor((c.y + PI) / (2f * PI) * T);
					ti = ((ti % T) + T) % T;
					var idx = BodyPartProfiles.CellIndex(part, hi, ti);
					grid[idx] = min(grid[idx], c.z);
				}

				// 2. 放射変位
				for (var part = 1; part < P; part++)
				{
					var usable = profiles.Usable[part] != 0;
					for (var hi = 0; hi < H; hi++)
					for (var ti = 0; ti < T; ti++)
					{
						var idx = BodyPartProfiles.CellIndex(part, hi, ti);
						var rMin = grid[idx];
						var radius = profiles.Radius[idx];
						if (!usable || isinf(rMin) || isnan(radius))
						{
							grid[idx] = float.NaN;
							continue;
						}
						var target = clamp(rMin, radius + minGap, radius + maxGap);
						var dr = target - rMin;
						if (maxShrink > 0f)
							dr = max(dr, -maxShrink * rMin); // 周を maxShrink より縮めない(布の潰れを抑える)
						grid[idx] = dr;
					}
				}

				// 3. 縁取り
				for (var pass = 0; pass < 2; pass++)
				{
					for (var c = 0; c < cellCount; c++)
						scratch[c] = grid[c];
					for (var part = 1; part < P; part++)
					for (var hi = 0; hi < H; hi++)
					for (var ti = 0; ti < T; ti++)
					{
						var idx = BodyPartProfiles.CellIndex(part, hi, ti);
						if (!isnan(scratch[idx]))
							continue;
						var sum = 0f;
						var count = 0;
						if (hi > 0) Accumulate(scratch[BodyPartProfiles.CellIndex(part, hi - 1, ti)], ref sum, ref count);
						if (hi < H - 1) Accumulate(scratch[BodyPartProfiles.CellIndex(part, hi + 1, ti)], ref sum, ref count);
						Accumulate(scratch[BodyPartProfiles.CellIndex(part, hi, (ti + T - 1) % T)], ref sum, ref count);
						Accumulate(scratch[BodyPartProfiles.CellIndex(part, hi, (ti + 1) % T)], ref sum, ref count);
						if (count > 0)
							grid[idx] = sum / count;
					}
				}
				for (var c = 0; c < cellCount; c++)
				{
					if (isnan(grid[c]))
						grid[c] = 0f;
				}

				// 4. 平滑化
				for (var it = 0; it < smoothIterations; it++)
				{
					for (var c = 0; c < cellCount; c++)
						scratch[c] = grid[c];
					for (var part = 1; part < P; part++)
					for (var hi = 0; hi < H; hi++)
					for (var ti = 0; ti < T; ti++)
					{
						var idx = BodyPartProfiles.CellIndex(part, hi, ti);
						var sum = 0f;
						var count = 0;
						if (hi > 0) Accumulate(scratch[BodyPartProfiles.CellIndex(part, hi - 1, ti)], ref sum, ref count);
						if (hi < H - 1) Accumulate(scratch[BodyPartProfiles.CellIndex(part, hi + 1, ti)], ref sum, ref count);
						Accumulate(scratch[BodyPartProfiles.CellIndex(part, hi, (ti + T - 1) % T)], ref sum, ref count);
						Accumulate(scratch[BodyPartProfiles.CellIndex(part, hi, (ti + 1) % T)], ref sum, ref count);
						if (count > 0)
							grid[idx] = lerp(scratch[idx], sum / count, smoothStrength);
					}
				}
			}

			private static void Accumulate(float value, ref float sum, ref int count)
			{
				if (isnan(value))
					return;
				sum += value;
				count++;
			}
		}

		/// <summary>
		/// 各頂点の所属スロットごとに Δr をサンプルし、放射方向の変位に合成する。
		/// jointFan なら、所属が混ざる頂点(関節・縫い目)は所属パーツの軸(線分)の最寄り点から放射する
		/// (折れ線軸の近似。関節の外側で放射方向が扇状につながる)。
		/// </summary>
		[BurstCompile]
		public struct RadialApplyJob : IJobParallelFor
		{
			[ReadOnly] public NativeArray<float4> coords;
			[ReadOnly] public NativeArray<float3> radialDirs;
			[ReadOnly] public NativeArray<PartWeights> parts;
			[ReadOnly] public NativeArray<float> grid;
			[ReadOnly] public NativeArray<float3> vertices;
			public BodyPartProfiles profiles;
			public int jointFan;
			[WriteOnly] public NativeArray<float3> delta;
			[WriteOnly] public NativeArray<byte> valid;

			public void Execute(int index)
			{
				var sum = float3.zero;
				var magnitude = 0f;
				byte any = 0;
				var pw = parts[index];
				for (var s = 0; s < 4; s++)
				{
					var c = coords[index * 4 + s];
					if (c.w <= 0f)
						continue;
					var part = pw.Parts[s];
					var d = BodyPartProfiles.SampleGridSkipNaN(in grid, part, c.x, c.y);
					if (isnan(d))
						continue;
					sum += radialDirs[index * 4 + s] * (d * c.w);
					magnitude += d * c.w;
					any = 1;
				}

				if (jointFan != 0 && any != 0 && pw.Parts.y != 0 && pw.Weights.y > 0f)
				{
					// 所属パーツの線分の最寄り点から放射する
					var p = vertices[index];
					var bestSq = float.MaxValue;
					var nearest = float3.zero;
					for (var s = 0; s < 4; s++)
					{
						var part = pw.Parts[s];
						if (part == 0 || pw.Weights[s] <= 0f || !profiles.IsUsable(part))
							continue;
						var axis = profiles.Axes[part];
						var t = clamp(dot(p - axis.Origin, axis.Direction), 0f, axis.Length);
						var q = axis.Origin + axis.Direction * t;
						var dSq = distancesq(p, q);
						if (dSq < bestSq)
						{
							bestSq = dSq;
							nearest = q;
						}
					}
					if (bestSq > 1e-12f && bestSq < float.MaxValue)
						sum = (p - nearest) * (magnitude / sqrt(bestSq));
				}
				delta[index] = sum;
				valid[index] = any;
			}
		}

		/// <summary>装飾の追従: follow[i] ≥ 0 の頂点は、その頂点の変位と有効フラグをそのまま写す</summary>
		[BurstCompile]
		public struct FollowJob : IJobParallelFor
		{
			[ReadOnly] public NativeArray<int> follow;
			[ReadOnly] public NativeArray<float3> input;
			[ReadOnly] public NativeArray<byte> validIn;
			[WriteOnly] public NativeArray<float3> output;
			[WriteOnly] public NativeArray<byte> validOut;

			public void Execute(int index)
			{
				var source = follow[index];
				if (source < 0 || source == index)
				{
					output[index] = input[index];
					validOut[index] = validIn[index];
					return;
				}
				output[index] = input[source];
				validOut[index] = validIn[source];
			}
		}

		/// <summary>
		/// 縫い目の頂点(所属が 2 パーツ以上、または隣に支配パーツの違う頂点がある)を平滑化の対象にする。
		/// all が非 0 なら有効な全頂点。
		/// </summary>
		[BurstCompile]
		public struct SeamMaskJob : IJobParallelFor
		{
			[ReadOnly] public NativeArray<int> adjStart;
			[ReadOnly] public NativeArray<int> adjList;
			[ReadOnly] public NativeArray<PartWeights> parts;
			[ReadOnly] public NativeArray<byte> valid;
			public int all;
			[WriteOnly] public NativeArray<byte> target;

			public void Execute(int index)
			{
				if (valid[index] == 0)
				{
					target[index] = 0;
					return;
				}
				if (all != 0)
				{
					target[index] = 1;
					return;
				}
				var pw = parts[index];
				if (pw.Parts.y != 0 && pw.Weights.y > 0f)
				{
					target[index] = 1;
					return;
				}
				var dominant = pw.Parts.x;
				var start = adjStart[index];
				var end = adjStart[index + 1];
				for (var i = start; i < end; i++)
				{
					if (parts[adjList[i]].Parts.x != dominant)
					{
						target[index] = 1;
						return;
					}
				}
				target[index] = 0;
			}
		}

		/// <summary>対象の頂点(target)だけ、有効な隣接の平均へ strength だけ寄せる(SmoothJob の対象限定版)</summary>
		[BurstCompile]
		public struct MaskedSmoothJob : IJobParallelFor
		{
			[ReadOnly] public NativeArray<int> adjStart;
			[ReadOnly] public NativeArray<int> adjList;
			[ReadOnly] public NativeArray<byte> valid;
			[ReadOnly] public NativeArray<byte> target;
			[ReadOnly] public NativeArray<float3> input;
			[WriteOnly] public NativeArray<float3> output;
			public float strength;

			public void Execute(int index)
			{
				var value = input[index];
				if (target[index] == 0)
				{
					output[index] = value;
					return;
				}
				var start = adjStart[index];
				var end = adjStart[index + 1];
				var sum = float3.zero;
				var count = 0;
				for (var i = start; i < end; i++)
				{
					var j = adjList[i];
					if (valid[j] == 0)
						continue;
					sum += input[j];
					count++;
				}
				output[index] = count == 0 ? value : lerp(value, sum / count, strength);
			}
		}

		// ---- ジョブ共通の数式 ----

		/// <summary>頂点の所属パーツから検索マスクを作る(重み 0.25 以上 + 支配パーツ。所属なしは 0 = 絞らない)</summary>
		public static int PartMaskOf(in PartWeights pw)
		{
			var mask = pw.Mask(0.25f);
			if (pw.Parts.x != 0)
				mask |= 1 << pw.Parts.x;
			return mask;
		}

		/// <summary>
		/// 二重球の領域重み(軸空間)。内半径の内側で 1、外半径で 0、間は smoothstep で減衰。
		/// </summary>
		public static float RegionWeight(float3 worldPoint, float4x4 meshToAxis, int wholeMesh,
			float innerRadius, float outerRadius)
		{
			if (wholeMesh != 0)
				return 1f;

			var dist = length(mul(meshToAxis, float4(worldPoint, 1f)).xyz);
			if (dist >= outerRadius)
				return 0f;
			if (dist <= innerRadius)
				return 1f;
			return smoothstep(0f, 1f, unlerp(outerRadius, innerRadius, dist));
		}

		/// <summary>
		/// 押し出し(隙間が minGap 未満)の基準にする最近接点を決める。
		/// パーツ制限付きの最近接(hit)は、パーツの三角形集合が閉じていない(骨盤と太ももの境界の面など)ため、
		/// パーツの外側にある頂点でも境界面の裏側にあれば「内側」と判定し、面の向こうへ大きく押し込んでしまう
		/// (腰より下に垂れる裾が股へ押し込まれる)。そこで全身の最近接点も見て、必要な補正量(minGap − d)が
		/// 小さい方を採用する。全身で見て隙間が足りていれば押し出さない(false)。
		/// hit が minGap 以上ならそのまま false。
		/// </summary>
		public static bool ResolvePushOut(in MeshSurfaceData surface, float3 p, float searchDistance, int partMask,
			float minGap, ref MeshSurfaceHit hit)
		{
			if (hit.SignedDistance >= minGap)
				return false;
			if (partMask == 0 || !surface.HasPartMasks)
				return true;
			if (surface.FindClosest(p, searchDistance, 0, out var whole) && whole.Triangle != hit.Triangle &&
			    whole.SignedDistance > hit.SignedDistance)
			{
				hit = whole;
				if (hit.SignedDistance >= minGap)
					return false;
			}
			return true;
		}

		/// <summary>探索距離の上限付近(75%〜100%)で 1 → 0 へ滑らかに減衰する係数</summary>
		public static float SearchFalloff(float distance, float searchDistance)
		{
			var fadeStart = searchDistance * 0.75f;
			if (searchDistance <= 0f || distance <= fadeStart)
				return 1f;
			return 1f - smoothstep(fadeStart, searchDistance, distance);
		}

		/// <summary>
		/// 最近接点から見た「体の外向き」の単位ベクトル。
		/// 頂点が表面から離れていれば (p − q) を符号で外向きに揃えたもの、
		/// 表面上なら擬似法線を使う。
		/// </summary>
		public static float3 OutwardDirection(float3 p, in MeshSurfaceHit hit)
		{
			if (hit.Distance > 1e-6f)
				return (p - hit.Point) * (hit.Sign / hit.Distance);
			return hit.Normal;
		}
	}
}
