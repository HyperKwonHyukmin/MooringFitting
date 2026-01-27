using MooringFitting2026.Inspector.ElementInspector;
using MooringFitting2026.Inspector.NodeInspector;
using MooringFitting2026.Model.Entities;
using System;
using System.Collections.Generic;
using System.Linq;

namespace MooringFitting2026.Inspector
{
  /// <summary>
  /// FE 해석 모델의 구조적 건전성(Sanity)을 종합적으로 검증하는 정적 오케스트레이터입니다.
  /// 위상(Topology), 형상(Geometry), 데이터 무결성(Integrity)을 순차적으로 진단합니다.
  /// </summary>
  public static class StructuralSanityInspector
  {
    /// <summary>
    /// 모델 전체 검사를 수행하고 결과를 콘솔에 출력합니다.
    /// </summary>
    public static void Inspect(FeModelContext context, InspectorOptions opt)
    {
      if (context is null) throw new ArgumentNullException(nameof(context));
      opt ??= new InspectorOptions(); // 호출자가 opt를 null로 줘도 크래시 안 나도록

      Console.WriteLine("\n[Structural Sanity Inspection Started]");
      Console.WriteLine("--------------------------------------------------");

      // 1. 위상학적 연결성 검사 (Topology)
      InspectTopology(context, opt);

      // 2. 기하학적 형상 검사 (Geometry)
      InspectGeometry(context, opt);

      // 3. Equivalence 검사
      InspectEquivalence(context, opt);

      // 4. Duplicate 검사
      InspectDuplicate(context);

      // 5. 데이터 무결성 검사 (Integrity)
      InspectIntegrity(context);

      // 6. 고립 요소 검사 (Isolation)
      InspectIsolation(context);

      Console.WriteLine("--------------------------------------------------");
      Console.WriteLine("[Inspection Completed]\n");
    }

    private static void InspectTopology(FeModelContext context, InspectorOptions opt)
    {
      // 01. Element 그룹 연결성 확인
      var connectedGroups = ElementConnectivityInspector.FindConnectedElementGroups(context.Elements);

      if (connectedGroups.Count <= 1)
      {
        Console.WriteLine($"01 - Topology : [PASS] Connected element groups = {connectedGroups.Count}");
      }
      else
      {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"01 - Topology : [Warning] Disconnected element groups = {connectedGroups.Count}");
        Console.ResetColor();
      }

      // 02. 노드 사용 빈도(Degree) 분석
      var nodeDegree = NodeDegreeInspector.BuildNodeDegree(context);

      // 자유단 노드 (Degree = 1)
      var endNodes = nodeDegree
        .Where(kv => kv.Value == 1)
        .Select(kv => kv.Key)
        .ToList();

      PrintNodeStat("02_A - 자유단 Node (1 connection)", endNodes, opt);

      // 미사용 노드 (Degree = 0)
      // ⚠️ BuildNodeDegree가 "모든 노드"를 0으로 초기화해 넣지 않는 경우,
      // nodeDegree에 아예 없는 노드가 미사용 노드인데도 누락될 수 있음 → 전체 노드 기준으로 보정
      var isolatedNodes = context.Nodes.GetAllNodes()
        .Select(kv => kv.Key)
        .Where(id => !nodeDegree.TryGetValue(id, out var deg) || deg == 0)
        .ToList();


      PrintNodeStat("02_B - 미사용 Node (0 connection)", isolatedNodes, opt);

      // 고아 노드 제거 (Element 연결 기준)
      int removedOrphans = RemoveOrphanNodesByElementConnection(context, isolatedNodes);
      if (opt.PrintNodeIds)
        Console.WriteLine($"[Cleanup] 사용없는 Node 제거 = {removedOrphans}");
    }

    private static void InspectGeometry(FeModelContext context, InspectorOptions opt)
    {
      var shortElements = ElementDetectShortInspector.Run(context, opt);
      Console.WriteLine($"03 - Geometry : Short Elements (<{opt.ShortElementDistanceThreshold}) = {shortElements.Count}");
    }

