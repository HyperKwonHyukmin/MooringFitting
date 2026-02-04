using MooringFitting2026.Model.Entities;
using MooringFitting2026.Model.Geometry;
using MooringFitting2026.Utils.Geometry;
using System;
using System.Collections.Generic;
using System.Linq;

namespace MooringFitting2026.Modifier.ElementModifier
{
  /// <summary>
  /// 서로 교차하는(X자, T자 등) 요소(Element)들을 찾아 교차점에 노드를 생성하고,
  /// 해당 노드를 기준으로 요소를 분할(Split)하는 수정자입니다.
  /// </summary>
  public static class ElementIntersectionSplitModifier
  {
    public sealed record Options(
        double DistTol = 1.0,                 // 두 선분 사이의 최단 거리가 이 값 이내면 교차로 간주
        double ParamTol = 1e-9,               // 교차점 파라미터(0~1) 경계 오차 허용값
        double GridCellSize = 200.0,          // 검색 가속화를 위한 그리드 셀 크기
        double MinSegLenTol = 1e-6,           // 분할 후 생성될 세그먼트의 최소 길이 (너무 짧으면 무시)
        double MergeTolAlong = 0.05,          // 한 요소 위에서 교차점이 너무 가까우면 하나로 병합
        bool ReuseOriginalIdForFirst = true,  // 분할된 첫 번째 조각에 원본 ID 유지 여부
        bool CreateNodeUsingAddOrGet = true,  // 노드 생성 시 중복 좌표 체크(AddOrGet) 사용 여부
        bool DryRun = false,                  // true일 경우 실제 분할 없이 로그만 출력
        bool Debug = false,                   // 상세 디버그 로그 출력
        int MaxPrint = 50                     // 로그 출력 최대 개수 제한
    );

    public sealed record Result(
        int ElementsScanned,
        int CandidatePairsTested,
        int IntersectionsFound,
        int NodesCreatedOrReused,
        int ElementsNeedSplit,
        int ElementsSplit,
        int ElementsRemoved,
        int ElementsAdded
    );

