using MooringFitting2026.Extensions;       // AddWithID, AddNew 등
using MooringFitting2026.Model.Entities;
using MooringFitting2026.Model.Geometry; // Point3D, BoundingBox (표준 사용)
using MooringFitting2026.Utils;           // ProjectionUtils, DistanceUtils
using MooringFitting2026.Utils.Geometry;
using System;
using System.Collections.Generic;
using System.Linq;

namespace MooringFitting2026.Modifier.ElementModifier
{
  public static class ElementSplitByExistingNodesModifier
  {
    public sealed record Options(
        double DistanceTol = 0.5,       // 점-선 거리 허용치
        double ParamTol = 1e-9,         // 투영 파라미터 경계 허용
        double MergeTolAlong = 0.05,    // 같은 위치 병합 톨러런스
        double MinSegLenTol = 1e-6,     // 최소 세그먼트 길이
        double GridCellSize = 5.0,      // SpatialHash 셀 크기
        bool SnapNodeToLine = false,    // 노드 스냅 여부
        bool ReuseOriginalIdForFirst = true,
        bool DryRun = false,
        bool Debug = false,
        int MaxPrintElements = 50,
        int MaxPrintNodesPerElement = 10
    );

    public sealed record Result(
        int ElementsScanned,
        int ElementsNeedSplit,
        int ElementsActuallySplit,
        int ElementsRemoved,
        int ElementsAdded
    );

    // ===== Public Entry =====
    public static Result Run(FeModelContext context, Options? opt = null, Action<string>? log = null)
    {
      opt ??= new Options();
      log ??= Console.WriteLine;

      var nodes = context.Nodes;

      // 1) 후보 빠르게 찾기 위한 Spatial Hash
      var grid = new SpatialHash(nodes, opt.GridCellSize);

      // 2) 탐지 (Find Candidates)
      var (scanned, candidates) = FindSplitCandidates(context, grid, opt, log);

      if (opt.DryRun)
      {
        PrintCandidatesSummary(candidates, opt, log);
        return new Result(scanned, candidates.Count, 0, 0, 0);
      }

      // 3) 적용 (Apply Split)
      var (splitCount, removed, added) = ApplySplit(context, candidates, opt, log);

      return new Result(scanned, candidates.Count, splitCount, removed, added);
    }

    private static (int scanned, Dictionary<int, List<int>> candidates) FindSplitCandidates(
        FeModelContext context, SpatialHash grid, Options opt, Action<string> log)
    {
      var nodes = context.Nodes;
      var elements = context.Elements;
      int scanned = 0;
      var candidates = new Dictionary<int, List<int>>();

      // 변경 대비 Snapshot
      var elementIds = elements.Keys.ToList();

      foreach (var eid in elementIds)
      {
        if (!elements.Contains(eid)) continue;
        scanned++;

        var e = elements[eid];
        if (e.NodeIDs == null || e.NodeIDs.Count < 2) continue;

        int nA = e.NodeIDs.First();
        int nB = e.NodeIDs.Last();
        if (!nodes.Contains(nA) || !nodes.Contains(nB)) continue;

        var A = nodes.GetNodeCoordinates(nA);
        var B = nodes.GetNodeCoordinates(nB);

        // 길이 0 체크
        double len = DistanceUtils.GetDistanceBetweenNodes(A, B);
        if (len <= 1e-9) continue;

        // [수정] BoundingBox 생성 (표준 클래스 사용)
        var bbox = CreateBoundingBoxFromSegment(A, B, opt.DistanceTol);

        // SpatialHash로 후보 노드 검색
        var candidateNodeIds = grid.Query(bbox);
        var hits = new List<NodeHit>();

        foreach (var nid in candidateNodeIds)
        {
          if (nid == nA || nid == nB) continue;
          var P = nodes.GetNodeCoordinates(nid);

          // [수정] ProjectionUtils 사용: 스칼라 파라미터(u) 계산
          double u = ProjectionUtils.ProjectPointToScalar(P, A, B);

          // 범위 체크 (0 < u < 1)
          if (u <= 0.0 + opt.ParamTol || u >= 1.0 - opt.ParamTol) continue;

          // [수정] ProjectionUtils 사용: 거리 계산
          // (ProjectPointToInfiniteLine 결과에서 Distance 가져옴)
          var projResult = ProjectionUtils.ProjectPointToInfiniteLine(P, A, B);
          if (projResult.Distance > opt.DistanceTol) continue;

          double s = u * len; // 선분 시작점으로부터의 거리 (정렬용)
          hits.Add(new NodeHit(nid, u, s, projResult.Distance));
        }

        if (hits.Count == 0) continue;

        // 정렬 + 근접 병합
        hits.Sort((x, y) => x.S.CompareTo(y.S));
        var merged = MergeCloseHits(hits, opt.MergeTolAlong);

        // [옵션] 노드 스냅 (좌표 보정)
        if (opt.SnapNodeToLine)
        {
          foreach (var h in merged)
          {
            var P = nodes.GetNodeCoordinates(h.NodeId);
            // [수정] 이미 계산된 투영점 사용 또는 재계산
            var proj = ProjectionUtils.ProjectPointToInfiniteLine(P, A, B).ProjectedPoint;

            // 노드 좌표 업데이트 (AddWithID는 덮어쓰기/수정 역할도 겸한다고 가정)
            nodes.AddWithID(h.NodeId, proj.X, proj.Y, proj.Z);
          }
        }

        var internalNodeIds = merged.Select(h => h.NodeId).ToList();
        if (internalNodeIds.Count > 0)
          candidates[eid] = internalNodeIds;
      }

      return (scanned, candidates);
    }

