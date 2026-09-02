using MeshModifier.NDMFDeform.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine.UIElements;

namespace MeshModifier.NDMFDeform.Tests
{
	/// <summary>
	/// エディタ UI アセット(UXML / USS)が GUID から解決できることの検証。
	/// NdmfDeformUI の GUID 定数と各 .meta の不一致(リネーム・再生成事故)を検出する。
	/// </summary>
	public class EditorUiAssetTests
	{
		[TestCase(NdmfDeformUI.CommonStyleGuid, TestName = "CommonStyle")]
		public void StyleSheetResolves(string guid)
		{
			Assert.That(NdmfDeformUI.Load<StyleSheet>(guid), Is.Not.Null,
				$"USS が GUID {guid} から解決できません(.meta と NdmfDeformUI の GUID を一致させてください)");
		}

		[TestCase(NdmfDeformUI.DeformerInspectorGuid, TestName = "DeformerInspector")]
		[TestCase(NdmfDeformUI.StackInspectorGuid, TestName = "StackInspector")]
		[TestCase(NdmfDeformUI.StackRowGuid, TestName = "StackRow")]
		[TestCase(NdmfDeformUI.StackInlineGuid, TestName = "StackInline")]
		[TestCase(NdmfDeformUI.LatticeInspectorGuid, TestName = "LatticeInspector")]
		[TestCase(NdmfDeformUI.UVIslandMaskInspectorGuid, TestName = "UVIslandMaskInspector")]
		[TestCase(NdmfDeformUI.UVIslandSelectorGuid, TestName = "UVIslandSelector")]
		[TestCase(NdmfDeformUI.BlendShapeOverrideRowGuid, TestName = "BlendShapeOverrideRow")]
		[TestCase(NdmfDeformUI.PointGridOverlayGuid, TestName = "PointGridOverlay")]
		[TestCase(NdmfDeformUI.BodyFitInspectorGuid, TestName = "BodyFitInspector")]
		public void VisualTreeResolves(string guid)
		{
			var tree = NdmfDeformUI.Load<VisualTreeAsset>(guid);
			Assert.That(tree, Is.Not.Null,
				$"UXML が GUID {guid} から解決できません(.meta と NdmfDeformUI の GUID を一致させてください)");

			// 展開できること(XML 構文・要素名の破損検出)
			var root = new VisualElement();
			tree.CloneTree(root);
			Assert.That(root.childCount, Is.GreaterThan(0), "UXML が空です");
		}
	}
}
