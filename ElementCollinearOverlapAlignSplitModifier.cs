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
    /// 중복/공선 그룹을 하나의 기준선(Reference Line)에 정렬시키고, 공유된 노드 위치에서 요소를 분할합니다.
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

      // 외부 연결 노드 복제 맵 (옵션)
      Dictionary<int, HashSet<int>>? nodeToElements = null;
      if (cloneExternalNodes)
      {
        nodeToElements = BuildNodeToElementsMap(elements);
      }

      int processedCount = 0;
      int totalSplitCount = 0;

      foreach (var group in overlapGroups)
      {
        if (group == null || group.Count < 2) continue;
        processedCount++;

        // ---------------------------------------------------------
        // 1. 그룹 정보 요약 출력
        // ---------------------------------------------------------
        var validElements = group.Where(elements.Contains).ToList();
        if (validElements.Count < 2) continue;

        if (debug)
        {
          log($"   [그룹 {processedCount}] 요소 {validElements.Count}개 처리: ID[{string.Join(", ", validElements)}]");
        }

        // ---------------------------------------------------------
        // 2. 기준선(Reference Line) 계산
        // ---------------------------------------------------------
        if (!TryBuildReferenceLine(nodes, elements, group, out var P0, out var vRef))
        {
          if (debug) log("      -> [건너뜀] 기준선을 계산할 수 없습니다 (점들이 너무 가깝거나 정의되지 않음).");
          continue;
        }

        // ---------------------------------------------------------
        // 3. 노드 투영 및 정렬 (Project & Align)
        // ---------------------------------------------------------
        var nodeMap = BuildProjectedNodeMap(
            nodes, elements, group, P0, vRef,
            nodeToElements, cloneExternalNodes);

        // Element의 노드 ID를 투영된(또는 복제된) 노드로 교체
        ReplaceGroupElementsNodeIDs(nodes, elements, group, nodeMap);

        // 정렬 품질 확인 (로그)
        if (debug)
        {
          double maxErr = CalculateMaxAlignmentError(elements, nodes, validElements, P0, vRef);
          string status = maxErr < 1.0 ? "양호" : "주의(오차큼)";
          log($"      -> 정렬 상태: {status} (최대 오차: {maxErr:F4})");
        }

        // ---------------------------------------------------------
        // 4. 분할 포인트(Global T) 계산 및 실행
        // ---------------------------------------------------------
        var globalT = BuildGlobalTSplitPoints(nodes, elements, group, P0, vRef, tTol);

        int createdSegments = SplitAllElementsByGlobalT(nodes, elements, group, P0, vRef, globalT, tTol, minSegLenTol);
        totalSplitCount += createdSegments;

        if (debug)
        {
          log($"      -> 분할 완료: {createdSegments}개의 정렬된 세그먼트 생성됨.");
        }
      }

      if (debug) log($"   -> [Stage 01 요약] 총 {processedCount}개 그룹 처리 완료, 전체 {totalSplitCount}개 세그먼트 생성.");
    }

    // =================================================================
    // Private Logic Methods (핵심 로직은 유지하되 불필요한 코드는 생략)
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

      // 기준점 P0 = 모든 점의 무게중심 (안정적인 위치)
      P0 = Point3dUtils.GetCentroid(endpoints);

      // 기준 벡터 vRef = 가장 긴 요소의 방향을 따름
      var seedEle = elements[seedId];
      var (s1, s2) = seedEle.GetEndNodePair();
      vRef = Vector3dUtils.Normalize(Vector3dUtils.Direction(nodes[s1], nodes[s2]));

      return true;
    }

    private static Dictionary<int, int> BuildProjectedNodeMap(
        Nodes nodes, Elements elements, HashSet<int> group,
        Point3D P0, Point3D vRef,
        Dictionary<int, HashSet<int>>? nodeToElements,
        bool cloneExternalNodes)
    {
      var map = new Dictionary<int, int>();
      var groupNodeIDs = new HashSet<int>();

      foreach (var eid in group)
      {
        if (!elements.Contains(eid)) continue;
        foreach (var nid in elements[eid].NodeIDs) groupNodeIDs.Add(nid);
      }

      foreach (var nid in groupNodeIDs)
      {
        var originalPos = nodes[nid];
        var proj = ProjectionUtils.ProjectPointToLine(originalPos, P0, vRef);

        if (cloneExternalNodes && nodeToElements != null && IsConnectedOutsideGroup(nodeToElements, nid, group))
        {
          int newId = nodes.LastNodeID + 1;
          nodes.AddWithID(newId, proj.X, proj.Y, proj.Z);
          map[nid] = newId;
        }
        else
        {
          // 단순 이동 (좌표 덮어쓰기)
          nodes.AddWithID(nid, proj.X, proj.Y, proj.Z);
          map[nid] = nid;
        }
      }
      return map;
    }

    private static void ReplaceGroupElementsNodeIDs(
        Nodes nodes, Elements elements, HashSet<int> group, Dictionary<int, int> nodeMap)
    {
      foreach (var eid in group)
      {
        if (!elements.Contains(eid)) continue;
        var e = elements[eid];

        var newNodeIDs = e.NodeIDs
            .Select(nid => nodeMap.TryGetValue(nid, out var mapped) ? mapped : nid)
            .ToList();

        if (newNodeIDs.Count >= 2 && newNodeIDs.First() == newNodeIDs.Last())
        {
          elements.Remove(eid);
          continue;
        }

        var extra = e.ExtraData.ToDictionary(k => k.Key, v => v.Value);
        elements.AddWithID(eid, newNodeIDs, e.PropertyID, extra);
      }
    }

    private static List<double> BuildGlobalTSplitPoints(
        Nodes nodes, Elements elements, HashSet<int> group,
        Point3D P0, Point3D vRef, double tTol)
    {
      var tList = new List<double>();
      foreach (var eid in group)
      {
        if (!elements.Contains(eid)) continue;
        foreach (var nid in elements[eid].NodeIDs)
        {
          double t = Point3dUtils.Dot(Point3dUtils.Sub(nodes[nid], P0), vRef);
          tList.Add(t);
        }
      }
      tList.Sort();
      return MathUtils.MergeClose(tList, tTol);
    }

    private static int SplitAllElementsByGlobalT(
        Nodes nodes, Elements elements, HashSet<int> group,
        Point3D P0, Point3D vRef, List<double> globalT,
        double tTol, double minSegLenTol)
    {
      int createdCount = 0;
      var targetElements = group.Where(elements.Contains).ToList();

      foreach (var eid in targetElements)
      {
        var e = elements[eid];
        var (n1, n2) = e.GetEndNodePair();
        double t1 = Point3dUtils.Dot(Point3dUtils.Sub(nodes[n1], P0), vRef);
        double t2 = Point3dUtils.Dot(Point3dUtils.Sub(nodes[n2], P0), vRef);

        double tMin = Math.Min(t1, t2);
        double tMax = Math.Max(t1, t2);

        var validT = globalT
            .Where(t => t > tMin + minSegLenTol && t < tMax - minSegLenTol)
            .ToList();

        if (validT.Count == 0) continue;

        bool forward = (t2 > t1);
        validT.Add(t1);
        validT.Add(t2);

        var sortedT = forward
            ? validT.OrderBy(x => x).ToList()
            : validT.OrderByDescending(x => x).ToList();

        var extra = e.ExtraData.ToDictionary(k => k.Key, v => v.Value);
        int propID = e.PropertyID;
        elements.Remove(eid);

        for (int i = 0; i < sortedT.Count - 1; i++)
        {
          double ta = sortedT[i];
          double tb = sortedT[i + 1];

          int na = nodes.GetOrCreateNodeAtT(P0, vRef, ta);
          int nb = nodes.GetOrCreateNodeAtT(P0, vRef, tb);

          if (na == nb) continue;

          elements.AddNew(new List<int> { na, nb }, propID, extra);
          createdCount++;
        }
      }
      return createdCount;
    }

    private static double CalculateMaxAlignmentError(
        Elements elements, Nodes nodes, List<int> groupEids, Point3D P0, Point3D vRef)
    {
      double maxDist = 0.0;
      foreach (var eid in groupEids)
      {
        if (!elements.Contains(eid)) continue;
        foreach (var nid in elements[eid].NodeIDs)
        {
          double d = DistanceUtils.DistancePointToLine(nodes[nid], P0, vRef);
          if (d > maxDist) maxDist = d;
        }
      }
      return maxDist;
    }

    // --- Helper Methods ---
    private static Dictionary<int, HashSet<int>> BuildNodeToElementsMap(Elements elements)
    {
      var map = new Dictionary<int, HashSet<int>>();
      foreach (var kv in elements.AsReadOnly())
      {
        foreach (var nid in kv.Value.NodeIDs)
        {
          if (!map.TryGetValue(nid, out var set))
            map[nid] = set = new HashSet<int>();
          set.Add(kv.Key);
        }
      }
      return map;
    }

    private static bool IsConnectedOutsideGroup(Dictionary<int, HashSet<int>> map, int nid, HashSet<int> group)
    {
      if (map.TryGetValue(nid, out var eids))
      {
        return eids.Any(eid => !group.Contains(eid));
      }
      return false;
    }
  }
}
