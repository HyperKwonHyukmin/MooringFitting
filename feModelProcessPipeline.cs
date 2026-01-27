using MooringFitting2026.Exporters;
using MooringFitting2026.Inspector;
using MooringFitting2026.Inspector.ElementInspector;
using MooringFitting2026.Model.Entities;
using MooringFitting2026.Modifier.ElementModifier;
using MooringFitting2026.Modifier.NodeModifier;
using MooringFitting2026.Utils.Geometry;
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
      ExportBaseline();

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
      // Stage 01 : 미세하게 틀어지며 겹치는 Element의 경우 동일한 벡터로 element 각 수정하고 겹치는 Node 기준 모두 쪼개기
      RunStage("STAGE_01", () =>
      {
        ElementCollinearOverlapGroupRun();
      });

      // Stage 02 : Element 선상에 존재하는 Node 기준으로 Element 모두 쪼개기 
      RunStage("STAGE_02", () =>
      {
        ElementSplitByExistingNodesRun();
      });

      // Stage 03 : Element 끼리 서로 교차하는 교점을 Node를 만들어, 그 Node 기준 Element 쪼개기 
      RunStage("STAGE_03", () =>
      {
        ElementIntersectionSplitRun();
      });

      // Stage 03.5 : Duplicate 되어 있는 부재들의 등가 Property 계산하여 Element 1개로 치환
      RunStage("STAGE_03_5", () =>
      {
        ElementDuplicateMergeRun();
      });

      // Stage 04 :임의 Element의 Node 1개가 다른 Element 선상에서 일정거리 떨어진 경우, 방향백터로 확장하여 붙이기 
      //RunStage("STAGE_04", () =>
      //{
      //  ElementExtendToBBoxIntersectAndSplitRun();
      //});

    }

    private void RunStage(string stageName, Action action)
    {
      Console.WriteLine($"================ {stageName} =================");
      action();
      StructuralSanityInspector.Inspect(_context, _inspectOpt);
      BdfExporter.Export(_context, _csvPath, stageName);
    }

    private void ElementCollinearOverlapGroupRun()
    {
      var overlapGroup = ElementCollinearOverlapGroupInspector.FindSegmentationGroups(
        _context, angleToleranceRad: 3e-2, distanceTolerance: 20.0);

      ElementCollinearOverlapAlignSplitModifier.Run(_context, overlapGroup,
        tTol: 0.05, minSegLenTol: 1e-3, debug: false);
    }

    private void ElementSplitByExistingNodesRun()
    {
      var opt = new ElementSplitByExistingNodesModifier.Options(
        DistanceTol: 1.0,
        GridCellSize: 5.0,
        DryRun: false, // false면 수행
        Debug: false
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
      var duplicateGroups = ElementDuplicateInspector.FindDuplicateGroups(_context);

      if (duplicateGroups.Count > 0)
      {
        foreach (var group in duplicateGroups)
        {
          foreach(var ele in group)
          {
            Console.WriteLine($"{ele}:{_context.Elements[ele]}");
          }
          Console.WriteLine();
 
        }
  
      }
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
