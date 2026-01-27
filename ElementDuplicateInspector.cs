using System;
using System.Collections.Generic;
using System.Linq;
using MooringFitting2026.Model.Entities;

namespace MooringFitting2026.Inspector.ElementInspector
{
  public static class ElementDuplicateInspector
  {
    /// <summary>
    /// 동일한 노드 구성을 가진 중복 요소(Duplicate Elements)를 찾아냅니다.
    /// (예: Element A(1,2)와 Element B(2,1)은 중복으로 간주)
    /// </summary>
    /// <param name="context">FE 모델 컨텍스트</param>
    /// <returns>중복으로 판명된(삭제 권장되는) Element ID 리스트</returns>
    public static List<List<int>> FindDuplicateGroups(FeModelContext context)
    {
      // Key: 토폴로지 키 (예: "1-2"), Value: 해당 키를 가진 Element ID 리스트
      var topologyGroups = new Dictionary<string, List<int>>();

      foreach (var kvp in context.Elements)
      {
        int elementID = kvp.Key;
        var element = kvp.Value;
        var nodes = element.NodeIDs;

        if (nodes == null || nodes.Count < 2) continue;

        // 1. 노드 ID 정렬 (방향 무관하게 비교하기 위함)
        // (1, 2)와 (2, 1)을 동일하게 취급
        var sortedNodeIDs = nodes.OrderBy(n => n).ToList();

        // 2. 고유 키 생성 ("1-2" 형태)
        string topologyKey = string.Join("-", sortedNodeIDs);

        // 3. 딕셔너리에 추가
        if (!topologyGroups.ContainsKey(topologyKey))
        {
          topologyGroups[topologyKey] = new List<int>();
        }
        topologyGroups[topologyKey].Add(elementID);
      }

      // 4. 요소가 2개 이상인 그룹(중복된 세트)만 필터링하여 반환
      // 예: [[101, 102], [205, 208, 210], ...]
      return topologyGroups.Values
                           .Where(group => group.Count > 1)
                           .ToList();
    }
  }
  
}
