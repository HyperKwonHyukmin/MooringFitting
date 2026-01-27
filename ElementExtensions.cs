using MooringFitting2026.Model.Entities;
using System;
using System.Collections.Generic;
using System.Linq;

// ★ [핵심] 모호함 방지: 이 파일에서 'Element'는 무조건 이 클래스를 뜻함
using Element = MooringFitting2026.Model.Entities.Element;

namespace MooringFitting2026.Extensions
{
  public static class ElementExtensions
  {
    public static (int StartNodeID, int EndNodeID) GetEndNodePair(this Element e)
    {
      if (e == null) throw new ArgumentNullException(nameof(e));
      if (e.NodeIDs == null || e.NodeIDs.Count == 0)
        throw new InvalidOperationException("Element has no nodes.");

      return (e.NodeIDs[0], e.NodeIDs[e.NodeIDs.Count - 1]);
    }

    public static Element CloneWithNodeIDs(this Element e, IEnumerable<int> newNodeIDs)
    {
      if (e == null) throw new ArgumentNullException(nameof(e));

      var extra = (e.ExtraData != null)
          ? e.ExtraData.ToDictionary(kv => kv.Key, kv => kv.Value)
          : new Dictionary<string, string>();

      // Element 생성자 호출
      return new Element(newNodeIDs.ToList(), e.PropertyID, extra);
    }

    public static bool TryReplaceNode(this Element e, int oldNodeId, int newNodeId, out Element replaced)
    {
      if (e == null) throw new ArgumentNullException(nameof(e));
      var list = e.NodeIDs.ToList();
      bool changed = false;

      for (int i = 0; i < list.Count; i++)
      {
        if (list[i] == oldNodeId)
        {
          list[i] = newNodeId;
          changed = true;
        }
      }

      if (!changed || list.Distinct().Count() != list.Count)
      {
        replaced = e;
        return false;
      }

      replaced = e.CloneWithNodeIDs(list);
      return true;
    }

    public static double GetReferencedPropertyDim(this Element element, Properties properties)
    {
      if (properties == null)
        throw new System.ArgumentNullException(nameof(properties));

      var prop = properties[element.PropertyID];
      var dim = prop.Dim;

      if (dim == null || dim.Count == 0)
        return 0.0;


      // 기존 정책(I/T는 dim[2]/2)
      if (prop.Type == "I" || prop.Type == "T")
      {
        if (dim.Count > 2)
          return System.Math.Round(dim[2] / 2.0, 1);


        // dim[2]가 없으면 최대값 fallback
        return dim.Max();
      }

      // 나머지 타입: 일단 가장 큰 치수를 대표치수로 사용(검색영역 목적이라 안전함)
      return dim.Max();
    }
  }
}
