using MooringFitting2026.Extensions;
using MooringFitting2026.Inspector.NodeInspector;
using MooringFitting2026.Model.Entities;
using MooringFitting2026.Model.Geometry;
using MooringFitting2026.Utils;
using MooringFitting2026.Utils.Geometry;
using System;
using System.Collections.Generic;
using System.Linq;

namespace MooringFitting2026.Modifier.ElementModifier
{
  public static class ElementExtendToBBoxIntersectAndSplitModifier
  {
    public sealed record Options
    {
      public double SearchRatio { get; init; } = 5.0;
      public double DefaultSearchDist { get; init; } = 50.0;
      public double IntersectionTolerance { get; init; } = 1.0;
      public double GridCellSize { get; init; } = 50.0;
      public bool DryRun { get; init; } = false;
      public bool Debug { get; init; } = false;
      public HashSet<int> WatchNodeIDs { get; init; } = new HashSet<int>();
    }

    public sealed record Result(
        int CheckedNodes,
        int ExtensionsFound,
        int SuccessConnections
    );

    public static Result Run(FeModelContext context, Options? opt = null, Action<string>? log = null)
    {
      opt ??= new Options();
      log ??= Console.WriteLine;

      var nodes = context.Nodes;
      var elements = context.Elements;
      var props = context.Properties;

      var nodeDegree = NodeDegreeInspector.BuildNodeDegree(context);
      var freeNodes = nodeDegree.Where(kv => kv.Value == 1).Select(kv => kv.Key).ToList();

      if (opt.Debug) log($"[Stage 04] Found {freeNodes.Count} free-end nodes. Grid: {opt.GridCellSize}");

      var grid = new LocalSpatialHash(elements, nodes, opt.GridCellSize, opt.IntersectionTolerance);
      var actions = new List<ExtensionAction>();
      int processed = 0;

      // 1. 모든 후보 탐색 (변경 없이 수집만 수행)
      foreach (var freeNodeID in freeNodes)
      {
        processed++;
        if (opt.Debug && processed % 100 == 0) log($"... Processed {processed}/{freeNodes.Count} ...");

        var sourceEleID = FindConnectedElementID(elements, freeNodeID);
        if (sourceEleID == -1) continue;

        var sourceEle = elements[sourceEleID];
        var (startID, endID) = sourceEle.GetEndNodePair();
        int connectedNodeID = (startID == freeNodeID) ? endID : startID;

        var pFree = nodes[freeNodeID];
        var pConn = nodes[connectedNodeID];
        var dirVector = Vector3dUtils.Normalize(Vector3dUtils.Direction(pConn, pFree));

        // 검색 거리
        double maxDist = opt.DefaultSearchDist;
        double refDim = 0.0;
        int propID = sourceEle.PropertyID;

        if (props.Contains(propID))
        {
          var prop = props[propID];
          if (opt.WatchNodeIDs != null && opt.WatchNodeIDs.Contains(freeNodeID))
          {
            log($"\n[DEBUG-PROP] Node {freeNodeID} (Ele {sourceEleID}) PropID {propID} Type='{prop.Type}' Dims=[{string.Join(", ", prop.Dim)}]");
          }

          if (prop.Dim != null && prop.Dim.Count > 0)
          {
            if (prop.Type.Equals("T", StringComparison.OrdinalIgnoreCase))
              refDim = (prop.Dim.Count > 0) ? prop.Dim[0] / 2.0 : 0;
            else if (prop.Type.Equals("I", StringComparison.OrdinalIgnoreCase))
              refDim = (prop.Dim.Count > 2) ? prop.Dim[2] / 2.0 : 0;
            else
              refDim = prop.Dim.Max();
          }
          if (refDim > 0) maxDist = refDim * opt.SearchRatio;
        }

        maxDist = Math.Max(maxDist, opt.DefaultSearchDist);
        if (maxDist > 2000.0) maxDist = 500.0;

        // 진단
        if (opt.WatchNodeIDs != null && opt.WatchNodeIDs.Contains(freeNodeID))
        {
          log($"    -> Final MaxDist: {maxDist:F2}");
          DiagnoseNode(freeNodeID, pFree, dirVector, maxDist, opt.IntersectionTolerance,
                       sourceEleID, elements, nodes, log);
        }

        // Query
        Vector3D rayVec = new Vector3D(dirVector.X * maxDist, dirVector.Y * maxDist, dirVector.Z * maxDist);
        var rayEnd = Point3dUtils.Move(pFree, rayVec);
        var searchBox = BoundingBox.FromSegment(pFree, rayEnd, opt.IntersectionTolerance);
        var candidates = grid.Query(searchBox);

        IntersectionHit? bestHit = null;

        foreach (var targetEleID in candidates)
        {
          if (targetEleID == sourceEleID) continue;
          if (!TryGetElementSegment(nodes, elements, targetEleID, out var tP1, out var tP2)) continue;

          if (TryGetRaySegmentIntersection(
              pFree, dirVector, maxDist,
              tP1, tP2, opt.IntersectionTolerance,
              out var hitPoint, out var distFromFreeNode))
          {
            if (bestHit == null || distFromFreeNode < bestHit.Distance)
            {
              bestHit = new IntersectionHit(targetEleID, hitPoint, distFromFreeNode);
            }
          }
        }

        if (bestHit != null)
        {
          actions.Add(new ExtensionAction(freeNodeID, sourceEleID, bestHit.TargetEleID, bestHit.Point));
          if (opt.Debug || (opt.WatchNodeIDs?.Contains(freeNodeID) ?? false))
            log($"  -> [MATCH] Node {freeNodeID} extends to E{bestHit.TargetEleID} (Dist: {bestHit.Distance:F2})");
        }
      }

      if (opt.DryRun) return new Result(freeNodes.Count, actions.Count, 0);

      // 2. 일괄 적용 (Bulk Apply)
      if (opt.Debug && actions.Count > 0) log($"[Stage 04] Applying {actions.Count} extensions in BULK...");
      int successCount = ApplyActionsBulk(context, actions, opt.Debug, log);

      return new Result(freeNodes.Count, actions.Count, successCount);
    }

