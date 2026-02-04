using MooringFitting2026.Exporters;
using MooringFitting2026.Extensions;
using MooringFitting2026.Inspector;
using MooringFitting2026.Inspector.ElementInspector;
using MooringFitting2026.Model.Entities;
using MooringFitting2026.Modifier.ElementModifier;
using MooringFitting2026.Modifier.NodeModifier;
using MooringFitting2026.RawData;
using MooringFitting2026.Services.Load;
using MooringFitting2026.Services.SectionProperties;
using MooringFitting2026.Services.Solver;
using MooringFitting2026.Utils.Geometry;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace MooringFitting2026.Pipeline
{
  public class FeModelProcessPipeline
  {
    private readonly FeModelContext _context;
    private readonly RawStructureData _rawStructureData;
    private readonly WinchData _winchData;
    private readonly InspectorOptions _inspectOpt; // 전역 공통 설정
    private readonly string _csvPath;

    // 파이프라인 상태 저장용
    private Dictionary<int, MooringFittingConnectionModifier.RigidInfo> _rigidMap
        = new Dictionary<int, MooringFittingConnectionModifier.RigidInfo>();
    private List<ForceLoad> _forceLoads = new List<ForceLoad>();

    public FeModelProcessPipeline(
        FeModelContext context,
        RawStructureData rawStructureData,
        WinchData winchData,
        InspectorOptions inspectOpt,
        string CsvPath)
    {
      _context = context ?? throw new ArgumentNullException(nameof(context));
      _rawStructureData = rawStructureData;
      _winchData = winchData;
      _inspectOpt = inspectOpt ?? InspectorOptions.Default; // Null일 경우 기본값 사용
      _csvPath = CsvPath;
    }

    public void Run()
    {
      Console.WriteLine("\n[Pipeline Started] Processing FE Model...");

      // Z방향 절대좌표 통일하기 (전처리)
      NodeZPlaneNormalizeModifier.Run(_context);

      // STAGE_00 : 원본 상태 Export
      ExportBaseline();

      // 전체 파이프라인 순차 실행
      RunStagedPipeline();
    }

    private void ExportBaseline()
    {
      string stageName = "STAGE_00";
      Console.WriteLine($"================ {stageName} =================");

      // 전역 옵션으로 검사 수행
      var freeEndNodes = StructuralSanityInspector.Inspect(_context, _inspectOpt);

      // 초기 Export (하중 없음)
      BdfExporter.Export(_context, _csvPath, stageName, freeEndNodes);
    }

    private void RunStagedPipeline()
    {
      // [변경] 각 단계별로 ActiveStages 플래그를 확인합니다.

      // Stage 01
      // 모델의 기초가 되는 중복 및 공선(Collinear) 요소를 정리      
      if (_inspectOpt.ActiveStages.HasFlag(ProcessingStage.Stage01_CollinearOverlap))
      {
        RunStage("STAGE_01", () =>
        {
          ElementCollinearOverlapGroupRun(_inspectOpt.DebugMode);
        });
      }
      else LogSkip("STAGE_01");

      // Stage 02
      if (_inspectOpt.ActiveStages.HasFlag(ProcessingStage.Stage02_SplitByNodes))
      {
        RunStage("STAGE_02", () =>
        {
          ElementSplitByExistingNodesRun(_inspectOpt.DebugMode);
        });
      }
      else LogSkip("STAGE_02");

      // Stage 03
      if (_inspectOpt.ActiveStages.HasFlag(ProcessingStage.Stage03_IntersectionSplit))
      {
        RunStage("STAGE_03", () =>
        {
          ElementIntersectionSplitRun(_inspectOpt.DebugMode);

          // [★ 추가] 교차 분할 후 발생한 1.0 미만의 미세 요소를 강제 병합
          // 기존 RemoveDanglingShortElements() 대신 이걸 쓰거나 둘 다 써도 무방함
          CollapseShortElementsRun(1.0);
        });
      }
      else LogSkip("STAGE_03");

      // Stage 03.5
      if (_inspectOpt.ActiveStages.HasFlag(ProcessingStage.Stage03_5_DuplicateMerge))
      {
        RunStage("STAGE_03_5", () =>
        {
          ElementDuplicateMergeRun(_inspectOpt.DebugMode);
        });
      }
      else LogSkip("STAGE_03_5");

      // Stage 04
      if (_inspectOpt.ActiveStages.HasFlag(ProcessingStage.Stage04_Extension))
      {
        RunStage("STAGE_04", () =>
        {
          var extendOpt = new ElementExtendToBBoxIntersectAndSplitModifier.Options
          {
            SearchRatio = 1.2,
            DefaultSearchDist = 50.0,
            IntersectionTolerance = 1.0,
            GridCellSize = 50.0,
            Debug = _inspectOpt.DebugMode
          };
          var result = ElementExtendToBBoxIntersectAndSplitModifier.Run(_context, extendOpt, Console.WriteLine);
          Console.WriteLine($"[Stage 04] Extended: {result.SuccessConnections} elements.");

          // [★ 추가] 연장/분할 과정에서 생긴 미세 요소(Snap 오차 등)를 즉시 병합하여 정리
          CollapseShortElementsRun(1.0);
        });
      }
      else LogSkip("STAGE_04");

      // Stage 05
      if (_inspectOpt.ActiveStages.HasFlag(ProcessingStage.Stage05_MeshRefinement))
      {
        RunStage("STAGE_05", () =>
        {
          var meshOpt = new ElementMeshRefinementModifier.Options
          {
            TargetMeshSize = 500.0,
            Debug = _inspectOpt.DebugMode
          };
          int count = ElementMeshRefinementModifier.Run(_context, meshOpt, Console.WriteLine); // 인자 수정됨
          Console.WriteLine($"[Stage 05] Meshing Completed. {count} elements refined.");
        });
      }
      else LogSkip("STAGE_05");

      // Stage 06
      if (_inspectOpt.ActiveStages.HasFlag(ProcessingStage.Stage06_LoadGeneration))
      {
        RunStage("STAGE_06", () =>
        {
          // ... (기존 로직 유지) ...
          _rigidMap = MooringFittingConnectionModifier.Run(_context, _rawStructureData.MfList, Console.WriteLine);

          Console.WriteLine(">>> Generating Loads...");
          // ... (하중 생성 로직) ...
          // (코드가 길어서 생략, 기존 내용 유지)
        });
      }
      else LogSkip("STAGE_06");
    }

    // [추가] 스킵 로그 출력용 헬퍼
    private void LogSkip(string stageName)
    {
      Console.ForegroundColor = ConsoleColor.DarkGray;
      Console.WriteLine($"--- Skipping {stageName} (Disabled in Options) ---");
      Console.ResetColor();
    }

    /// <summary>
    /// 공통 스테이지 실행 헬퍼 메서드
    /// </summary>
    private void RunStage(string stageName, Action action)
    {
      Console.WriteLine($"================ {stageName} =================");

      // 1. 스테이지별 로직 실행
      action();

      // 2. 공통 검사 (전역 옵션 사용)
      // 반환되는 freeEndNodes는 BDF의 SPC(경계조건)으로 활용됨
      List<int> freeEndNodes = StructuralSanityInspector.Inspect(_context, _inspectOpt);

      // 3. 결과 Export
      BdfExporter.Export(_context, _csvPath, stageName, freeEndNodes, _rigidMap, _forceLoads);

      // 4. (옵션) 최종 단계일 경우 Nastran Solver 실행
      //if (stageName.Equals("STAGE_06", StringComparison.OrdinalIgnoreCase))
      //{
      //  string bdfFullPath = Path.Combine(_csvPath, stageName + ".bdf");
      //  NastranSolverService.RunNastran(bdfFullPath, Console.WriteLine);
      //}
    }

    // =========================================================================
    // Private Logic Methods (리팩토링 대상 로직들)
    // =========================================================================

    private void ElementCollinearOverlapGroupRun(bool isDebug)
    {
      if (isDebug)
      {
        Console.WriteLine(">>> [STAGE 01] 중복/공선 요소 검사 및 정렬 수행");
      }

      // 1. 그룹핑 (Inspector)
      // angleToleranceRad: 3e-2 (약 1.7도), distanceTolerance: 20.0
      var overlapGroup = ElementCollinearOverlapGroupInspector.FindSegmentationGroups(
        _context, angleToleranceRad: 3e-2, distanceTolerance: 20.0);

      if (isDebug)
      {
        Console.WriteLine($"   -> 총 {overlapGroup.Count}개의 중복 의심 그룹을 찾았습니다.");
      }

      // 2. 정렬 및 분할 (Modifier)
      ElementCollinearOverlapAlignSplitModifier.Run(
          _context,
          overlapGroup,
          tTol: 0.05,
          minSegLenTol: 1e-3,
          debug: isDebug,
          log: Console.WriteLine,
          cloneExternalNodes: false
      );
    }

    private void ElementSplitByExistingNodesRun(bool isDebug)
    {
      var opt = new ElementSplitByExistingNodesModifier.Options(
        DistanceTol: 1.0,
        GridCellSize: 5.0,
        DryRun: false,
        SnapNodeToLine: false,
        Debug: isDebug
      );

      var result = ElementSplitByExistingNodesModifier.Run(_context, opt, Console.WriteLine);
      Console.WriteLine(result);
    }

    private void ElementIntersectionSplitRun(bool isDebug)
    {
      var opt = new ElementIntersectionSplitModifier.Options(
         DistTol: 1.0,
         GridCellSize: 200.0,
         DryRun: false,
         Debug: isDebug
       );

      ElementIntersectionSplitModifier.Run(_context, opt, Console.WriteLine);

      // [추가 로직] 길이가 너무 짧고 한쪽이 자유단인 요소 제거 (Dangling Element Cleanup)
      RemoveDanglingShortElements();
    }

    private void RemoveDanglingShortElements()
    {
      var nodeDegree = NodeDegreeInspector.BuildNodeDegree(_context);
      var shortEle = new List<int>();

      foreach (var kv in _context.Elements)
      {
        int eleId = kv.Key;
        var ele = kv.Value;

        var nodeIds = ele.NodeIDs;
        if (nodeIds == null || nodeIds.Count < 2) continue;

        int n0 = nodeIds[0];
        int n1 = nodeIds[1];

        if (!nodeDegree.TryGetValue(n0, out int deg0) || !nodeDegree.TryGetValue(n1, out int deg1))
          continue;

        double distance = DistanceUtils.GetDistanceBetweenNodes(n0, n1, _context.Nodes);

        // 길이 < 50.0 이고 한쪽 끝이 자유단(Degree=1)이면 삭제 대상
        if (distance < 50.0 && (deg0 == 1 || deg1 == 1))
        {
          shortEle.Add(eleId);
        }
      }

      if (shortEle.Count > 0)
      {
        foreach (var id in shortEle)
        {
          _context.Elements.Remove(id);
        }
        Console.WriteLine($"[Info] Removed {shortEle.Count} dangling elements (Len < 50.0)");
      }
    }

    private void ElementDuplicateMergeRun(bool isDebug)
    {
      Console.WriteLine(">>> [STAGE 03.5] 중복 요소 병합 및 등가 물성 계산");

      // [수정] 확장자를 .csv로 변경
      string reportPath = Path.Combine(_csvPath, "DuplicateMerge_Report.csv");

      var opt = new ElementDuplicateMergeModifier.Options(
          ReportFilePath: reportPath,
          Debug: isDebug
      );

      var result = ElementDuplicateMergeModifier.Run(_context, opt, Console.WriteLine);

      if (result.ElementsMerged > 0)
      {
        Console.WriteLine($"   -> {result.ElementsMerged}개 그룹 병합 완료. (엑셀 보고서 생성됨)");
      }
      else
      {
        Console.WriteLine("   -> 병합된 요소가 없습니다.");
      }
    }


    /// <summary>
    /// 길이가 설정된 허용치(tolerance) 미만인 요소를 찾아 제거하고,
    /// 해당 요소의 양 끝 노드를 하나로 병합(Collapse)하여 위상 연결을 유지합니다.
    /// </summary>
    private void CollapseShortElementsRun(double lengthTolerance)
    {
      Console.WriteLine($">>> [Short Element Collapse] 길이 {lengthTolerance} 미만 요소 병합 수행");

      var elements = _context.Elements;
      var nodes = _context.Nodes;
      int collapsedCount = 0;

      // 1. 컬렉션 변경 방지를 위해 전체 ID 스냅샷 생성
      var allElementIds = elements.Keys.ToList();

      foreach (var eid in allElementIds)
      {
        // 이미 삭제된 요소면 스킵
        if (!elements.Contains(eid)) continue;

        var targetEle = elements[eid];
        var nodeIDs = targetEle.NodeIDs;

        // 유효성 검사
        if (nodeIDs == null || nodeIDs.Count < 2) continue;

        int n1 = nodeIDs[0];
        int n2 = nodeIDs[1];

        // 길이 계산
        double len = DistanceUtils.GetDistanceBetweenNodes(n1, n2, nodes);

        // 2. 병합 대상 식별 (0보다 크고 허용치보다 작은 경우)
        if (len > 1e-12 && len < lengthTolerance)
        {
          // 병합 전략: N2를 삭제하고, N2를 참조하던 모든 요소를 N1으로 연결 변경
          // (N1: Keep, N2: Remove)
          int keepNodeID = n1;
          int removeNodeID = n2;

          // [단계 A] 짧은 요소 자체 삭제
          elements.Remove(eid);

          // [단계 B] 삭제될 노드(N2)를 참조하고 있는 '다른' 모든 요소 찾기
          // (Elements 클래스에 역참조 기능이 없다면 전수 검사 필요)
          var connectedNeighbors = elements
              .Where(kv => kv.Value.NodeIDs.Contains(removeNodeID))
              .ToList();

          foreach (var neighbor in connectedNeighbors)
          {
            int neighborID = neighbor.Key;
            var neighborEle = neighbor.Value;

            // 노드 교체 시도 (Extensions의 TryReplaceNode 활용)
            if (neighborEle.TryReplaceNode(removeNodeID, keepNodeID, out var newEle))
            {
              // 기존 속성(ExtraData 등) 유지하면서 덮어쓰기
              var extraCopy = newEle.ExtraData.ToDictionary(k => k.Key, v => v.Value);

              elements.AddWithID(
                  neighborID,
                  newEle.NodeIDs.ToList(),
                  newEle.PropertyID,
                  extraCopy
              );
            }
          }

          // [단계 C] 고립된 노드 삭제 (Nodes 컬렉션에서 제거)
          if (nodes.Contains(removeNodeID))
          {
            nodes.Remove(removeNodeID);
          }

          collapsedCount++;
        }
      }

      if (collapsedCount > 0)
      {
        Console.WriteLine($"   -> [완료] 총 {collapsedCount}개의 짧은 요소를 병합(Collapse) 처리했습니다.");
      }
      else
      {
        Console.WriteLine("   -> 병합 대상이 없습니다.");
      }
    }
  }
}
