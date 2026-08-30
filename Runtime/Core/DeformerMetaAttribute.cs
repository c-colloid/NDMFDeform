using System;

namespace MeshModifier.NDMFDeform.Core
{
	/// <summary>
	/// デフォーマの機能ベース分類。
	/// 「デフォーマ追加」メニューの階層とスタックUI上のバッジ表示に使用する。
	/// </summary>
	public enum DeformerCategory
	{
		/// <summary>頂点位置を変形する</summary>
		Shape = 0,

		/// <summary>適用範囲・重みを制御する</summary>
		Mask = 1,

		/// <summary>補助機能</summary>
		Utility = 2,

		/// <summary>実験的機能</summary>
		Experimental = 3,
	}

	/// <summary>
	/// デフォーマのメタデータ宣言。
	/// 「デフォーマ追加」メニュー・分類・ツールチップに使用する。
	/// </summary>
	[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
	public sealed class DeformerMetaAttribute : Attribute
	{
		public string Name { get; set; }
		public DeformerCategory Category { get; set; } = DeformerCategory.Shape;
		public string Description { get; set; }
	}
}
