using MooringFitting2026.Extensions;
using MooringFitting2026.Model.Entities;
using MooringFitting2026.Model.Geometry;
using MooringFitting2026.Utils;
using MooringFitting2026.Utils.Geometry;
using System;
using System.Collections.Generic;
using System.Linq;

namespace MooringFitting2026.Modifier.ElementModifier
{
  public static class ElementCollinearOverlapAlignSplitModifier
  {
    /// <summary>
    /// 중복/공선 그룹을 전역 그리드(Global Grid)에 맞춰 정렬하고 재구성합니다.
    /// (Gap 메우기, 미세 중복 병합, 길이 비례 스냅 적용)
    /// </summary>
    public static void Run(
        FeModelContext context,
        List<HashSet<int>> overlapGroups,
        double tTol = 0.05,
        double minSegLenTol = 1e-3,
        bool debug = false,
        Action<string>? log = null,
        bool cloneExternalNodes = false)
    {
      log ??= Console.WriteLine;

      if (overlapGroups == null || overlapGroups.Count == 0)
      {
        if (debug) log("   -> 처리할 중복 그룹이 없습니다.");
        return;
      }

      var nodes = context.Nodes;
      var elements = context.Elements;

      // [설정] 통합 허용 거리 (Gap Closing Tolerance)
      // 이 거리 이내의 점들은 무조건 하나로 합쳐집니다. (예: 50mm)
      double clusterTolerance = 50.0;

      int processedCount = 0;
      int totalSegments = 0;

      foreach (var group in overlapGroups)
      {
        if (group == null || group.Count < 2) continue;
        processedCount++;

        var validElements = group.Where(elements.Contains).ToList();
        if (validElements.Count < 2) continue;

        if (debug)
        {
          log($"   [그룹 {processedCount}] 요소 {validElements.Count}개 재구성 시작: ID[{string.Join(", ", validElements)}]");
        }

        // 1. 기준선(Reference Line) 계산
        if (!TryBuildReferenceLine(nodes, elements, group, out var P0, out var vRef))
        {
          if (debug) log("      -> [건너뜀] 기준선을 계산할 수 없습니다.");
          continue;
        }

        // 2. 전역 그리드 정렬 및 분할 실행 (핵심 로직 변경)
        int created = RunGlobalGridAlignment(
            nodes, elements, group,
            P0, vRef,
            clusterTolerance, minSegLenTol,
            debug, log);

        totalSegments += created;
      }

      if (debug) log($"   -> [Stage 01 요약] 총 {processedCount}개 그룹 처리, {totalSegments}개 세그먼트로 재구성.");
    }

    // =================================================================
    // [핵심] 전역 그리드 기반 정렬 및 분할 (Global Grid Alignment)
    // =================================================================
    private static int RunGlobalGridAlignment(
        Nodes nodes, Elements elements, HashSet<int> group,
        Point3D P0, Point3D vRef,
        double clusterTolerance,
        double minSegLen,
        bool debug, Action<string> log)
    {
      var targetEids = group.Where(elements.Contains).ToList();

      // 1. 모든 요소의 끝점을 기준선 위로 투영(Project)하여 T값 수집
      var rawPoints = new List<double>();
      var elementRanges = new Dictionary<int, (double start, double end)>();

      foreach (var eid in targetEids)
      {
        var e = elements[eid];
        var (n1, n2) = e.GetEndNodePair();

        double t1 = Point3dUtils.Dot(Point3dUtils.Sub(nodes[n1], P0), vRef);
        double t2 = Point3dUtils.Dot(Point3dUtils.Sub(nodes[n2], P0), vRef);

        // 항상 작은 값이 Start가 되도록 정규화
        elementRanges[eid] = (Math.Min(t1, t2), Math.Max(t1, t2));

        rawPoints.Add(t1);
        rawPoints.Add(t2);
      }

      // 2. 좌표 군집화 (Clustering) -> 이것이 "Master Grid"가 됩니다.
      rawPoints.Sort();
      var gridPoints = ClusterValues(rawPoints, clusterTolerance);

      if (debug) log($"      -> Grid 생성: 원본 점 {rawPoints.Count}개 -> 통합 Grid {gridPoints.Count}개 (Tol: {clusterTolerance})");

      int createdCount = 0;

      // 3. 각 요소를 Grid에 맞게 변형 및 분할
      foreach (var eid in targetEids)
      {
        if (!elements.Contains(eid)) continue;

        var originalRange = elementRanges[eid];
        double orgLen = originalRange.end - originalRange.start;

        // (A) 스냅: 시작점과 끝점을 가장 가까운 Grid Point로 이동
        // 단, '30% 길이 규칙' 적용
        double snapRatio = 0.3;
        double maxSnapMove = Math.Max(orgLen * snapRatio, clusterTolerance);

        double newStart = SnapToGrid(originalRange.start, gridPoints, maxSnapMove);
        double newEnd = SnapToGrid(originalRange.end, gridPoints, maxSnapMove);

        // 스냅 결과 길이가 너무 짧아지면(붕괴), 제거 (노이즈 부재)
        if (Math.Abs(newEnd - newStart) < minSegLen)
        {
          elements.Remove(eid);
          continue;
        }

        // (B) 분할: newStart ~ newEnd 사이에 있는 모든 Grid Point를 찾음
        var segments = gridPoints
            .Where(g => g > newStart + 1e-5 && g < newEnd - 1e-5)
            .ToList();

        segments.Insert(0, newStart);
        segments.Add(newEnd);

        // (C) 요소 재생성
        var e = elements[eid];
        var extra = e.ExtraData.ToDictionary(k => k.Key, v => v.Value);
        int propID = e.PropertyID;
        elements.Remove(eid); // 원본 삭제

        for (int i = 0; i < segments.Count - 1; i++)
        {
          double ta = segments[i];
          double tb = segments[i + 1];

          // Grid 상에서 실제 노드 생성 (투영된 좌표 P0 + t*vRef)
          int na = nodes.GetOrCreateNodeAtT(P0, vRef, ta);
          int nb = nodes.GetOrCreateNodeAtT(P0, vRef, tb);

          if (na == nb) continue;

          elements.AddNew(new List<int> { na, nb }, propID, extra);
          createdCount++;
        }
      }

      return createdCount;
    }

