using System.Collections.Generic;
using UnityEngine;
using Unity.Jobs;
using Unity.Burst;
using Unity.Collections;
using Unity.Mathematics;

namespace Deform.Masking
{
    /// <summary>
    /// UV Island based mask for deformation
    /// UVアイランドに基づく変形マスク
    /// </summary>
    [System.Serializable]
    [Deformer(Name = "UV Island Mask", Description = "Masks deformation based on UV island selection", Type = typeof(UVIslandMask), Category = Category.Mask)]
    public class UVIslandMask : Deformer
    {
        [Header("Mask Settings")]
        [SerializeField] private List<int> selectedIslandIDs = new List<int>();
        [SerializeField] private bool invertMask = false;
        [SerializeField, Range(0f, 1f)] private float maskStrength = 1f;
        
        // Cached UV Island data (serialized for performance)
        [SerializeField, HideInInspector] private List<SerializableUVIsland> cachedIslands = new List<SerializableUVIsland>();
        [SerializeField, HideInInspector] private string cachedMeshInstanceID = "";
        [SerializeField, HideInInspector] private long cachedMeshModificationTime = 0;
        
        // Runtime data
        [System.NonSerialized] private NativeArray<float> maskValues;
        [System.NonSerialized] private bool maskDataReady = false;
        [System.NonSerialized] private Mesh cachedMesh;
        
        // Cached renderer for editor access
        [System.NonSerialized] private Renderer cachedRenderer;
        [System.NonSerialized] private Transform cachedRendererTransform;
        
        // Properties for editor access
        public List<int> SelectedIslandIDs => selectedIslandIDs;
        public bool InvertMask { get => invertMask; set => invertMask = value; }
	    public float MaskStrength { get => maskStrength; set => maskStrength = Mathf.Clamp01(value); }
	    public Mesh CachedMesh => cachedMesh;
	    public Renderer CachedRenderer => cachedRenderer;
	    public Transform CachedRendererTransform => cachedRendererTransform;
	    
	    // Cached UV Island access for editor performance
	    public List<SerializableUVIsland> CachedIslands => cachedIslands;
	    public bool HasValidIslandCache => !string.IsNullOrEmpty(cachedMeshInstanceID) && cachedIslands.Count > 0;
        
        public override DataFlags DataFlags => DataFlags.Vertices;
        
        public override JobHandle Process(MeshData data, JobHandle dependency = default)
        {
            // Cache renderer information for editor access
            if (cachedRenderer == null || cachedRendererTransform == null)
            {
                cachedRenderer = data.Target.GetRenderer();
                cachedRendererTransform = cachedRenderer?.transform;
            }
            
            // Update mask data when mesh changes or no mask exists
            if (!maskDataReady || maskValues.Length != data.Length || data.Target.GetMesh() != cachedMesh)
            {
	            cachedMesh = data.DynamicMesh;
                UpdateMaskData(data);
            }
            
            if (!maskValues.IsCreated || maskValues.Length == 0)
            {
                // No mask data, pass through unchanged
                return dependency;
            }
            
            var job = new UVIslandMaskJob
            {
                currentVertices = data.DynamicNative.VertexBuffer,
                maskVertices = data.DynamicNative.MaskVertexBuffer,
                maskValues = maskValues,
                invertMask = invertMask,
                maskStrength = maskStrength
            };
            
            return job.Schedule(data.Length, 64, dependency);
        }
        
        private void UpdateMaskData(MeshData data)
        {
            if (maskValues.IsCreated)
                maskValues.Dispose();
                
            maskValues = new NativeArray<float>(data.Length, Allocator.Persistent);
            
            // Initialize all vertices to masked (1.0 = revert to original, no deformation)
            for (int i = 0; i < maskValues.Length; i++)
            {
                maskValues[i] = 1f;
            }
            
            // Apply deformation only to selected islands
	        var mesh = data.OriginalMesh; // Get mesh from Deformable target
            if (mesh != null && selectedIslandIDs.Count > 0)
            {
                var uvs = mesh.uv;
                var triangles = mesh.triangles;
                
                if (uvs != null && uvs.Length > 0)
                {
                    var islands = UVIslandAnalyzer.AnalyzeUVIslands(mesh);
                    
                    // Allow deformation for selected islands only
                    foreach (var islandID in selectedIslandIDs)
                    {
                        var island = islands.Find(i => i.islandID == islandID);
                        if (island != null)
                        {
                            foreach (var vertexIndex in island.vertexIndices)
                            {
                                if (vertexIndex < maskValues.Length)
                                {
                                    maskValues[vertexIndex] = 0f; // Allow deformation
                                }
                            }
                        }
                    }
                }
            }
            
            maskDataReady = true;
        }
        
        private void OnDestroy()
        {
            if (maskValues.IsCreated)
                maskValues.Dispose();
        }
        
        public void SetSelectedIslands(List<int> islandIDs)
        {
            selectedIslandIDs = islandIDs ?? new List<int>();
            maskDataReady = false;
        }
        
