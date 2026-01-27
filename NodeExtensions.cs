using MooringFitting2026.Model.Entities;
using MooringFitting2026.Model.Geometry; // Point3D

namespace MooringFitting2026.Extensions
{
  public static class NodeExtensions
  {
    /// <summary>
    /// 기준점(P0)과 방향벡터(vRef), 매개변수(t)를 이용하여 좌표를 계산하고,
    /// 해당 위치에 노드를 생성하거나 기존 노드를 반환합니다.
    /// </summary>
    public static int GetOrCreateNodeAtT(this Nodes nodes, Point3D P0, Point3D vRef, double t)
    {
      // [개선] Point3dUtils 대신 연산자(+)와 (*) 사용 -> 훨씬 직관적!
      // 수식: P = P0 + (v * t)
      Point3D p = P0 + (vRef * t);

      // Nodes 클래스에 AddOrGet 메서드가 있다고 가정
      return nodes.AddOrGet(p.X, p.Y, p.Z);
    }
  }
}
