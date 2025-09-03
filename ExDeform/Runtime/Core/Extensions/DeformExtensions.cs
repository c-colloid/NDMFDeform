using System;
using System.Collections.Generic;
using UnityEngine;
using Unity.Jobs;
using ExDeform.Core.Interfaces;

namespace ExDeform.Core.Extensions
{
    /// <summary>
    /// 外部Deform拡張との統合を行う拡張メソッド群
    /// リフレクションを使用して外部Deform APIに安全にアクセス
    /// </summary>
    public static class DeformExtensions
    {
        #region 外部Deform型キャッシュ
        private static Type deformableType;
        private static Type meshDataType;
        private static Type deformerType;
        private static bool typesInitialized = false;
        
        /// <summary>
        /// 外部Deform型の遅延初期化
        /// アセンブリが存在しない場合のフォールバック対応
        /// </summary>
        private static void InitializeDeformTypes()
        {
            if (typesInitialized) return;
            
            try
            {
                // 外部Deformアセンブリから型を取得
                deformableType = Type.GetType("Deform.Deformable, Deform");
                meshDataType = Type.GetType("Deform.MeshData, Deform");
                deformerType = Type.GetType("Deform.Deformer, Deform");
                
                typesInitialized = true;
                
                if (deformableType == null || meshDataType == null || deformerType == null)
                {
                    Debug.LogWarning("[ExDeform] 外部Deform拡張が見つかりません。一部機能が制限されます。");
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[ExDeform] 外部Deform統合の初期化に失敗: {e.Message}");
                typesInitialized = true; // エラーでも初期化完了扱い（無限ループ回避）
            }
        }
        #endregion
        
        #region Deformable拡張メソッド
        /// <summary>
        /// 外部DeformableからMeshDataを安全に取得
        /// </summary>
        /// <param name="deformableObj">外部Deformableオブジェクト</param>
        /// <returns>MeshData、取得失敗時null</returns>
        public static object GetMeshDataSafe(this object deformableObj)
        {
            InitializeDeformTypes();
            
            if (deformableType == null || deformableObj?.GetType() != deformableType)
                return null;
                
            try
            {
                // リフレクションでMeshDataプロパティにアクセス
                var meshDataProperty = deformableType.GetProperty("MeshData");
                return meshDataProperty?.GetValue(deformableObj);
            }
            catch (Exception e)
            {
                Debug.LogError($"[ExDeform] MeshData取得エラー: {e.Message}");
                return null;
            }
        }
        
        /// <summary>
        /// 外部Deformableにカスタムデformerを追加
        /// </summary>
        /// <param name="deformableObj">外部Deformableオブジェクト</param>
        /// <param name="customDeformer">追加するカスタムDeformer</param>
        /// <returns>追加成功時true</returns>
        public static bool AddExDeformer(this object deformableObj, IExDeformer customDeformer)
        {
            InitializeDeformTypes();
            
            if (deformableType == null || deformableObj?.GetType() != deformableType)
                return false;
                
            try
            {
                // ExDeformerラッパーを作成して外部Deformに登録
                var wrapper = new ExDeformerWrapper(customDeformer);
                
                var addDeformerMethod = deformableType.GetMethod("AddDeformer", new[] { deformerType });
                addDeformerMethod?.Invoke(deformableObj, new object[] { wrapper });
                
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"[ExDeform] Deformer追加エラー: {e.Message}");
                return false;
            }
        }
        
        /// <summary>
        /// メッシュハッシュ値の計算（キャッシュキー用）
        /// </summary>
        /// <param name="meshDataObj">外部MeshDataオブジェクト</param>
        /// <returns>ハッシュ値</returns>
        public static int GetMeshHash(this object meshDataObj)
        {
            InitializeDeformTypes();
            
            if (meshDataType == null || meshDataObj?.GetType() != meshDataType)
                return 0;
                
            try
            {
                // VertexBufferプロパティからハッシュ計算
                var vertexBufferProperty = meshDataType.GetProperty("VertexBuffer");
                var vertexBuffer = vertexBufferProperty?.GetValue(meshDataObj);
                
                if (vertexBuffer != null)
                {
                    return vertexBuffer.GetHashCode();
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[ExDeform] メッシュハッシュ計算エラー: {e.Message}");
            }
            
            return meshDataObj?.GetHashCode() ?? 0;
        }
        #endregion
        
        #region MeshData拡張メソッド
        /// <summary>
        /// 外部MeshDataから頂点数を取得
        /// </summary>
        public static int GetVertexCount(this object meshDataObj)
        {
            InitializeDeformTypes();
            
            if (meshDataType == null || meshDataObj?.GetType() != meshDataType)
                return 0;
                
            try
            {
                var lengthProperty = meshDataType.GetProperty("Length");
                return (int)(lengthProperty?.GetValue(meshDataObj) ?? 0);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[ExDeform] 頂点数取得エラー: {e.Message}");
                return 0;
            }
        }
        
        /// <summary>
        /// 外部MeshDataから元メッシュを取得
        /// </summary>
        public static Mesh GetOriginalMesh(this object meshDataObj)
        {
            InitializeDeformTypes();
            
            if (meshDataType == null || meshDataObj?.GetType() != meshDataType)
                return null;
                
            try
            {
                var meshProperty = meshDataType.GetProperty("OriginalMesh");
                return meshProperty?.GetValue(meshDataObj) as Mesh;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[ExDeform] 元メッシュ取得エラー: {e.Message}");
                return null;
            }
        }
        #endregion
        
        #region 外部Deform互換性チェック
        /// <summary>
        /// 外部Deform拡張の利用可能性確認
        /// </summary>
        public static bool IsDeformAvailable()
        {
            InitializeDeformTypes();
            return deformableType != null && meshDataType != null && deformerType != null;
        }
        
        /// <summary>
        /// 外部Deformのバージョン情報取得
        /// </summary>
        public static Version GetDeformVersion()
        {
            try
            {
                var assembly = deformableType?.Assembly;
                var version = assembly?.GetName().Version;
                return version ?? new Version(0, 0, 0);
            }
            catch
            {
                return new Version(0, 0, 0);
            }
        }
        #endregion
    }
    
    /// <summary>
    /// IExDeformerを外部Deformerでラップするアダプター
    /// </summary>
    internal class ExDeformerWrapper
    {
        private readonly IExDeformer exDeformer;
        
        public ExDeformerWrapper(IExDeformer exDeformer)
        {
            this.exDeformer = exDeformer;
        }
        
        // 外部Deformerインターフェースの実装
        // リフレクションや動的プロキシを使用して外部APIと統合
        // （実装詳細は外部Deform拡張の仕様に依存）
    }
}