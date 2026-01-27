using System;
using System.Collections.Generic;
using MooringFitting2026.Model.Entities; // Nodes, Point3D 위치

namespace MooringFitting2026.Utils.Geometry
{
  public static class SizeUtils
  {
    /// <summary>
    /// 모델 전체 노드의 Bounding Box 대각선 길이(Model Size)를 계산합니다.
    /// (Tolerance 결정 등의 기준 척도로 사용됨)
    /// </summary>
    public static double GetModelSize(Nodes nodes)
    {
      // 1. 방어 코드: 노드가 없거나 null이면 크기는 0
      // (Nodes 클래스가 ICollection을 구현했다면 .Count, 아니면 LINQ .Any() 사용)
      if (nodes == null) return 0.0;

      // Nodes가 커스텀 컬렉션이라 Count 프로퍼티가 확실치 않다면,
      // 아래 루프가 한 번도 안 돌았을 때를 대비한 플래그가 필요함.
      bool isEmpty = true;

      double minX = double.MaxValue, minY = double.MaxValue, minZ = double.MaxValue;
      double maxX = double.MinValue, maxY = double.MinValue, maxZ = double.MinValue;

      // 2. Min/Max 탐색
      foreach (var kv in nodes) // kv: KeyValuePair<int, Point3D>
      {
        isEmpty = false;
        var p = kv.Value;

        if (p.X < minX) minX = p.X;
        if (p.Y < minY) minY = p.Y;
        if (p.Z < minZ) minZ = p.Z;

        if (p.X > maxX) maxX = p.X;
        if (p.Y > maxY) maxY = p.Y;
        if (p.Z > maxZ) maxZ = p.Z;
      }

      // 3. 노드가 하나도 없었다면 0 반환
      if (isEmpty) return 0.0;

      // 4. 대각선 길이 계산
      double dx = maxX - minX;
      double dy = maxY - minY;
      double dz = maxZ - minZ;

      return Math.Sqrt(dx * dx + dy * dy + dz * dz);
    }
  }
}