    /// <summary>
    /// 수정자 실행 진입점
    /// </summary>
    public static Result Run(FeModelContext context, Options? opt = null, Action<string>? log = null)
    {
      opt ??= new Options();
      log ??= Console.WriteLine;

      var nodes = context.Nodes;
      var elements = context.Elements;

      // 1) Spatial Hash 그리드 생성 (교차 후보 고속 탐색용)
      var grid = new ElementSpatialHash(elements, nodes, opt.GridCellSize, opt.DistTol);

      int scanned = 0;
      int pairTested = 0;
      int intersections = 0;
      int nodesCreated = 0;

      // 분할 정보 저장소: ElementID -> [(NodeID, 위치파라미터 u)]
      var splitMap = new Dictionary<int, List<(int nodeId, double u)>>();

      // 컬렉션 변경 방지를 위해 ID 리스트 복사
      var elementIds = elements.Keys.ToList();

      // 중복 검사 방지용 (Pair Set)
      var visited = new HashSet<(int a, int b)>();

      foreach (var eid in elementIds)
      {
        if (!elements.Contains(eid)) continue;
        scanned++;

        if (!TryGetSegment(nodes, elements, eid, out var A0, out var A1, out var aN0, out var aN1))
          continue;

        // 그리드를 통해 인근 후보 요소들만 가져옴
        foreach (var otherId in grid.QueryCandidates(eid))
        {
          if (otherId == eid) continue;
          if (!elements.Contains(otherId)) continue;

          // (A, B) 와 (B, A) 중복 방지
          int a = Math.Min(eid, otherId);
          int b = Math.Max(eid, otherId);
          if (!visited.Add((a, b))) continue;

          if (!TryGetSegment(nodes, elements, otherId, out var B0, out var B1, out var bN0, out var bN1))
            continue;

          // 이미 끝점을 공유하고 있다면(연결됨), 교차 분할 대상 아님
          if (aN0 == bN0 || aN0 == bN1 || aN1 == bN0 || aN1 == bN1)
            continue;

          pairTested++;

          // [중요] 거의 평행한 경우(Overlap 성격)는 Stage 01에서 처리하므로 여기선 건너뜀
          // 이를 통해 중복 요소끼리 서로를 난도질하는 것을 방지함
          if (IsNearlyParallel(A0, A1, B0, B1))
            continue;

          // 세그먼트 간 교차 검사
          if (TrySegmentSegmentIntersection(A0, A1, B0, B1, opt.DistTol, opt.ParamTol,
                out var s, out var t, out var P, out var Q, out var dist))
          {
            intersections++;

            // 교차점 위치 (두 직선 사이 최단 거리의 중점)
            var X = Point3dUtils.Mid(P, Q);

            int nid;
            if (opt.DryRun)
            {
              nid = -1; // 가상 노드
            }
            else
            {
              // 교차점에 노드 생성 (또는 기존 노드 재사용)
              nid = opt.CreateNodeUsingAddOrGet
                  ? nodes.AddOrGet(X.X, X.Y, X.Z)
                  : AddNewNodeById(nodes, X.X, X.Y, X.Z);

              nodesCreated++;
            }

            // 두 요소 모두에게 분할 예약
            // (중복 요소가 있어도 여기서 각각 제3의 요소와 교차 판정되어 분할됨)
            AddSplitPoint(splitMap, eid, nid, s);
            AddSplitPoint(splitMap, otherId, nid, t);

            if (opt.Debug && intersections <= opt.MaxPrint)
              log($"[교차 발견] E{eid} & E{otherId} -> 교차점 N{nid} (s={s:F3}, t={t:F3}, 거리={dist:F4})");
          }
        }
      }

      // 2) 실제 분할 수행 (Apply Split)
      int needSplit = splitMap.Count;
      int splitCount = 0, removed = 0, added = 0;

      if (!opt.DryRun && splitMap.Count > 0)
      {
        var r = ApplySplit(context, splitMap, opt, log);
        splitCount = r.splitCount;
        removed = r.removed;
        added = r.added;
      }

      return new Result(
          ElementsScanned: scanned,
          CandidatePairsTested: pairTested,
          IntersectionsFound: intersections,
          NodesCreatedOrReused: nodesCreated,
          ElementsNeedSplit: needSplit,
          ElementsSplit: splitCount,
          ElementsRemoved: removed,
          ElementsAdded: added
      );
    }

    // ----------------------------
    // Split logic
    // ----------------------------

