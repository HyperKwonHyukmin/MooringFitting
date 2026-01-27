using MooringFitting2026.Extensions;
using MooringFitting2026.Model.Entities;
using MooringFitting2026.Model.Geometry;
using MooringFitting2026.Utils;
using MooringFitting2026.Utils.Geometry;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MooringFitting2026.Modifier.ElementModifier
{
  public static class ElementCollinearOverlapAlignSplitModifier
  {
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

      var nodes = context.Nodes;
      var elements = context.Elements;

      if (overlapGroups == null || overlapGroups.Count == 0)
      {
        if (debug) log("[AlignSplit] overlapGroups is empty.");
        return;
      }

      // cloneExternalNodes 옵션이 켜져 있을 때만 node -> connected element set 생성
      Dictionary<int, HashSet<int>>? nodeToElements = null;
      if (cloneExternalNodes)
      {
        nodeToElements = BuildNodeToElementsMap(elements);
        if (debug) log($"[AlignSplit] nodeToElements map built. nodes={nodeToElements.Count}");
      }

      int groupIndex = 0;

      foreach (var group in overlapGroups)
      {
        if (group == null || group.Count < 2)
        {
          groupIndex++;
          continue;
        }

        if (debug)
        {
          log($"\n==== [Group {groupIndex}] elements: {string.Join(",", group.OrderBy(x => x))} ====");
          DumpGroupBasic(elements, nodes, group, log);
        }

        // 1) 그룹 기준선 계산
        if (!TryBuildReferenceLine(nodes, elements, group, out var P0, out var vRef))
        {
          if (debug) log("  [Skip] TryBuildReferenceLine failed.");
          groupIndex++;
          continue;
        }

        if (debug)
        {
          log($"  P0={P0}, vRef={vRef}");
          DumpGroupLineQuality(elements, nodes, group, P0, vRef, log);
        }

        // 2) 그룹 노드들을 기준선으로 투영하여 정렬
        //    핵심: 투영 단계에서는 AddOrGet 사용 금지(노드가 조기에 병합되어 split 포인트가 사라지는 문제 방지)
        var nodeMap = BuildProjectedNodeMap(
          nodes, elements, group, P0, vRef,
          nodeToElements, cloneExternalNodes,
          debug, log);

        // 3) (옵션) cloneExternalNodes=true면 element 노드 ID를 새 nodeID로 치환해야 함
        //    cloneExternalNodes=false이면 대부분 identity map이므로 실질적 변화는 없음(그래도 안전하게 적용)
        ReplaceGroupElementsNodeIDs(nodes, elements, group, nodeMap);

        if (debug)
        {
          log($"  nodeMap: {nodeMap.Count} nodes remapped. distinctNew={nodeMap.Values.Distinct().Count()}");
          DumpGroupLineQuality(elements, nodes, group.Where(elements.Contains).ToHashSet(), P0, vRef, log);
        }

        // 4) 그룹 전체 공통 t 집합 구축
        var globalT = BuildGlobalTSplitPoints(nodes, elements, group, P0, vRef, tTol);

        if (debug)
        {
          log($"  globalT count={globalT.Count}" +
              (globalT.Count > 0 ? $", range=[{globalT.First():F6} ~ {globalT.Last():F6}]" : ""));
          if (globalT.Count <= 2)
            log("  !!! globalT<=2 : split 포인트가 거의 없음(쪼개질 가능성 낮음). tTol이 너무 크거나, 노드가 너무 뭉개졌을 수 있음.");
        }

        // 5) 모든 element를 globalT 기준으로 split (공통 노드 강제 공유)
        SplitAllElementsByGlobalT(nodes, elements, group, P0, vRef, globalT, tTol, minSegLenTol, debug, log);

        if (debug) log($"==== [Group {groupIndex}] done ====");
        groupIndex++;
      }
    }

    // ---------------------------
    // 1) Reference line
    // ---------------------------
    private static bool TryBuildReferenceLine(
      Nodes nodes, Elements elements, HashSet<int> group,
      out Point3D P0, out Point3D vRef)
    {
      P0 = new Point3D(0, 0, 0);
      vRef = new Point3D(0, 0, 0);

      int seedId = -1;
      double maxLen = -1;

      var endpoints = new List<Point3D>();

      foreach (var eid in group)
      {
        if (!elements.Contains(eid)) continue;

        var e = elements[eid];
        var (aId, bId) = e.GetEndNodePair();

        var a = nodes[aId];
        var b = nodes[bId];

        endpoints.Add(a);
        endpoints.Add(b);

        double len = DistanceUtils.GetDistanceBetweenNodes(a, b);
        if (len > maxLen)
        {
          maxLen = len;
          seedId = eid;
        }
      }

      if (seedId < 0 || maxLen <= 0 || endpoints.Count < 2)
        return false;

      // centroid
      P0 = new Point3D(
        endpoints.Average(p => p.X),
        endpoints.Average(p => p.Y),
        endpoints.Average(p => p.Z));

      // seed direction
      {
        var seed = elements[seedId];
        var (aId, bId) = seed.GetEndNodePair();
        var a = nodes[aId];
        var b = nodes[bId];
        vRef = Point3dUtils.Normalize(Point3dUtils.Sub(b, a));
      }

      // length-weighted average direction with sign alignment
      double sx = 0, sy = 0, sz = 0;

      foreach (var eid in group)
      {
        if (!elements.Contains(eid)) continue;

        var e = elements[eid];
        var (aId, bId) = e.GetEndNodePair();

        var a = nodes[aId];
        var b = nodes[bId];

        var dir = Point3dUtils.Normalize(Point3dUtils.Sub(b, a));
        double len = DistanceUtils.GetDistanceBetweenNodes(a, b);
        if (len <= 0) continue;

        if (Point3dUtils.Dot(dir, vRef) < 0)
          dir = Point3dUtils.Mul(dir, -1);

        sx += dir.X * len;
        sy += dir.Y * len;
        sz += dir.Z * len;
      }

      var sum = new Point3D(sx, sy, sz);
      if (Point3dUtils.Norm(sum) < 1e-12)
        return true;

      vRef = Point3dUtils.Normalize(sum);
      return true;
    }

    // ---------------------------
    // 2) Node projection map
    // ---------------------------
    private static Dictionary<int, int> BuildProjectedNodeMap(
      Nodes nodes, Elements elements, HashSet<int> group,
      Point3D P0, Point3D vRef,
      Dictionary<int, HashSet<int>>? nodeToElements,
      bool cloneExternalNodes,
      bool debug,
      Action<string> log)
    {
      var groupNodeIDs = new HashSet<int>();

      foreach (var eid in group)
      {
        if (!elements.Contains(eid)) continue;
        foreach (var nid in elements[eid].NodeIDs)
          groupNodeIDs.Add(nid);
      }

      if (debug) log($"  group nodes(unique)={groupNodeIDs.Count}");

      var map = new Dictionary<int, int>();

      foreach (var nid in groupNodeIDs)
      {
        var x = nodes[nid];
        var proj = ProjectionUtils.ProjectPointToLine(x, P0, vRef);

        // (중요) 투영 단계에서는 AddOrGet로 병합하지 않는다.
        // - cloneExternalNodes=false: nodeID 유지 + 좌표만 이동(AddWithID)
        // - cloneExternalNodes=true: 그룹 밖 element에 연결된 node는 clone을 만들어 그룹 내부에서만 치환
        if (cloneExternalNodes && nodeToElements != null && IsConnectedOutsideGroup(nodeToElements, nid, group))
        {
          int newId = nodes.LastNodeID + 1; // AddWithID가 내부 nextID/LastID 갱신함
          nodes.AddWithID(newId, proj.X, proj.Y, proj.Z);
          map[nid] = newId;

          if (debug)
            log($"    clone node: oldN{nid} -> newN{newId} (outside-connected)");
        }
        else
        {
          nodes.AddWithID(nid, proj.X, proj.Y, proj.Z);
          map[nid] = nid;
        }
      }

      return map;
    }

    private static bool IsConnectedOutsideGroup(Dictionary<int, HashSet<int>> nodeToElements, int nodeId, HashSet<int> group)
    {
      if (!nodeToElements.TryGetValue(nodeId, out var eids) || eids.Count == 0)
        return false;

      foreach (var eid in eids)
        if (!group.Contains(eid))
          return true;

      return false;
    }

    private static Dictionary<int, HashSet<int>> BuildNodeToElementsMap(Elements elements)
    {
      var map = new Dictionary<int, HashSet<int>>();

      foreach (var kv in elements.AsReadOnly()) // Elements.AsReadOnly() 제공됨:contentReference[oaicite:3]{index=3}
      {
        int eid = kv.Key;
        var e = kv.Value;

        foreach (var nid in e.NodeIDs)
        {
          if (!map.TryGetValue(nid, out var set))
          {
            set = new HashSet<int>();
            map[nid] = set;
          }
          set.Add(eid);
        }
      }

      return map;
    }

    // ---------------------------
    // 3) Replace element node IDs by map (needed if clones exist)
    // ---------------------------
    private static void ReplaceGroupElementsNodeIDs(
      Nodes nodes, Elements elements, HashSet<int> group,
      Dictionary<int, int> nodeMap)
    {
      foreach (var eid in group)
      {
        if (!elements.Contains(eid)) continue;

        var e = elements[eid];

        var newNodeIDs = e.NodeIDs
          .Select(nid => nodeMap.TryGetValue(nid, out var nn) ? nn : nid)
          .ToList();

        // 투영/치환 후 양끝이 같은 노드면 길이 0 element → 제거
        if (newNodeIDs.Count >= 2 && newNodeIDs.First() == newNodeIDs.Last())
        {
          elements.Remove(eid);
          continue;
        }

        var extra = e.ExtraData.ToDictionary(kv => kv.Key, kv => kv.Value);
        elements.AddWithID(eid, newNodeIDs, e.PropertyID, extra);
      }
    }

    // ---------------------------
    // 4) Global split points (t)
    // ---------------------------
    private static List<double> BuildGlobalTSplitPoints(
      Nodes nodes, Elements elements, HashSet<int> group,
      Point3D P0, Point3D vRef, double tTol)
    {
      var tList = new List<double>();

      foreach (var eid in group)
      {
        if (!elements.Contains(eid)) continue;
        var e = elements[eid];

        foreach (var nid in e.NodeIDs)
        {
          var p = nodes[nid];
          double t = Point3dUtils.Dot(Point3dUtils.Sub(p, P0), vRef);
          tList.Add(t);
        }
      }

      tList.Sort();
      return MathUtils.MergeClose(tList, tTol);
    }

    // ---------------------------
    // 5) Split elements by global T
    // ---------------------------
    private static void SplitAllElementsByGlobalT(
      Nodes nodes, Elements elements, HashSet<int> group,
      Point3D P0, Point3D vRef, List<double> globalT,
      double tTol, double minSegLenTol,
      bool debug, Action<string> log)
    {
      var groupIdsSnapshot = group.Where(elements.Contains).ToList();

      foreach (var eid in groupIdsSnapshot)
      {
        if (!elements.Contains(eid)) continue;

        var e = elements[eid];
        var (aId, bId) = e.GetEndNodePair();

        var a = nodes[aId];
        var b = nodes[bId];

        double tA = Point3dUtils.Dot(Point3dUtils.Sub(a, P0), vRef);
        double tB = Point3dUtils.Dot(Point3dUtils.Sub(b, P0), vRef);
        double tMin = Math.Min(tA, tB);
        double tMax = Math.Max(tA, tB);

        // element 구간 안에 들어오는 globalT + endpoints
        var tLocal = globalT
          .Where(t => t >= tMin - tTol && t <= tMax + tTol)
          .Concat(new[] { tA, tB })
          .OrderBy(t => t)
          .ToList();

        tLocal = MathUtils.MergeClose(tLocal, tTol);



        // 세그먼트 생성
        var segs = new List<(int n1, int n2)>();

        for (int i = 0; i < tLocal.Count - 1; i++)
        {
          double t1 = tLocal[i];
          double t2 = tLocal[i + 1];

          if (Math.Abs(t2 - t1) < minSegLenTol)
            continue;

          int n1 = nodes.GetOrCreateNodeAtT(P0, vRef, t1);
          int n2 = nodes.GetOrCreateNodeAtT(P0, vRef, t2);
          if (n1 == n2) continue;

          // 방향은 element 원래 방향을 따라가도록 정렬
          if (tA <= tB)
            segs.Add((n1, n2));
          else
            segs.Add((n2, n1));
        }

        if (debug)
        {
          log($"  E{eid}: tA={tA:F6}, tB={tB:F6}, tLocal={tLocal.Count}, segs={segs.Count}");
          if (segs.Count <= 1)
            log("    (info) segs<=1 이면 실제 AddNew가 발생하지 않아 '안쪼개진 것처럼' 보일 수 있음.");
        }

        if (segs.Count == 0)
        {
          elements.Remove(eid);
          continue;
        }

        var extra = e.ExtraData.ToDictionary(kv => kv.Key, kv => kv.Value);

        elements.Remove(eid);

        List<int> addElements = new List<int>();
        for (int i = 0; i < segs.Count; i++)
        {
          int newEleID = elements.AddNew(new List<int> { segs[i].n1, segs[i].n2 }, e.PropertyID, extra);
          addElements.Add(newEleID);
        }
        if (debug) Console.WriteLine($"삭제되는 ele:{eid}, 생성되는 ele: {string.Join(",", addElements)}");

      }
    }

    // ---------------------------
    // Debug helpers
    // ---------------------------
    private static void DumpGroupBasic(Elements elements, Nodes nodes, HashSet<int> group, Action<string> log)
    {
      int existing = group.Count(elements.Contains);
      var nodeSet = new HashSet<int>();

      foreach (var eid in group)
      {
        if (!elements.Contains(eid)) continue;
        foreach (var nid in elements[eid].NodeIDs)
          nodeSet.Add(nid);
      }

      log($"  elements(existing)={existing}, nodes(unique)={nodeSet.Count}");

      foreach (var eid in group.OrderBy(x => x))
      {
        if (!elements.Contains(eid))
        {
          log($"    E{eid}: (missing)");
          continue;
        }

        var e = elements[eid];
        var (aId, bId) = e.GetEndNodePair();
        var a = nodes[aId];
        var b = nodes[bId];
        double len = DistanceUtils.GetDistanceBetweenNodes(a, b);

        log($"    E{eid}: N[{aId},{bId}] len={len:F3} prop={e.PropertyID}");
      }
    }

    private static void DumpGroupLineQuality(Elements elements, Nodes nodes, HashSet<int> group, Point3D P0, Point3D vRef, Action<string> log)
    {
      double maxDist = 0.0;
      double maxAngDeg = 0.0;

      foreach (var eid in group.OrderBy(x => x))
      {
        if (!elements.Contains(eid)) continue;

        var e = elements[eid];
        var (aId, bId) = e.GetEndNodePair();
        var a = nodes[aId];
        var b = nodes[bId];

        double da = DistanceUtils.DistancePointToLine(a, P0, vRef);
        double db = DistanceUtils.DistancePointToLine(b, P0, vRef);
        maxDist = Math.Max(maxDist, Math.Max(da, db));

        var dir = Point3dUtils.Normalize(Point3dUtils.Sub(b, a));
        double dot = Math.Abs(Point3dUtils.Dot(dir, vRef));
        dot = Math.Max(-1.0, Math.Min(1.0, dot));
        double angDeg = Math.Acos(dot) * 180.0 / Math.PI;
        maxAngDeg = Math.Max(maxAngDeg, angDeg);
      }

      log($"  [Quality] maxNodeDistToLine={maxDist:F6}, maxAngleToRef={maxAngDeg:F6}deg");
    }
  }
}


