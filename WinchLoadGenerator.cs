using MooringFitting2026.Model.Entities;
using MooringFitting2026.RawData;
using MooringFitting2026.Model.Geometry;
using System;
using System.Collections.Generic;
using System.Linq;

namespace MooringFitting2026.Services.Load
{
  public static class WinchLoadGenerator
  {
    /// <summary>
    /// WinchData의 각 딕셔너리(LC)를 하나의 Load Case로 그룹화하여 하중을 생성합니다.
    /// </summary>
    public static List<ForceLoad> Generate(
        FeModelContext context,
        WinchData winchData,
        Action<string> log,
        int startId)
    {
      var loads = new List<ForceLoad>();
      int currentLoadId = startId;

      log($"[Winch Load Gen] Starting Multi-Case Grouping (Start ID: {startId})...");

      // 처리할 하중 케이스 정의 (순서대로 ID 부여)
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
        // 데이터가 비어있으면 다음 케이스로 넘어감 (ID 증가 안 함? -> 사용자 의도에 따라 빈 케이스라도 ID를 확보하려면 로직 변경 필요. 여기선 데이터 없으면 스킵)
        if (lcData == null || lcData.Count == 0) continue;

        int countInCase = 0;

        // [핵심] 딕셔너리 내부의 모든 윈치 하중은 동일한 currentLoadId를 가짐
        foreach (var kvp in lcData)
        {
          string winchID = kvp.Key;
          List<string> values = kvp.Value; // [Fx, Fy, Fz]

          // 데이터 유효성 검사 (3성분 필수)
          if (values == null || values.Count < 3)
          {
            // Test LC 등 데이터가 부족한 경우 스킵 (로그 필요시 주석 해제)
            // log($"  [Skip] {caseName} - {winchID}: Insufficient data components ({values?.Count}).");
            continue;
          }

          try
          {
            // 1. 위치 정보 확인
            if (!winchData.WinchLocation.TryGetValue(winchID, out var locStrs) || locStrs.Count < 3)
            {
              log($"  [Skip] {caseName} - {winchID}: Location data missing.");
              continue;
            }

            // 2. 노드 찾기
            double x = ParseDouble(locStrs[0]);
            double y = ParseDouble(locStrs[1]);
            double z = ParseDouble(locStrs[2]);

            int targetNodeID = context.Nodes.FindClosestNodeID(x, y, z);
            if (targetNodeID == -1) continue;

            // 3. 하중 파싱 (X, Y, Z 성분)
            double fx_ton = ParseDouble(values[0]);
            double fy_ton = ParseDouble(values[1]);
            double fz_ton = ParseDouble(values[2]);

            // 모두 0이면 스킵
            if (Math.Abs(fx_ton) < 1e-9 && Math.Abs(fy_ton) < 1e-9 && Math.Abs(fz_ton) < 1e-9)
              continue;

            // 4. 벡터 생성 (Ton -> kgf/N * 1000)
            Vector3D forceVec = new Vector3D(
                fx_ton * 1000.0,
                fy_ton * 1000.0,
                fz_ton * 1000.0
            );

            // 5. 하중 추가 (여기서는 ID 증가시키지 않음!)
            loads.Add(new ForceLoad(targetNodeID, currentLoadId, forceVec));
            countInCase++;
          }
          catch (Exception ex)
          {
            log($"  [Error] {caseName} - {winchID}: {ex.Message}");
          }
        }

        // 해당 케이스에 유효한 하중이 하나라도 생성되었다면, ID를 증가시켜 다음 케이스 준비
        if (countInCase > 0)
        {
          // log($"  -> Generated {countInCase} loads for '{caseName}' (Load Case {currentLoadId})");
          currentLoadId++;
        }
        else
        {
          log($"  [Info] '{caseName}' skipped or empty (No valid loads created).");
        }
      }

      log($"[Winch Load Gen] Total {loads.Count} loads generated. (Final ID Sequence: {startId} ~ {currentLoadId - 1})");
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
