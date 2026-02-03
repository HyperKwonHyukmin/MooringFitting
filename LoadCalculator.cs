using System;
using System.Collections.Generic;
using System.Linq;
using MooringFitting2026.Model.Geometry;
using MooringFitting2026.Utils; // Point3dUtils, Vector3dUtils 사용

namespace MooringFitting2026.Utils.Geometry
{
  public static class LoadCalculator
  {
    /// <summary>
    /// 경사면에 설치된 MF의 Dependent Node 4개를 이용해 설치 각도를 자동 추정하고,
    /// 로프 각도(a,b,c)를 고려한 Global 하중 벡터를 반환합니다.
    /// </summary>
    public static Vector3D CalculateGlobalForceOnSlantedDeck(
        List<Point3D> depNodes,
        double tensionP,
        double horizAngleDeg,
        double vertAngleDeg)
    {
      if (depNodes == null || depNodes.Count < 3)
        throw new ArgumentException("적어도 3개 이상의 노드가 필요합니다.");

      // 1. [Local Z축] 경사면의 법선 벡터(Normal) 구하기
      // 대각선 벡터 2개를 외적(Cross Product)하여 구함 (평면의 수직 방향)
      // [수정] Point3dUtils.SubToVector 사용 (점-점=벡터)
      Vector3D v1 = Point3dUtils.SubToVector(depNodes[2], depNodes[0]);
      Vector3D v2 = Point3dUtils.SubToVector(depNodes[3], depNodes[1]);

      // 외적: v1 x v2 = 법선 벡터
      Vector3D normal = Vector3dUtils.Normalize(Vector3dUtils.Cross(v1, v2));

      // 법선 벡터가 아래(-Z)를 향하면 위(+Z)로 뒤집어줌 (데크 윗면 기준)
      // [수정] Vector3dUtils.Mul이 없으므로 직접 연산
      if (normal.Z < 0)
      {
        normal = new Vector3D(-normal.X, -normal.Y, -normal.Z);
      }
      Vector3D localZ = normal; // w

      // 2. [Local X축] 선수미 방향(Global X)을 경사면에 투영
      Vector3D globalX = new Vector3D(1, 0, 0);

      // 투영 공식: Proj = V - (V . n) * n
      double dot = Vector3dUtils.Dot(globalX, localZ);

      // 벡터 스칼라 곱 직접 연산 (n * dot)
      double nX = localZ.X * dot;
      double nY = localZ.Y * dot;
      double nZ = localZ.Z * dot;

      Vector3D projX = new Vector3D(
          globalX.X - nX,
          globalX.Y - nY,
          globalX.Z - nZ
      );
      Vector3D localX = Vector3dUtils.Normalize(projX); // u

      // 3. [Local Y축] 오른손 법칙으로 Y축 결정 (Z x X)
      Vector3D localY = Vector3dUtils.Normalize(Vector3dUtils.Cross(localZ, localX)); // v

      // ---------------------------------------------------------
      // 4. 로컬 하중 벡터 생성 (각도 a,b,c 적용)
      // ---------------------------------------------------------
      double hRad = horizAngleDeg * Math.PI / 180.0; // 수평각 (a or b)
      double vRad = vertAngleDeg * Math.PI / 180.0;  // 수직각 (c)

      // 로컬 좌표계에서의 힘 분해
      double f_local_z = tensionP * Math.Sin(vRad);
      double f_plane = tensionP * Math.Cos(vRad);

      double f_local_x = f_plane * Math.Cos(hRad);
      double f_local_y = f_plane * Math.Sin(hRad);

      // ---------------------------------------------------------
      // 5. 로컬 -> Global 변환 (Basis Vector 합산)
      // Global F = (Fx_local * u) + (Fy_local * v) + (Fz_local * w)
      // ---------------------------------------------------------

      double Gx = f_local_x * localX.X + f_local_y * localY.X + f_local_z * localZ.X;
      double Gy = f_local_x * localX.Y + f_local_y * localY.Y + f_local_z * localZ.Y;
      double Gz = f_local_x * localX.Z + f_local_y * localY.Z + f_local_z * localZ.Z;

      return new Vector3D(Gx, Gy, Gz);
    }
  }
}
