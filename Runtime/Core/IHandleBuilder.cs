#if UNITY_EDITOR
using UnityEngine;

namespace MeshModifier.NDMFDeform.Core
{
	public enum HandleAxis
	{
		X,
		Y,
		Z,
	}

	public enum HandleLineStyle
	{
		Solid,
		Dotted,
	}

	/// <summary>
	/// 宣言的シーンハンドル API。
	/// デフォーマは DescribeHandles で「どのプロパティをどのハンドルで編集させるか」を宣言し、
	/// 描画・SerializedProperty バインド・Undo・複数選択・座標変換はフレームワーク側が処理する。
	/// 全プリミティブはデフォーマの軸空間(Axis の TRS)で解釈される。
	/// プロパティ名は nameof(シリアライズフィールド) で渡す。
	/// 宣言面は Runtime アセンブリに置く(#if UNITY_EDITOR)ことで、
	/// デフォーマを 1 ファイルで完結して書けるようにしている。実装は Editor アセンブリ側。
	/// </summary>
	public interface IHandleBuilder
	{
		/// <summary>float プロパティ: along 軸上の符号付き距離として編集するスライダー</summary>
		void AxisSlider(string property, HandleAxis along, HandleLineStyle style = HandleLineStyle.Solid);

		/// <summary>
		/// float プロパティ: along 軸の負方向・距離 value×scale の位置に置くスライダー(半径編集用)。
		/// scale は「シリアライズ値と実効距離の比」(例: SphereMask は実効半径が値の 0.5 倍)。
		/// offsetProperty を指定すると、キャップの基点を offsetAxis 軸上のその値へずらす
		/// (円柱の top リングの縁にキャップを載せる等。中空に浮かせないため)。
		/// pairProperty は radius/scope・inner/outer のような同軸ペアのもう一方の半径プロパティ
		/// (scale は同一である前提)。指定すると、ホバー矢印がペアのリングの方向を向く
		/// (外側のハンドルは内向き・内側は外向き)ため、重なっていても互いに区別できる。
		/// 値が等しいときは Solid=外向き / Dotted=内向き。
		/// </summary>
		void RadiusSlider(string property, HandleAxis along, HandleLineStyle style = HandleLineStyle.Solid,
			float scale = 1f, string offsetProperty = null, HandleAxis offsetAxis = HandleAxis.Z,
			string pairProperty = null);

		/// <summary>表示専用の円: normal 軸まわり、offsetProperty の位置に radiusProperty の半径で描く</summary>
		void Circle(HandleAxis normal, string offsetProperty, string radiusProperty, HandleLineStyle style = HandleLineStyle.Solid);

		/// <summary>表示専用の円: normal 軸まわり、固定オフセット位置に radiusProperty×scale の半径で描く</summary>
		void Circle(HandleAxis normal, float offset, string radiusProperty,
			HandleLineStyle style = HandleLineStyle.Solid, float scale = 1f);

		/// <summary>表示専用の円: normal 軸まわり、固定オフセット・固定半径(プロパティ非バインド)</summary>
		void Circle(HandleAxis normal, float offset, float radius, HandleLineStyle style = HandleLineStyle.Solid);

		/// <summary>
		/// Bounds プロパティ: ワイヤーキューブ表示 + 6 面のドラッグハンドルで編集する
		/// (BoxCollider 相当。バウンズは軸空間で解釈される)。
		/// </summary>
		void Box(string boundsProperty, HandleLineStyle style = HandleLineStyle.Solid);

		/// <summary>Vector3 プロパティ: 位置ハンドル</summary>
		void Position(string property);

		/// <summary>表示専用の線分(軸空間)</summary>
		void Line(Vector3 from, Vector3 to, HandleLineStyle style = HandleLineStyle.Solid);

		/// <summary>表示専用の矢印付き線分(軸空間。減衰方向などの向きを示す)</summary>
		void Arrow(Vector3 from, Vector3 to, HandleLineStyle style = HandleLineStyle.Solid);

		/// <summary>
		/// float プロパティ: along 軸上の距離 d = k / 値 の位置で編集するスライダー
		/// (指数減衰の falloff 用。キャップは「減衰 50% の距離」等に置かれ、
		/// ドラッグすると値が距離に反比例して変わる。キャップ位置には半径 ringRadius の
		/// リングが描かれ、操作中は減衰カーブのリング列が表示される)。
		/// </summary>
		void DecaySlider(string property, HandleAxis along, float k, float ringRadius,
			HandleLineStyle style = HandleLineStyle.Solid);

		/// <summary>
		/// 格子制御点編集(Lattice 等)。
		/// pointsProperty は float3[] のシリアライズフィールド(軸空間 [-0.5,0.5] の位置)、
		/// resolution はその格子分割数。
		/// mirrorAxisProperty(任意)は MirrorAxis のシリアライズフィールドで、
		/// 編集時の対称マッピング(選択・移動の対称側反映)に使う。
		/// クリック / Shift 追加 / 矩形 / Ctrl ループ選択 / Ctrl+Shift シート選択、
		/// 奥点フェード・スライス表示はフレームワーク側が提供する。
		/// </summary>
		void PointGrid(string pointsProperty, Vector3Int resolution, string mirrorAxisProperty = null);
	}
}
#endif