    private static (int splitCount, int removed, int added) ApplySplit(
        FeModelContext context,
        Dictionary<int, List<(int nodeId, double u)>> splitMap,
        Options opt,
        Action<string> log)
    {
      var nodes = context.Nodes;
      var elements = context.Elements;

      int splitCount = 0;
      int removed = 0;
      int added = 0;

      var targetEids = splitMap.Keys.ToList();

      foreach (var eid in targetEids)
      {
        if (!elements.Contains(eid)) continue;

        var e = elements[eid];
        if (e.NodeIDs == null || e.NodeIDs.Count < 2) continue;

        int n0 = e.NodeIDs.First();
        int n1 = e.NodeIDs.Last();

        if (!nodes.Contains(n0) || !nodes.Contains(n1))
        {
          elements.Remove(eid);
          removed++;
          continue;
        }

        var A = nodes[n0];
        var B = nodes[n1];

        // 교차점 정렬 및 근접점 병합
        var hits = splitMap[eid]
            .Where(x => x.nodeId > 0)
            .OrderBy(x => x.u)
            .ToList();

        // 같은 위치(u)에 여러 교차점이 찍힌 경우 하나로 병합 (중복 요소들이 겹쳐있을 때 중요)
        hits = MergeCloseByU(hits, opt.MergeTolAlong, A, B);

        // 체인 구성: [Start] -> [Split1] -> ... -> [End]
        var chain = new List<int> { n0 };
        foreach (var h in hits)
        {
          // 양 끝점과 중복되면 제외
          if (h.nodeId == n0 || h.nodeId == n1) continue;
          chain.Add(h.nodeId);
        }
        chain.Add(n1);

        // 연속된 중복 노드 ID 제거
        chain = chain.Where((id, idx) => idx == 0 || chain[idx - 1] != id).ToList();

        // 세그먼트 생성
        var segs = new List<(int n1, int n2)>();
        for (int i = 0; i < chain.Count - 1; i++)
        {
          int a = chain[i];
          int b = chain[i + 1];
          if (a == b) continue;

          var p1 = nodes[a];
          var p2 = nodes[b];

          // 너무 짧은 세그먼트는 생성하지 않음
          double len = Point3dUtils.Norm(Point3dUtils.Sub(p2, p1));
          if (len < opt.MinSegLenTol) continue;

          segs.Add((a, b));
        }

        if (segs.Count == 0)
        {
          elements.Remove(eid);
          removed++;
          continue;
        }

        // 속성 복사
        var extraBase = (e.ExtraData == null)
            ? new Dictionary<string, string>()
            : new Dictionary<string, string>(e.ExtraData);

        Dictionary<string, string> CopyExtra() => new Dictionary<string, string>(extraBase);

        if (opt.ReuseOriginalIdForFirst)
        {
          // 기존 ID 덮어쓰기
          elements.Remove(eid);
          elements.AddWithID(eid, new List<int> { segs[0].n1, segs[0].n2 }, e.PropertyID, CopyExtra());

          for (int i = 1; i < segs.Count; i++)
          {
            int newId = elements.AddNew(new List<int> { segs[i].n1, segs[i].n2 }, e.PropertyID, CopyExtra());
            added++;
            if (opt.Debug)
              log($"   -> [추가 생성] E{newId} (노드: {segs[i].n1}-{segs[i].n2})");
          }
          if (opt.Debug)
            log($"[분할 적용] E{eid} 유지 및 분할됨 (총 {segs.Count} 조각).");
        }
        else
        {
          // 원본 삭제 후 모두 신규 생성
          elements.Remove(eid);
          removed++;

          for (int i = 0; i < segs.Count; i++)
          {
            int newId = elements.AddNew(new List<int> { segs[i].n1, segs[i].n2 }, e.PropertyID, CopyExtra());
            added++;
            if (opt.Debug)
              log($"   -> [신규 생성] E{newId} (노드: {segs[i].n1}-{segs[i].n2})");
          }
          if (opt.Debug)
            log($"[분할 적용] 원본 E{eid} 삭제 후 {segs.Count} 조각으로 재생성.");
        }

        splitCount++;
      }

      return (splitCount, removed, added);
    }

    // ----------------------------
    // Helpers
    // ----------------------------

    private static List<(int nodeId, double u)> MergeCloseByU(
        List<(int nodeId, double u)> hits,
        double mergeTolAlong,
        Point3D A,
        Point3D B)
    {
      if (hits.Count <= 1) return hits;

      double abLen = Point3dUtils.Dist(A, B);
      if (abLen < 1e-18) return hits;

      var merged = new List<(int nodeId, double u)>();
      var cur = hits[0];
      merged.Add(cur);

      for (int i = 1; i < hits.Count; i++)
      {
        var h = hits[i];
        double distAlong = Math.Abs(h.u - cur.u) * abLen;

        if (distAlong <= mergeTolAlong) continue;

        cur = h;
        merged.Add(cur);
      }

      return merged;
    }

    private static void AddSplitPoint(
        Dictionary<int, List<(int nodeId, double u)>> map,
        int eid,
        int nodeId,
        double u)
    {
      if (nodeId <= 0) return;

      if (!map.TryGetValue(eid, out var list))
      {
        list = new List<(int nodeId, double u)>();
        map[eid] = list;
      }
      list.Add((nodeId, u));
    }

