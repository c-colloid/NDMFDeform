using MeshModifier.NDMFDeform.Core;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace MeshModifier.NDMFDeform.Editor
{
	/// <summary>
	/// DeformStack の UITK インスペクタ。
	/// M0 時点は自動生成のみ。M2 以降でフォーク実装済みの
	/// ListView + ドラッグ&ドロップ UI を移植して置き換える。
	/// </summary>
	[CustomEditor(typeof(DeformStack))]
	public class DeformStackEditor : UnityEditor.Editor
	{
		public override VisualElement CreateInspectorGUI()
		{
			var root = new VisualElement();
			NdmfDeformFonts.ApplyEditorUiFont(root);
			root.Add(new PropertyField(serializedObject.FindProperty("deformers")));
			root.Add(new PropertyField(serializedObject.FindProperty("normalsMode"), "法線"));
			root.Add(new PropertyField(serializedObject.FindProperty("nonlinearShapeCorrection"), "シェイプ非線形補正"));
			root.Add(new PropertyField(serializedObject.FindProperty("blendShapeOverrides"), "シェイプ個別設定"));
			return root;
		}
	}
}
