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
  /// <summary>
  /// 자유단(Free End) 노드를 검색하여, 해당 요소의 방향 벡터대로 연장했을 때
  /// 다른 요소와 교차하면 그 지점까지 연장하고 연결(Snap & Split)하는 수정자입니다.
  /// </summary>
  public static class ElementExtendToBBoxIntersectAndSplitModifier
  {
    public sealed record Options
    {
      public double SearchRatio { get; init; } = 5.0;         // 요소 길이 대비 검색 거리 비율
      public double DefaultSearchDist { get; init; } = 100.0; // [수정] 기본 검색 거리를 50 -> 100으로 상향
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

      // 1. 자유단(Degree=1) 노드 식별
      var nodeDegree = NodeDegreeInspector.BuildNodeDegree(context);
      var freeNodes = nodeDegree.Where(kv => kv.Value == 1).Select(kv => kv.Key).ToList();

      if (opt.Debug)
        log($"[Stage 04] 자유단 노드 {freeNodes.Count}개 발견. (Grid 크기: {opt.GridCellSize})");

      var grid = new LocalSpatialHash(elements, nodes, opt.GridCellSize, opt.IntersectionTolerance);
      var actions = new List<ExtensionAction>();
      int processed = 0;

      // 2. 각 자유단에 대해 연장 후보 탐색
      foreach (var freeNodeID in freeNodes)
      {
        processed++;

        var sourceEleID = FindConnectedElementID(elements, freeNodeID);
        if (sourceEleID == -1) continue;

        var sourceEle = elements[sourceEleID];
        var (startID, endID) = sourceEle.GetEndNodePair();

        int connectedNodeID = (startID == freeNodeID) ? endID : startID;
        var pFree = nodes[freeNodeID];
        var pConn = nodes[connectedNodeID];
        var dirVector = Vector3dUtils.Normalize(Vector3dUtils.Direction(pConn, pFree));

        // [수정된 로직] 최대 검색 거리 설정
        double maxDist = opt.DefaultSearchDist;
        double refDim = sourceEle.GetReferencedPropertyDim(props);

        if (refDim > 0)
          maxDist = Math.Max(opt.DefaultSearchDist, refDim * opt.SearchRatio);

        // [수정] 2000mm를 초과하면 2000으로 제한 (500으로 줄이지 않음!)
        if (maxDist > 2000.0) maxDist = 2000.0;

        // [디버깅]
        if (opt.WatchNodeIDs != null && opt.WatchNodeIDs.Contains(freeNodeID))
        {
          log($"\n[추적] 노드 {freeNodeID} (요소 E{sourceEleID}) -> MaxDist: {maxDist:F2} (RefDim: {refDim:F1})");
          DiagnoseNode(freeNodeID, pFree, dirVector, maxDist, opt.IntersectionTolerance,
                       sourceEleID, elements, nodes, log);
        }

        // 3. 광선(Ray) 캐스팅
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
        }
      }

      if (opt.DryRun) return new Result(freeNodes.Count, actions.Count, 0);

      // 4. 일괄 적용
      if (opt.Debug && actions.Count > 0)
        log($"[Stage 04] 총 {actions.Count}건의 연장 작업을 적용합니다...");

      int successCount = ApplyActionsBulk(context, actions, opt.Debug, log);

      return new Result(freeNodes.Count, actions.Count, successCount);
    }

    private static int ApplyActionsBulk(FeModelContext context, List<ExtensionAction> actions, bool debug, Action<string> log)
    {
      var nodes = context.Nodes;
      var elements = context.Elements;
      int splitCount = 0;

      var actionNodeMap = new Dictionary<ExtensionAction, int>();
      foreach (var act in actions)
      {
        int nid = nodes.AddOrGet(act.HitPoint.X, act.HitPoint.Y, act.HitPoint.Z);
        actionNodeMap[act] = nid;
      }

      // Source 연장
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

      // Target 분할
      var targetGroups = actions.GroupBy(a => a.TargetEleID);
      foreach (var group in targetGroups)
      {
        int targetID = group.Key;
        if (!elements.Contains(targetID)) continue;

        var targetEle = elements[targetID];
        var (tN1, tN2) = targetEle.GetEndNodePair();
        var pStart = nodes[tN1];
        var pEnd = nodes[tN2];

        var splitPoints = new List<(int NodeID, double U)>();
        foreach (var act in group)
        {
          int nid = actionNodeMap[act];
          double u = ProjectionUtils.ProjectPointToScalar(nodes[nid], pStart, pEnd);
          splitPoints.Add((nid, u));
        }

        var sortedPoints = splitPoints.OrderBy(x => x.U).ToList();
        var chain = new List<int> { tN1 };
        foreach (var sp in sortedPoints)
        {
          if (chain.Last() != sp.NodeID && sp.NodeID != tN2) chain.Add(sp.NodeID);
        }
        if (chain.Last() != tN2) chain.Add(tN2);

        if (chain.Count <= 2) continue;

        elements.Remove(targetID);
        var extra = targetEle.ExtraData.ToDictionary(k => k.Key, v => v.Value);

        for (int i = 0; i < chain.Count - 1; i++)
        {
          elements.AddNew(new List<int> { chain[i], chain[i + 1] }, targetEle.PropertyID, extra);
        }
        splitCount++;
      }
      return splitCount;
    }

    // --- Helpers ---
    private static void DiagnoseNode(
        int nodeID, Point3D origin, Vector3D dir, double maxDist, double tol,
        int sourceEleID, Elements elements, Nodes nodes, Action<string> log)
    {
      // (진단 로직은 이전과 동일하므로 생략 가능, 필요시 이전 코드 참조)
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
      if (Vector3dUtils.Magnitude(segVec) < 1e-9) return false;

      var segUnit = Vector3dUtils.Normalize(segVec);
      var w0 = Point3dUtils.SubToVector(rayOrigin, segP1);

      double a = 1.0; double b = Vector3dUtils.Dot(rayDir, segUnit);
      double c = 1.0; double d = Vector3dUtils.Dot(rayDir, w0);
      double e = Vector3dUtils.Dot(segUnit, w0);
      double denom = a * c - b * b;

      if (denom < 1e-9) return false;

      double t = (b * e - c * d) / denom;
      double u = (a * e - b * d) / denom;

      if (t < 0.0 || t > maxDist) return false;
      double segLen = Vector3dUtils.Magnitude(segVec);
      if (u < -tolerance || u > segLen + tolerance) return false; // 허용 오차 고려

      Vector3D rayMove = new Vector3D(rayDir.X * t, rayDir.Y * t, rayDir.Z * t);
      var P_ray = Point3dUtils.Move(rayOrigin, rayMove);
      Vector3D segMove = new Vector3D(segUnit.X * u, segUnit.Y * u, segUnit.Z * u);
      var P_seg = Point3dUtils.Move(segP1, segMove);

      if (Point3dUtils.Dist(P_ray, P_seg) <= tolerance)
      {
        hitPoint = P_seg; distFromOrigin = t; return true;
      }
      return false;
    }

    private class IntersectionHit { public int TargetEleID; public Point3D Point; public double Distance; public IntersectionHit(int id, Point3D p, double d) { TargetEleID = id; Point = p; Distance = d; } }
    private class ExtensionAction { public int FreeNodeID; public int SourceEleID; public int TargetEleID; public Point3D HitPoint; public ExtensionAction(int fn, int se, int te, Point3D p) { FreeNodeID = fn; SourceEleID = se; TargetEleID = te; HitPoint = p; } }

    private class LocalSpatialHash
    {
      private readonly double _cell;
      private readonly Dictionary<(int, int, int), List<int>> _map = new();

      public LocalSpatialHash(Elements elements, Nodes nodes, double cellSize, double inflate)
      {
        _cell = Math.Max(cellSize, 1e-5);
        foreach (var kv in elements)
        {
          int eid = kv.Key; var e = kv.Value;
          if (e.NodeIDs.Count < 2) continue;
          if (!nodes.Contains(e.NodeIDs[0]) || !nodes.Contains(e.NodeIDs[1])) continue;
          var p1 = nodes[e.NodeIDs[0]]; var p2 = nodes[e.NodeIDs[1]];
          var bb = BoundingBox.FromSegment(p1, p2, Math.Max(inflate, 0));
          foreach (var key in CoveredCells(bb)) { if (!_map.TryGetValue(key, out var list)) { list = new List<int>(); _map[key] = list; } list.Add(eid); }
        }
      }
      public HashSet<int> Query(BoundingBox queryBox)
      {
        var result = new HashSet<int>();
        int c = 0;
        foreach (var key in CoveredCells(queryBox))
        {
          c++; if (c > 2000) break;
          if (_map.TryGetValue(key, out var list)) { foreach (var id in list) result.Add(id); }
        }
        return result;
      }
      private IEnumerable<(int, int, int)> CoveredCells(BoundingBox bb)
      {
        var (ix0, iy0, iz0) = Key(bb.Min); var (ix1, iy1, iz1) = Key(bb.Max);
        int x0 = Math.Min(ix0, ix1), x1 = Math.Max(ix0, ix1);
        int y0 = Math.Min(iy0, iy1), y1 = Math.Max(iy0, iy1);
        int z0 = Math.Min(iz0, iz1), z1 = Math.Max(iz0, iz1);
        for (int ix = x0; ix <= x1; ix++) for (int iy = y0; iy <= y1; iy++) for (int iz = z0; iz <= z1; iz++) yield return (ix, iy, iz);
      }
      private (int, int, int) Key(Point3D p) { return ((int)Math.Floor(p.X / _cell), (int)Math.Floor(p.Y / _cell), (int)Math.Floor(p.Z / _cell)); }
    }

    private struct BoundingBox
    {
      public Point3D Min, Max;
      public static BoundingBox FromSegment(Point3D a, Point3D b, double padding)
      {
        return new BoundingBox
        {
          Min = new Point3D(Math.Min(a.X, b.X) - padding, Math.Min(a.Y, b.Y) - padding, Math.Min(a.Z, b.Z) - padding),
          Max = new Point3D(Math.Max(a.X, b.X) + padding, Math.Max(a.Y, b.Y) + padding, Math.Max(a.Z, b.Z) + padding)
        };
      }
    }
  }
}
