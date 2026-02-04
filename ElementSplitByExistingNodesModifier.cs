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
  /// <summary>
  /// 기존에 존재하는 노드(Node)가 요소(Element)의 경로 위에 있을 경우,
  /// 해당 노드를 기준으로 요소를 분할(Split)하는 수정자(Modifier)입니다.
  /// </summary>
  public static class ElementSplitByExistingNodesModifier
  {
    /// <summary>
    /// 실행 옵션 정의
    /// </summary>
    public sealed record Options(
        double DistanceTol = 0.5,       // 점-선 거리 허용치 (이 거리 이내면 선 위의 점으로 간주)
        double ParamTol = 1e-9,         // 선분 양 끝점 제외를 위한 파라미터 여유값
        double MergeTolAlong = 0.05,    // 선분 방향으로 매우 가까운 노드들을 하나로 병합할 허용치
        double MinSegLenTol = 1e-6,     // 분할 후 생성되는 세그먼트의 최소 길이 (너무 짧으면 생성 안 함)
        double GridCellSize = 5.0,      // 검색 속도 향상을 위한 SpatialHash 셀 크기
        bool SnapNodeToLine = false,    // true일 경우, 노드 좌표를 직선상으로 강제 이동(Snap) 시킴
        bool ReuseOriginalIdForFirst = true, // 첫 번째 분할 조각에 기존 Element ID를 재사용할지 여부
        bool DryRun = false,            // true일 경우 실제 분할은 하지 않고 로그만 출력
        bool Debug = false,             // 상세 디버그 로그 출력 여부
        int MaxPrintElements = 50,      // 디버그 시 출력할 최대 요소 개수
        int MaxPrintNodesPerElement = 10 // 요소당 출력할 내부 노드 개수
    );

    public sealed record Result(
        int ElementsScanned,      // 검사한 총 요소 수
        int ElementsNeedSplit,    // 분할이 필요한 요소 수 (후보)
        int ElementsActuallySplit,// 실제로 분할 수행된 요소 수
        int ElementsRemoved,      // 삭제된 원본 요소 수
        int ElementsAdded         // 새로 생성된 요소 수
    );

    // =================================================================
    // Public Entry Point
    // =================================================================

    /// <summary>
    /// 수정자 실행 메인 메서드입니다.
    /// </summary>
    public static Result Run(FeModelContext context, Options? opt = null, Action<string>? log = null)
    {
      opt ??= new Options();
      log ??= Console.WriteLine;

      var nodes = context.Nodes;

      // 1) Spatial Hash 구축 (노드 검색 가속화)
      //    모든 노드를 그리드에 매핑하여, 요소 주변의 노드를 빠르게 찾습니다.
      var grid = new SpatialHash(nodes, opt.GridCellSize);

      // 2) 분할 후보 탐색 (Find Candidates)
      var (scanned, candidates) = FindSplitCandidates(context, grid, opt, log);

      // DryRun 모드면 여기서 종료
      if (opt.DryRun)
      {
        PrintCandidatesSummary(candidates, opt, log);
        return new Result(scanned, candidates.Count, 0, 0, 0);
      }

      // 3) 분할 적용 (Apply Split)
      var (splitCount, removed, added) = ApplySplit(context, candidates, opt, log);

      return new Result(scanned, candidates.Count, splitCount, removed, added);
    }

    // =================================================================
    // Internal Logic
    // =================================================================

    private static (int scanned, Dictionary<int, List<int>> candidates) FindSplitCandidates(
        FeModelContext context, SpatialHash grid, Options opt, Action<string> log)
    {
      var nodes = context.Nodes;
      var elements = context.Elements;
      int scanned = 0;
      var candidates = new Dictionary<int, List<int>>();

      // 변경 중 컬렉션 오류 방지를 위해 ID 리스트 스냅샷 생성
      var elementIds = elements.Keys.ToList();

      foreach (var eid in elementIds)
      {
        if (!elements.Contains(eid)) continue;
        scanned++;

        var e = elements[eid];
        if (e.NodeIDs == null || e.NodeIDs.Count < 2) continue;

        int nA = e.NodeIDs.First();
        int nB = e.NodeIDs.Last();

        // 양 끝 노드가 유효한지 확인
        if (!nodes.Contains(nA) || !nodes.Contains(nB)) continue;

        var A = nodes.GetNodeCoordinates(nA);
        var B = nodes.GetNodeCoordinates(nB);

        // 요소 길이 검사 (0인 경우 스킵)
        double len = DistanceUtils.GetDistanceBetweenNodes(A, B);
        if (len <= 1e-9) continue;

        // [최적화] 해당 요소를 감싸는 BoundingBox 생성
        var bbox = CreateBoundingBoxFromSegment(A, B, opt.DistanceTol);

        // Grid를 통해 주변 노드 후보 검색
        var candidateNodeIds = grid.Query(bbox);
        var hits = new List<NodeHit>();

        foreach (var nid in candidateNodeIds)
        {
          // 자기 자신의 양 끝점은 제외
          if (nid == nA || nid == nB) continue;

          var P = nodes.GetNodeCoordinates(nid);

          // P가 선분 AB 위에 있는지 투영(Projection)하여 확인
          var projResult = ProjectionUtils.ProjectPointToInfiniteLine(P, A, B);
          double u = projResult.T; // 매개변수 t (0.0 ~ 1.0)

          // 1. 범위 체크 (양 끝점 근처 제외)
          if (u <= 0.0 + opt.ParamTol || u >= 1.0 - opt.ParamTol) continue;

          // 2. 거리 체크 (직선과의 거리가 허용오차 이내인지)
          if (projResult.Distance > opt.DistanceTol) continue;

          double s = u * len; // 시작점으로부터의 실제 거리
          hits.Add(new NodeHit(nid, u, s, projResult.Distance));
        }

        if (hits.Count == 0) continue;

        // 시작점 기준 정렬 및 근접 노드 병합
        hits.Sort((x, y) => x.S.CompareTo(y.S));
        var merged = MergeCloseHits(hits, opt.MergeTolAlong);

        // [옵션] 노드 스냅: 노드를 정확히 직선 위로 이동
        if (opt.SnapNodeToLine)
        {
          foreach (var h in merged)
          {
            var P = nodes.GetNodeCoordinates(h.NodeId);
            var proj = ProjectionUtils.ProjectPointToInfiniteLine(P, A, B).ProjectedPoint;

            // 좌표 업데이트 (덮어쓰기)
            nodes.AddWithID(h.NodeId, proj.X, proj.Y, proj.Z);
          }
        }

        var internalNodeIds = merged.Select(h => h.NodeId).ToList();
        if (internalNodeIds.Count > 0)
          candidates[eid] = internalNodeIds;
      }

      return (scanned, candidates);
    }

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

        // 유효성 재확인
        if (e.NodeIDs == null || e.NodeIDs.Count < 2) continue;

        int nA = e.NodeIDs.First();
        int nB = e.NodeIDs.Last();
        var A = nodes.GetNodeCoordinates(nA);
        var B = nodes.GetNodeCoordinates(nB);

        // 분할 점들을 매개변수 u 기준으로 정렬
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

        // 연결 체인 생성: [Start] -> [Mid1] -> [Mid2] -> ... -> [End]
        var chain = new List<int>(ordered.Count + 2);
        chain.Add(nA);
        chain.AddRange(ordered);
        chain.Add(nB);

        // 세그먼트(요소) 생성 준비
        var segs = new List<(int n1, int n2)>();
        for (int i = 0; i < chain.Count - 1; i++)
        {
          int n1 = chain[i];
          int n2 = chain[i + 1];
          if (n1 == n2) continue; // 동일 노드 방어

          var p1 = nodes.GetNodeCoordinates(n1);
          var p2 = nodes.GetNodeCoordinates(n2);

          // 너무 짧은 요소 생성 방지
          if (DistanceUtils.GetDistanceBetweenNodes(p1, p2) < opt.MinSegLenTol) continue;

          segs.Add((n1, n2));
        }

        // 생성된 세그먼트가 없으면 원본 삭제만 수행
        if (segs.Count == 0)
        {
          elements.Remove(eid);
          removed++;
          continue;
        }

        // 속성 복사
        var extra = (e.ExtraData != null) ? e.ExtraData.ToDictionary(k => k.Key, v => v.Value) : null;

        if (opt.ReuseOriginalIdForFirst)
        {
          // 첫 번째 조각은 기존 ID 재사용 (덮어쓰기)
          elements.AddWithID(eid, new List<int> { segs[0].n1, segs[0].n2 }, e.PropertyID, extra);

          // 나머지 조각은 신규 생성
          for (int i = 1; i < segs.Count; i++)
          {
            int newId = elements.AddNew(new List<int> { segs[i].n1, segs[i].n2 }, e.PropertyID, extra);
            added++;
            if (opt.Debug)
              log($"   -> [추가] E{newId} 생성 (노드: {segs[i].n1}-{segs[i].n2})");
          }
          if (opt.Debug)
            log($"[분할 완료] E{eid} 유지 및 분할됨. (총 {segs.Count}개 조각)");
        }
        else
        {
          // 기존 ID 삭제 후 모두 신규 생성
          elements.Remove(eid);
          removed++;
          for (int i = 0; i < segs.Count; i++)
          {
            int newId = elements.AddNew(new List<int> { segs[i].n1, segs[i].n2 }, e.PropertyID, extra);
            added++;
            if (opt.Debug)
              log($"   -> [신규] E{newId} 생성 (노드: {segs[i].n1}-{segs[i].n2})");
          }
          if (opt.Debug)
            log($"[분할 완료] 원본 E{eid} 삭제 후 {segs.Count}개로 재생성.");
        }
        splitCount++;
      }
      return (splitCount, removed, added);
    }

    // =================================================================
    // Helper Methods & Classes
    // =================================================================

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

    /// <summary>
    /// 선분 방향으로 매우 가까운 노드들은 하나로 병합 (가장 가까운 놈 선택)
    /// </summary>
    private static List<NodeHit> MergeCloseHits(List<NodeHit> hits, double mergeTolAlong)
    {
      if (hits.Count == 0) return hits;
      var merged = new List<NodeHit>();
      NodeHit cur = hits[0];
      merged.Add(cur);

      for (int i = 1; i < hits.Count; i++)
      {
        var h = hits[i];
        // 거리차이가 허용치 이내라면 병합
        if (Math.Abs(h.S - cur.S) <= mergeTolAlong)
        {
          // 직선에 더 가까운(Dist가 작은) 노드를 우선시
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

    private static void PrintCandidatesSummary(Dictionary<int, List<int>> candidates, Options opt, Action<string> log)
    {
      log($"[DryRun] 분할 대상 요소 수: {candidates.Count}");
      int shown = 0;
      foreach (var kv in candidates.OrderBy(k => k.Key))
      {
        if (shown >= opt.MaxPrintElements) { log($"[DryRun] ... (최대 {opt.MaxPrintElements}개까지만 표시)"); break; }
        var preview = kv.Value.Take(opt.MaxPrintNodesPerElement);
        log($" - E{kv.Key}: 분할 예정 노드[{kv.Value.Count}] -> {string.Join(",", preview)}");
        shown++;
      }
    }

    private readonly struct NodeHit
    {
      public readonly int NodeId;
      public readonly double U;    // 비율 (0~1)
      public readonly double S;    // 시작점으로부터 거리
      public readonly double Dist; // 직선과의 수직 거리
      public NodeHit(int nodeId, double u, double s, double dist)
      {
        NodeId = nodeId; U = u; S = s; Dist = dist;
      }
    }

    // 내부 전용 SpatialHash (단순화 버전)
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