        /// <summary>
        /// Force update renderer cache from the Deformable component (for editor use)
        /// </summary>
        public void UpdateRendererCache()
        {
            var deformable = GetComponent<Deform.Deformable>();
            if (deformable != null && deformable.HasTarget())
            {
                cachedRenderer = deformable.GetRenderer();
                cachedRendererTransform = cachedRenderer?.transform;
            }
        }
        
        /// <summary>
        /// Update cached UV island data for editor performance
        /// エディタパフォーマンス向上のためのUVアイランドキャッシュ更新
        /// </summary>
        public void UpdateIslandCache(List<UVIslandAnalyzer.UVIsland> islands, Mesh originalMesh)
        {
            if (originalMesh == null || islands == null) return;
            
            // Use original mesh for stable caching, not dynamic mesh
            cachedMeshInstanceID = originalMesh.GetInstanceID().ToString();
            
            #if UNITY_EDITOR
            var assetPath = UnityEditor.AssetDatabase.GetAssetPath(originalMesh);
            if (!string.IsNullOrEmpty(assetPath))
            {
                cachedMeshModificationTime = UnityEditor.AssetDatabase.GetAssetDependencyHash(assetPath).GetHashCode();
            }
            else
            {
                // For runtime meshes, use vertex count and triangle count as simple hash
                cachedMeshModificationTime = (originalMesh.vertexCount * 31 + originalMesh.triangles.Length).GetHashCode();
            }
            #endif
            
            cachedIslands.Clear();
            foreach (var island in islands)
            {
                cachedIslands.Add(SerializableUVIsland.FromUVIsland(island));
            }
            
            
            #if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(this);
            #endif
        }
        
        /// <summary>
        /// Check if cached island data is valid for current mesh
        /// 現在のメッシュに対してキャッシュされたアイランドデータが有効かチェック
        /// </summary>
        public bool IsIslandCacheValid(Mesh mesh)
        {
            if (mesh == null)
                return false;
                
            if (cachedIslands.Count == 0)
                return false;
                
            if (string.IsNullOrEmpty(cachedMeshInstanceID))
                return false;
                
            var currentMeshID = mesh.GetInstanceID().ToString();
            if (cachedMeshInstanceID != currentMeshID)
                return false;
                
            #if UNITY_EDITOR
            var assetPath = UnityEditor.AssetDatabase.GetAssetPath(mesh);
            if (string.IsNullOrEmpty(assetPath))
                return true; // Runtime meshes don't have file paths, accept cache
            
            var currentModTime = UnityEditor.AssetDatabase.GetAssetDependencyHash(assetPath).GetHashCode();
            if (currentModTime != cachedMeshModificationTime)
                return false;
            #endif
            
            return true;
        }
    }
    
    [BurstCompile(CompileSynchronously = true)]
    public struct UVIslandMaskJob : IJobParallelFor
    {
        public NativeArray<float3> currentVertices;
        [ReadOnly] public NativeArray<float3> maskVertices;
        [ReadOnly] public NativeArray<float> maskValues;
        [ReadOnly] public bool invertMask;
        [ReadOnly] public float maskStrength;
        
        public void Execute(int index)
        {
            if (index >= maskValues.Length) return;
            
            float maskValue = maskValues[index];
            if (invertMask)
                maskValue = 1f - maskValue;
                
            // Apply mask strength
            float t = math.lerp(0f, maskValue, maskStrength);
            
            // Lerp between current vertex (deformed) and mask vertex (original)
            // t=0: keep deformation, t=1: revert to original (no deformation)
            currentVertices[index] = math.lerp(currentVertices[index], maskVertices[index], t);
        }
    }
    
    /// <summary>
    /// Serializable UV Island data for caching performance
    /// パフォーマンス向上のためのシリアライズ可能なUVアイランドデータ
    /// </summary>
    [System.Serializable]
    public class SerializableUVIsland
    {
        public int islandID;
        public List<int> vertexIndices = new List<int>();
        public List<int> triangleIndices = new List<int>();
        public List<Vector2> uvCoordinates = new List<Vector2>();
        public Bounds uvBounds;
        public Color maskColor = Color.red;
        public int faceCount => triangleIndices.Count;
        
        // Convert from UVIslandAnalyzer.UVIsland
        public static SerializableUVIsland FromUVIsland(UVIslandAnalyzer.UVIsland island)
        {
            return new SerializableUVIsland
            {
                islandID = island.islandID,
                vertexIndices = new List<int>(island.vertexIndices),
                triangleIndices = new List<int>(island.triangleIndices),
                uvCoordinates = new List<Vector2>(island.uvCoordinates),
                uvBounds = island.uvBounds,
                maskColor = island.maskColor
            };
        }
        
        // Convert to UVIslandAnalyzer.UVIsland for compatibility
        public UVIslandAnalyzer.UVIsland ToUVIsland()
        {
            return new UVIslandAnalyzer.UVIsland
            {
                islandID = this.islandID,
                vertexIndices = new List<int>(this.vertexIndices),
                triangleIndices = new List<int>(this.triangleIndices),
                uvCoordinates = new List<Vector2>(this.uvCoordinates),
                uvBounds = this.uvBounds,
                maskColor = this.maskColor
            };
        }
    }
}