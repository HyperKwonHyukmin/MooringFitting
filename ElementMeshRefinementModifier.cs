using MooringFitting2026.Model.Entities;
using MooringFitting2026.Model.Geometry;
using MooringFitting2026.Utils.Geometry;
using System;
using System.Collections.Generic;
using System.Linq;

namespace MooringFitting2026.Modifier.ElementModifier
{
  /// <summary>
  /// 요소의 길이가 목표 크기(TargetMeshSize)보다 긴 경우,
  /// 해당 요소를 여러 개의 세그먼트로 균등 분할(Refinement)하는 수정자입니다.
  /// </summary>
  public static class ElementMeshRefinementModifier
  {
    public sealed record Options
    {
      public double TargetMeshSize { get; init; } = 500.0; // 목표 메쉬 크기 (이 값보다 길면 분할)
      public bool Debug { get; init; } = false;            // 디버그 로그 출력 여부
    }

    public sealed record Result(
        int ElementsScanned,      // 검사한 총 요소 수
        int ElementsRefined,      // 분할이 수행된 요소 수
        int NewSegmentsCreated    // 새로 생성된 세그먼트(요소) 총 개수
    );

    /// <summary>
    /// 실행 진입점
    /// </summary>
    public static Result Run(FeModelContext context, Options opt, Action<string> log)
    {
      log ??= Console.WriteLine;

      var nodes = context.Nodes;
      var elements = context.Elements;

      int scanned = 0;
      int refinedCount = 0;
      int segmentsCreated = 0;

      // 컬렉션 변경 방지를 위해 ID 리스트 복사
      var elementIDs = elements.Keys.ToList();

      foreach (var eid in elementIDs)
      {
        if (!elements.Contains(eid)) continue;
        scanned++;

        var elem = elements[eid];

        // 유효성 검사
        if (elem.NodeIDs == null || elem.NodeIDs.Count < 2) continue;

        var (n1, n2) = (elem.NodeIDs[0], elem.NodeIDs[1]);

        // 노드 존재 여부 확인
        if (!nodes.Contains(n1) || !nodes.Contains(n2)) continue;

        var p1 = nodes[n1];
        var p2 = nodes[n2];

        // 1. 길이 계산
        double length = DistanceUtils.GetDistanceBetweenNodes(p1, p2);

        // 2. 분할 개수 계산 (올림 처리)
        // 예: 길이 1200, Target 500 -> 2.4 -> 3등분 (개당 400)
        int segments = (int)Math.Ceiling(length / opt.TargetMeshSize);

        // 분할이 필요 없으면 스킵
        if (segments <= 1) continue;

        // 3. 분할 수행
        // 원본 요소 삭제 (대신 분할된 조각들이 들어감)
        elements.Remove(eid);

        // 속성 복사
        var extra = (elem.ExtraData != null)
            ? elem.ExtraData.ToDictionary(k => k.Key, v => v.Value)
            : new Dictionary<string, string>();

        // 방향 벡터 (전체 길이 기준)
        var vec = Point3dUtils.Sub(p2, p1);

        int prevNodeID = n1;

        // 중간 노드 생성 및 요소 연결
        for (int i = 1; i < segments; i++)
        {
          double ratio = (double)i / segments;

          // 선형 보간 (Linear Interpolation)
          Point3D pNew = Point3dUtils.Add(p1, Point3dUtils.Mul(vec, ratio));

          // 노드 생성 (이미 존재하면 재사용 - Snap 효과)
          int newNodeID = nodes.AddOrGet(pNew.X, pNew.Y, pNew.Z);

          // [이전 노드] -> [새 노드] 요소 생성
          elements.AddNew(new List<int> { prevNodeID, newNodeID }, elem.PropertyID, extra);
          segmentsCreated++;

          prevNodeID = newNodeID;
        }

        // 마지막 조각: [마지막 중간 노드] -> [끝 노드]
        elements.AddNew(new List<int> { prevNodeID, n2 }, elem.PropertyID, extra);
        segmentsCreated++;
        refinedCount++;

        if (opt.Debug)
          log($"   -> [분할 적용] E{eid} (길이 {length:F1}) -> {segments}개로 분할됨 (개당 약 {length / segments:F1})");
      }

      return new Result(scanned, refinedCount, segmentsCreated);
    }
  }
}