    private static void InspectEquivalence(FeModelContext context, InspectorOptions opt)
    {
      var coincidentGroups = NodeEquivalenceInspector.InspectEquivalenceNodes(context, opt);

      if (coincidentGroups.Count == 0)
      {
        Console.WriteLine($"04 - Equivalence : [PASS] No coincident nodes found (Tol: {opt.EquivalenceTolerance}).");
        return;
      }

      Console.ForegroundColor = ConsoleColor.Yellow;
      Console.WriteLine($"04 - Equivalence : [Warning] Found {coincidentGroups.Count} groups of coincident nodes!");
      Console.ResetColor();

      // 상세 내용 출력 (최대 20개만)
      int shown = 0;
      foreach (var group in coincidentGroups.Take(20))
      {
        shown++;

        int repID = group.Count > 0 ? group[0] : -1;
        string ids = string.Join(", ", group);

        if (repID >= 0 && context.Nodes.Contains(repID)) // ✅
        {
          var node = context.Nodes[repID];              // ✅ indexer로 좌표 얻기
          Console.WriteLine($"     Group {shown}: IDs [{ids}] at ({node.X:F3}, {node.Y:F3}, {node.Z:F3})");
        }
        else
        {
          Console.WriteLine($"     Group {shown}: IDs [{ids}] at (Unknown)");
        }
      }

      if (coincidentGroups.Count > 20)
      {
        Console.WriteLine("     ... (More groups omitted)");
      }
    }

    private static void InspectDuplicate(FeModelContext context)
    {
      // 수정된 메서드 호출 (반환 타입이 List<List<int>>임)
      var duplicateGroups = ElementDuplicateInspector.FindDuplicateGroups(context);

      if (duplicateGroups.Count > 0)
      {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"05 - Duplicate : [CRITICAL] Found {duplicateGroups.Count} sets of duplicates!");

        int groupIndex = 1;
        foreach (var group in duplicateGroups)
        {
          // 각 그룹별로 어떤 ID들이 겹쳐있는지 모두 출력
          Console.WriteLine($"   Set #{groupIndex++}: [{string.Join(", ", group)}]");
        }
        Console.ResetColor();
      }
      else
      {
        Console.WriteLine("05 - Duplicate : [PASS] No duplicate elements found.");
      }
    }

    private static void InspectIntegrity(FeModelContext context)
    {
      var invalidElements = ElementIntegrityInspector.FindElementsWithInvalidReference(context);

      if (invalidElements.Count > 0)
      {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"06 - Integrity : [FAIL] Invalid reference elements = {invalidElements.Count}");
        Console.WriteLine($"     IDs to check: {string.Join(", ", invalidElements.Take(10))}{(invalidElements.Count > 10 ? ", ..." : "")}");
        Console.ResetColor();
      }
      else
      {
        Console.WriteLine("06 - Integrity : [PASS] All elements reference valid data.");
      }
    }

    private static void InspectIsolation(FeModelContext context)
    {
      var isolation = ElementIsolationInspector.FindIsolatedElements(context);

      if (isolation.Count > 0)
      {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"07 - Isolation : [Warning] Isolated elements = {isolation.Count}");
        Console.WriteLine($"     IDs to check: {string.Join(", ", isolation.Take(10))}{(isolation.Count > 10 ? ", ..." : "")}");
        Console.ResetColor();
      }
      else
      {
        Console.WriteLine("07 - Isolation : [PASS] No isolated elements found.");
      }
    }

    // 리포팅 헬퍼
    private static void PrintNodeStat(string title, List<int> nodes, InspectorOptions opt)
    {
      Console.WriteLine($"{title} : {nodes.Count}");

      // 옵션이 있고, ID 출력이 켜져있으며, 노드가 있을 때
      if (opt != null && opt.PrintNodeIds && nodes.Count > 0)
      {
        // 1. 출력 제한 개수 설정 (모두 출력 옵션이면 MaxValue, 아니면 50)
        int limit = opt.PrintAllNodeIds ? int.MaxValue : 50;

        // 2. 제한 개수만큼 가져오기
        var subset = nodes.Take(limit);
        string ids = string.Join(", ", subset);

        // 3. 전체 개수가 제한보다 클 때만 "..." 붙이기
        if (nodes.Count > limit)
        {
          ids += ", ...";
        }

        Console.WriteLine($"      IDs: {ids}");
      }
    }

    private static int RemoveOrphanNodesByElementConnection(FeModelContext context, List<int> isolatedNodes)
    {
      if (isolatedNodes == null || isolatedNodes.Count == 0)
        return 0;

      int removed = 0;

      // 혹시라도 nodeDegree가 틀렸을 수 있으니 "진짜로 element가 참조하는지" 최종 확인 후 삭제
      foreach (var nid in isolatedNodes)
      {
        if (!context.Nodes.Contains(nid))
          continue;

        bool referenced = false;
        foreach (var kv in context.Elements) // Elements는 IEnumerable<KeyValuePair<int, Element>>
        {
          var e = kv.Value;
          if (e.NodeIDs.Contains(nid))
          {
            referenced = true;
            break;
          }
        }

        if (referenced)
          continue;

        context.Nodes.Remove(nid);
        removed++;
      }

      return removed;
    }

  }
}
