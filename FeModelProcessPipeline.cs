using MooringFitting2026.Exporters;
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
      // Stage 01 : 미세하게 틀어지며 겹치는 Element 처리
      RunStage("STAGE_01", () =>
      {
        ElementCollinearOverlapGroupRun(_inspectOpt.DebugMode);
      });

      // Stage 02 : Element 선상에 존재하는 Node 기준으로 쪼개기 
      RunStage("STAGE_02", () =>
      {
        ElementSplitByExistingNodesRun(_inspectOpt.DebugMode);
      });

      // Stage 03 : Element 교차점 생성 및 쪼개기 
      RunStage("STAGE_03", () =>
      {
        ElementIntersectionSplitRun(_inspectOpt.DebugMode);
      });

      // Stage 03.5 : 중복 부재 등가 Property 계산 및 병합
      RunStage("STAGE_03_5", () =>
      {
        ElementDuplicateMergeRun(_inspectOpt.DebugMode);
      });

      // Stage 04 : 자유단 노드 연장 및 연결
      RunStage("STAGE_04", () =>
      {
        var extendOpt = new ElementExtendToBBoxIntersectAndSplitModifier.Options
        {
          SearchRatio = 1.2,
          DefaultSearchDist = 50.0,
          IntersectionTolerance = 1.0,
          GridCellSize = 50.0,
          Debug = _inspectOpt.DebugMode, // 전역 디버그 설정 연동
                                         // WatchNodeIDs = new HashSet<int> { } // 필요시 특정 노드 감시
        };

        var result = ElementExtendToBBoxIntersectAndSplitModifier.Run(_context, extendOpt, Console.WriteLine);
        Console.WriteLine($"[Stage 04] Extended: {result.SuccessConnections} elements.");
      });

      // Stage 05 : Mesh Refinement
      RunStage("STAGE_05", () =>
      {
        var meshOpt = new ElementMeshRefinementModifier.Options
        {
          TargetMeshSize = 500.0,
          Debug = _inspectOpt.DebugMode
        };

        // [수정] _rawStructureData 인자 제거 -> (context, opt, log) 3개 전달
        int count = ElementMeshRefinementModifier.Run(_context, meshOpt, Console.WriteLine);
        Console.WriteLine($"[Stage 05] Meshing Completed. {count} elements refined.");
      });

      // Stage 06 : MF Rigid 연결 및 하중 생성 (최종 단계)
      RunStage("STAGE_06", () =>
      {
        // 1. Rigid 생성
        _rigidMap = MooringFittingConnectionModifier.Run(_context, _rawStructureData.MfList, Console.WriteLine);

        // 2. MF 하중 생성
        Console.WriteLine(">>> Generating Mooring Fitting Loads...");
        int startLoadId = 2; // Force ID 2번부터

        var mfLoads = MooringLoadGenerator.Generate(
            _context,
            _rawStructureData.MfList,
            _rigidMap,
            Console.WriteLine
        );
        _forceLoads.AddRange(mfLoads);

        // 3. Winch 하중 생성
        int winchStartId = (_forceLoads.Count > 0)
            ? _forceLoads.Max(f => f.LoadCaseID) + 1
            : startLoadId;

        var winchLoads = WinchLoadGenerator.Generate(
            _context,
            _winchData,
            Console.WriteLine,
            startId: winchStartId
        );
        _forceLoads.AddRange(winchLoads);
      });
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
      if (isDebug) Console.WriteLine(">>> [STAGE 01] Start Collinear/Overlap Check...");

      var overlapGroup = ElementCollinearOverlapGroupInspector.FindSegmentationGroups(
        _context, angleToleranceRad: 3e-2, distanceTolerance: 20.0);

      if (isDebug) Console.WriteLine($"   -> Found {overlapGroup.Count} overlap groups.");

      ElementCollinearOverlapAlignSplitModifier.Run(
          _context,
          overlapGroup,
          tTol: 0.05,
          minSegLenTol: 1e-3,
          debug: isDebug,
          log: Console.WriteLine
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
      Console.WriteLine(">>> [STAGE 03.5] Merging Duplicate Elements with Parallel Axis Theorem...");

      var duplicateGroups = ElementDuplicateInspector.FindDuplicateGroups(_context);

      if (duplicateGroups.Count == 0)
      {
        Console.WriteLine("No duplicate elements found.");
        return;
      }

      int mergedCount = 0;

      foreach (var groupIDs in duplicateGroups)
      {
        if (groupIDs == null || groupIDs.Count < 2) continue;

        var targetElements = new List<Element>();
        foreach (var id in groupIDs)
        {
          if (_context.Elements.Contains(id))
            targetElements.Add(_context.Elements[id]);
        }

        if (targetElements.Count < 2) continue;

        // 1. 등가 물성 계산
        SectionResult mergedProp = EquivalentPropertyMerger.Merge(targetElements, _context.Properties);

        // 2. 새 Property 생성
        var newDims = new List<double>
                {
                    mergedProp.Area,
                    mergedProp.Izz,
                    mergedProp.Iyy,
                    mergedProp.J
                };

        int baseMatID = 1;
        if (_context.Properties.Contains(targetElements[0].PropertyID))
        {
          baseMatID = _context.Properties[targetElements[0].PropertyID].MaterialID;
        }

        int newPropID = _context.Properties.AddOrGet("EQUIV_PBEAM", newDims, baseMatID);

        // 3. 병합 (첫 번째 요소 유지, 나머지 삭제)
        int primaryEleID = groupIDs[0];
        var primaryEle = _context.Elements[primaryEleID];

        // Property 교체 (Elements는 AddWithID로 덮어쓰기)
        _context.Elements.AddWithID(primaryEleID, primaryEle.NodeIDs.ToList(), newPropID,
                                    new Dictionary<string, string>(primaryEle.ExtraData));

        for (int i = 1; i < groupIDs.Count; i++)
        {
          _context.Elements.Remove(groupIDs[i]);
        }

        mergedCount++;
        if (isDebug) Console.WriteLine($"Merged Elements [{string.Join(",", groupIDs)}] -> ID {primaryEleID} (New PropID: {newPropID})");
      }

      Console.WriteLine($"Total {mergedCount} groups merged.");
    }
  }
}
