using MooringFitting2026.Inspector.ElementInspector;
using MooringFitting2026.Inspector.NodeInspector;
using MooringFitting2026.Model.Entities;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace MooringFitting2026.Inspector
{
  /// <summary>
  /// FE 모델의 구조적 건전성(무결성)을 검사하고, 치명적인 오류를 자동 복구하는 클래스입니다.
  /// </summary>
  public static class StructuralSanityInspector
  {
    /// <summary>
    /// 모델 전체 검사를 수행하고 결과를 콘솔에 출력합니다.
    /// (outputDir가 제공되면 삭제된 불량 요소 로그를 CSV로 저장합니다.)
    /// </summary>
    public static List<int> Inspect(
        FeModelContext context,
        InspectorOptions opt,
        HashSet<int>? protectedNodes = null,
        HashSet<int>? rbeDependentNodes = null,
        string outputDir = null)
    {
      if (context is null) throw new ArgumentNullException(nameof(context));
      opt ??= InspectorOptions.Default;

      // 1. 활성화된 검사가 하나라도 있는지 확인
      bool anyCheckEnabled = opt.CheckTopology || opt.CheckGeometry ||
                             opt.CheckEquivalence || opt.CheckDuplicate ||
                             opt.CheckIntegrity || opt.CheckIsolation;

      // 2. 모든 검사가 꺼져있고, 디버그 모드도 아니라면 종료
      if (!anyCheckEnabled && !opt.DebugMode)
      {
        return new List<int>();
      }

      Console.WriteLine("\n[구조 건전성 검사 및 복구 시작]");
      if (opt.DebugMode) Console.WriteLine("  * 디버그 모드: 켜짐 (상세 로그 출력)");
      Console.WriteLine("--------------------------------------------------");

      List<int> freeEndNodes = new List<int>();

      // 1. 위상학적 연결성 검사 (Topology)
      if (opt.CheckTopology)
      {
        freeEndNodes = InspectTopology(context, opt, protectedNodes, rbeDependentNodes);
      }

      // 2. 기하학적 형상 검사 (Geometry)
      if (opt.CheckGeometry)
      {
        InspectGeometry(context, opt);
      }

      // 3. Equivalence 검사 (노드 중복)
      if (opt.CheckEquivalence)
      {
        InspectEquivalence(context, opt);
      }

      // 4. Duplicate 검사 (요소 중복)
      if (opt.CheckDuplicate)
      {
        InspectDuplicate(context, opt);
      }

      // 5. 데이터 무결성 검사 (★ 자동 복구 기능 내장)
      if (opt.CheckIntegrity)
      {
        InspectIntegrity(context, opt, outputDir);
      }

      // 6. 고립 요소 검사 (Isolation)
      if (opt.CheckIsolation)
      {
        InspectIsolation(context, opt);
      }

      Console.WriteLine("--------------------------------------------------");
      Console.WriteLine("[검사 완료]\n");
      return freeEndNodes;
    }

    // --------------------------------------------------------------------------
    // Private Inspection Methods
    // --------------------------------------------------------------------------

    private static List<int> InspectTopology(
        FeModelContext context,
        InspectorOptions opt,
        HashSet<int>? protectedNodes,
        HashSet<int>? rbeDependentNodes)
    {
      // 01. Element 그룹 연결성 확인
      var connectedGroups = ElementConnectivityInspector.FindConnectedElementGroups(context.Elements);
      if (connectedGroups.Count <= 1)
        LogPass($"01 - 위상 연결성 : 전체 모델이 {connectedGroups.Count}개의 그룹으로 잘 연결되어 있습니다.");
      else
        LogWarning($"01 - 위상 연결성 : 모델이 {connectedGroups.Count}개의 분리된 덩어리로 나뉘어 있습니다. (의도치 않은 분리 주의)");

      // 02. 노드 사용 빈도(Degree) 분석
      var nodeDegree = NodeDegreeInspector.BuildNodeDegree(context);

      // A. 자유단 노드 (Degree = 1) -> SPC 생성 대상
      var endNodes = nodeDegree.Where(kv => kv.Value == 1).Select(kv => kv.Key).ToList();

      if (rbeDependentNodes != null && rbeDependentNodes.Count > 0)
      {
        int initialCount = endNodes.Count;
        endNodes.RemoveAll(id => rbeDependentNodes.Contains(id));

        int excludedCount = initialCount - endNodes.Count;
        if (excludedCount > 0)
        {
          if (opt.DebugMode)
            Console.WriteLine($"      [SPC 제외] RBE 종속 노드 {excludedCount}개를 자유단 목록에서 제외했습니다.");
        }
      }

      PrintNodeStat("02_A - 자유단 노드 (연결 1개)", endNodes, opt, isWarning: false);

      // B. 미사용 노드 (Degree = 0)
      var isolatedNodes = context.Nodes.GetAllNodes()
          .Select(kv => kv.Key)
          .Where(id => !nodeDegree.TryGetValue(id, out var deg) || deg == 0)
          .ToList();

      PrintNodeStat("02_B - 고립된 노드 (연결 0개)", isolatedNodes, opt, isWarning: true);

      int removedOrphans = RemoveOrphanNodesByElementConnection(context, isolatedNodes, protectedNodes);
      if (removedOrphans > 0)
        Console.WriteLine($"      [자동 정리] 사용되지 않는 고립 노드 {removedOrphans}개를 삭제했습니다.");

      return endNodes;
    }

    private static void InspectGeometry(FeModelContext context, InspectorOptions opt)
    {
      var shortElements = ElementDetectShortInspector.Run(context, opt);

      if (shortElements.Count == 0)
      {
        LogPass("03 - 기하 형상 : 너무 짧은 요소가 없습니다.");
      }
      else
      {
        LogWarning($"03 - 기하 형상 : 길이가 {opt.ShortElementDistanceThreshold} 미만인 짧은 요소가 {shortElements.Count}개 발견되었습니다.");
        if (opt.DebugMode)
        {
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
        LogPass($"04 - 노드 중복 : 허용오차({opt.EquivalenceTolerance}) 내에 겹치는 노드가 없습니다.");
        return;
      }

      LogWarning($"04 - 노드 중복 : 위치가 겹치는 노드 그룹이 {coincidentGroups.Count}개 발견되었습니다.");

      if (opt.DebugMode)
      {
        int shown = 0;
        foreach (var group in coincidentGroups.Take(10))
        {
          shown++;
          int repID = group.FirstOrDefault();
          string ids = string.Join(", ", group);

          if (context.Nodes.Contains(repID))
          {
            var node = context.Nodes[repID];
            Console.WriteLine($"     그룹 {shown}: IDs [{ids}] 위치 ({node.X:F1}, {node.Y:F1}, {node.Z:F1})");
          }
        }
        if (coincidentGroups.Count > 10) Console.WriteLine("     ... (생략됨)");
      }
    }

    private static void InspectDuplicate(FeModelContext context, InspectorOptions opt)
    {
      var duplicateGroups = ElementDuplicateInspector.FindDuplicateGroups(context);

      if (duplicateGroups.Count == 0)
      {
        LogPass("05 - 요소 중복 : 완전히 겹치는 중복 요소가 없습니다.");
        return;
      }

      LogCritical($"05 - 요소 중복 : 노드 구성이 동일한 중복 요소 세트가 {duplicateGroups.Count}개 발견되었습니다!");

      if (opt.DebugMode)
      {
        int limit = opt.PrintAllNodeIds ? int.MaxValue : 20;
        int count = 0;
        foreach (var group in duplicateGroups)
        {
          if (++count > limit) break;
          Console.WriteLine($"   세트 #{count}: [{string.Join(", ", group)}]");
        }
        if (duplicateGroups.Count > limit) Console.WriteLine("   ...");
      }
    }

    // ★ [수정됨] 데이터 무결성 검사 및 자동 복구
    private static void InspectIntegrity(FeModelContext context, InspectorOptions opt, string outputDir)
    {
      var invalidElements = ElementIntegrityInspector.FindElementsWithInvalidReference(context);

      if (invalidElements.Count == 0)
      {
        LogPass("06 - 데이터 무결성 : 모든 요소가 유효한 노드와 속성을 참조하고 있습니다.");
        return;
      }

      // 로그 메시지 (복구 알림)
      Console.ForegroundColor = ConsoleColor.Magenta;
      Console.WriteLine($"[복구] 06 - 데이터 무결성 : 존재하지 않는 노드/속성 참조 요소 {invalidElements.Count}개를 삭제합니다.");
      Console.ResetColor();

      // CSV 로그 기록
      if (!string.IsNullOrEmpty(outputDir))
      {
        string logPath = Path.Combine(outputDir, "Deleted_Invalid_Elements.csv");
        try
        {
          using (var sw = new StreamWriter(logPath, false, System.Text.Encoding.UTF8))
          {
            sw.WriteLine("ElementID,PropertyID,Reason,NodeIDs");
            foreach (var eid in invalidElements)
            {
              if (context.Elements.TryGetValue(eid, out var elem))
              {
                string nodes = string.Join(";", elem.NodeIDs);
                sw.WriteLine($"{eid},{elem.PropertyID},Invalid_Reference,{nodes}");
              }
              else
              {
                sw.WriteLine($"{eid},Unknown,Already_Missing,");
              }
            }
          }
          Console.WriteLine($"      -> [기록] 삭제된 요소 목록을 저장했습니다: {Path.GetFileName(logPath)}");
        }
        catch (Exception ex)
        {
          Console.WriteLine($"      -> [오류] 로그 저장 실패: {ex.Message}");
        }
      }

      // 요소 삭제 (자동 복구)
      int deletedCount = 0;
      foreach (var eid in invalidElements)
      {
        // ★ [수정] Elements.Remove는 void이므로 if문 조건으로 쓸 수 없음
        // 존재 여부를 확인하고 삭제
        if (context.Elements.Contains(eid))
        {
          context.Elements.Remove(eid);
          deletedCount++;
        }
      }

      Console.WriteLine($"      -> [완료] 총 {deletedCount}개의 불량 요소를 모델에서 제거했습니다.");
    }

    private static void InspectIsolation(FeModelContext context, InspectorOptions opt)
    {
      var isolation = ElementIsolationInspector.FindIsolatedElements(context);

      if (isolation.Count == 0)
      {
        LogPass("07 - 요소 고립 : 고립된(연결되지 않은) 요소가 없습니다.");
        return;
      }

      LogWarning($"07 - 요소 고립 : 다른 요소와 연결되지 않은 고립 요소가 {isolation.Count}개 있습니다.");
      if (opt.DebugMode)
      {
        Console.WriteLine($"     IDs: {SummarizeIds(isolation, opt)}");
      }
    }

    // --------------------------------------------------------------------------
    // Helpers
    // --------------------------------------------------------------------------

    private static void PrintNodeStat(string title, List<int> nodes, InspectorOptions opt, bool isWarning)
    {
      if (nodes.Count == 0) return;

      string msg = $"{title} : {nodes.Count}개 발견";
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
      int limit = opt.PrintAllNodeIds ? int.MaxValue : 30;

      var subset = ids.Take(limit);
      string str = string.Join(", ", subset);
      if (ids.Count > limit) str += ", ...";
      return str;
    }

    private static int RemoveOrphanNodesByElementConnection(
        FeModelContext context,
        List<int> isolatedNodes,
        HashSet<int>? protectedNodes)
    {
      if (isolatedNodes == null || isolatedNodes.Count == 0) return 0;
      int removed = 0;
      foreach (var nid in isolatedNodes)
      {
        if (protectedNodes != null && protectedNodes.Contains(nid)) continue;
        if (!context.Nodes.Contains(nid)) continue;

        bool referenced = context.Elements.Any(kv => kv.Value.NodeIDs.Contains(nid));
        if (!referenced)
        {
          context.Nodes.Remove(nid);
          removed++;
        }
      }
      return removed;
    }

    private static void LogPass(string msg)
    {
      Console.WriteLine($"[통과] {msg}");
    }

    private static void LogWarning(string msg)
    {
      Console.ForegroundColor = ConsoleColor.Yellow;
      Console.WriteLine($"[주의] {msg}");
      Console.ResetColor();
    }

    private static void LogCritical(string msg)
    {
      Console.ForegroundColor = ConsoleColor.Red;
      Console.WriteLine($"[실패] {msg}");
      Console.ResetColor();
    }
  }
}
