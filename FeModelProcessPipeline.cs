using MooringFitting2026.Exporters;
using MooringFitting2026.Inspector;
using MooringFitting2026.Inspector.ElementInspector;
using MooringFitting2026.Model;
using MooringFitting2026.Model.Entities;
using MooringFitting2026.Modifier.ElementModifier;
using MooringFitting2026.Modifier.NodeModifier;
using MooringFitting2026.RawData;
using MooringFitting2026.Services.Load;
using MooringFitting2026.Services.SectionProperties;
using MooringFitting2026.Utils.Geometry;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace MooringFitting2026.Pipeline
{
  public class FeModelProcessPipeline
  {
    private readonly FeModelContext _context;
    private readonly RawStructureData _rawStructureData;
    private readonly WinchData _winchData;
    private readonly InspectorOptions _inspectOpt;
    private readonly string _csvPath;
    private Dictionary<int, MooringFittingConnectionModifier.RigidInfo> _rigidMap
        = new Dictionary<int, MooringFittingConnectionModifier.RigidInfo>();
    private List<ForceLoad> _forceLoads = new List<ForceLoad>();

    public FeModelProcessPipeline(FeModelContext context, RawStructureData rawStructureData,
      WinchData winchData, InspectorOptions inspectOpt, string CsvPath)
    {
      _context = context ?? throw new ArgumentNullException(nameof(context));
      _rawStructureData = rawStructureData;
      _winchData = winchData;
      _inspectOpt = inspectOpt ?? new InspectorOptions(); // 방어 로직
      _csvPath = CsvPath;
    }

    public void Run()
    {
      Console.WriteLine("\n[Pipeline Started] Processing FE Model...");

      // Z방향 절대좌표 통일하기. 
      NodeZPlaneNormalizeModifier.Run(_context);

      // STAGE_00 : 원본 CSV 그대로 FE Model 출력
      ExportBaseline();

      RunStagedPipeline();
    }

    private void ExportBaseline()
    {
      string stageName = "STAGE_00";
      Console.WriteLine($"================ {stageName} =================");
      var optStage0 = new InspectorOptions
      {
        DebugMode = false,         // 요약만 출력
        CheckTopology = false,      // 기본 연결성 확인
        CheckGeometry = false,     // (이미 검증되었다면 생략 가능)
        CheckEquivalence = false,  // (오래 걸릴 수 있음)
        CheckDuplicate = false,     // 치명적이므로 유지
        CheckIntegrity = false,
        CheckIsolation = false
      };
      StructuralSanityInspector.Inspect(_context, optStage0);
      BdfExporter.Export(_context, _csvPath, stageName);

    }

    private void RunStagedPipeline()
    {

      // Stage 01 : 미세하게 틀어지며 겹치는 Element의 경우 동일한 벡터로 element 각 수정하고 겹치는 Node 기준 모두 쪼개기
      var optStage1 = new InspectorOptions
      {
        DebugMode = false,         // 요약만 출력
        CheckTopology = false,      // 기본 연결성 확인
        CheckGeometry = false,     // (이미 검증되었다면 생략 가능)
        CheckEquivalence = false,  // (오래 걸릴 수 있음)
        CheckDuplicate = false,     // 치명적이므로 유지
        CheckIntegrity = false,
        CheckIsolation = false
      };
      RunStage("STAGE_01", () =>
      {
        ElementCollinearOverlapGroupRun(optStage1.DebugMode);
      }, optStage1); // RunStage가 opt를 받도록 수정 필요


      // Stage 02 : Element 선상에 존재하는 Node 기준으로 Element 모두 쪼개기 
      var optStage2 = new InspectorOptions
      {
        DebugMode = false,         // 요약만 출력
        CheckTopology = false,      // 기본 연결성 확인
        CheckGeometry = false,     // (이미 검증되었다면 생략 가능)
        CheckEquivalence = false,  // (오래 걸릴 수 있음)
        CheckDuplicate = false,     // 치명적이므로 유지
        CheckIntegrity = false,
        CheckIsolation = false
      };
      RunStage("STAGE_02", () =>
      {
        ElementSplitByExistingNodesRun(optStage2.DebugMode);
      }, optStage2);


      // Stage 03 : Element 끼리 서로 교차하는 교점을 Node를 만들어, 그 Node 기준 Element 쪼개기 
      var optStage3 = new InspectorOptions
      {
        DebugMode = false,         // 요약만 출력
        CheckTopology = false,      // 기본 연결성 확인
        CheckGeometry = false,     // (이미 검증되었다면 생략 가능)
        CheckEquivalence = false,  // (오래 걸릴 수 있음)
        CheckDuplicate = false,     // 치명적이므로 유지
        CheckIntegrity = false,
        CheckIsolation = false
      };
      RunStage("STAGE_03", () =>
      {
        ElementIntersectionSplitRun(optStage3.DebugMode);
      }, optStage3);


      // Stage 03.5 : Duplicate 되어 있는 부재들의 등가 Property 계산하여 Element 1개로 치환
      var optStage3_5 = new InspectorOptions
      {
        DebugMode = false,         // 요약만 출력
        CheckTopology = false,      // 기본 연결성 확인
        CheckGeometry = false,     // (이미 검증되었다면 생략 가능)
        CheckEquivalence = false,  // (오래 걸릴 수 있음)
        CheckDuplicate = false,     // 치명적이므로 유지
        CheckIntegrity = false,
        CheckIsolation = false
      };
      RunStage("STAGE_03_5", () =>
      {
        ElementDuplicateMergeRun(optStage3_5.DebugMode);
      }, optStage3_5);


      // Stage 04 :임의 Element의 Node 1개가 다른 Element 선상에서 일정거리 떨어진 경우, 방향백터로 확장하여 붙이기       
      var optStage4 = new InspectorOptions
      {
        DebugMode = false,         // 요약만 출력
        CheckTopology = false,
        CheckGeometry = false,
        CheckEquivalence = false,
        CheckDuplicate = true,
        CheckIntegrity = false,
        CheckIsolation = false
      };

      RunStage("STAGE_04", () =>
      {
        var extendOpt = new ElementExtendToBBoxIntersectAndSplitModifier.Options
        {
          SearchRatio = 1.2,        // 2.0 -> 5.0 (비율 증가)
          DefaultSearchDist = 50.0, // 기본 거리
          IntersectionTolerance = 1.0,
          GridCellSize = 50.0,

          Debug = true,
          // ★ [진단] 의심되는 노드 번호를 여기에 넣으세요!
          //WatchNodeIDs = new HashSet<int> { 185 } // 예: 142번 노드 감시
        };

        var result = ElementExtendToBBoxIntersectAndSplitModifier.Run(_context, extendOpt, Console.WriteLine);
        Console.WriteLine($"[Stage 04] Extended: {result.SuccessConnections} elements.");

      }, optStage4);


      // Stage 05 : Mesh 쪼개기 작업     
      var optStage5 = new InspectorOptions
      {
        DebugMode = false,       
        CheckTopology = false,
        CheckGeometry = false,
        CheckEquivalence = false,
        CheckDuplicate = false,
        CheckIntegrity = false,
        CheckIsolation = false
      };
      RunStage("STAGE_05", () =>
      {
        var meshOpt = new ElementMeshRefinementModifier.Options
        {
          TargetMeshSize = 500.0, // 원하는 메쉬 간격 
          Debug = true
        };

        int count = ElementMeshRefinementModifier.Run(_context, meshOpt, Console.WriteLine);
        Console.WriteLine($"[Stage 05] Meshing Completed. {count} elements refined.");

      }, optStage5);


      // Stage 06 : MF의 Rigid 연결 및 하중 생성
      var optStage6 = new InspectorOptions { DebugMode = true, CheckTopology = false };

      RunStage("STAGE_06", () =>
      {
        // 1. Rigid 생성
        _rigidMap = MooringFittingConnectionModifier.Run(_context, _rawStructureData.MfList, Console.WriteLine);

        // 2. MF 하중 생성 (ID 2번부터 시작)
        Console.WriteLine(">>> Generating Mooring Fitting Loads...");

        // 시작 ID 지정 (Force ID는 2부터 시작)
        int startLoadId = 2;

        var mfLoads = MooringFitting2026.Services.Load.MooringLoadGenerator.Generate(
            _context,
            _rawStructureData.MfList,
            _rigidMap,
            Console.WriteLine,
            startId: startLoadId
        );

        // 전체 하중 리스트에 추가
        _forceLoads.AddRange(mfLoads);

        // [Winch 확장 포인트]
        // 나중에 Winch를 추가할 때, MF에서 사용한 마지막 ID 다음부터 시작하도록 계산 가능
        // int nextStartId = (_forceLoads.Count > 0) ? _forceLoads.Max(f => f.LoadCaseID) + 1 : 2;
        // var winchLoads = WinchLoadGenerator.Generate(..., startId: nextStartId);
        // _forceLoads.AddRange(winchLoads);

      }, optStage6);
    }




    private void RunStage(string stageName, Action action, InspectorOptions stageOptions = null)
    {
      Console.WriteLine($"================ {stageName} =================");
      action();

      var optionsToUse = stageOptions ?? _inspectOpt;
      List<int> freeEndNodes = StructuralSanityInspector.Inspect(_context, optionsToUse);

      // [수정] _forceLoads 전달
      BdfExporter.Export(_context, _csvPath, stageName, freeEndNodes, _rigidMap, _forceLoads);
    }

    private void ElementCollinearOverlapGroupRun(bool isDebug)
    {
      if(isDebug) Console.WriteLine(">>> [STAGE 01] Start Collinear/Overlap Check...");

      var overlapGroup = ElementCollinearOverlapGroupInspector.FindSegmentationGroups(
        _context, angleToleranceRad: 3e-2, distanceTolerance: 20.0);

      if (isDebug) Console.WriteLine($"   -> Found {overlapGroup.Count} overlap groups.");

      // 디버깅 활성화 (debug: true)
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
        DryRun: false, // false면 수행
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
         DryRun: false, // false면 수행
         Debug: isDebug
       );

      var r = ElementIntersectionSplitModifier.Run(_context, opt, Console.WriteLine);

      // 길이가 특정 길이 이상이고 Node 2개중 1개가 자유단이면 삭제 (존재하면 용접위치 병합 시, 엉망됨)
      var nodeDegree = NodeDegreeInspector.BuildNodeDegree(_context);

      // 중복 방지용 (혹시 같은 key가 여러 번 들어갈 가능성 대비)
      var shortEle = new List<int>();

      foreach (var kv in _context.Elements)
      {
        int eleId = kv.Key;
        var ele = kv.Value;

        var nodeIds = ele.NodeIDs;
        if (nodeIds == null || nodeIds.Count < 2)
          continue; // 또는 로그/에러 처리

        int n0 = nodeIds[0];
        int n1 = nodeIds[1];

        // nodeDegree에 키가 없으면 안전하게 스킵(또는 0으로 간주 등 정책 결정)
        if (!nodeDegree.TryGetValue(n0, out int deg0) ||
            !nodeDegree.TryGetValue(n1, out int deg1))
          continue;

        // 거리 계산 (DistanceUtils가 nodeId 기반이면 node 존재 확인이 필요할 수 있음)
        double distance = DistanceUtils.GetDistanceBetweenNodes(n0, n1, _context.Nodes);

        if (distance < 50.0 && (deg0 == 1 || deg1 == 1))
        {
          shortEle.Add(eleId);
        }
      }
      // 실제 삭제
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

      // 1. 중복 그룹 찾기
      var duplicateGroups = ElementDuplicateInspector.FindDuplicateGroups(_context);

      if (duplicateGroups.Count == 0)
      {
        Console.WriteLine("No duplicate elements found.");
        return;
      }

      int mergedCount = 0;

      foreach (var groupIDs in duplicateGroups)
      {
        // 그룹 유효성 체크
        if (groupIDs == null || groupIDs.Count < 2) continue;

        // Element 객체 리스트 확보
        var targetElements = new List<Element>();
        foreach (var id in groupIDs)
        {
          if (_context.Elements.Contains(id))
            targetElements.Add(_context.Elements[id]);
        }

        if (targetElements.Count < 2) continue;

        // 2. 등가 물성 계산 (평행축 정리)
        SectionResult mergedProp = EquivalentPropertyMerger.Merge(targetElements, _context.Properties);

        // 3. 새로운 PBEAM Property 생성
        // Nastran PBEAM 포맷에 맞게 Dim 구성 (여기서는 예시로 Area, Izz, Iyy, J 순서 저장)
        // 실제 PBEAM 카드는 더 많은 파라미터가 필요하므로, 추후 BdfExporter에서 이 순서를 맞춰야 함
        var newDims = new List<double>
        {
            mergedProp.Area,
            mergedProp.Izz,
            mergedProp.Iyy,
            mergedProp.J
        };

        // 재질 ID는 첫 번째 요소의 것을 따라감 (가정)
        int baseMatID = 1;
        if (_context.Properties.Contains(targetElements[0].PropertyID))
        {
          baseMatID = _context.Properties[targetElements[0].PropertyID].MaterialID;
        }

        // 새 Property 등록 ("EQUIV_PBEAM" 타입)
        int newPropID = _context.Properties.AddOrGet("EQUIV_PBEAM", newDims, baseMatID);

        // 4. 모델 업데이트 (병합)
        // 첫 번째 요소를 대표(Primary)로 남기고 나머지는 삭제
        int primaryEleID = groupIDs[0];
        var primaryEle = _context.Elements[primaryEleID];

        // 대표 요소의 Property 교체
        // (Element는 불변 객체이므로 삭제 후 재생성 혹은 AddWithID 덮어쓰기)
        var newEle = new Element(primaryEle.NodeIDs.ToList(), newPropID,
                                 new Dictionary<string, string>(primaryEle.ExtraData));

        // 덮어쓰기
        _context.Elements.AddWithID(primaryEleID, newEle.NodeIDs.ToList(), newPropID,
                                    new Dictionary<string, string>(primaryEle.ExtraData));

        // 나머지 요소 삭제
        for (int i = 1; i < groupIDs.Count; i++)
        {
          _context.Elements.Remove(groupIDs[i]);
        }

        mergedCount++;
        // (선택) 로그 출력
        if (isDebug) Console.WriteLine($"Merged Elements [{string.Join(",", groupIDs)}] -> ID {primaryEleID} (New PropID: {newPropID})");
      }

      Console.WriteLine($"Total {mergedCount} groups merged.");
    }

  }
}