    // --- ★ [핵심 수정] 일괄 적용 로직 ---
    private static int ApplyActionsBulk(FeModelContext context, List<ExtensionAction> actions, bool debug, Action<string> log)
    {
      var nodes = context.Nodes;
      var elements = context.Elements;
      int splitCount = 0;

      // 1. 교차점 노드 생성 및 매핑
      // (하나의 타겟에 여러 점이 찍힐 수 있으므로, Action별로 NodeID를 미리 확보)
      var actionNodeMap = new Dictionary<ExtensionAction, int>();
      foreach (var act in actions)
      {
        int nid = nodes.AddOrGet(act.HitPoint.X, act.HitPoint.Y, act.HitPoint.Z);
        actionNodeMap[act] = nid;
      }

      // 2. Source Elements 연장 (먼저 수행)
      // 타겟이 쪼개지더라도 소스는 '좌표'나 '새 노드ID'만 알면 되므로 먼저 처리
      foreach (var act in actions)
      {
        if (!elements.Contains(act.SourceEleID)) continue;
        var srcEle = elements[act.SourceEleID];
        int newNodeID = actionNodeMap[act];

        if (srcEle.TryReplaceNode(act.FreeNodeID, newNodeID, out var newSrc))
        {
          elements.AddWithID(act.SourceEleID, newSrc.NodeIDs.ToList(), newSrc.PropertyID,
              newSrc.ExtraData.ToDictionary(k => k.Key, k => k.Value));
        }
      }

      // 3. Target Elements 그룹화 및 분할
      var targetGroups = actions.GroupBy(a => a.TargetEleID);

      foreach (var group in targetGroups)
      {
        int targetID = group.Key;
        if (!elements.Contains(targetID)) continue; // 이미 삭제된 경우(중복?) 방어

        var targetEle = elements[targetID];
        var (tN1, tN2) = targetEle.GetEndNodePair();
        var pStart = nodes[tN1];
        var pEnd = nodes[tN2];

        // 해당 타겟 위의 모든 교차점 수집
        var splitPoints = new List<(int NodeID, double U)>();

        foreach (var act in group)
        {
          int nid = actionNodeMap[act];
          // 시작점부터의 거리(비율) 계산 -> 정렬용
          double u = ProjectionUtils.ProjectPointToScalar(nodes[nid], pStart, pEnd);
          splitPoints.Add((nid, u));
        }

        // 정렬 (시작점 -> 끝점 순서)
        var sortedPoints = splitPoints.OrderBy(x => x.U).ToList();

        // 연결 체인 구성 (Start -> P1 -> P2 ... -> End)
        var chain = new List<int> { tN1 };
        foreach (var sp in sortedPoints)
        {
          // 중복 노드 방지 (같은 위치에 여러개 붙을 경우)
          if (chain.Last() != sp.NodeID && sp.NodeID != tN2)
          {
            chain.Add(sp.NodeID);
          }
        }
        if (chain.Last() != tN2) chain.Add(tN2);

        // 분할이 필요 없으면 패스 (양끝점에 붙은 경우)
        if (chain.Count <= 2) continue;

        // 기존 요소 삭제
        elements.Remove(targetID);
        var extra = targetEle.ExtraData.ToDictionary(k => k.Key, v => v.Value);

        // 새 요소들 생성
        for (int i = 0; i < chain.Count - 1; i++)
        {
          elements.AddNew(new List<int> { chain[i], chain[i + 1] }, targetEle.PropertyID, extra);
        }

        if (debug) log($"    [Split] E{targetID} -> {chain.Count - 1} segments.");
        splitCount++;
      }

      return splitCount;
    }

