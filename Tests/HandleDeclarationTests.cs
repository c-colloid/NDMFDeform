using System.Linq;
using MeshModifier.NDMFDeform.Core;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace MeshModifier.NDMFDeform.Tests
{
	/// <summary>
	/// 全デフォーマの DescribeHandles 宣言が、実在するシリアライズプロパティに
	/// バインドされていることを検証する(nameof のタイポ・リネーム漏れの検出)。
	/// </summary>
	public class HandleDeclarationTests
	{
		private class ValidatingBuilder : IHandleBuilder
		{
			private readonly SerializedObject _serializedObject;
			private readonly string _typeName;

			public int Declarations;

			public ValidatingBuilder(SerializedObject serializedObject, string typeName)
			{
				_serializedObject = serializedObject;
				_typeName = typeName;
			}

			private void Check(string property)
			{
				Declarations++;
				Assert.That(_serializedObject.FindProperty(property), Is.Not.Null,
					$"{_typeName}.DescribeHandles が存在しないプロパティ '{property}' を宣言しています");
			}

			public void AxisSlider(string property, HandleAxis along, HandleLineStyle style) => Check(property);

			public void RadiusSlider(string property, HandleAxis along, HandleLineStyle style, float scale,
				string offsetProperty, HandleAxis offsetAxis, string pairProperty)
			{
				Check(property);
				if (offsetProperty != null)
					Check(offsetProperty);
				if (pairProperty != null)
					Check(pairProperty);
			}

			public void Circle(HandleAxis normal, string offsetProperty, string radiusProperty, HandleLineStyle style)
			{
				Check(offsetProperty);
				Check(radiusProperty);
			}

			public void Circle(HandleAxis normal, float offset, string radiusProperty, HandleLineStyle style,
				float scale) => Check(radiusProperty);

			public void Circle(HandleAxis normal, float offset, float radius, HandleLineStyle style) { }

			public void Box(string boundsProperty, HandleLineStyle style) => Check(boundsProperty);

			public void Position(string property) => Check(property);

			public void Line(Vector3 from, Vector3 to, HandleLineStyle style) { }

			public void Arrow(Vector3 from, Vector3 to, HandleLineStyle style) { }

			public void DecaySlider(string property, HandleAxis along, float k, float ringRadius,
				HandleLineStyle style) => Check(property);

			public void PointGrid(string pointsProperty, Vector3Int resolution, string mirrorAxisProperty)
			{
				Check(pointsProperty);
				if (mirrorAxisProperty != null)
					Check(mirrorAxisProperty);
			}
		}

		[Test]
		public void AllDeformerHandleDeclarationsBindToSerializedProperties()
		{
			var deformerTypes = typeof(DeformerBase).Assembly.GetTypes()
				.Where(t => typeof(DeformerBase).IsAssignableFrom(t) && !t.IsAbstract)
				.ToList();

			Assert.That(deformerTypes, Is.Not.Empty);

			foreach (var type in deformerTypes)
			{
				var go = new GameObject("HandleDeclTest_" + type.Name);
				try
				{
					var deformer = (DeformerBase)go.AddComponent(type);
					var serializedObject = new SerializedObject(deformer);
					var builder = new ValidatingBuilder(serializedObject, type.Name);
					deformer.DescribeHandles(builder);
				}
				finally
				{
					Object.DestroyImmediate(go);
				}
			}
		}
	}
}
