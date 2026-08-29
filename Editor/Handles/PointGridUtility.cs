using System.Collections.Generic;
using MeshModifier.NDMFDeform.Core;
using Unity.Mathematics;
using UnityEngine;

namespace MeshModifier.NDMFDeform.Editor
{
	/// <summary>
	/// 格子インデックス・選択集合・ミラー対応の純関数計算。
	/// レイアウトは LatticeDeformer と同じ index = x + y*resX + z*resX*resY。
	/// UI から分離してテスト可能にしている。
	/// </summary>
	public static class PointGridUtility
	{
		public static int GetIndex(Vector3Int res, int x, int y, int z)
		{
			return x + y * res.x + z * (res.x * res.y);
		}

		public static Vector3Int GetCoord(Vector3Int res, int index)
		{
			var x = index % res.x;
			var y = index / res.x % res.y;
			var z = index / (res.x * res.y);
			return new Vector3Int(x, y, z);
		}

		private static int AxisComponent(Vector3Int v, HandleAxis axis)
		{
			switch (axis)
			{
				case HandleAxis.X: return v.x;
				case HandleAxis.Y: return v.y;
				default: return v.z;
			}
		}

		private static int AxisResolution(Vector3Int res, HandleAxis axis) => AxisComponent(res, axis);

		/// <summary>through を通り、along 軸方向に並ぶ全制御点(ループ/行選択)</summary>
		public static List<int> LineIndices(Vector3Int res, Vector3Int through, HandleAxis along)
		{
			var result = new List<int>(AxisResolution(res, along));
			for (var i = 0; i < AxisResolution(res, along); i++)
			{
				var c = through;
				switch (along)
				{
					case HandleAxis.X: c.x = i; break;
					case HandleAxis.Y: c.y = i; break;
					default: c.z = i; break;
				}
				result.Add(GetIndex(res, c.x, c.y, c.z));
			}
			return result;
		}

		/// <summary>axis 軸の座標が fixedCoord の全制御点(シート/面選択)</summary>
		public static List<int> SheetIndices(Vector3Int res, HandleAxis axis, int fixedCoord)
		{
			var result = new List<int>();
			for (var z = 0; z < res.z; z++)
			for (var y = 0; y < res.y; y++)
			for (var x = 0; x < res.x; x++)
			{
				var c = new Vector3Int(x, y, z);
				if (AxisComponent(c, axis) == fixedCoord)
					result.Add(GetIndex(res, x, y, z));
			}
			return result;
		}

		/// <summary>normal 軸に垂直な2軸を返す</summary>
		public static (HandleAxis a, HandleAxis b) OtherAxes(HandleAxis normal)
		{
			switch (normal)
			{
				case HandleAxis.X: return (HandleAxis.Y, HandleAxis.Z);
				case HandleAxis.Y: return (HandleAxis.X, HandleAxis.Z);
				default: return (HandleAxis.X, HandleAxis.Y);
			}
		}

		/// <summary>
		/// normal 軸の座標を through に固定したシートの外周(リング/ループ)。
		/// 例: 縦軸に垂直なシートのリング = 腰回りの輪。
		/// </summary>
		public static List<int> RingIndices(Vector3Int res, HandleAxis normal, Vector3Int through)
		{
			var (a, b) = OtherAxes(normal);
			var fixedCoord = AxisComponent(through, normal);
			var result = new List<int>();
			for (var z = 0; z < res.z; z++)
			for (var y = 0; y < res.y; y++)
			for (var x = 0; x < res.x; x++)
			{
				var c = new Vector3Int(x, y, z);
				if (AxisComponent(c, normal) != fixedCoord)
					continue;
				var onBoundary =
					AxisComponent(c, a) == 0 || AxisComponent(c, a) == AxisComponent(res, a) - 1 ||
					AxisComponent(c, b) == 0 || AxisComponent(c, b) == AxisComponent(res, b) - 1;
				if (onBoundary)
					result.Add(GetIndex(res, x, y, z));
			}
			return result;
		}

		/// <summary>対称側の制御点インデックス(axis=None なら同じインデックス)</summary>
		public static int MirrorIndex(Vector3Int res, int index, MirrorAxis axis)
		{
			if (axis == MirrorAxis.None) return index;
			var c = GetCoord(res, index);
			switch (axis)
			{
				case MirrorAxis.X: c.x = res.x - 1 - c.x; break;
				case MirrorAxis.Y: c.y = res.y - 1 - c.y; break;
				case MirrorAxis.Z: c.z = res.z - 1 - c.z; break;
			}
			return GetIndex(res, c.x, c.y, c.z);
		}

		/// <summary>軸空間 [-0.5,0.5] における対称位置(対象成分の符号反転)</summary>
		public static float3 MirrorPosition(float3 position, MirrorAxis axis)
		{
			switch (axis)
			{
				case MirrorAxis.X: position.x = -position.x; break;
				case MirrorAxis.Y: position.y = -position.y; break;
				case MirrorAxis.Z: position.z = -position.z; break;
			}
			return position;
		}
	}
}
