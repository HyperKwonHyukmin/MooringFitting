using MooringFitting2026.Inspector.ElementInspector;
using MooringFitting2026.Inspector.NodeInspector;
using MooringFitting2026.Model.Entities;
using System;
using System.Collections.Generic;
using System.Linq;

namespace MooringFitting2026.Inspector
{
  /// <summary>
  /// FE 모델의 구조적 건전성을 검사하는 핵심 검사기입니다.
  /// 최소한의 필수 검사(연결성, 기하학적 오류, 중복)에 집중합니다.
  /// </summary>
  public static class StructuralSanityInspector
  {
    /// <summary>
    /// 모델 검사를 수행하고, 경계조건(SPC)으로 사용될 자유단(Free End) 노드 리스트를 반환합니다.
    /// </summary>
    public static List<int> Inspect(FeModelContext context, InspectorOptions opt)
    {
      if (context == null) throw new ArgumentNullException(nameof(context));
      opt ??= InspectorOptions.Default;

      if (opt.DebugMode)
      {
        Console.WriteLine("\n[Inspection] Starting Structural Sanity Check...");
      }

      // 1. [핵심] 위상학적 연결성 검사 (Topology)
      // 반환값: 자유단 노드 리스트 (SPC 생성용)
      // (Topology 옵션이 꺼져 있어도 SPC 추출을 위해 최소한의 Degree 계산은 수행하거나, 빈 리스트 반환)
      List<int> freeEndNodes = new List<int>();
      if (opt.CheckTopology)
      {
        freeEndNodes = RunTopologyCheck(context, opt);
      }

      // 2. 기하학적 형상 검사 (너무 짧은 요소)
      if (opt.CheckGeometry)
      {
        RunGeometryCheck(context, opt);
      }

      // 3. 중복 요소 검사
      if (opt.CheckDuplicate)
      {
        RunDuplicateCheck(context, opt);
      }

      // 4. (옵션) 기타 무결성 검사
      if (opt.CheckEquivalence) RunEquivalenceCheck(context, opt);
      if (opt.CheckIntegrity) RunIntegrityCheck(context, opt);

      if (opt.DebugMode) Console.WriteLine("[Inspection] Check Completed.\n");

      return freeEndNodes;
    }

    // ==========================================================================
    // 1. Topology Check (Essential)
    // ==========================================================================
    private static List<int> RunTopologyCheck(FeModelContext context, InspectorOptions opt)
    {
      // A. 노드 사용 빈도(Degree) 분석
      var nodeDegree = NodeDegreeInspector.BuildNodeDegree(context);

      // B. 자유단 노드 (Degree == 1) 추출 -> SPC용
      var endNodes = nodeDegree.Where(kv => kv.Value == 1).Select(kv => kv.Key).ToList();

      // C. 고립 노드 (Degree == 0) 추출 및 제거
      // Elements에 사용되지 않은 노드는 해석에 불필요하므로 정리
      var allNodeIds = context.Nodes.Keys.ToList();
      var isolatedNodes = allNodeIds.Where(id => !nodeDegree.ContainsKey(id) || nodeDegree[id] == 0).ToList();

      if (isolatedNodes.Count > 0)
      {
        int removedCount = 0;
        foreach (var nid in isolatedNodes)
        {
          context.Nodes.Remove(nid);
          removedCount++;
        }
        if (opt.DebugMode) LogWarning($"   -> Cleanup: Removed {removedCount} isolated (orphan) nodes.");
      }

      // 결과 리포팅
      if (opt.DebugMode)
      {
        Console.WriteLine($"   [Topology] Free Ends: {endNodes.Count} nodes found.");
        if (opt.PrintAllNodeIds && endNodes.Count > 0)
          Console.WriteLine($"      IDs: {SummarizeIds(endNodes, 20)}");
      }

      return endNodes;
    }

    // ==========================================================================
    // 2. Geometry Check (Short Elements)
    // ==========================================================================
    private static void RunGeometryCheck(FeModelContext context, InspectorOptions opt)
    {
      // 너무 짧은 요소는 Nastran 해석 시 에러 유발 가능성 있음
      var shortElements = ElementDetectShortInspector.Run(context, opt);

      if (shortElements.Count > 0)
      {
        LogWarning($"   [Geometry] Found {shortElements.Count} short elements (< {opt.ShortElementDistanceThreshold}).");
        if (opt.DebugMode)
        {
          var ids = shortElements.Select(t => t.eleId).ToList();
          Console.WriteLine($"      IDs: {SummarizeIds(ids, 20)}");
        }
      }
      else if (opt.DebugMode)
      {
        LogPass("   [Geometry] No short elements found.");
      }
    }

    // ==========================================================================
    // 3. Duplicate Check
    // ==========================================================================
    private static void RunDuplicateCheck(FeModelContext context, InspectorOptions opt)
    {
      var duplicateGroups = ElementDuplicateInspector.FindDuplicateGroups(context);

      if (duplicateGroups.Count > 0)
      {
        // 중복은 모델링 실수일 확률이 높으므로 경고
        LogWarning($"   [Duplicate] Found {duplicateGroups.Count} sets of duplicate elements.");
        if (opt.DebugMode)
        {
          foreach (var group in duplicateGroups.Take(5)) // 너무 많으면 5개만
            Console.WriteLine($"      Set: [{string.Join(", ", group)}]");
          if (duplicateGroups.Count > 5) Console.WriteLine("      ...");
        }
      }
      else if (opt.DebugMode)
      {
        LogPass("   [Duplicate] No duplicates found.");
      }
    }

    // ==========================================================================
    // 4. Other Checks (Optional)
    // ==========================================================================
    private static void RunEquivalenceCheck(FeModelContext context, InspectorOptions opt)
    {
      var coincidentGroups = NodeEquivalenceInspector.InspectEquivalenceNodes(context, opt);
      if (coincidentGroups.Count > 0)
      {
        LogWarning($"   [Equivalence] Found {coincidentGroups.Count} coincident node groups (Tol: {opt.EquivalenceTolerance}).");
      }
    }

    private static void RunIntegrityCheck(FeModelContext context, InspectorOptions opt)
    {
      var invalidElements = ElementIntegrityInspector.FindElementsWithInvalidReference(context);
      if (invalidElements.Count > 0)
      {
        LogError($"   [Integrity] Found {invalidElements.Count} elements with invalid Node/Property references.");
      }
    }

    // ==========================================================================
    // Helper Methods
    // ==========================================================================

    private static string SummarizeIds(List<int> ids, int limit)
    {
      if (ids == null || ids.Count == 0) return "";
      var subset = ids.Take(limit);
      string str = string.Join(", ", subset);
      if (ids.Count > limit) str += ", ...";
      return str;
    }

    private static void LogPass(string msg)
    {
      Console.ForegroundColor = ConsoleColor.Green;
      Console.WriteLine(msg);
      Console.ResetColor();
    }

    private static void LogWarning(string msg)
    {
      Console.ForegroundColor = ConsoleColor.Yellow;
      Console.WriteLine($"[WARN] {msg}");
      Console.ResetColor();
    }

    private static void LogError(string msg)
    {
      Console.ForegroundColor = ConsoleColor.Red;
      Console.WriteLine($"[FAIL] {msg}");
      Console.ResetColor();
    }
  }
}
