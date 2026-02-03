using MooringFitting2026.Model.Entities;
using MooringFitting2026.Model.Geometry;
using MooringFitting2026.Utils.Geometry;
using System;
using System.Collections.Generic;
using System.Linq;

namespace MooringFitting2026.Modifier.ElementModifier
{
  public static class ElementMeshRefinementModifier
  {
    public sealed record Options
    {
      public double TargetMeshSize { get; init; } = 500.0;
      public bool Debug { get; init; } = false;
    }

    // [수정] RawStructureData 파라미터 제거 (사용하지 않음)
    public static int Run(FeModelContext context, Options opt, Action<string> log)
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

        // 2. 분할 개수 계산
        int segments = (int)Math.Ceiling(length / opt.TargetMeshSize);
        if (segments <= 1) continue;

        // 3. 분할 수행
        elements.Remove(eid);
        var extra = elem.ExtraData.ToDictionary(k => k.Key, v => v.Value);
        var vec = Point3dUtils.Sub(p2, p1);

        int prevNodeID = n1;

        for (int i = 1; i < segments; i++)
        {
          double ratio = (double)i / segments;
          Point3D pNew = Point3dUtils.Add(p1, Point3dUtils.Mul(vec, ratio));
          int newNodeID = nodes.AddOrGet(pNew.X, pNew.Y, pNew.Z);

          elements.AddNew(new List<int> { prevNodeID, newNodeID }, elem.PropertyID, extra);
          prevNodeID = newNodeID;
        }

        elements.AddNew(new List<int> { prevNodeID, n2 }, elem.PropertyID, extra);
        splitCount++;
      }

      if (opt.Debug) log($"[Mesh] Split {splitCount} elements (Target Size: {opt.TargetMeshSize})");
      return splitCount;
    }
  }
}
