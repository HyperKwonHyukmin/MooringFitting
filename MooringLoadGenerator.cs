using MooringFitting2026.Model.Entities;
using MooringFitting2026.Model.Geometry;
using MooringFitting2026.Modifier.ElementModifier;
using MooringFitting2026.RawData;
using MooringFitting2026.Services.Reporting; // [추가] Namespace
using MooringFitting2026.Utils.Geometry;
using System;
using System.Collections.Generic;
using System.Linq;

namespace MooringFitting2026.Services.Load
{
  public static class MooringLoadGenerator
  {
    // [수정] 메서드 시그니처에 reporter 추가 (null 허용)
    public static List<ForceLoad> Generate(
        FeModelContext context,
        List<MFData> mfList,
        Dictionary<int, MooringFittingConnectionModifier.RigidInfo> rigidMap,
        Action<string> log,
        int startId,
        LoadCalculationReporter reporter = null) // [추가]
    {
      var loads = new List<ForceLoad>();
      var nodes = context.Nodes;
      int currentLoadId = startId;

      log($"[Load Gen] Starting Force Generation for MF (Start ID: {startId})...");

      var mfToRigidMap = rigidMap.Values.ToDictionary(r => r.RefID, r => r);

      foreach (var mf in mfList)
      {
        if (!mfToRigidMap.TryGetValue(mf.ID, out var rigidInfo))
        {
          // [추가] 실패 로그 기록 (선택 사항)
          reporter?.AddMfEntry("MF", mf.ID, -1, -1, mf.SWL, mf.a, mf.c, new Vector3D(0, 0, 0), "Skipped (No Rigid Info)");
          continue;
        }

        var depPoints = new List<Point3D>();
        foreach (var nid in rigidInfo.DependentNodeIDs)
        {
          if (nodes.Contains(nid)) depPoints.Add(nodes[nid]);
        }

        if (depPoints.Count < 3)
        {
          reporter?.AddMfEntry("MF", mf.ID, -1, rigidInfo.IndependentNodeID, mf.SWL, mf.a, mf.c, new Vector3D(0, 0, 0), "Skipped (Not enough nodes)");
          continue;
        }

        try
        {
          double loadVal = mf.SWL * 10000.0; // Ton -> kgf

          // [기존 로직] 하중 벡터 계산
          Vector3D forceVec = LoadCalculator.CalculateGlobalForceOnSlantedDeck(
              depPoints,
              loadVal,
              mf.a, // Horizontal Angle
              mf.c  // Vertical Angle
          );

          loads.Add(new ForceLoad(rigidInfo.IndependentNodeID, currentLoadId, forceVec));

          // [추가] 계산 성공 시 리포트에 상세 기록
          reporter?.AddMfEntry(
              "MF",
              mf.ID,
              currentLoadId,
              rigidInfo.IndependentNodeID,
              mf.SWL,   // Input Ton
              mf.a,     // Input Horiz Angle
              mf.c,     // Input Vert Angle
              forceVec, // Calculated Vector
              "Success"
          );

          currentLoadId++;
        }
        catch (Exception ex)
        {
          log($"  [Error] MF '{mf.ID}': {ex.Message}");
          // [추가] 에러 기록
          reporter?.AddMfEntry("MF", mf.ID, -1, rigidInfo.IndependentNodeID, mf.SWL, mf.a, mf.c, new Vector3D(0, 0, 0), $"Error: {ex.Message}");
        }
      }

      return loads;
    }
  }
}