    // --- Helper: 값 군집화 (Clustering) ---
    private static List<double> ClusterValues(List<double> values, double tolerance)
    {
      if (values.Count == 0) return new List<double>();

      var result = new List<double>();
      double currentSum = values[0];
      int currentCount = 1;
      double lastVal = values[0];

      for (int i = 1; i < values.Count; i++)
      {
        double val = values[i];
        if (val - lastVal <= tolerance) // 허용 오차 이내면 합침
        {
          currentSum += val;
          currentCount++;
        }
        else // 오차 벗어나면 그룹 확정
        {
          result.Add(currentSum / currentCount); // 평균값 사용
          currentSum = val;
          currentCount = 1;
        }
        lastVal = val;
      }
      result.Add(currentSum / currentCount);
      return result;
    }

    // --- Helper: 가장 가까운 Grid로 스냅 (제한 거리 적용) ---
    private static double SnapToGrid(double val, List<double> grid, double maxMove)
    {
      double best = val;
      double minDiff = double.MaxValue;

      foreach (var g in grid)
      {
        double diff = Math.Abs(val - g);
        if (diff < minDiff)
        {
          minDiff = diff;
          best = g;
        }
      }

      if (minDiff <= maxMove)
      {
        return best;
      }
      return best; // 멀더라도 구조적 연결을 위해 가장 가까운 Grid 선택 (Cluster에 포함되었으므로 안전)
    }

    // =================================================================
    // Helper: Reference Line Calculation (기존 로직 유지)
    // =================================================================
    private static bool TryBuildReferenceLine(Nodes nodes, Elements elements, HashSet<int> group, out Point3D P0, out Point3D vRef)
    {
      P0 = default; vRef = default;
      var endpoints = new List<Point3D>();

      int seedId = -1;
      double maxLen = -1;

      foreach (var eid in group)
      {
        if (!elements.Contains(eid)) continue;
        var e = elements[eid];
        var (n1, n2) = e.GetEndNodePair();

        var p1 = nodes[n1];
        var p2 = nodes[n2];
        endpoints.Add(p1);
        endpoints.Add(p2);

        double len = DistanceUtils.GetDistanceBetweenNodes(p1, p2);
        if (len > maxLen) { maxLen = len; seedId = eid; }
      }

      if (seedId < 0 || endpoints.Count < 2) return false;

      P0 = Point3dUtils.GetCentroid(endpoints);

      var seedEle = elements[seedId];
      var (s1, s2) = seedEle.GetEndNodePair();
      vRef = Vector3dUtils.Normalize(Vector3dUtils.Direction(nodes[s1], nodes[s2]));

      return true;
    }
  }
}