    // --- Helpers & Diagnosis (기존 동일) ---
    private static void DiagnoseNode(
        int nodeID, Point3D origin, Vector3D dir, double maxDist, double tol,
        int sourceEleID, Elements elements, Nodes nodes, Action<string> log)
    {
      // (진단 로직 기존 유지 - 생략 가능하나 컴파일 위해 포함 권장)
      // ... (이전 코드의 DiagnoseNode 그대로 사용) ...
      int checkedCount = 0;
      foreach (var kv in elements)
      {
        int eid = kv.Key;
        if (eid == sourceEleID) continue;
        if (!TryGetElementSegment(nodes, elements, eid, out var p1, out var p2)) continue;

        double rMinX = Math.Min(origin.X, origin.X + dir.X * maxDist) - tol;
        double rMaxX = Math.Max(origin.X, origin.X + dir.X * maxDist) + tol;
        double rMinY = Math.Min(origin.Y, origin.Y + dir.Y * maxDist) - tol;
        double rMaxY = Math.Max(origin.Y, origin.Y + dir.Y * maxDist) + tol;
        double rMinZ = Math.Min(origin.Z, origin.Z + dir.Z * maxDist) - tol;
        double rMaxZ = Math.Max(origin.Z, origin.Z + dir.Z * maxDist) + tol;

        double eMinX = Math.Min(p1.X, p2.X) - tol;
        double eMaxX = Math.Max(p1.X, p2.X) + tol;
        double eMinY = Math.Min(p1.Y, p2.Y) - tol;
        double eMaxY = Math.Max(p1.Y, p2.Y) + tol;
        double eMinZ = Math.Min(p1.Z, p2.Z) - tol;
        double eMaxZ = Math.Max(p1.Z, p2.Z) + tol;

        if (rMaxX < eMinX || rMinX > eMaxX || rMaxY < eMinY || rMinY > eMaxY || rMaxZ < eMinZ || rMinZ > eMaxZ) continue;

        checkedCount++;
        var segVec = Point3dUtils.SubToVector(p2, p1);
        var segUnit = Vector3dUtils.Normalize(segVec);
        var w0 = Point3dUtils.SubToVector(origin, p1);

        double a = 1.0; double b = Vector3dUtils.Dot(dir, segUnit); double c = 1.0;
        double d = Vector3dUtils.Dot(dir, w0); double e = Vector3dUtils.Dot(segUnit, w0);
        double denom = a * c - b * b;

        if (denom < 1e-9) { log($"    E{eid}: Parallel (Skipped)"); continue; }

        double t = (b * e - c * d) / denom;
        double u = (a * e - b * d) / denom;

        Vector3D rayMove = new Vector3D(dir.X * t, dir.Y * t, dir.Z * t);
        var P_ray = Point3dUtils.Move(origin, rayMove);
        Vector3D segMove = new Vector3D(segUnit.X * u, segUnit.Y * u, segUnit.Z * u);
        var P_seg = Point3dUtils.Move(p1, segMove);
        double skewDist = Point3dUtils.Dist(P_ray, P_seg);

        string reason = "";
        if (t < 0) reason += "[Behind] ";
        if (t > maxDist) reason += $"[Too Far: {t:F1} > {maxDist:F1}] ";

        double segLen = Vector3dUtils.Magnitude(segVec);
        if (u < -tol || u > segLen + tol) reason += $"[Miss Segment: u={u:F1}/Len={segLen:F1}] ";
        if (skewDist > tol) reason += $"[Skew Too Large: {skewDist:F2} > {tol}] ";

        if (reason == "") log($"    [SUCCESS CANDIDATE] E{eid}: Dist={t:F2}, Skew={skewDist:F4}");
        else if (t > 0 && t < maxDist * 1.5) log($"    [FAIL REASON] E{eid}: Dist={t:F2}, Skew={skewDist:F4} -> {reason}");
      }
      log($"    (Diagnosed {checkedCount} intersection candidates)");
    }

