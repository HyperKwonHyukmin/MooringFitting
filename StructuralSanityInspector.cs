using MooringFitting2026.Inspector.ElementInspector;
using MooringFitting2026.Inspector.NodeInspector;
using MooringFitting2026.Model.Entities;
using System;
using System.Collections.Generic;
using System.Linq;

namespace MooringFitting2026.Inspector
{
  public static class StructuralSanityInspector
  {
    /// <summary>
    /// 모델 전체 검사를 수행하고 결과를 콘솔에 출력합니다.
    /// </summary>
    public static List<int> Inspect(FeModelContext context, InspectorOptions opt)
    {
      if (context is null) throw new ArgumentNullException(nameof(context));
      opt ??= new InspectorOptions(); // 방어 로직

      Console.WriteLine("\n[Structural Sanity Inspection Started]");
      // Debug 모드일 때만 안내 메시지 출력
      if (opt.DebugMode) Console.WriteLine("  * Debug Mode: ON (Detailed Logs Enabled)");
      Console.WriteLine("--------------------------------------------------");

      // 위상학적 연결성 검사 (Topology) -> 자유단 노드 리스트 확보
      List<int> freeEndNodes = new List<int>();

      // 1. 위상학적 연결성 검사 (Topology)
      if (opt.CheckTopology)
      {
        freeEndNodes = InspectTopology(context, opt);
      }

      // 2. 기하학적 형상 검사 (Geometry)
      if (opt.CheckGeometry)
      {
        InspectGeometry(context, opt);
      }

      // 3. Equivalence 검사
      if (opt.CheckEquivalence)
      {
        InspectEquivalence(context, opt);
      }

      // 4. Duplicate 검사
      if (opt.CheckDuplicate)
      {
        InspectDuplicate(context, opt); // (필요하다면 opt 추가 전달)
      }

      // 5. 데이터 무결성 검사 (Integrity)
      if (opt.CheckIntegrity)
      {
        InspectIntegrity(context, opt); // (필요하다면 opt 추가 전달)
      }

      // 6. 고립 요소 검사 (Isolation)
      if (opt.CheckIsolation)
      {
        InspectIsolation(context, opt); // (필요하다면 opt 추가 전달)
      }

      Console.WriteLine("--------------------------------------------------");
      Console.WriteLine("[Inspection Completed]\n");
      return freeEndNodes;
    }

    // --------------------------------------------------------------------------

    private static List<int> InspectTopology(FeModelContext context, InspectorOptions opt)
    {
      // 01. Element 그룹 연결성 확인
      var connectedGroups = ElementConnectivityInspector.FindConnectedElementGroups(context.Elements);
      if (connectedGroups.Count <= 1) LogPass($"01 - Topology : Connected groups = {connectedGroups.Count}");
      else LogWarning($"01 - Topology : Disconnected groups = {connectedGroups.Count}");

      // 02. 노드 사용 빈도(Degree) 분석
      var nodeDegree = NodeDegreeInspector.BuildNodeDegree(context);

      // A. 자유단 노드 (Degree = 1) -> 이 리스트를 반환할 것임
      var endNodes = nodeDegree.Where(kv => kv.Value == 1).Select(kv => kv.Key).ToList();
      PrintNodeStat("02_A - 자유단 Node (1 conn)", endNodes, opt, isWarning: false);

      // B. 미사용 노드
      var isolatedNodes = context.Nodes.GetAllNodes()
        .Select(kv => kv.Key)
        .Where(id => !nodeDegree.TryGetValue(id, out var deg) || deg == 0)
        .ToList();

      PrintNodeStat("02_B - 미사용 Node (0 conn)", isolatedNodes, opt, isWarning: true);

      // 고아 노드 제거
      int removedOrphans = RemoveOrphanNodesByElementConnection(context, isolatedNodes);
      if (removedOrphans > 0) Console.WriteLine($"      [Cleanup] Removed {removedOrphans} orphan nodes.");

      return endNodes; // ★ 리스트 반환
    }

    private static void InspectGeometry(FeModelContext context, InspectorOptions opt)
    {
      // shortElements는 List<(int eleId, int n1, int n2)> 타입임
      var shortElements = ElementDetectShortInspector.Run(context, opt);

      if (shortElements.Count == 0)
      {
        LogPass("03 - Geometry : No short elements found.");
      }
      else
      {
        LogWarning($"03 - Geometry : Short Elements (<{opt.ShortElementDistanceThreshold}) = {shortElements.Count}");

        if (opt.DebugMode)
        {
          // ★ [수정] 튜플에서 eleId만 뽑아서 List<int>로 변환하여 전달
          var elementIds = shortElements.Select(t => t.eleId).ToList();
          Console.WriteLine($"      IDs: {SummarizeIds(elementIds, opt)}");
        }
      }
    }

