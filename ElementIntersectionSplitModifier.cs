using MooringFitting2026.Model.Entities;
using MooringFitting2026.Model.Geometry;
using MooringFitting2026.Utils.Geometry;
using System;
using System.Collections.Generic;
using System.Linq;

namespace MooringFitting2026.Modifier.ElementModifier
{
  /// <summary>
  /// 교차하는 Element(선분)를 찾아 교차점에 Node를 생성하고,
  /// 그 Node를 기준으로 교차하는 Element들을 모두 split 한다.
  ///
  /// - DryRun=true  : 교차 후보/교차점 개수만 출력(Inspector 모드)
  /// - DryRun=false : 실제 Node 생성 + split 수행(Modifier 모드)
  /// </summary>
  public static class ElementIntersectionSplitModifier
  {
    public sealed record Options(
      double DistTol = 1.0,                 // 선분-선분 최단거리 <= DistTol 이면 교차로 판정
      double ParamTol = 1e-9,               // s,t가 [0,1] 판정 경계 오차
      double GridCellSize = 200.0,          // 요소 그리드 셀 크기(모델 스케일에 맞춰 크게 잡는 편이 보통 좋음)
      double MinSegLenTol = 1e-6,           // 너무 짧은 세그먼트 제거
      double MergeTolAlong = 0.05,          // 같은 위치(거의 같은 교차점) 병합용(선분 방향거리 기준)
      bool ReuseOriginalIdForFirst = true,  // 첫 세그먼트에 원래 EID 유지
      bool CreateNodeUsingAddOrGet = true,  // true: nodes.AddOrGet 사용(프로젝트 정책 따름)
      bool DryRun = false,
      bool Debug = false,
      int MaxPrint = 50
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

    public static Result Run(FeModelContext context, Options? opt = null, Action<string>? log = null)
    {
      opt ??= new Options();
      log ??= Console.WriteLine;

      var nodes = context.Nodes;
      var elements = context.Elements;

      // 1) Element grid 생성(빠른 후보 탐색)
      var grid = new ElementSpatialHash(elements, nodes, opt.GridCellSize, opt.DistTol);

      int scanned = 0;
      int pairTested = 0;
      int intersections = 0;
      int nodesCreated = 0;

      // 교차점 저장: elementId -> [(nodeId, u)]
      // u는 element 선분의 파라메터(0~1), split 시 정렬에 사용
      var splitMap = new Dictionary<int, List<(int nodeId, double u)>>();

      var elementIds = elements.Keys.ToList();
      var visited = new HashSet<(int a, int b)>();

      foreach (var eid in elementIds)
      {
        if (!elements.Contains(eid)) continue;
        scanned++;

        if (!TryGetSegment(nodes, elements, eid, out var A0, out var A1, out var aN0, out var aN1))
          continue;

        foreach (var otherId in grid.QueryCandidates(eid))
        {
          if (otherId == eid) continue;
          if (!elements.Contains(otherId)) continue;

          int a = Math.Min(eid, otherId);
          int b = Math.Max(eid, otherId);
          if (!visited.Add((a, b))) continue;

          if (!TryGetSegment(nodes, elements, otherId, out var B0, out var B1, out var bN0, out var bN1))
            continue;

          // 같은 노드 공유하는 경우(연결)면 교차로 볼 필요 없음
          if (aN0 == bN0 || aN0 == bN1 || aN1 == bN0 || aN1 == bN1)
            continue;

          pairTested++;

          // ✅ 치명 수정: 거의 평행한 경우(가까워도 overlap 성격) => 이 단계에서는 제외(모델 난도질 방지)
          if (IsNearlyParallel(A0, A1, B0, B1))
            continue;

          if (TrySegmentSegmentIntersection(A0, A1, B0, B1, opt.DistTol, opt.ParamTol,
                out var s, out var t, out var P, out var Q, out var dist))
          {
            intersections++;

            // 교차점 위치(두 최단점의 중간점)
            var X = Point3dUtils.Mid(P, Q);

            // 노드 생성/재사용
            int nid;
            if (opt.DryRun)
            {
              nid = -1;
            }
            else
            {
              nid = opt.CreateNodeUsingAddOrGet
                ? nodes.AddOrGet(X.X, X.Y, X.Z)
                : AddNewNodeById(nodes, X.X, X.Y, X.Z);

              nodesCreated++;
            }

            // eid와 otherId 각각에 대해 split 포인트 추가
            AddSplitPoint(splitMap, eid, nid, s);
            AddSplitPoint(splitMap, otherId, nid, t);

            if (opt.Debug && intersections <= opt.MaxPrint)
              log($"[Intersect] E{eid} & E{otherId} -> N{nid} (s={s:F6}, t={t:F6}, dist={dist:F4})");
          }
        }
      }

      // 2) split 수행(또는 DryRun이면 통계만)
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

    // nodes/element를 수정하지 않고 splitMap만 적용(테스트/파이프라인용)
    public static (int splitCount, int removed, int added) ApplySplitOnly(
      FeModelContext context,
      Dictionary<int, List<(int nodeId, double u)>> splitMap,
      Options? opt = null,
      Action<string>? log = null)
    {
      opt ??= new Options();
      log ??= Console.WriteLine;

      // DryRun이면 split을 하면 안되니 방어
      if (opt.DryRun) return (0, 0, 0);

      return ApplySplit(context, splitMap, opt, log);
    }

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

      // split 대상 elementId를 고정(수정 중 컬렉션 변경 방지)
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

        // hits 정렬 + u 근접 병합
        var hits = splitMap[eid]
          .Where(x => x.nodeId > 0)                 // ✅ 방어(혹시라도 잘못 들어온 값 제거)
          .OrderBy(x => x.u)
          .ToList();

        hits = MergeCloseByU(hits, opt.MergeTolAlong, A, B);

        // (기존 양 끝 노드 포함) chain 구성
        var chain = new List<int>();
        chain.Add(n0);
        foreach (var h in hits)
        {
          if (h.nodeId == n0 || h.nodeId == n1) continue;
          chain.Add(h.nodeId);
        }
        chain.Add(n1);

        // 연속 중복 제거
        chain = chain.Where((id, idx) => idx == 0 || chain[idx - 1] != id).ToList();

        // chain을 인접 쌍으로 seg list 생성
        var segs = new List<(int n1, int n2)>();
        for (int i = 0; i < chain.Count - 1; i++)
        {
          int a = chain[i];
          int b = chain[i + 1];
          if (a == b) continue;

          var p1 = nodes[a];
          var p2 = nodes[b];
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

        // ✅ 치명 수정: ExtraData null 방어 + 타입 호환(=Dictionary로 강제)
        var extraBase = (e.ExtraData == null)
          ? new Dictionary<string, string>()
          : new Dictionary<string, string>(e.ExtraData);

        Dictionary<string, string> CopyExtra() => new Dictionary<string, string>(extraBase);

        if (opt.ReuseOriginalIdForFirst)
        {
          // ✅ 치명 수정: AddWithID 키중복 예외 방지 위해 기존 eid를 제거 후 재추가(Replace 효과)
          elements.Remove(eid);

          elements.AddWithID(eid, new List<int> { segs[0].n1, segs[0].n2 }, e.PropertyID, CopyExtra());

          for (int i = 1; i < segs.Count; i++)
          {
            int newId = elements.AddNew(new List<int> { segs[i].n1, segs[i].n2 }, e.PropertyID, CopyExtra());
            added++;
            if (opt.Debug) log($"[Split] E{eid} -> AddNew E{newId} N[{segs[i].n1},{segs[i].n2}]");
          }
        }
        else
        {
          elements.Remove(eid);
          removed++;

          for (int i = 0; i < segs.Count; i++)
          {
            int newId = elements.AddNew(new List<int> { segs[i].n1, segs[i].n2 }, e.PropertyID, CopyExtra());
            added++;
            if (opt.Debug) log($"[Split] Remove E{eid} -> AddNew E{newId} N[{segs[i].n1},{segs[i].n2}]");
          }
        }

        splitCount++;
      }

      return (splitCount, removed, added);
    }

    private static List<(int nodeId, double u)> MergeCloseByU(
      List<(int nodeId, double u)> hits,
      double mergeTolAlong,
      Point3D A,
      Point3D B)
    {
      if (hits.Count <= 1) return hits;

      // u -> 선분거리 s 로 변환해서 병합(mergeTolAlong는 “선분 방향 거리” 단위로 쓰는게 직관적)
      var AB = Point3dUtils.Sub(B, A);
      double abLen = Point3dUtils.Norm(AB);
      if (abLen < 1e-18) return hits;

      var list = hits
        .OrderBy(h => h.u)
        .ToList();

      var merged = new List<(int nodeId, double u)>();
      var cur = list[0];
      merged.Add(cur);

      for (int i = 1; i < list.Count; i++)
      {
        var h = list[i];
        double distAlong = Math.Abs(h.u - cur.u) * abLen;

        if (distAlong <= mergeTolAlong)
        {
          // 같은 교차점으로 간주: 먼저 들어온 nodeId 유지(정책)
          continue;
        }

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
      // ✅ 치명 방어: nodeId가 0/음수면 split 대상에 넣지 않음
      if (nodeId <= 0) return;

      if (!map.TryGetValue(eid, out var list))
      {
        list = new List<(int nodeId, double u)>();
        map[eid] = list;
      }
      list.Add((nodeId, u));
    }

    // ----------------------------
    // Element segment helpers
    // ----------------------------
    private static bool TryGetSegment(
      Nodes nodes, Elements elements, int eid,
      out Point3D A0, out Point3D A1,
      out int n0, out int n1)
    {
      A0 = default!;
      A1 = default!;
      n0 = -1;
      n1 = -1;

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

    private static bool TrySegmentSegmentIntersection(
      Point3D P0, Point3D P1,
      Point3D Q0, Point3D Q1,
      double distTol,
      double paramTol,
      out double s, out double t,
      out Point3D Pc, out Point3D Qc,
      out double dist)
    {
      // Algorithm: closest points of two segments in 3D
      // returns s,t in [0,1] if closest points inside segments
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

      // compute the line parameters of the two closest points
      if (D < EPS)
      {
        // almost parallel
        sN = 0.0;
        sD = 1.0;
        tN = e;
        tD = c;
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
        if (-d < 0.0) sN = 0.0;
        else if (-d > a) sN = sD;
        else { sN = -d; sD = a; }
      }
      else if (tN > tD)
      {
        tN = tD;
        if ((-d + b) < 0.0) sN = 0.0;
        else if ((-d + b) > a) sN = sD;
        else { sN = (-d + b); sD = a; }
      }

      sc = (Math.Abs(sN) < EPS ? 0.0 : sN / sD);
      tc = (Math.Abs(tN) < EPS ? 0.0 : tN / tD);

      Pc = Point3dUtils.Add(P0, Point3dUtils.Mul(u, sc));
      Qc = Point3dUtils.Add(Q0, Point3dUtils.Mul(v, tc));

      dist = Point3dUtils.Norm(Point3dUtils.Sub(Pc, Qc));

      s = sc;
      t = tc;

      // 세그먼트 내부인지 + 거리 조건
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

      // |sin(theta)| = |a x b| / (|a||b|)
      var cx = Point3dUtils.Cross(a, b);
      double sin = Point3dUtils.Norm(cx) / (na * nb);
      return sin < 1e-4; // 거의 평행
    }

    private static int AddNewNodeById(Nodes nodes, double x, double y, double z)
    {
      int newId = nodes.LastNodeID + 1;
      nodes.AddWithID(newId, x, y, z);
      return newId;
    }
  }

  /// <summary>
  /// Element 후보를 빠르게 찾기 위한 간단한 3D Spatial Hash.
  /// 각 element(선분)의 bounding box( inflate 포함 )가 커버하는 grid cell에 eid를 넣고,
  /// Query 시 동일 cell들에 들어있는 후보 eid들을 반환한다.
  /// </summary>
  public sealed class ElementSpatialHash
  {
    private readonly double _cell;
    private readonly double _inflate;
    private readonly Dictionary<(int ix, int iy, int iz), List<int>> _map = new();
    private readonly Dictionary<int, BoundingBox> _bbox = new();

    public ElementSpatialHash(Elements elements, Nodes nodes, double cellSize, double inflate)
    {
      _cell = Math.Max(cellSize, 1e-9);
      _inflate = Math.Max(inflate, 0);

      // ✅ 치명 수정: ctor 미완성/중괄호 깨짐 제거 + 인덱싱 로직 완성
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

    private bool TryGetSegment(Nodes nodes, Elements elements, int eid, out Point3D a, out Point3D b)
    {
      a = default!;
      b = default!;

      if (!elements.Contains(eid)) return false;
      var e = elements[eid];
      if (e.NodeIDs == null || e.NodeIDs.Count < 2) return false;

      int n0 = e.NodeIDs.First();
      int n1 = e.NodeIDs.Last();
      if (!nodes.Contains(n0) || !nodes.Contains(n1)) return false;

      a = nodes[n0];
      b = nodes[n1];
      return true;
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
      int ix = (int)Math.Floor(p.X / _cell);
      int iy = (int)Math.Floor(p.Y / _cell);
      int iz = (int)Math.Floor(p.Z / _cell);
      return (ix, iy, iz);
    }

    private readonly struct BoundingBox
    {
      public readonly Point3D Min;
      public readonly Point3D Max;

      public BoundingBox(Point3D min, Point3D max)
      {
        Min = min;
        Max = max;
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
