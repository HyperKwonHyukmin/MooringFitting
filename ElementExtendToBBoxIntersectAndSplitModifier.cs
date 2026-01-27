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
      /// <summary>
      /// 검색 거리 계수 (Prop.Dim[2] * Ratio)
      /// </summary>
      public double SearchRatio { get; init; } = 2.0;

      /// <summary>
      /// Prop 정보가 없거나 0일 때 사용할 기본 검색 거리
      /// </summary>
      public double DefaultSearchDist { get; init; } = 50.0;

      /// <summary>
      /// 직선 교차 판정 허용 오차 (3D 공간상 빗나감 허용치)
      /// </summary>
      public double IntersectionTolerance { get; init; } = 1.0;

      public double GridCellSize { get; init; } = 500.0;
      public bool DryRun { get; init; } = false;
      public bool Debug { get; init; } = false;
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

      // 1. 자유단(Degree=1) 노드 찾기
      var nodeDegree = NodeDegreeInspector.BuildNodeDegree(context);
      var freeNodes = nodeDegree.Where(kv => kv.Value == 1).Select(kv => kv.Key).ToList();

      if (opt.Debug) log($"[Stage 04] Found {freeNodes.Count} free-end nodes.");

      // 2. 검색 가속을 위한 공간 분할 (Target Elements)
      //    (이미 있는 ElementSpatialHash 활용 권장, 없으면 로컬 구현)
      //    여기서는 Stage 03에서 쓴 ElementSpatialHash를 재사용한다고 가정
      //    GridCellSize는 검색 범위보다 충분히 커야 함
      var grid = new ElementSpatialHash(elements, nodes, opt.GridCellSize, opt.IntersectionTolerance);

      // 변경 사항 저장소 (즉시 변경 시 반복문 문제 발생)
      var actions = new List<ExtensionAction>();

      foreach (var freeNodeID in freeNodes)
      {
        // 3. Source Element 정보 획득
        var sourceEleID = FindConnectedElementID(elements, freeNodeID);
        if (sourceEleID == -1) continue;

        var sourceEle = elements[sourceEleID];
        var (startID, endID) = sourceEle.GetEndNodePair();

        // 방향 벡터 계산 ( ConnectedNode -> FreeNode 방향이 '확장' 방향 )
        int connectedNodeID = (startID == freeNodeID) ? endID : startID;

        var pFree = nodes[freeNodeID];
        var pConn = nodes[connectedNodeID];
        var dirVector = Vector3dUtils.Normalize(Vector3dUtils.Direction(pConn, pFree));

        // 4. 검색 거리 산정 (Dim[2] 기반)
        double maxDist = opt.DefaultSearchDist;
        if (props.Contains(sourceEle.PropertyID))
        {
          var prop = props[sourceEle.PropertyID];
          // Dim[2]가 존재하면 사용 (I형강 폭 등), 없으면 기본값
          if (prop.Dim != null && prop.Dim.Count > 2)
          {
            maxDist = prop.Dim[2] * opt.SearchRatio;
          }
          // T형강 등 다른 케이스 처리 필요시 추가 (예: Dim.Max())
          else if (prop.Dim != null && prop.Dim.Count > 0)
          {
            maxDist = prop.Dim.Max() * opt.SearchRatio;
          }
        }

        // 5. 교차 후보 탐색 (Ray Casting)
        // Ray: pFree + t * dirVector (0 < t <= maxDist)
        // BoundingBox for Ray
        var rayEnd = Point3dUtils.Add(pFree, Point3dUtils.Mul(dirVector, maxDist));
        var searchBox = BoundingBox.FromSegment(pFree, rayEnd, opt.IntersectionTolerance);

        // Grid Query
        var candidates = grid.Query(searchBox.Min, searchBox.Max);

        // 최적 교차점 찾기
        IntersectionHit bestHit = null;

        foreach (var targetEleID in candidates)
        {
          if (targetEleID == sourceEleID) continue; // 자기 자신 제외

          if (!TryGetElementSegment(nodes, elements, targetEleID, out var tP1, out var tP2))
            continue;

          // Ray-Segment Intersection Check
          // Source: pFree, Dir: dirVector, MaxDist: maxDist
          // Target: tP1 - tP2
          if (TryGetRaySegmentIntersection(
              pFree, dirVector, maxDist,
              tP1, tP2, opt.IntersectionTolerance,
              out var hitPoint, out var distFromFreeNode))
          {
            // 가장 가까운 교차점 선택
            if (bestHit == null || distFromFreeNode < bestHit.Distance)
            {
              bestHit = new IntersectionHit(targetEleID, hitPoint, distFromFreeNode);
            }
          }
        }

        if (bestHit != null)
        {
          actions.Add(new ExtensionAction(freeNodeID, sourceEleID, bestHit.TargetEleID, bestHit.Point));
          if (opt.Debug) log($"  -> Candidate: Node {freeNodeID} extends to E{bestHit.TargetEleID} (Dist: {bestHit.Distance:F2})");
        }
      }

      if (opt.DryRun)
      {
        return new Result(freeNodes.Count, actions.Count, 0);
      }

      // 6. 변경 사항 적용 (Apply)
      int successCount = ApplyActions(context, actions, log);

      return new Result(freeNodes.Count, actions.Count, successCount);
    }

    // --- Helpers ---

    private static int FindConnectedElementID(Elements elements, int nodeID)
    {
      foreach (var kv in elements)
      {
        if (kv.Value.NodeIDs.Contains(nodeID)) return kv.Key;
      }
      return -1;
    }

    private static bool TryGetElementSegment(Nodes nodes, Elements elements, int eid, out Point3D p1, out Point3D p2)
    {
      p1 = default; p2 = default;
      if (!elements.Contains(eid)) return false;
      var e = elements[eid];
      if (e.NodeIDs.Count < 2) return false;
      if (!nodes.Contains(e.NodeIDs[0]) || !nodes.Contains(e.NodeIDs[1])) return false;

      p1 = nodes[e.NodeIDs[0]];
      p2 = nodes[e.NodeIDs[1]];
      return true;
    }

    /// <summary>
    /// 3D 공간에서 Ray와 선분(Segment)의 근접 교차점을 찾습니다.
    /// </summary>
    private static bool TryGetRaySegmentIntersection(
        Point3D rayOrigin, Vector3D rayDir, double maxDist,
        Point3D segP1, Point3D segP2, double tolerance,
        out Point3D hitPoint, out double distFromOrigin)
    {
      hitPoint = default;
      distFromOrigin = double.MaxValue;

      var segVec = Point3dUtils.SubToVector(segP2, segP1);
      // 길이 계산
      double segLen = Vector3dUtils.Magnitude(segVec);
      if (segLen < 1e-9) return false;
      // [수정 2] 단위 벡터 (Vector3D)
      var segUnit = Vector3dUtils.Normalize(segVec);

      // [수정 3] w0 = rayOrigin - segP1 를 Vector3D로 계산
      var w0 = Point3dUtils.SubToVector(rayOrigin, segP1);

      // 선분 벡터
      var segDir = Point3dUtils.Sub(segP2, segP1);
      if (segLen < 1e-9) return false;


      // 두 직선 사이의 최단 거리 계산 (Skew Lines Distance)
      // V_ray = rayDir, V_seg = segUnit
      // P_ray(t) = rayOrigin + t * rayDir
      // P_seg(u) = segP1 + u * segUnit
  
      double a = Vector3dUtils.Dot(rayDir, rayDir); // always 1 if normalized
      double b = Vector3dUtils.Dot(rayDir, segUnit);
      double c = Vector3dUtils.Dot(segUnit, segUnit); // always 1
      double d = Vector3dUtils.Dot(rayDir, (Vector3D)w0); // Point3D -> Vector3D cast logic needed or wrapper
      double e = Vector3dUtils.Dot(segUnit, (Vector3D)w0);

      double denom = a * c - b * b;

      // 평행 체크
      if (denom < 1e-9) return false;

      // Parameters for closest points
      double t = (b * e - c * d) / denom;
      double u = (a * e - b * d) / denom;

      // 1. Ray 범위 체크 (Forward direction & within MaxDist)
      //    t는 rayOrigin으로부터의 거리
      if (t < 0.0 || t > maxDist) return false;

      // 2. Segment 범위 체크 (Between P1 and P2)
      //    u는 segP1으로부터의 거리. 0 <= u <= segLen
      if (u < -tolerance || u > segLen + tolerance) return false;

      // 3. 최단 거리(Skew distance) 체크
      // Closest Points
      Vector3D rayMove = new Vector3D(rayDir.X * t, rayDir.Y * t, rayDir.Z * t);
      var P_ray = Point3dUtils.Move(rayOrigin, rayMove);

      // P_seg = segP1 + segUnit * u
      Vector3D segMove = new Vector3D(segUnit.X * u, segUnit.Y * u, segUnit.Z * u);
      var P_seg = Point3dUtils.Move(segP1, segMove);

      double dist = Point3dUtils.Dist(P_ray, P_seg);

      if (dist <= tolerance)
      {
        // 교차점은 Target Segment 위의 점(P_seg)으로 함 (확장해서 붙여야 하므로)
        hitPoint = P_seg;
        distFromOrigin = t; // 확장해야 할 길이
        return true;
      }

      return false;
    }

    private static int ApplyActions(FeModelContext context, List<ExtensionAction> actions, Action<string> log)
    {
      int count = 0;
      var nodes = context.Nodes;
      var elements = context.Elements;

      // TargetID가 중복될 수 있으므로 처리에 주의 (한 Element가 두 번 쪼개질 수 있음)
      // 여기서는 단순화를 위해 순차 처리하되, 유효성 검사를 수행
      foreach (var act in actions)
      {
        if (!elements.Contains(act.SourceEleID) || !elements.Contains(act.TargetEleID))
          continue;

        // 1. 교차점 노드 생성
        int intersectNodeID = nodes.AddOrGet(act.HitPoint.X, act.HitPoint.Y, act.HitPoint.Z);

        // 2. Source Element 연장 (FreeNode를 IntersectNode로 교체)
        var sourceEle = elements[act.SourceEleID];
        if (sourceEle.TryReplaceNode(act.FreeNodeID, intersectNodeID, out var newSourceEle))
        {
          // 불변 객체 교체 (Remove & Add)
          // (주의: AddWithID는 덮어쓰기 지원하므로 바로 호출 가능)
          elements.AddWithID(act.SourceEleID, newSourceEle.NodeIDs.ToList(), newSourceEle.PropertyID,
              newSourceEle.ExtraData.ToDictionary(k => k.Key, k => k.Value));
        }

        // 3. Target Element 분할 (Target -> N1-Int-N2)
        var targetEle = elements[act.TargetEleID];
        var (tN1, tN2) = targetEle.GetEndNodePair();

        // 이미 교차점이 Target의 끝점과 같다면 분할 불필요 (연결만 됨)
        if (intersectNodeID == tN1 || intersectNodeID == tN2)
        {
          count++;
          continue;
        }

        // 기존 Target 삭제
        elements.Remove(act.TargetEleID);

        // 두 개의 새 Element 생성 (속성 계승)
        var extraMap = targetEle.ExtraData.ToDictionary(k => k.Key, v => v.Value);

        // Seg 1
        elements.AddNew(new List<int> { tN1, intersectNodeID }, targetEle.PropertyID, extraMap);
        // Seg 2
        elements.AddNew(new List<int> { intersectNodeID, tN2 }, targetEle.PropertyID, extraMap);

        count++;
      }
      return count;
    }

    // --- Internal Classes ---
    private class IntersectionHit
    {
      public int TargetEleID { get; }
      public Point3D Point { get; }
      public double Distance { get; }
      public IntersectionHit(int id, Point3D p, double d) { TargetEleID = id; Point = p; Distance = d; }
    }

    private class ExtensionAction
    {
      public int FreeNodeID { get; }
      public int SourceEleID { get; }
      public int TargetEleID { get; }
      public Point3D HitPoint { get; }
      public ExtensionAction(int fn, int se, int te, Point3D p)
      { FreeNodeID = fn; SourceEleID = se; TargetEleID = te; HitPoint = p; }
    }

    // BoundingBox Helper (Local definition if not available elsewhere)
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
