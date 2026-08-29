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
			root.Add(new PropertyField(serializedObject.FindProperty("deformers")));
			return root;
		}
	}
}