    private static int FindConnectedElementID(Elements elements, int nodeID)
    {
      foreach (var kv in elements) { if (kv.Value.NodeIDs.Contains(nodeID)) return kv.Key; }
      return -1;
    }

    private static bool TryGetElementSegment(Nodes nodes, Elements elements, int eid, out Point3D p1, out Point3D p2)
    {
      p1 = default; p2 = default;
      if (!elements.Contains(eid)) return false;
      var e = elements[eid];
      if (e.NodeIDs.Count < 2) return false;
      if (!nodes.Contains(e.NodeIDs[0]) || !nodes.Contains(e.NodeIDs[1])) return false;
      p1 = nodes[e.NodeIDs[0]]; p2 = nodes[e.NodeIDs[1]];
      return true;
    }

    private static bool TryGetRaySegmentIntersection(
        Point3D rayOrigin, Vector3D rayDir, double maxDist,
        Point3D segP1, Point3D segP2, double tolerance,
        out Point3D hitPoint, out double distFromOrigin)
    {
      hitPoint = default; distFromOrigin = double.MaxValue;
      var segVec = Point3dUtils.SubToVector(segP2, segP1);
      double segLen = Vector3dUtils.Magnitude(segVec);
      if (segLen < 1e-9) return false;

      var segUnit = Vector3dUtils.Normalize(segVec);
      var w0 = Point3dUtils.SubToVector(rayOrigin, segP1);

      double a = 1.0;
      double b = Vector3dUtils.Dot(rayDir, segUnit);
      double c = 1.0;
      double d = Vector3dUtils.Dot(rayDir, w0);
      double e = Vector3dUtils.Dot(segUnit, w0);

      double denom = a * c - b * b;
      if (denom < 1e-9) return false;

      double t = (b * e - c * d) / denom;
      double u = (a * e - b * d) / denom;

      if (t < 0.0 || t > maxDist) return false;
      if (u < -tolerance || u > segLen + tolerance) return false;

      Vector3D rayMove = new Vector3D(rayDir.X * t, rayDir.Y * t, rayDir.Z * t);
      var P_ray = Point3dUtils.Move(rayOrigin, rayMove);
      Vector3D segMove = new Vector3D(segUnit.X * u, segUnit.Y * u, segUnit.Z * u);
      var P_seg = Point3dUtils.Move(segP1, segMove);
      double dist = Point3dUtils.Dist(P_ray, P_seg);

      if (dist <= tolerance) { hitPoint = P_seg; distFromOrigin = t; return true; }
      return false;
    }

