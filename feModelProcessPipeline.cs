using MooringFitting2026.Exporters;
using MooringFitting2026.Inspector;
using MooringFitting2026.Inspector.ElementInspector;
using MooringFitting2026.Model.Entities;
using MooringFitting2026.Modifier.ElementModifier;
using MooringFitting2026.Modifier.NodeModifier;
using MooringFitting2026.Utils.Geometry;
using MooringFitting2026.Services.SectionProperties;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MooringFitting2026.Pipeline
{
  public class FeModelProcessPipeline
  {
    private readonly FeModelContext _context;
    private readonly InspectorOptions _inspectOpt;
    private readonly string _csvPath;

    public FeModelProcessPipeline(FeModelContext context, InspectorOptions inspectOpt,
      string CsvPath)
    {
      _context = context ?? throw new ArgumentNullException(nameof(context));
      _inspectOpt = inspectOpt ?? new InspectorOptions(); // 방어 로직
      _csvPath = CsvPath;
    }

    public void Run()
    {
      Console.WriteLine("\n[Pipeline Started] Processing FE Model...");

      // Z방향 절대좌표 통일하기. 
      NodeZPlaneNormalizeModifier.Run(_context);

      // STAGE_00 : 원본 CSV 그대로 FE Model 출력
      //ExportBaseline();

      RunStagedPipeline();
    }

    private void ExportBaseline()
    {
      string stageName = "STAGE_00";
      Console.WriteLine($"================ {stageName} =================");
      StructuralSanityInspector.Inspect(_context, _inspectOpt);
      BdfExporter.Export(_context, _csvPath, stageName);
    }

    private void RunStagedPipeline()
    {
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

      // Stage 01 : 미세하게 틀어지며 겹치는 Element의 경우 동일한 벡터로 element 각 수정하고 겹치는 Node 기준 모두 쪼개기
      RunStage("STAGE_01", () =>
      {
        ElementCollinearOverlapGroupRun(optStage1.DebugMode);
      }, optStage1); // RunStage가 opt를 받도록 수정 필요

      //// Stage 02 : Element 선상에 존재하는 Node 기준으로 Element 모두 쪼개기 
      RunStage("STAGE_02", () =>
      {
        ElementSplitByExistingNodesRun();
      });

      //// Stage 03 : Element 끼리 서로 교차하는 교점을 Node를 만들어, 그 Node 기준 Element 쪼개기 
      //RunStage("STAGE_03", () =>
      //{
      //  ElementIntersectionSplitRun();
      //});

      //// Stage 03.5 : Duplicate 되어 있는 부재들의 등가 Property 계산하여 Element 1개로 치환
      //RunStage("STAGE_03_5", () =>
      //{
      //  ElementDuplicateMergeRun();
      //});

      // Stage 04 :임의 Element의 Node 1개가 다른 Element 선상에서 일정거리 떨어진 경우, 방향백터로 확장하여 붙이기 
      //RunStage("STAGE_04", () =>
      //{
      //  ElementExtendToBBoxIntersectAndSplitRun();
      //});

    }

    // [수정] 세 번째 인자(stageOptions)를 선택적으로 받을 수 있도록 변경
    private void RunStage(string stageName, Action action, InspectorOptions stageOptions = null)
    {
      Console.WriteLine($"================ {stageName} =================");

      // 파이프라인 로직 수행
      action();

      // 전달받은 옵션이 있으면 그것을 쓰고, 없으면(null) 클래스 기본 옵션(_inspectOpt) 사용
      var optionsToUse = stageOptions ?? _inspectOpt;

      // 검사 수행
      //StructuralSanityInspector.Inspect(_context, optionsToUse);

      // 결과 내보내기
      BdfExporter.Export(_context, _csvPath, stageName);
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
        Debug: isDebug
      );

      var result = ElementSplitByExistingNodesModifier.Run(_context, opt, Console.WriteLine);
      Console.WriteLine(result);
    }

    private void ElementIntersectionSplitRun()
    {
      var opt = new ElementIntersectionSplitModifier.Options(
         DistTol: 1.0,
         GridCellSize: 200.0,
         DryRun: false, // false면 수행
         Debug: false
       );

      var r = ElementIntersectionSplitModifier.Run(_context, opt, Console.WriteLine);
      Console.WriteLine(r);

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
      foreach (var id in shortEle)
      {
        _context.Elements.Remove(id);
      }
    }

    private void ElementDuplicateMergeRun()
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
        Console.WriteLine($"Merged Elements [{string.Join(",", groupIDs)}] -> ID {primaryEleID} (New PropID: {newPropID})");
      }

      Console.WriteLine($"Total {mergedCount} groups merged.");
    }

    private void ElementExtendToBBoxIntersectAndSplitRun()
    {
      var opt = new ElementExtendToBBoxIntersectAndSplitModifier.Options
      {
        BBoxInflateRatio = 1.0,
        GridCellSize = 500.0,
        DistTol = 1.0,

        // ★ 63번, 126번만 집중 감시! (비워두면 null)
        //WatchList = new HashSet<int> { 63, 126 },

        DryRun = false // 진단 모드니까 true
      };

      // 로거(원하면 Debug일 때만 출력하도록 바꿔도 됨)
      Action<string> logger = s => Console.WriteLine(s);

      //var r = ElementExtendToBBoxIntersectAndSplitModifier.Run(_context, opt, logger);

      string logPath = "DebugLog.txt";
      Console.WriteLine($"[로그 모드] 모든 출력 내용이 '{System.IO.Path.GetFullPath(logPath)}'에 저장됩니다...");

      // 3. 파일 스트림 열기
      using (System.IO.StreamWriter writer = new System.IO.StreamWriter(logPath))
      {
        writer.AutoFlush = true; // 중요: 버퍼링 없이 바로 기록

        // 로거 정의 (화면 + 파일 동시 출력)
        Action<string> fileLogger = (msg) =>
        {
          Console.WriteLine(msg);
          writer.WriteLine(msg);
        };

        // 4. Modifier 실행
        // (_context는 이미 모델이 로드되어 있다고 가정)
        var result = ElementExtendToBBoxIntersectAndSplitModifier.Run(
            _context,
            opt,
            fileLogger // <--- 로거 전달
        );

        // 결과 요약 출력
        fileLogger($"\n[완료] 결과: TotalChecks={result.TotalChecks}, Success={result.SuccessConnections}");
      }

      Console.WriteLine("프로그램 종료. DebugLog.txt 파일을 열어보세요.");

    }

  }
}
