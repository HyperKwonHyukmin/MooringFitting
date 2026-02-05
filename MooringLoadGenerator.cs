using MooringFitting2026.Model.Entities;
using MooringFitting2026.Model.Geometry;
using MooringFitting2026.Modifier.ElementModifier;
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
        Action<string> log,
        int startId)
    {
      var loads = new List<ForceLoad>();
      var nodes = context.Nodes;
      int currentLoadId = startId;

      log($"[Load Gen] Starting Force Generation for MF (Start ID: {startId})...");

      var mfToRigidMap = rigidMap.Values.ToDictionary(r => r.RefID, r => r);

      foreach (var mf in mfList)
      {
        if (!mfToRigidMap.TryGetValue(mf.ID, out var rigidInfo)) continue;

        var depPoints = new List<Point3D>();
        foreach (var nid in rigidInfo.DependentNodeIDs)
        {
          if (nodes.Contains(nid)) depPoints.Add(nodes[nid]);
        }

        if (depPoints.Count < 3) continue;

        try
        {
          double loadVal = mf.SWL * 1000.0; // Ton -> N/kgf

          Vector3D forceVec = LoadCalculator.CalculateGlobalForceOnSlantedDeck(
              depPoints,
              loadVal,
              mf.a,
              mf.c
          );

          // [핵심] 하중 추가 시 currentLoadId 사용 후 증가
          loads.Add(new ForceLoad(rigidInfo.IndependentNodeID, currentLoadId, forceVec));
          currentLoadId++;
        }
        catch (Exception ex)
        {
          log($"  [Error] MF '{mf.ID}': {ex.Message}");
        }
      }

      return loads;
    }
  }
}