    private static bool TryGetSegment(
        Nodes nodes, Elements elements, int eid,
        out Point3D A0, out Point3D A1,
        out int n0, out int n1)
    {
      A0 = default!; A1 = default!;
      n0 = -1; n1 = -1;

      if (!elements.Contains(eid)) return false;
      var e = elements[eid];
      if (e.NodeIDs == null || e.NodeIDs.Count < 2) return false;

      n0 = e.NodeIDs.First();
      n1 = e.NodeIDs.Last();
      if (!nodes.Contains(n0) || !nodes.Contains(n1)) return false;

      A0 = nodes[n0];
      A1 = nodes[n1];
      return true;
    }

    /// <summary>
    /// [수정됨] 두 3D 선분 간의 교차 여부 및 파라미터(s, t)를 계산합니다.
    /// (구문 오류를 방지하기 위해 if-else 블록을 명확히 분리함)
    /// </summary>
    private static bool TrySegmentSegmentIntersection(
        Point3D P0, Point3D P1,
        Point3D Q0, Point3D Q1,
        double distTol,
        double paramTol,
        out double s, out double t,
        out Point3D Pc, out Point3D Qc,
        out double dist)
    {
      var u = Point3dUtils.Sub(P1, P0);
      var v = Point3dUtils.Sub(Q1, Q0);
      var w = Point3dUtils.Sub(P0, Q0);

      double a = Point3dUtils.Dot(u, u);
      double b = Point3dUtils.Dot(u, v);
      double c = Point3dUtils.Dot(v, v);
      double d = Point3dUtils.Dot(u, w);
      double e = Point3dUtils.Dot(v, w);

      double D = a * c - b * b;
      double sc, sN, sD = D;
      double tc, tN, tD = D;

      const double EPS = 1e-18;

      if (D < EPS)
      {
        // 평행에 가까움 (교차 아님)
        s = t = dist = 0.0;
        Pc = Qc = default!;
        return false;
      }
      else
      {
        sN = (b * e - c * d);
        tN = (a * e - b * d);

        if (sN < 0.0)
        {
          sN = 0.0;
          tN = e;
          tD = c;
        }
        else if (sN > sD)
        {
          sN = sD;
          tN = e + b;
          tD = c;
        }
      }

      if (tN < 0.0)
      {
        tN = 0.0;
        if (-d < 0.0)
          sN = 0.0;
        else if (-d > a)
          sN = sD;
        else
        {
          sN = -d;
          sD = a;
        }
      }
      else if (tN > tD)
      {
        tN = tD;
        if ((-d + b) < 0.0)
          sN = 0.0;
        else if ((-d + b) > a)
          sN = sD;
        else
        {
          sN = (-d + b);
          sD = a;
        }
      }

      sc = (Math.Abs(sN) < EPS ? 0.0 : sN / sD);
      tc = (Math.Abs(tN) < EPS ? 0.0 : tN / tD);

      Pc = Point3dUtils.Add(P0, Point3dUtils.Mul(u, sc));
      Qc = Point3dUtils.Add(Q0, Point3dUtils.Mul(v, tc));

      dist = Point3dUtils.Norm(Point3dUtils.Sub(Pc, Qc));

      s = sc;
      t = tc;

      // 선분 내부(0~1)에 있는지 확인
      bool inside =
          sc >= 0.0 - paramTol && sc <= 1.0 + paramTol &&
          tc >= 0.0 - paramTol && tc <= 1.0 + paramTol;

      return inside && dist <= distTol;
    }

    private static bool IsNearlyParallel(Point3D A0, Point3D A1, Point3D B0, Point3D B1)
    {
      var a = Point3dUtils.Sub(A1, A0);
      var b = Point3dUtils.Sub(B1, B0);
      double na = Point3dUtils.Norm(a);
      double nb = Point3dUtils.Norm(b);
      if (na < 1e-18 || nb < 1e-18) return false;

      // 외적의 크기가 작으면 평행
      var cx = Point3dUtils.Cross(a, b);
      double sin = Point3dUtils.Norm(cx) / (na * nb);
      return sin < 1e-4;
    }

    private static int AddNewNodeById(Nodes nodes, double x, double y, double z)
    {
      int newId = nodes.LastNodeID + 1;
      nodes.AddWithID(newId, x, y, z);
      return newId;
    }

