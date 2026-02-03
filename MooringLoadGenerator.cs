using MooringFitting2026.Model.Entities;
using MooringFitting2026.Model.Geometry;
using MooringFitting2026.Modifier.ElementModifier; // RigidInfo
using MooringFitting2026.RawData;
using MooringFitting2026.Utils.Geometry;
using System;
using System.Collections.Generic;
using System.Linq;

namespace MooringFitting2026.Services.Load
{
  public static class MooringLoadGenerator
  {
    public static List<ForceLoad> Generate(
        FeModelContext context,
        List<MFData> mfList,
        Dictionary<int, MooringFittingConnectionModifier.RigidInfo> rigidMap,
        Action<string> log)
    {
      var loads = new List<ForceLoad>();
      var nodes = context.Nodes;

      log("[Load Gen] Starting Force Generation based on MF Geometry...");

      // RigidMap(ElementID -> Info)을 MF ID로 쉽게 찾기 위해 룩업 생성
      // RigidInfo.RefID가 MFData.ID와 매칭된다고 가정
      var mfToRigidMap = rigidMap.Values.ToDictionary(r => r.RefID, r => r);

      foreach (var mf in mfList)
      {
        if (!mfToRigidMap.TryGetValue(mf.ID, out var rigidInfo))
        {
          log($"  [Skip] MF '{mf.ID}': No associated Rigid Element found.");
          continue;
        }

        // 1. Dependent Nodes 좌표 수집 (설치 각도 추정용)
        var depPoints = new List<Point3D>();
        foreach (var nid in rigidInfo.DependentNodeIDs)
        {
          if (nodes.Contains(nid))
            depPoints.Add(nodes[nid]);
        }

        if (depPoints.Count < 3)
        {
          log($"  [Skip] MF '{mf.ID}': Not enough dependent nodes ({depPoints.Count}) to determine plane.");
          continue;
        }

        // 2. LoadCalculator를 이용해 Global Force Vector 계산
        try
        {
          // CSV에서 읽은 a, b, c, SWL 사용
          // 정책: a값을 수평각으로 우선 사용 (필요시 b값에 대한 로직 추가 가능)
          double targetHorizAngle = mf.a;
          double targetVertAngle = mf.c;
          double loadVal = mf.SWL; // 또는 tow 값

          Vector3D forceVec = LoadCalculator.CalculateGlobalForceOnSlantedDeck(
              depPoints,
              loadVal,
              targetHorizAngle,
              targetVertAngle
          );

          // 3. 하중 데이터 생성 (Load Case 2번으로 고정)
          loads.Add(new ForceLoad(rigidInfo.IndependentNodeID, 2, forceVec));

          // log($"  [Load] MF '{mf.ID}' -> Force: ({forceVec.X:F1}, {forceVec.Y:F1}, {forceVec.Z:F1}) on Node {rigidInfo.IndependentNodeID}");
        }
        catch (Exception ex)
        {
          log($"  [Error] MF '{mf.ID}': Calculation failed - {ex.Message}");
        }
      }

      log($"[Load Gen] Generated {loads.Count} force vectors.");
      return loads;
    }
  }
}