    // 헬퍼: 세그먼트용 BoundingBox 생성
    private static BoundingBox CreateBoundingBoxFromSegment(Point3D a, Point3D b, double inflate)
    {
      double minX = Math.Min(a.X, b.X) - inflate;
      double minY = Math.Min(a.Y, b.Y) - inflate;
      double minZ = Math.Min(a.Z, b.Z) - inflate;

      double maxX = Math.Max(a.X, b.X) + inflate;
      double maxY = Math.Max(a.Y, b.Y) + inflate;
      double maxZ = Math.Max(a.Z, b.Z) + inflate;

      return new BoundingBox(new Point3D(minX, minY, minZ), new Point3D(maxX, maxY, maxZ));
    }

    private static List<NodeHit> MergeCloseHits(List<NodeHit> hits, double mergeTolAlong)
    {
      if (hits.Count == 0) return hits;
      var merged = new List<NodeHit>();
      NodeHit cur = hits[0];
      merged.Add(cur);

      for (int i = 1; i < hits.Count; i++)
      {
        var h = hits[i];
        // s(거리) 기준으로 가까우면 병합 -> 더 선에 가까운(Dist 작은) 놈을 선택
        if (Math.Abs(h.S - cur.S) <= mergeTolAlong)
        {
          if (h.Dist < cur.Dist)
          {
            cur = h;
            merged[merged.Count - 1] = cur;
          }
          continue;
        }
        cur = h;
        merged.Add(cur);
      }
      return merged;
    }

    // ===== 2) Apply Split =====
    private static (int splitCount, int removed, int added) ApplySplit(
        FeModelContext context, Dictionary<int, List<int>> candidates, Options opt, Action<string> log)
    {
      var nodes = context.Nodes;
      var elements = context.Elements;
      int splitCount = 0, removed = 0, added = 0;

      var targetElementIds = candidates.Keys.ToList();

      foreach (var eid in targetElementIds)
      {
        if (!elements.Contains(eid)) continue;
        var e = elements[eid];
        if (e.NodeIDs == null || e.NodeIDs.Count < 2) continue;

        int nA = e.NodeIDs.First();
        int nB = e.NodeIDs.Last();
        var A = nodes.GetNodeCoordinates(nA);
        var B = nodes.GetNodeCoordinates(nB);

        // 내부 노드 정렬 (u 기준)
        var internalNodeIds = candidates[eid];
        var ordered = internalNodeIds
            .Select(nid =>
            {
              var P = nodes.GetNodeCoordinates(nid);
              double u = ProjectionUtils.ProjectPointToScalar(P, A, B);
              return (nid, u);
            })
            .OrderBy(x => x.u)
            .Select(x => x.nid)
            .ToList();

        // 체인 생성
        var chain = new List<int>(ordered.Count + 2);
        chain.Add(nA);
        chain.AddRange(ordered);
        chain.Add(nB);

        // 세그먼트 생성
        var segs = new List<(int n1, int n2)>();
        for (int i = 0; i < chain.Count - 1; i++)
        {
          int n1 = chain[i];
          int n2 = chain[i + 1];
          if (n1 == n2) continue;

          var p1 = nodes.GetNodeCoordinates(n1);
          var p2 = nodes.GetNodeCoordinates(n2);

          if (DistanceUtils.GetDistanceBetweenNodes(p1, p2) < opt.MinSegLenTol) continue;

          segs.Add((n1, n2));
        }

        if (segs.Count == 0)
        {
          elements.Remove(eid);
          removed++;
          continue;
        }

        var extra = (e.ExtraData != null) ? e.ExtraData.ToDictionary(k => k.Key, v => v.Value) : null;

        if (opt.ReuseOriginalIdForFirst)
        {
          elements.AddWithID(eid, new List<int> { segs[0].n1, segs[0].n2 }, e.PropertyID, extra);
          for (int i = 1; i < segs.Count; i++)
          {
            int newId = elements.AddNew(new List<int> { segs[i].n1, segs[i].n2 }, e.PropertyID, extra);
            added++;
            if (opt.Debug) log($"[Split] E{eid} -> AddNew E{newId} (N{segs[i].n1},N{segs[i].n2})");
          }
        }
        else
        {
          elements.Remove(eid);
          removed++;
          for (int i = 0; i < segs.Count; i++)
          {
            int newId = elements.AddNew(new List<int> { segs[i].n1, segs[i].n2 }, e.PropertyID, extra);
            added++;
            if (opt.Debug) log($"[Split] Remove E{eid} -> AddNew E{newId} (N{segs[i].n1},N{segs[i].n2})");
          }
        }
        splitCount++;
      }
      return (splitCount, removed, added);
    }

