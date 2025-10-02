using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Unity.Jobs;
using Unity.Burst;
using Unity.Collections;
using Unity.Mathematics;

namespace Deform.Masking
{
    /// <summary>
    /// Serializable per-submesh island selection data
    /// サブメッシュ毎のアイランド選択データ
    /// </summary>
    [System.Serializable]
    public class SubmeshIslandSelection
    {
        public int submeshIndex;
        public List<int> selectedIslandIDs = new List<int>();

        public SubmeshIslandSelection(int submeshIndex, List<int> islandIDs)
        {
            this.submeshIndex = submeshIndex;
            this.selectedIslandIDs = islandIDs ?? new List<int>();
        }
    }

    /// <summary>
    /// UV Island based mask for deformation
    /// UVアイランドに基づく変形マスク
    /// </summary>
    [System.Serializable]
    [Deformer(Name = "UV Island Mask", Description = "Masks deformation based on UV island selection", Type = typeof(UVIslandMask), Category = Category.Mask)]
    public class UVIslandMask : Deformer
    {
        [Header("Mask Settings")]
        [SerializeField] private List<int> selectedSubmeshes = new List<int> { 0 };
        [SerializeField] private int currentPreviewSubmesh = 0; // Current submesh being previewed in editor
        [SerializeField] private List<int> selectedIslandIDs = new List<int>(); // Legacy: flat list for backward compatibility
        [SerializeField] private List<SubmeshIslandSelection> perSubmeshSelections = new List<SubmeshIslandSelection>(); // New: per-submesh selections
        [SerializeField] private List<int> selectedVertexIndices = new List<int>(); // Direct vertex list
        [SerializeField] private bool invertMask = false;
        [SerializeField, Range(0f, 1f)] private float maskStrength = 1f;
        
        // Runtime data
        [System.NonSerialized] private NativeArray<float> maskValues;
        [System.NonSerialized] private bool maskDataReady = false;
        [System.NonSerialized] private bool isDisposing = false;
	    [System.NonSerialized] private Mesh cachedMesh;
	    [System.NonSerialized] private Mesh originalMesh;
        
        // Cached renderer for editor access
        [System.NonSerialized] private Renderer cachedRenderer;
        [System.NonSerialized] private Transform cachedRendererTransform;
        
        // Properties for editor access
        public List<int> SelectedSubmeshes => selectedSubmeshes;
        public int CurrentPreviewSubmesh { get => currentPreviewSubmesh; set => currentPreviewSubmesh = value; }
        public List<int> SelectedIslandIDs => selectedIslandIDs; // Legacy property for backward compatibility
        public List<SubmeshIslandSelection> PerSubmeshSelections => perSubmeshSelections;
        public List<int> SelectedVertexIndices => selectedVertexIndices;
        public bool InvertMask { get => invertMask; set => invertMask = value; }
	    public float MaskStrength { get => maskStrength; set => maskStrength = Mathf.Clamp01(value); }
	    public Mesh CachedMesh => cachedMesh;
	    public Mesh OriginalMesh => originalMesh;
	    public Renderer CachedRenderer => cachedRenderer;
	    public Transform CachedRendererTransform => cachedRendererTransform;
        
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
            
	        if (!maskDataReady || maskValues.Length != data.Length || data.OriginalMesh != originalMesh)
	        {
		        originalMesh = data.OriginalMesh;
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
            // Safely dispose existing mask values
            if (maskValues.IsCreated && !isDisposing)
            {
                try
                {
                    maskValues.Dispose();
                }
                catch (System.ObjectDisposedException)
                {
                    // Already disposed, continue
                }
            }

            if (!isDisposing)
            {
                maskValues = new NativeArray<float>(data.Length, Allocator.Persistent);
            }

            // Initialize all vertices to masked (1.0 = revert to original, no deformation)
            if (maskValues.IsCreated && !isDisposing)
            {
                for (int i = 0; i < maskValues.Length; i++)
                {
                    maskValues[i] = 1f;
                }
            }

            // Priority 1: Use direct vertex list if provided (most efficient)
            if (selectedVertexIndices != null && selectedVertexIndices.Count > 0)
            {
                foreach (var vertexIndex in selectedVertexIndices)
                {
                    if (vertexIndex < maskValues.Length)
                    {
                        maskValues[vertexIndex] = 0f; // Allow deformation
                    }
                }
            }
            // Priority 2: Fall back to island-based analysis for backward compatibility
            else if (selectedIslandIDs.Count > 0 && selectedSubmeshes.Count > 0)
            {
                var mesh = data.OriginalMesh; // Get mesh from Deformable target
                if (mesh != null)
                {
                    var uvs = new List<Vector2>();
                    mesh.GetUVs(0, uvs);

                    if (uvs != null && uvs.Count > 0)
                    {
                        // Analyze only selected submeshes for better performance
                        var islands = UVIslandAnalyzer.AnalyzeUVIslands(mesh, selectedSubmeshes);

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
            }

            maskDataReady = true;
        }
        
        private void OnDestroy()
        {
            if (!isDisposing)
            {
                DisposeMaskValues();
            }
        }
        
        private void OnDisable()
        {
            // Only dispose in OnDisable for editor scenarios if not already disposing
            if (!isDisposing && Application.isEditor)
            {
                DisposeMaskValues();
            }
        }
        
        private void DisposeMaskValues()
        {
            if (isDisposing) return;
            
            isDisposing = true;
            
            if (maskValues.IsCreated)
            {
                try
                {
                    maskValues.Dispose();
                }
                catch (System.ObjectDisposedException)
                {
                    // Already disposed, ignore
                }
                catch (System.InvalidOperationException)
                {
                    // Invalid state, ignore
                }
            }
            
            maskDataReady = false;
        }
        
        public void SetSelectedIslands(List<int> islandIDs)
        {
            selectedIslandIDs = islandIDs ?? new List<int>();
            maskDataReady = false;
        }

        /// <summary>
        /// Set per-submesh island selections (new format)
        /// サブメッシュ毎のアイランド選択を設定（新形式）
        /// </summary>
        public void SetPerSubmeshSelections(Dictionary<int, HashSet<int>> selections)
        {
            perSubmeshSelections.Clear();
            foreach (var kvp in selections)
            {
                perSubmeshSelections.Add(new SubmeshIslandSelection(kvp.Key, kvp.Value.ToList()));
            }

            // Also update legacy flat list for backward compatibility
            selectedIslandIDs.Clear();
            foreach (var selection in perSubmeshSelections)
            {
                selectedIslandIDs.AddRange(selection.selectedIslandIDs);
            }

            maskDataReady = false;
        }

        /// <summary>
        /// Get per-submesh selections as Dictionary (for editor use)
        /// サブメッシュ毎の選択をDictionaryとして取得（エディタ用）
        /// </summary>
        public Dictionary<int, HashSet<int>> GetPerSubmeshSelections()
        {
            var result = new Dictionary<int, HashSet<int>>();
            foreach (var selection in perSubmeshSelections)
            {
                result[selection.submeshIndex] = new HashSet<int>(selection.selectedIslandIDs);
            }
            return result;
        }

        public void SetSelectedSubmeshes(List<int> submeshIndices)
        {
            selectedSubmeshes = submeshIndices ?? new List<int> { 0 };
            maskDataReady = false;
        }

        /// <summary>
        /// Set selected vertex indices directly (most efficient method)
        /// 選択された頂点インデックスを直接設定（最も効率的な方法）
        /// </summary>
        public void SetSelectedVertexIndices(List<int> vertexIndices)
        {
            selectedVertexIndices = vertexIndices ?? new List<int>();
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
}