using MooringFitting2026.Model.Entities;
using MooringFitting2026.Model.Geometry;
using MooringFitting2026.Utils.Geometry;
using System;

namespace MooringFitting2026.Utils
{
  public static class ProjectionUtils
  {
    private const double EPSILON = 1e-9;

    // [수정] 메서드 이름을 ProjectToSegment -> ProjectPointToSegment 로 변경 (호환성 확보)
    public static (Point3D ProjectedPoint, double T, double Distance) ProjectPointToSegment(Point3D P, Point3D A, Point3D B)
    {
      // ... (내용은 이전과 동일, 선분 Clamp 로직) ...

      double apx = P.X - A.X;
      double apy = P.Y - A.Y;
      double apz = P.Z - A.Z;

      double abx = B.X - A.X;
      double aby = B.Y - A.Y;
      double abz = B.Z - A.Z;

      double abLenSq = abx * abx + aby * aby + abz * abz;

      if (abLenSq < EPSILON) return (A, 0.0, Point3dUtils.Dist(P, A));

      double t = (apx * abx + apy * aby + apz * abz) / abLenSq;

      if (t < 0.0)
      {
        return (A, 0.0, Point3dUtils.Dist(P, A));
      }
      else if (t > 1.0)
      {
        return (B, 1.0, Point3dUtils.Dist(P, B));
      }

      Point3D proj = new Point3D(A.X + abx * t, A.Y + aby * t, A.Z + abz * t);
      return (proj, t, Point3dUtils.Dist(P, proj));
    }

    // [추가] 직선 투영 (자네의 로직에 이게 더 필요해 보이네)
    public static (Point3D ProjectedPoint, double T, double Distance) ProjectPointToInfiniteLine(Point3D P, Point3D A, Point3D B)
    {
      double apx = P.X - A.X;
      double apy = P.Y - A.Y;
      double apz = P.Z - A.Z;

      double abx = B.X - A.X;
      double aby = B.Y - A.Y;
      double abz = B.Z - A.Z;

      double abLenSq = abx * abx + aby * aby + abz * abz;

      if (abLenSq < EPSILON) return (A, 0.0, Point3dUtils.Dist(P, A));

      // Clamp 없이 t 계산 그대로 사용 (무한 직선 투영)
      double t = (apx * abx + apy * aby + apz * abz) / abLenSq;

      Point3D proj = new Point3D(A.X + abx * t, A.Y + aby * t, A.Z + abz * t);
      return (proj, t, Point3dUtils.Dist(P, proj));
    }

    // [이전 요청사항] 스칼라 투영
    public static double ProjectPointToScalar(Point3D P, Point3D A, Point3D B)
    {
      double apx = P.X - A.X; double apy = P.Y - A.Y; double apz = P.Z - A.Z;
      double abx = B.X - A.X; double aby = B.Y - A.Y; double abz = B.Z - A.Z;
      double abLenSq = abx * abx + aby * aby + abz * abz;
      if (abLenSq < EPSILON) return 0.0;
      return (apx * abx + apy * aby + apz * abz) / abLenSq;
    }

    private static bool IsOnSameLine(Point3D a, Point3D b, Point3D p, FeModelContext context, double tol)
    {
      // [Architect's Fix]
      // "직선과의 거리만 판단"하려면 Segment(선분) 제한이 없는 투영을 해야 하네.
      // ProjectPointToSegment를 쓰면 선분 밖의 점은 끝점과의 거리가 반환되어
      // 직선 위에 있어도 False가 나올 수 있네.

      var proj = ProjectionUtils.ProjectPointToInfiniteLine(p, a, b);

      return proj.Distance < tol;
    }

    public static Point3D ProjectPointToLine(Point3D x, Point3D P0, Point3D vUnit)
    {
      // vUnit은 unit vector라고 가정
      var dx = Point3dUtils.Sub(x, P0);
      double t = Point3dUtils.Dot(dx, vUnit);
      return Point3dUtils.Add(P0, Point3dUtils.Mul(vUnit, t));
    }
  }
}
