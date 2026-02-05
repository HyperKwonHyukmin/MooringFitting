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
    public record RigidInfo(int IndependentNodeID, List<int> DependentNodeIDs, string RefID);

    public static Dictionary<int, RigidInfo> Run(
        FeModelContext context,
        List<MFData> mfList,
        List<int> spcNodeIDs,
        Action<string> log)
    {
      var rigidMap = new Dictionary<int, RigidInfo>();

      if (mfList == null || mfList.Count == 0) return rigidMap;

      log($"[Stage 06] Generating Mooring Fitting Connections (Expanded Bounding Box Mode)...");

      var nodes = context.Nodes;
      var spcSet = (spcNodeIDs != null) ? new HashSet<int>(spcNodeIDs) : new HashSet<int>();

      int nextRigidID = 900001;
      if (context.Elements.Count > 0)
      {
        int maxElemID = context.Elements.Keys.Max();
        if (maxElemID >= nextRigidID) nextRigidID = maxElemID + 1;
      }

      double selectionMargin = 50.0;

      foreach (var mf in mfList)
      {
        // 1. Rigid Range (4개 지점) 가져오기
        if (mf.RigidRange == null || mf.RigidRange.Count < 3) continue;
        var polygon = mf.RigidRange.Select(pt => new Point3D(pt.Item1, pt.Item2, pt.Item3)).ToList();

        // 2. 영역 내 Dependent Nodes 검색 (Z 무시 2D 검색)
        List<int> depNodeIDs = FindNodesInExpandedBox(nodes, polygon, selectionMargin, toleranceZ: double.MaxValue);

        // SPC 노드 제외
        if (spcSet.Count > 0)
        {
          depNodeIDs.RemoveAll(id => spcSet.Contains(id));
        }

        if (depNodeIDs.Count == 0)
        {
          log($"  [Warn] MF '{mf.ID}': No valid nodes found. Skipping.");
          continue;
        }

        // 3. Independent Node (중심점) 생성
        // [수정] Z값을 평균이 아닌 '장비의 원본 높이'로 설정
        double massX = mf.Location[0];
        double massY = mf.Location[1];
        double massZ = mf.Location[2]; // 원본 높이 유지

        int indNodeID = nodes.AddOrGet(massX, massY, massZ);

        // 자기 자신 제외
        if (depNodeIDs.Contains(indNodeID)) depNodeIDs.Remove(indNodeID);

        // 4. 결과 저장
        rigidMap.Add(nextRigidID, new RigidInfo(indNodeID, depNodeIDs, mf.ID));
        log($"  -> [Registered] MF '{mf.ID}' (Ind: {indNodeID} @ Z={massZ}, Deps: {depNodeIDs.Count})");

        nextRigidID++;
      }

      ValidateRigidDependencies(rigidMap, log);

      log($"[Stage 06] Generated {rigidMap.Count} Rigid connection definitions.");
      return rigidMap;
    }

    private static void ValidateRigidDependencies(Dictionary<int, RigidInfo> rigidMap, Action<string> log)
    {
      var dependencyMap = new Dictionary<int, int>();
      int errorCount = 0;

      foreach (var kv in rigidMap)
      {
        int rigidID = kv.Key;
        var info = kv.Value;

        foreach (int depNode in info.DependentNodeIDs)
        {
          if (dependencyMap.TryGetValue(depNode, out int existingRigidID))
          {
            log($"  [CRITICAL ERROR] Double Dependency on Node {depNode} (Rigid E{existingRigidID} & E{rigidID})");
            errorCount++;
          }
          else
          {
            dependencyMap[depNode] = rigidID;
          }
        }
      }
      if (errorCount > 0) log($"  [Validation Failed] Found {errorCount} double dependency errors.");
    }

    private static List<int> FindNodesInExpandedBox(Nodes nodes, List<Point3D> polygon, double margin, double toleranceZ)
    {
      var foundIDs = new List<int>();

      double minX = polygon.Min(p => p.X); double maxX = polygon.Max(p => p.X);
      double minY = polygon.Min(p => p.Y); double maxY = polygon.Max(p => p.Y);

      double searchMinX = minX - margin; double searchMaxX = maxX + margin;
      double searchMinY = minY - margin; double searchMaxY = maxY + margin;

      double avgZ = polygon.Average(p => p.Z);
      double minZ = avgZ - toleranceZ; double maxZ = avgZ + toleranceZ;

      foreach (var kv in nodes)
      {
        var p = kv.Value;
        bool insideX = (p.X >= searchMinX) && (p.X <= searchMaxX);
        bool insideY = (p.Y >= searchMinY) && (p.Y <= searchMaxY);
        bool insideZ = (toleranceZ == double.MaxValue) || ((p.Z >= minZ) && (p.Z <= maxZ));

        if (insideX && insideY && insideZ)
        {
          foundIDs.Add(kv.Key);
        }
      }
      return foundIDs;
    }
  }
}
