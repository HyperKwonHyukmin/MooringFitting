using MooringFitting2026.Model.Entities;
using MooringFitting2026.Model.Geometry;
using MooringFitting2026.Utils.Geometry;
using MooringFitting2026.Utils;
using System;
using System.Collections.Generic;
using System.Linq;

namespace MooringFitting2026.Inspector.ElementInspector
{
  /// <summary>
  /// 요소(Element)들의 공선(Collinear) 및 중복(Overlap) 관계를 분석하여 그룹화합니다.
  /// </summary>
  public static class ElementCollinearOverlapGroupInspector
  {
    /// <summary>
    /// 공선성(같은 직선 위)과 중복(겹침)을 기준으로 병합되어야 할 Element 그룹들을 찾습니다.
    /// </summary>
    public static List<HashSet<int>> FindSegmentationGroups(
        FeModelContext context,
        double angleToleranceRad,
        double distanceTolerance)
    {
      // 1. 공선 그룹(Collinear Groups) 찾기: 방향이 같고 일직선상에 있는 요소끼리 1차 묶음
      var collinearGroups = ElementCollinearityInspector.FindCollinearGroups(
          context, angleToleranceRad, distanceTolerance);

      // 2. 중복 요소(Overlap Elements) 찾기: 공선 그룹 내에서 실제로 겹치는 구간이 있는 요소 쌍 찾기
      var overlaps = ElementOverlapInspector.FindOverlaps(context, collinearGroups);

      // 3. Union-Find 알고리즘으로 중복된 요소들을 하나의 그룹으로 최종 병합
      // (A와 B가 겹치고, B와 C가 겹치면 -> A, B, C는 한 그룹)
      var distinctIds = overlaps
          .SelectMany(p => new[] { p.ElementA, p.ElementB })
          .Distinct()
          .ToList();

      if (distinctIds.Count == 0)
        return new List<HashSet<int>>();

      var uf = new UnionFind(distinctIds);
      foreach (var (a, b) in overlaps)
      {
        uf.Union(a, b);
      }

      // 4. 그룹화 결과 반환
      var groups = new Dictionary<int, HashSet<int>>();
      foreach (var id in distinctIds)
      {
        int root = uf.Find(id);
        if (!groups.TryGetValue(root, out var set))
          groups[root] = set = new HashSet<int>();
        set.Add(id);
      }

      // 요소가 2개 이상인 그룹만 유효하므로 필터링하여 반환
      return groups.Values
          .Where(g => g.Count >= 2)
          .ToList();
    }
  }
}

/// <summary>
/// 내부 클래스 1: 공선성(Collinearity) 검사기
/// </summary>
public static class ElementCollinearityInspector
  {
    public static List<List<int>> FindCollinearGroups(
        FeModelContext context,
        double angleToleranceRad,
        double distanceTolerance)
    {
      var elements = context.Elements.ToList();
      if (elements.Count == 0) return new List<List<int>>();

      var elementIds = elements.Select(e => e.Key);
      var uf = new UnionFind(elementIds);

      // 모든 쌍 비교 (O(N^2))
      for (int i = 0; i < elements.Count; i++)
      {
        var (id1, e1) = elements[i];
        var (p1a, p1b) = GetEndPoints(context, e1);

        // Vector3dUtils 사용 (신형 구조체 대응)
        var dir1 = Vector3dUtils.Direction(p1a, p1b);

        for (int j = i + 1; j < elements.Count; j++)
        {
          var (id2, e2) = elements[j];
          var (p2a, p2b) = GetEndPoints(context, e2);
          var dir2 = Vector3dUtils.Direction(p2a, p2b);

          // [조건 1] 방향 평행 여부 (Parallel Check)
          if (!DistanceUtils.IsParallel(dir1, dir2, angleToleranceRad))
            continue;

          // [조건 2] 직선 일치 여부 (Collinear Check)
          // ProjectPointToInfiniteLine 사용 (직선 연장선까지 고려)
          if (!IsOnSameLine(p1a, p1b, p2a, distanceTolerance))
            continue;

          // 공선 관계 -> Union
          uf.Union(id1, id2);
        }
      }

      var groups = new Dictionary<int, List<int>>();
      foreach (var id in elementIds)
      {
        int root = uf.Find(id);
        if (!groups.ContainsKey(root)) groups[root] = new List<int>();
        groups[root].Add(id);
      }

      return groups.Values.Where(g => g.Count > 1).ToList();
    }

    private static (Point3D A, Point3D B) GetEndPoints(FeModelContext context, Element element)
    {
      // context.Nodes가 신형 Point3D를 반환한다고 가정
      return (
          context.Nodes.GetNodeCoordinates(element.NodeIDs[0]),
          context.Nodes.GetNodeCoordinates(element.NodeIDs[1])
      );
    }

    private static bool IsOnSameLine(Point3D a, Point3D b, Point3D p, double tol)
    {
      // [중요 수정] ProjectPointToSegment 대신 InfiniteLine 사용
      // 선분 밖이라도 같은 직선상에 있으면 OK여야 하므로.
      var result = ProjectionUtils.ProjectPointToInfiniteLine(p, a, b);
      return result.Distance < tol;
    }
  }

  /// <summary>
  /// 내부 클래스 2: 중복(Overlap) 검사기
  /// </summary>
  public static class ElementOverlapInspector
  {
    public static List<(int ElementA, int ElementB)> FindOverlaps(
        FeModelContext context,
        List<List<int>> collinearGroups)
    {
      // 모델 크기 기반 허용 오차 설정
      double Lref = SizeUtils.GetModelSize(context.Nodes);
      double distTol = 2e-3 * Lref;

      var overlaps = new List<(int, int)>();

      foreach (var group in collinearGroups)
      {
        if (group.Count < 2) continue;

        for (int i = 0; i < group.Count; i++)
        {
          int id1 = group[i];
          var e1 = context.Elements[id1];
          var (a1, b1) = GetEndPoints(context, e1);

          for (int j = i + 1; j < group.Count; j++)
          {
            int id2 = group[j];
            var e2 = context.Elements[id2];
            var (a2, b2) = GetEndPoints(context, e2);

            if (IsSegmentOverlap(a1, b1, a2, b2, distTol))
              overlaps.Add((id1, id2));
          }
        }
      }
      return overlaps;
    }

    private static (Point3D A, Point3D B) GetEndPoints(FeModelContext context, Element element)
    {
      return (
          context.Nodes.GetNodeCoordinates(element.NodeIDs[0]),
          context.Nodes.GetNodeCoordinates(element.NodeIDs[1])
      );
    }

    private static bool IsSegmentOverlap(Point3D a1, Point3D b1, Point3D a2, Point3D b2, double tol)
    {
      // 스칼라 투영을 이용한 1D 구간 겹침 검사
      double t2a = ProjectionUtils.ProjectPointToScalar(a2, a1, b1);
      double t2b = ProjectionUtils.ProjectPointToScalar(b2, a1, b1);

      double min2 = Math.Min(t2a, t2b);
      double max2 = Math.Max(t2a, t2b);

      // 기준 선분 [0, 1]과 비교 선분 [min2, max2]의 교집합
      double overlapStart = Math.Max(0.0, min2);
      double overlapEnd = Math.Min(1.0, max2);

      // 겹친 구간 길이(비율) -> 실제 길이 환산
      double baseLen = DistanceUtils.GetDistanceBetweenNodes(a1, b1);
      double overlapLen = (overlapEnd - overlapStart) * baseLen;

      return overlapLen > tol;
    }
  }