    private class IntersectionHit { public int TargetEleID; public Point3D Point; public double Distance; public IntersectionHit(int id, Point3D p, double d) { TargetEleID = id; Point = p; Distance = d; } }
    private class ExtensionAction { public int FreeNodeID; public int SourceEleID; public int TargetEleID; public Point3D HitPoint; public ExtensionAction(int fn, int se, int te, Point3D p) { FreeNodeID = fn; SourceEleID = se; TargetEleID = te; HitPoint = p; } }

    private class LocalSpatialHash
    {
      private readonly double _cell;
      private readonly double _inflate;
      private readonly Dictionary<(int, int, int), List<int>> _map = new();

      public LocalSpatialHash(Elements elements, Nodes nodes, double cellSize, double inflate)
      {
        _cell = Math.Max(cellSize, 1e-5);
        _inflate = Math.Max(inflate, 0);
        foreach (var kv in elements)
        {
          int eid = kv.Key; var e = kv.Value;
          if (e.NodeIDs.Count < 2) continue;
          if (!nodes.Contains(e.NodeIDs[0]) || !nodes.Contains(e.NodeIDs[1])) continue;
          var p1 = nodes[e.NodeIDs[0]]; var p2 = nodes[e.NodeIDs[1]];
          var bb = BoundingBox.FromSegment(p1, p2, _inflate);
          foreach (var key in CoveredCells(bb)) { if (!_map.TryGetValue(key, out var list)) { list = new List<int>(); _map[key] = list; } list.Add(eid); }
        }
      }
      public HashSet<int> Query(BoundingBox queryBox)
      {
        var result = new HashSet<int>();
        int cellCount = 0;
        foreach (var key in CoveredCells(queryBox))
        {
          cellCount++; if (cellCount > 1000) break;
          if (_map.TryGetValue(key, out var list)) { foreach (var id in list) result.Add(id); }
        }
        return result;
      }
      private IEnumerable<(int, int, int)> CoveredCells(BoundingBox bb)
      {
        var (ix0, iy0, iz0) = Key(bb.Min); var (ix1, iy1, iz1) = Key(bb.Max);
        int x0 = Math.Min(ix0, ix1), x1 = Math.Max(ix0, ix1); int y0 = Math.Min(iy0, iy1), y1 = Math.Max(iy0, iy1); int z0 = Math.Min(iz0, iz1), z1 = Math.Max(iz0, iz1);
        for (int ix = x0; ix <= x1; ix++) for (int iy = y0; iy <= y1; iy++) for (int iz = z0; iz <= z1; iz++) yield return (ix, iy, iz);
      }
      private (int, int, int) Key(Point3D p) { return ((int)Math.Floor(p.X / _cell), (int)Math.Floor(p.Y / _cell), (int)Math.Floor(p.Z / _cell)); }
    }

    private struct BoundingBox
    {
      public Point3D Min, Max;
      public static BoundingBox FromSegment(Point3D a, Point3D b, double padding)
      {
        return new BoundingBox { Min = new Point3D(Math.Min(a.X, b.X) - padding, Math.Min(a.Y, b.Y) - padding, Math.Min(a.Z, b.Z) - padding), Max = new Point3D(Math.Max(a.X, b.X) + padding, Math.Max(a.Y, b.Y) + padding, Math.Max(a.Z, b.Z) + padding) };
      }
    }
  }
}
