using MooringFitting2026.Model; // Point3D, Vector3D 위치
using MooringFitting2026.Model.Entities;
using MooringFitting2026.Model.Geometry; 
using System;

namespace MooringFitting2026.Utils.Geometry
{
  public static class DistanceUtils
  {
    // =================================================================
    // 1. 기본 거리 계산 (Critical Fix: Round 제거)
    // =================================================================

    public static double GetDistanceBetweenPoints(double x1, double y1, double z1, double x2, double y2, double z2)
    {
      // [수정] Math.Round 제거! 소수점 정밀도를 그대로 반환해야 함.
      return Math.Sqrt(Math.Pow(x1 - x2, 2) + Math.Pow(y1 - y2, 2) + Math.Pow(z1 - z2, 2));
    }

    // =================================================================
    // 2. Node/Point 간 거리 (재사용)
    // =================================================================

    public static double GetDistanceBetweenNodes(Point3D firstNode, Point3D secondNode)
    {
      // 위에서 만든 기본 메서드를 재사용하여 로직 중복 제거
      return GetDistanceBetweenPoints(
          firstNode.X, firstNode.Y, firstNode.Z,
          secondNode.X, secondNode.Y, secondNode.Z
      );
    }

    public static double GetDistanceBetweenNodes(int firstNodeID, int secondNodeID, Nodes nodes)
    {
      if (!nodes.Contains(firstNodeID) || !nodes.Contains(secondNodeID))
        throw new KeyNotFoundException("One or both Node IDs do not exist.");

      Point3D a = nodes[firstNodeID];
      Point3D b = nodes[secondNodeID];

      double dx = b.X - a.X;
      double dy = b.Y - a.Y;
      double dz = b.Z - a.Z;

      return Math.Sqrt(dx * dx + dy * dy + dz * dz);
    }

    // (선택사항) 거리 제곱만 필요한 경우 (성능 최적화용)
    // Sqrt 연산이 무겁기 때문에, 단순히 거리 비교만 할 때는 이걸 쓰는 게 좋음
    public static double GetDistanceSq(Point3D p1, Point3D p2)
    {
      return Math.Pow(p1.X - p2.X, 2) + Math.Pow(p1.Y - p2.Y, 2) + Math.Pow(p1.Z - p2.Z, 2);
    }

    // =================================================================
    // 3. 평행 여부 확인 (IsParallel) - 안전성 강화
    // =================================================================

    /// <summary>
    /// 두 벡터가 평행한지 확인합니다. (방향이 같거나 정반대인 경우 모두 true)
    /// </summary>
    /// <param name="angleTol">허용 오차 각도 (Radian 단위)</param>
    public static bool IsParallel(Vector3D v1, Vector3D v2, double angleTol)
    {
      // 1. 벡터의 크기(길이) 계산
      double len1 = Math.Sqrt(v1.X * v1.X + v1.Y * v1.Y + v1.Z * v1.Z);
      double len2 = Math.Sqrt(v2.X * v2.X + v2.Y * v2.Y + v2.Z * v2.Z);

      // 길이가 0인 벡터가 있으면 평행 판단 불가 (혹은 false)
      if (len1 < 1e-9 || len2 < 1e-9) return false;

      // 2. 내적 계산 (Dot Product)
      // v1 . v2 = |v1| |v2| cos(theta)
      double dot = v1.X * v2.X + v1.Y * v2.Y + v1.Z * v2.Z; // 혹은 v1.Dot(v2)

      // 3. 코사인 값 추출 (크기로 나누어 정규화 효과)
      double cosTheta = Math.Abs(dot) / (len1 * len2);

      // 부동소수점 오차로 1.000000002가 나올 수 있으므로 clamp
      if (cosTheta > 1.0) cosTheta = 1.0;

      // 4. 각도 비교
      // cos(0) = 1 이므로, cosTheta가 허용치보다 크면 평행에 가까움
      return cosTheta >= Math.Cos(angleTol);
    }

    public static double DistancePointToLine(Point3D x, Point3D P0, Point3D vUnit)
    {
      var proj = ProjectionUtils.ProjectPointToLine(x, P0, vUnit);
      return Point3dUtils.Norm(Point3dUtils.Sub(x, proj));
    }
  }
}
