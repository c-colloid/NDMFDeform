using System.Collections.Generic;
using System.Linq;
using MeshModifier.NDMFDeform.Core;
using NUnit.Framework;
using UnityEngine;

namespace MeshModifier.NDMFDeform.Tests
{
	/// <summary>
	/// MeshAdjacency(位置溶接した CSR 隣接)の検証。
	/// シームで分割された頂点が同じ隣接集合を共有すること、
	/// 同一位置の頂点同士が隣接に含まれないことを確認する。
	/// </summary>
	public class MeshAdjacencyTests
	{
		private static HashSet<int> NeighborGroups(MeshAdjacency adjacency, int vertex)
		{
			var set = new HashSet<int>();
			for (var i = adjacency.Start[vertex]; i < adjacency.Start[vertex + 1]; i++)
				set.Add(adjacency.GroupOf[adjacency.Neighbors[i]]);
			return set;
		}

		[Test]
		public void Build_SeamVerticesShareNeighbors()
		{
			// 2 枚の四角形が辺 (1,0,0)-(1,1,0) で接する。接する辺の頂点は各四角形で別番号(シーム)
			var vertices = new[]
			{
				new Vector3(0f, 0f, 0f), new Vector3(1f, 0f, 0f), new Vector3(1f, 1f, 0f), new Vector3(0f, 1f, 0f),
				new Vector3(1f, 0f, 0f), new Vector3(1f, 1f, 0f), new Vector3(2f, 0f, 0f), new Vector3(2f, 1f, 0f),
			};
			var triangles = new[]
			{
				0, 2, 1, 0, 3, 2,
				4, 5, 6, 5, 7, 6,
			};
			var adjacency = MeshAdjacency.Build(vertices, triangles);

			Assert.That(adjacency.VertexCount, Is.EqualTo(8));
			Assert.That(adjacency.HasEdges, Is.True);
			Assert.That(adjacency.GroupOf[1], Is.EqualTo(adjacency.GroupOf[4]), "同一位置は同じグループ");
			Assert.That(adjacency.GroupOf[2], Is.EqualTo(adjacency.GroupOf[5]));
			Assert.That(adjacency.GroupOf[0], Is.Not.EqualTo(adjacency.GroupOf[1]));

			// 頂点 1 と 4 は同じ位置なので隣接集合が一致し、両方の四角形の隣接を含む
			var n1 = NeighborGroups(adjacency, 1);
			var n4 = NeighborGroups(adjacency, 4);
			Assert.That(n1.SetEquals(n4), Is.True, "シームの両側で隣接が共有される");
			Assert.That(n1, Has.Member(adjacency.GroupOf[0]));
			Assert.That(n1, Has.Member(adjacency.GroupOf[2]));
			Assert.That(n1, Has.Member(adjacency.GroupOf[6]));
			Assert.That(n1, Has.No.Member(adjacency.GroupOf[1]), "同一位置の頂点は隣接に含めない");

			// 隣接リストは代表頂点を指す
			var representatives = new HashSet<int>(adjacency.Representative);
			foreach (var neighbor in adjacency.Neighbors)
				Assert.That(representatives, Has.Member(neighbor));

			// 端の頂点 0 の隣接は 1(=4) と 2(=5) と 3
			var n0 = NeighborGroups(adjacency, 0);
			Assert.That(n0.Count, Is.EqualTo(3));
		}

		[Test]
		public void Build_WithoutTrianglesHasNoEdges()
		{
			var vertices = new[] { new Vector3(0f, 0f, 0f), new Vector3(1f, 0f, 0f) };
			var adjacency = MeshAdjacency.Build(vertices, null);
			Assert.That(adjacency.VertexCount, Is.EqualTo(2));
			Assert.That(adjacency.HasEdges, Is.False);
			Assert.That(adjacency.Start.All(s => s == 0), Is.True);
		}

		[Test]
		public void Build_EmptyMesh()
		{
			var adjacency = MeshAdjacency.Build(new Vector3[0], new int[0]);
			Assert.That(adjacency.VertexCount, Is.EqualTo(0));
			Assert.That(adjacency.HasEdges, Is.False);
		}
	}
}
