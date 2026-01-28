using MooringFitting2026.Model.Entities;
using MooringFitting2026.Model.Geometry;
using MooringFitting2026.Utils.Geometry; // Point3dUtils, DistanceUtils
using MooringFitting2026.RawData;
using System;
using System.Collections.Generic;
using System.Linq;

namespace MooringFitting2026.Modifier.ElementModifier
{
  public static class ElementMeshRefinementModifier
  {
    public sealed record Options
    {
      public double TargetMeshSize { get; init; } = 500.0; // 목표 메쉬 크기
      public bool Debug { get; init; } = false;
    }

    public static int Run(FeModelContext context, RawStructureData rawStructureData,
      Options opt, Action<string> log)
    {
      var nodes = context.Nodes;
      var elements = context.Elements;
      int splitCount = 0;

      // 컬렉션 변경 방지를 위해 ID 리스트 복사
      var elementIDs = elements.Keys.ToList();

      foreach (var eid in elementIDs)
      {
        if (!elements.Contains(eid)) continue;

        var elem = elements[eid];
        var (n1, n2) = (elem.NodeIDs[0], elem.NodeIDs[1]);

        var p1 = nodes[n1];
        var p2 = nodes[n2];

        // 1. 길이 계산
        double length = DistanceUtils.GetDistanceBetweenNodes(p1, p2);

        // 2. 분할 개수 계산 (반올림 또는 올림 정책)
        // 예: 길이 120, 타겟 50 -> 120/50 = 2.4 -> 3등분 (개당 40)
        int segments = (int)Math.Ceiling(length / opt.TargetMeshSize);

        if (segments <= 1) continue; // 이미 충분히 작음

        // 3. 분할 수행
        // 원본 삭제
        elements.Remove(eid);

        // ExtraData 복사 (매우 중요!)
        var extra = elem.ExtraData.ToDictionary(k => k.Key, v => v.Value);

        // 방향 벡터 (전체 길이 기준)
        // p(t) = p1 + (p2 - p1) * (i / segments)
        var vec = Point3dUtils.Sub(p2, p1);

        int prevNodeID = n1;

        for (int i = 1; i < segments; i++)
        {
          double ratio = (double)i / segments;

          // 중간점 계산
          Point3D pNew = Point3dUtils.Add(p1, Point3dUtils.Mul(vec, ratio));

          // 노드 생성 (AddOrGet으로 중복 방지)
          int newNodeID = nodes.AddOrGet(pNew.X, pNew.Y, pNew.Z);

          // 요소 생성 (prev -> new)
          elements.AddNew(new List<int> { prevNodeID, newNodeID }, elem.PropertyID, extra);

          prevNodeID = newNodeID;
        }

        // 마지막 조각 (last -> n2)
        elements.AddNew(new List<int> { prevNodeID, n2 }, elem.PropertyID, extra);

        splitCount++;
      }

      if (opt.Debug) log($"[Mesh] Split {splitCount} elements (Target Size: {opt.TargetMeshSize})");
      return splitCount;
    }
  }
}
