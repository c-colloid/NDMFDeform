using Unity.Jobs;
using UnityEngine;

namespace ExDeform.Runtime.Core.Interfaces
{
    /// <summary>
    /// ExDeform拡張Deformer統一インターフェース
    /// 外部Deform拡張との橋渡しとプラグイン化を実現
    /// </summary>
    public interface IExDeformer
    {
        /// <summary>
        /// Deformer名前（外部Deform拡張に表示される）
        /// </summary>
        string DeformerName { get; }
        
        /// <summary>
        /// カテゴリ分類（外部Deformエディタでの分類用）
        /// </summary>
        DeformerCategory Category { get; }
        
        /// <summary>
        /// 説明文（外部Deformエディタツールチップ等）
        /// </summary>
        string Description { get; }
        
        /// <summary>
        /// 外部Deformとの互換性バージョン
        /// </summary>
        System.Version CompatibleDeformVersion { get; }
        
        /// <summary>
        /// 初期化処理（外部Deform拡張読み込み時）
        /// </summary>
        /// <param name="deformable">対象Deformable</param>
        /// <returns>初期化成功時true</returns>
        bool Initialize(object deformable);
        
        /// <summary>
        /// 外部Deform拡張のMeshData処理と統合
        /// </summary>
        /// <param name="meshData">外部Deformの MeshData</param>
        /// <param name="dependency">Job依存関係</param>
        /// <returns>処理後JobHandle</returns>
        JobHandle ProcessMesh(object meshData, JobHandle dependency);
        
        /// <summary>
        /// リソースクリーンアップ
        /// </summary>
        void Cleanup();
        
        /// <summary>
        /// エディタでの表示/非表示制御
        /// </summary>
        bool IsVisibleInEditor { get; }
        
        /// <summary>
        /// ランタイムでの有効/無効制御
        /// </summary>
        bool IsEnabledInRuntime { get; }
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