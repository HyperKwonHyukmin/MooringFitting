using MooringFitting2026.Model.Entities;
using MooringFitting2026.RawData;
using MooringFitting2026.Model.Geometry;
using MooringFitting2026.Services.Reporting; // [추가] Namespace
using System;
using System.Collections.Generic;
using System.Linq;

namespace MooringFitting2026.Services.Load
{
  public static class WinchLoadGenerator
  {
    // [수정] reporter 인자 추가
    public static List<ForceLoad> Generate(
        FeModelContext context,
        WinchData winchData,
        Action<string> log,
        int startId,
        LoadCalculationReporter reporter = null) // [추가]
    {
      var loads = new List<ForceLoad>();
      int currentLoadId = startId;

      log($"[Winch Load Gen] Starting Multi-Case Grouping (Start ID: {startId})...");

      var loadCases = new List<(string Name, Dictionary<string, List<string>> Data)>
            {
                ("Forward LC", winchData.Forward_LC),
                ("Backward LC", winchData.Backward_LC),
                ("GreenSea Front Left LC", winchData.GreenSea_FrongLeft_LC),
                ("GreenSea Front Right LC", winchData.Forward_GreenSea_FrongRight_LC),
                ("Test LC", winchData.Test_LC)
            };

      foreach (var (caseName, lcData) in loadCases)
      {
        if (lcData == null || lcData.Count == 0) continue;

        int countInCase = 0;

        foreach (var kvp in lcData)
        {
          string winchID = kvp.Key;
          List<string> values = kvp.Value;

          // 데이터 부족 시 스킵 기록
          if (values == null || values.Count < 3)
          {
            reporter?.AddWinchEntry(caseName, winchID, currentLoadId, -1, 0, 0, 0, new Vector3D(0, 0, 0), "Insufficient Data");
            continue;
          }

          try
          {
            if (!winchData.WinchLocation.TryGetValue(winchID, out var locStrs) || locStrs.Count < 3)
            {
              reporter?.AddWinchEntry(caseName, winchID, currentLoadId, -1, 0, 0, 0, new Vector3D(0, 0, 0), "Location Missing");
              continue;
            }

            double x = ParseDouble(locStrs[0]);
            double y = ParseDouble(locStrs[1]);
            double z = ParseDouble(locStrs[2]);

            int targetNodeID = context.Nodes.FindClosestNodeID(x, y, z);

            // 노드를 못 찾았을 경우
            if (targetNodeID == -1)
            {
              reporter?.AddWinchEntry(caseName, winchID, currentLoadId, -1, 0, 0, 0, new Vector3D(0, 0, 0), $"Node Not Found at {x},{y},{z}");
              continue;
            }

            // [중요] 원본 입력값 (Ton) 파싱
            double fx_ton = ParseDouble(values[0]);
            double fy_ton = ParseDouble(values[1]);
            double fz_ton = ParseDouble(values[2]);

            // 모두 0이면 스킵 (기록은 남김)
            if (Math.Abs(fx_ton) < 1e-9 && Math.Abs(fy_ton) < 1e-9 && Math.Abs(fz_ton) < 1e-9)
            {
              reporter?.AddWinchEntry(caseName, winchID, currentLoadId, targetNodeID, fx_ton, fy_ton, fz_ton, new Vector3D(0, 0, 0), "Zero Load Skipped");
              continue;
            }

            // 벡터 생성 (Ton -> kgf/N * 1000)
            Vector3D forceVec = new Vector3D(
                fx_ton * 10000.0,
                fy_ton * 10000.0,
                fz_ton * 10000.0
            );

            loads.Add(new ForceLoad(targetNodeID, currentLoadId, forceVec));

            // [추가] 리포트 기록
            reporter?.AddWinchEntry(
                caseName,
                winchID,
                currentLoadId,
                targetNodeID,
                fx_ton, fy_ton, fz_ton, // 입력값
                forceVec,               // 변환값
                $"({x},{y},{z})"        // 위치
            );

            countInCase++;
          }
          catch (Exception ex)
          {
            log($"  [Error] {caseName} - {winchID}: {ex.Message}");
            reporter?.AddWinchEntry(caseName, winchID, currentLoadId, -1, 0, 0, 0, new Vector3D(0, 0, 0), $"Error: {ex.Message}");
          }
        }

        if (countInCase > 0) currentLoadId++;
      }

      return loads;
    }

    private static double ParseDouble(string val)
    {
      if (string.IsNullOrWhiteSpace(val) || val.Trim() == "_") return 0.0;
      if (double.TryParse(val, out double res)) return res;
      return 0.0;
    }
  }
}
