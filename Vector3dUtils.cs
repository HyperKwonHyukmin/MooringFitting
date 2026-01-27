using System;
using MooringFitting2026.Model.Geometry; // Point3D, Nodes, Vector3D가 위치한 네임스페이스
using MooringFitting2026.Model.Entities;

namespace MooringFitting2026.Utils
{
  public static class Vector3dUtils
  {
    private const double EPSILON = 1e-9;

    // =================================================================
    // 1. 방향 벡터 (Direction)
    // =================================================================

    /// <summary>
    /// 두 점(Point3D) 사이의 단위 방향 벡터를 반환합니다.
    /// </summary>
    public static Vector3D Direction(Point3D from, Point3D to)
    {
      // [수정] 입력 타입을 Nodes가 아니라 Point3D로 받아야 .X .Y 접근 가능
      double dx = to.X - from.X;
      double dy = to.Y - from.Y;
      double dz = to.Z - from.Z;

      double len = Math.Sqrt(dx * dx + dy * dy + dz * dz);

      if (len < EPSILON) return new Vector3D(0, 0, 0);

      return new Vector3D(dx / len, dy / len, dz / len);
    }

    /// <summary>
    /// [편의기능] Node ID와 Nodes 컨텍스트를 받아 방향 벡터를 계산합니다.
    /// </summary>
    public static Vector3D Direction(int fromID, int toID, Nodes nodes)
    {
      // Nodes 클래스에서 Point3D를 꺼내서 계산
      Point3D from = nodes.GetNodeCoordinates(fromID);
      Point3D to = nodes.GetNodeCoordinates(toID);

      return Direction(from, to);
    }

    // =================================================================
    // 2. 벡터 연산 (Operations)
    // =================================================================

    /// <summary>
    /// 두 벡터의 내적 (Dot Product)
    /// </summary>
    public static double Dot(Vector3D u, Vector3D v)
    {
      return u.X * v.X + u.Y * v.Y + u.Z * v.Z;
    }

    /// <summary>
    /// 두 벡터의 외적 (Cross Product)
    /// </summary>
    public static Vector3D Cross(Vector3D u, Vector3D v)
    {
      return new Vector3D(
          u.Y * v.Z - u.Z * v.Y,
          u.Z * v.X - u.X * v.Z,
          u.X * v.Y - u.Y * v.X
      );
    }

    /// <summary>
    /// 벡터의 크기(Magnitude)
    /// </summary>
    public static double Magnitude(Vector3D v)
    {
      return Math.Sqrt(v.X * v.X + v.Y * v.Y + v.Z * v.Z);
    }

    /// <summary>
    /// 벡터 정규화 (Normalize)
    /// </summary>
    public static Vector3D Normalize(Vector3D v)
    {
      double len = Magnitude(v);
      if (len < EPSILON) return new Vector3D(0, 0, 0);
      return new Vector3D(v.X / len, v.Y / len, v.Z / len);
    }
  }
}
