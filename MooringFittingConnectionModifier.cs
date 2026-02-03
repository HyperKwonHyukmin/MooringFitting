using MooringFitting2026.Model.Entities;
using MooringFitting2026.Model.Geometry;
using MooringFitting2026.RawData;
using MooringFitting2026.Utils.Geometry;
using System;
using System.Collections.Generic;
using System.Linq;

namespace MooringFitting2026.Modifier.ElementModifier
{
  public static class MooringFittingConnectionModifier
  {
    // 반환할 Rigid 정보 구조체
    public record RigidInfo(int IndependentNodeID, List<int> DependentNodeIDs, string RefID);

    public static Dictionary<int, RigidInfo> Run(FeModelContext context, List<MFData> mfList, Action<string> log)
    {
      var rigidMap = new Dictionary<int, RigidInfo>();

      if (mfList == null || mfList.Count == 0) return rigidMap;

      log($"[Stage 06] Generating Mooring Fitting Connections (Dictionary Mode)...");

      var nodes = context.Nodes;

      // Rigid ID 시작 번호 (기존 요소와 겹치지 않게 안전하게 900000번대 사용)
      int nextRigidID = 900001;
      if (context.Elements.Count > 0)
      {
        // 혹시라도 요소가 90만개를 넘거나 90만번대를 쓰고 있다면 그 다음 번호부터
        int maxElemID = context.Elements.Keys.Max();
        if (maxElemID >= nextRigidID) nextRigidID = maxElemID + 1;
      }

      foreach (var mf in mfList)
      {
        // 1. Independent Node (Point Mass 위치) 생성
        double massX = mf.Location[0];
        double massY = mf.Location[1];
        double massZ = mf.Location[2];

        int indNodeID = nodes.AddOrGet(massX, massY, massZ);

        // 2. Rigid Range Polygon 구성
        if (mf.RigidRange == null || mf.RigidRange.Count < 3) continue;

        var polygon = mf.RigidRange.Select(pt => new Point3D(pt.Item1, pt.Item2, pt.Item3)).ToList();

        // 3. 영역 내 Dependent Nodes 검색 (메싱된 노드 포함)
        List<int> depNodeIDs = FindNodesInPolygon(nodes, polygon, toleranceZ: 50.0);

        // 자기 자신 제외
        if (depNodeIDs.Contains(indNodeID)) depNodeIDs.Remove(indNodeID);

        if (depNodeIDs.Count == 0)
        {
          log($"  [Warn] MF '{mf.ID}': No nodes found in range. Skipping.");
          continue;
        }

        // 4. Element에 추가하지 않고, 딕셔너리에 정보 저장
        rigidMap.Add(nextRigidID, new RigidInfo(indNodeID, depNodeIDs, mf.ID));

        log($"  -> [Registered] MF '{mf.ID}' as Rigid E{nextRigidID} (Ind: {indNodeID}, Deps: {depNodeIDs.Count} nodes)");

        nextRigidID++;
      }

      log($"[Stage 06] Generated {rigidMap.Count} Rigid connection definitions.");
      return rigidMap;
    }

    // --- Helper Methods ---

    private static List<int> FindNodesInPolygon(Nodes nodes, List<Point3D> polygon, double toleranceZ)
    {
      var foundIDs = new List<int>();

      double minX = polygon.Min(p => p.X); double maxX = polygon.Max(p => p.X);
      double minY = polygon.Min(p => p.Y); double maxY = polygon.Max(p => p.Y);
      double avgZ = polygon.Average(p => p.Z);
      double minZ = avgZ - toleranceZ; double maxZ = avgZ + toleranceZ;

      foreach (var kv in nodes)
      {
        var p = kv.Value;
        if (p.X < minX || p.X > maxX || p.Y < minY || p.Y > maxY || p.Z < minZ || p.Z > maxZ) continue;

        if (IsPointInPolygon2D(p, polygon))
        {
          foundIDs.Add(kv.Key);
        }
      }
      return foundIDs;
    }

    private static bool IsPointInPolygon2D(Point3D p, List<Point3D> polygon)
    {
      bool inside = false;
      int j = polygon.Count - 1;
      for (int i = 0; i < polygon.Count; i++)
      {
        if ((polygon[i].Y > p.Y) != (polygon[j].Y > p.Y) &&
            (p.X < (polygon[j].X - polygon[i].X) * (p.Y - polygon[i].Y) / (polygon[j].Y - polygon[i].Y) + polygon[i].X))
        {
          inside = !inside;
        }
        j = i;
      }
      return inside;
    }
  }
}
