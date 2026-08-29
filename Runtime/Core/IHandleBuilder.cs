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

		/// <summary>float プロパティ: along 軸の負方向・距離 value の位置に置くスライダー(円柱半径編集用)</summary>
		void RadiusSlider(string property, HandleAxis along, HandleLineStyle style = HandleLineStyle.Solid);

		/// <summary>表示専用の円: normal 軸まわり、offsetProperty の位置に radiusProperty の半径で描く</summary>
		void Circle(HandleAxis normal, string offsetProperty, string radiusProperty, HandleLineStyle style = HandleLineStyle.Solid);

		/// <summary>Vector3 プロパティ: 位置ハンドル</summary>
		void Position(string property);

		/// <summary>表示専用の線分(軸空間)</summary>
		void Line(Vector3 from, Vector3 to, HandleLineStyle style = HandleLineStyle.Solid);
	}
}
#endif