    // ===== Utils =====
    private static void PrintCandidatesSummary(Dictionary<int, List<int>> candidates, Options opt, Action<string> log)
    {
      log($"[DryRun] ElementsNeedSplit = {candidates.Count}");
      int shown = 0;
      foreach (var kv in candidates.OrderBy(k => k.Key))
      {
        if (shown >= opt.MaxPrintElements) { log($"[DryRun] ... max {opt.MaxPrintElements}"); break; }
        var preview = kv.Value.Take(opt.MaxPrintNodesPerElement);
        log($" E{kv.Key}: internalNodes[{kv.Value.Count}] = {string.Join(",", preview)}");
        shown++;
      }
    }

    // 내부 전용 구조체 (NodeHit는 여기서만 쓰이므로 유지)
    private readonly struct NodeHit
    {
      public readonly int NodeId;
      public readonly double U;
      public readonly double S;
      public readonly double Dist;
      public NodeHit(int nodeId, double u, double s, double dist)
      {
        NodeId = nodeId; U = u; S = s; Dist = dist;
      }
    }

    // SpatialHash: 내부 로직은 유지하되 표준 BoundingBox 사용으로 변경
    private sealed class SpatialHash
    {
      private readonly double _cell;
      private readonly Dictionary<(int, int, int), List<int>> _map = new();

      public SpatialHash(Nodes nodes, double cellSize)
      {
        _cell = Math.Max(cellSize, 1e-9);
        foreach (var kv in nodes)
        {
          int nid = kv.Key;
          // nodes[nid] 대신 GetNodeCoordinates 사용 권장 (Point3D 반환 가정)
          var p = nodes.GetNodeCoordinates(nid);
          var key = Key(p);
          if (!_map.TryGetValue(key, out var list))
          {
            list = new List<int>();
            _map[key] = list;
          }
          list.Add(nid);
        }
      }

      public HashSet<int> Query(BoundingBox bbox)
      {
        var result = new HashSet<int>();
        var (ix0, iy0, iz0) = Key(bbox.Min);
        var (ix1, iy1, iz1) = Key(bbox.Max);

        for (int ix = Math.Min(ix0, ix1); ix <= Math.Max(ix0, ix1); ix++)
          for (int iy = Math.Min(iy0, iy1); iy <= Math.Max(iy0, iy1); iy++)
            for (int iz = Math.Min(iz0, iz1); iz <= Math.Max(iz0, iz1); iz++)
            {
              if (_map.TryGetValue((ix, iy, iz), out var list))
              {
                foreach (var nid in list) result.Add(nid);
              }
            }
        return result;
      }

      private (int, int, int) Key(Point3D p)
      {
        int ix = (int)Math.Floor(p.X / _cell);
        int iy = (int)Math.Floor(p.Y / _cell);
        int iz = (int)Math.Floor(p.Z / _cell);
        return (ix, iy, iz);
      }
    }
  }
}