    private static void InspectEquivalence(FeModelContext context, InspectorOptions opt)
    {
      var coincidentGroups = NodeEquivalenceInspector.InspectEquivalenceNodes(context, opt);

      if (coincidentGroups.Count == 0)
      {
        LogPass($"04 - Equivalence : No coincident nodes (Tol: {opt.EquivalenceTolerance}).");
        return;
      }

      LogWarning($"04 - Equivalence : Found {coincidentGroups.Count} coincident groups!");

      if (opt.DebugMode)
      {
        int shown = 0;
        foreach (var group in coincidentGroups.Take(10)) // Debug 모드여도 너무 많으면 10개만
        {
          shown++;
          int repID = group.FirstOrDefault();
          string ids = string.Join(", ", group);

          if (context.Nodes.Contains(repID))
          {
            var node = context.Nodes[repID];
            Console.WriteLine($"     Group {shown}: IDs [{ids}] at ({node.X:F1}, {node.Y:F1}, {node.Z:F1})");
          }
        }
        if (coincidentGroups.Count > 10) Console.WriteLine("     ... (More omitted)");
      }
    }

    private static void InspectDuplicate(FeModelContext context, InspectorOptions opt)
    {
      var duplicateGroups = ElementDuplicateInspector.FindDuplicateGroups(context);

      if (duplicateGroups.Count == 0)
      {
        LogPass("05 - Duplicate : No duplicate elements found.");
        return;
      }

      LogCritical($"05 - Duplicate : Found {duplicateGroups.Count} sets of duplicates!");

      if (opt.DebugMode)
      {
        int limit = opt.PrintAllNodeIds ? int.MaxValue : 20;
        int count = 0;
        foreach (var group in duplicateGroups)
        {
          if (++count > limit) break;
          Console.WriteLine($"   Set #{count}: [{string.Join(", ", group)}]");
        }
        if (duplicateGroups.Count > limit) Console.WriteLine("   ...");
      }
    }

    private static void InspectIntegrity(FeModelContext context, InspectorOptions opt)
    {
      var invalidElements = ElementIntegrityInspector.FindElementsWithInvalidReference(context);

      if (invalidElements.Count == 0)
      {
        LogPass("06 - Integrity : All elements reference valid data.");
        return;
      }

      LogCritical($"06 - Integrity : Invalid reference elements = {invalidElements.Count}");
      if (opt.DebugMode)
      {
        Console.WriteLine($"     IDs: {SummarizeIds(invalidElements, opt)}");
      }
    }

    private static void InspectIsolation(FeModelContext context, InspectorOptions opt)
    {
      var isolation = ElementIsolationInspector.FindIsolatedElements(context);

      if (isolation.Count == 0)
      {
        LogPass("07 - Isolation : No isolated elements found.");
        return;
      }

      LogWarning($"07 - Isolation : Isolated elements = {isolation.Count}");
      if (opt.DebugMode)
      {
        Console.WriteLine($"     IDs: {SummarizeIds(isolation, opt)}");
      }
    }

    // ==========================================================================
    // Helper Methods
    // ==========================================================================

    private static void PrintNodeStat(string title, List<int> nodes, InspectorOptions opt, bool isWarning)
    {
      // 개수가 0개여도 DebugMode가 아니면 굳이 출력 안함 (깔끔하게)
      if (nodes.Count == 0) return;

      string msg = $"{title} : {nodes.Count}";
      if (isWarning) LogWarning(msg);
      else Console.WriteLine(msg);

      if (opt.DebugMode && nodes.Count > 0)
      {
        Console.WriteLine($"      IDs: {SummarizeIds(nodes, opt)}");
      }
    }

    private static string SummarizeIds(List<int> ids, InspectorOptions opt)
    {
      if (ids == null || ids.Count == 0) return "";
      int limit = opt.PrintAllNodeIds ? int.MaxValue : 30; // 기본 30개만 표시

      var subset = ids.Take(limit);
      string str = string.Join(", ", subset);
      if (ids.Count > limit) str += ", ...";
      return str;
    }

    private static void LogPass(string msg)
    {
      // 성공 메시지는 기본색(흰색/회색)으로 출력하거나 녹색으로 할 수 있음
      Console.WriteLine($"[PASS] {msg}");
    }

    private static void LogWarning(string msg)
    {
      Console.ForegroundColor = ConsoleColor.Yellow;
      Console.WriteLine($"[WARN] {msg}");
      Console.ResetColor();
    }

    private static void LogCritical(string msg)
    {
      Console.ForegroundColor = ConsoleColor.Red;
      Console.WriteLine($"[FAIL] {msg}");
      Console.ResetColor();
    }

    private static int RemoveOrphanNodesByElementConnection(FeModelContext context, List<int> isolatedNodes)
    {
      if (isolatedNodes == null || isolatedNodes.Count == 0) return 0;
      int removed = 0;
      foreach (var nid in isolatedNodes)
      {
        if (!context.Nodes.Contains(nid)) continue;
        // 실제 사용 여부 재확인 (성능 위해 최적화 가능)
        bool referenced = context.Elements.Any(kv => kv.Value.NodeIDs.Contains(nid));
        if (!referenced)
        {
          context.Nodes.Remove(nid);
          removed++;
        }
      }
      return removed;
    }
  }
}
