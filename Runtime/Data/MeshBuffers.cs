using System;
using Unity.Collections;
using Unity.Mathematics;

namespace MeshModifier.NDMFDeform.Core
{
	/// <summary>
	/// 変形対象のメッシュチャンネルを NativeArray として保持するバッファ。
	/// ベイクコア(Editor 側)が構築し、各デフォーマのジョブが読み書きする。
	/// 頂点数・順序は常に保存される(インプレース変形のみ)。
	///
	/// 契約: Vertices は常に生成される。Normals / Tangents は
	/// スタック内のいずれかのデフォーマが DataFlags で要求し、かつ
	/// 元メッシュがそのチャンネルを持つ場合のみ生成される。
	/// Vertices 以外を使うデフォーマは Schedule 内で IsCreated を確認すること。
	/// </summary>
	public struct MeshBuffers : IDisposable
	{
		public NativeArray<float3> Vertices;
		public NativeArray<float3> Normals;
		public NativeArray<float4> Tangents;
		public int Length;

		public void Dispose()
		{
			if (Vertices.IsCreated) Vertices.Dispose();
			if (Normals.IsCreated) Normals.Dispose();
			if (Tangents.IsCreated) Tangents.Dispose();
			Length = 0;
		}
	}
}
