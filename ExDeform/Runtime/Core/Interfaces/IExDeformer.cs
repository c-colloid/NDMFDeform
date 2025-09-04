using Unity.Jobs;
using UnityEngine;

namespace ExDeform.Core.Interfaces
{
    /// <summary>
    /// ExDeform拡張Deformer統一基底クラス
    /// 外部Deform拡張との橋渡しとプラグイン化を実現
    /// Deformerクラスを継承してDeform拡張内のDeformerとして使用可能
    /// </summary>
    public abstract class IExDeformer : Deform.Deformer
    {
        /// <summary>
        /// Deformer名前（外部Deform拡張に表示される）
        /// </summary>
        public abstract string DeformerName { get; }
        
        /// <summary>
        /// カテゴリ分類（外部Deformエディタでの分類用）
        /// </summary>
        public abstract DeformerCategory Category { get; }
        
        /// <summary>
        /// 説明文（外部Deformエディタツールチップ等）
        /// </summary>
        public abstract string Description { get; }
        
        /// <summary>
        /// 外部Deformとの互換性バージョン
        /// </summary>
        public abstract System.Version CompatibleDeformVersion { get; }
        
        /// <summary>
        /// 初期化処理（外部Deform拡張読み込み時）
        /// </summary>
        /// <param name="deformable">対象Deformable</param>
        /// <returns>初期化成功時true</returns>
        public abstract bool Initialize(object deformable);
        
        /// <summary>
        /// 外部Deform拡張のMeshData処理と統合
        /// </summary>
        /// <param name="meshData">外部Deformの MeshData</param>
        /// <param name="dependency">Job依存関係</param>
        /// <returns>処理後JobHandle</returns>
        public abstract JobHandle ProcessMesh(object meshData, JobHandle dependency);
        
        /// <summary>
        /// リソースクリーンアップ
        /// </summary>
        public abstract void Cleanup();
        
        /// <summary>
        /// エディタでの表示/非表示制御
        /// </summary>
        public abstract bool IsVisibleInEditor { get; }
        
        /// <summary>
        /// ランタイムでの有効/無効制御
        /// </summary>
        public abstract bool IsEnabledInRuntime { get; }
        
        /// <summary>
        /// Deformerが変更するデータフラグ
        /// </summary>
        public override Deform.DataFlags DataFlags => Deform.DataFlags.Vertices;
        
        /// <summary>
        /// Deformerの処理実装 - ProcessMeshに委譲
        /// </summary>
        /// <param name="data">MeshData</param>
        /// <param name="dependency">Job依存関係</param>
        /// <returns>処理後JobHandle</returns>
        public override JobHandle Process(Deform.MeshData data, JobHandle dependency = default)
        {
            return ProcessMesh(data, dependency);
        }
        
        /// <summary>
        /// 前処理（オプション）
        /// </summary>
        public override void PreProcess()
        {
            // ExDeformerでは必要に応じてオーバーライド
        }
    }
    
    /// <summary>
    /// ExDeformerカテゴリ分類
    /// 外部Deformエディタでの整理に使用
    /// </summary>
    public enum DeformerCategory
    {
        /// <summary>メッシュ変形</summary>
        Deform = 0,
        
        /// <summary>マスク</summary>
        Mask = 1,
        
        /// <summary>UV操作</summary>
        UV = 2,
        
        /// <summary>ユーティリティ</summary>
        Utility = 3,
        
        /// <summary>実験的機能</summary>
        Experimental = 4
    }
}