    // --------------------------------------------------------------------------------
    // Inner Class: Spatial Hash for Segments
    // --------------------------------------------------------------------------------
    public sealed class ElementSpatialHash
    {
      private readonly double _cell;
      private readonly double _inflate;
      private readonly Dictionary<(int, int, int), List<int>> _map = new();
      private readonly Dictionary<int, BoundingBox> _bbox = new();

      public ElementSpatialHash(Elements elements, Nodes nodes, double cellSize, double inflate)
      {
        _cell = Math.Max(cellSize, 1e-9);
        _inflate = Math.Max(inflate, 0);

        var ids = elements.Keys.ToList();
        foreach (var eid in ids)
        {
          if (!elements.Contains(eid)) continue;

          if (!TryGetSegment(nodes, elements, eid, out var a, out var b))
            continue;

          var bb = BoundingBox.FromSegment(a, b, _inflate);
          _bbox[eid] = bb;

          foreach (var key in CoveredCells(bb))
          {
            if (!_map.TryGetValue(key, out var list))
            {
              list = new List<int>();
              _map[key] = list;
            }
            list.Add(eid);
          }
        }
      }

      public IEnumerable<int> QueryCandidates(int eid)
      {
        if (!_bbox.TryGetValue(eid, out var bb))
          return Enumerable.Empty<int>();

        var set = new HashSet<int>();
        foreach (var key in CoveredCells(bb))
        {
          if (_map.TryGetValue(key, out var list))
          {
            for (int i = 0; i < list.Count; i++)
              set.Add(list[i]);
          }
        }
        return set;
      }

      private IEnumerable<(int, int, int)> CoveredCells(BoundingBox bb)
      {
        var (ix0, iy0, iz0) = Key(bb.Min);
        var (ix1, iy1, iz1) = Key(bb.Max);

        int x0 = Math.Min(ix0, ix1), x1 = Math.Max(ix0, ix1);
        int y0 = Math.Min(iy0, iy1), y1 = Math.Max(iy0, iy1);
        int z0 = Math.Min(iz0, iz1), z1 = Math.Max(iz0, iz1);

        for (int ix = x0; ix <= x1; ix++)
          for (int iy = y0; iy <= y1; iy++)
            for (int iz = z0; iz <= z1; iz++)
              yield return (ix, iy, iz);
      }

      private (int, int, int) Key(Point3D p)
      {
        return ((int)Math.Floor(p.X / _cell), (int)Math.Floor(p.Y / _cell), (int)Math.Floor(p.Z / _cell));
      }

      private bool TryGetSegment(Nodes nodes, Elements elements, int eid, out Point3D a, out Point3D b)
      {
        a = default!; b = default!;
        if (!elements.Contains(eid)) return false;
        var e = elements[eid];
        if (e.NodeIDs == null || e.NodeIDs.Count < 2) return false;
        int n0 = e.NodeIDs.First(); int n1 = e.NodeIDs.Last();
        if (!nodes.Contains(n0) || !nodes.Contains(n1)) return false;
        a = nodes[n0]; b = nodes[n1];
        return true;
      }
    }

    private readonly struct BoundingBox
    {
      public readonly Point3D Min;
      public readonly Point3D Max;

      public BoundingBox(Point3D min, Point3D max)
      {
        Min = min; Max = max;
      }

      public static BoundingBox FromSegment(Point3D a, Point3D b, double inflate)
      {
        double minX = Math.Min(a.X, b.X) - inflate;
        double minY = Math.Min(a.Y, b.Y) - inflate;
        double minZ = Math.Min(a.Z, b.Z) - inflate;

        double maxX = Math.Max(a.X, b.X) + inflate;
        double maxY = Math.Max(a.Y, b.Y) + inflate;
        double maxZ = Math.Max(a.Z, b.Z) + inflate;

        return new BoundingBox(new Point3D(minX, minY, minZ), new Point3D(maxX, maxY, maxZ));
      }
    }
  }
}
