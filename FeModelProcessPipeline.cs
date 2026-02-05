using MooringFitting2026.Exporters;
using MooringFitting2026.Extensions;
using MooringFitting2026.Inspector;
using MooringFitting2026.Inspector.ElementInspector;
using MooringFitting2026.Inspector.NodeInspector;
using MooringFitting2026.Model.Entities;
using MooringFitting2026.Modifier.ElementModifier;
using MooringFitting2026.Modifier.NodeModifier;
using MooringFitting2026.RawData;
using MooringFitting2026.Services.Load;
using MooringFitting2026.Services.Reporting;
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
    private readonly InspectorOptions _inspectOpt;
    private readonly string _csvPath;
    private readonly string _modelName; // [추가] 모델 이름 (파일명)
    private readonly bool _runSolver;

    private Dictionary<int, MooringFittingConnectionModifier.RigidInfo> _rigidMap
        = new Dictionary<int, MooringFittingConnectionModifier.RigidInfo>();
    private List<ForceLoad> _forceLoads = new List<ForceLoad>();
    private List<int> _lastSpcList = new List<int>();

    // [수정] 생성자에 modelName 추가
    public FeModelProcessPipeline(
        FeModelContext context,
        RawStructureData rawStructureData,
        WinchData winchData,
        InspectorOptions inspectOpt,
        string CsvPath,
        string modelName, // [추가]
        bool runSolver = true)
    {
      _context = context ?? throw new ArgumentNullException(nameof(context));
      _rawStructureData = rawStructureData;
      _winchData = winchData;
      _inspectOpt = inspectOpt ?? InspectorOptions.Default;
      _csvPath = CsvPath;
      _modelName = modelName; // [추가]
      _runSolver = runSolver;
    }

    public void Run()
    {
      Console.WriteLine("\n[Pipeline Started] Processing FE Model...");
      Console.WriteLine($"   -> Model Name: {_modelName}");

      Console.WriteLine(">>> [Preprocessing] Normalizing Z-Plane (2D Conversion)...");
      NodeZPlaneNormalizeModifier.Run(_context);

      ExportBaseline();
      RunStagedPipeline();
    }

    private void ExportBaseline()
    {
      string stageName = "STAGE_00";
      Console.WriteLine($"================ {stageName} =================");
      var freeEndNodes = StructuralSanityInspector.Inspect(_context, _inspectOpt);
      _lastSpcList = freeEndNodes;
      BdfExporter.Export(_context, _csvPath, stageName, freeEndNodes);
    }

    private void RunStagedPipeline()
    {
      // Stage 01 ~ 05 (기존 유지)
      if (_inspectOpt.ActiveStages.HasFlag(ProcessingStage.Stage01_CollinearOverlap))
        RunStage("STAGE_01", () => ElementCollinearOverlapGroupRun(_inspectOpt.DebugMode));
      else LogSkip("STAGE_01");

      if (_inspectOpt.ActiveStages.HasFlag(ProcessingStage.Stage02_SplitByNodes))
        RunStage("STAGE_02", () => ElementSplitByExistingNodesRun(_inspectOpt.DebugMode));
      else LogSkip("STAGE_02");

      if (_inspectOpt.ActiveStages.HasFlag(ProcessingStage.Stage03_IntersectionSplit))
      {
        RunStage("STAGE_03", () => {
          ElementIntersectionSplitRun(_inspectOpt.DebugMode);
          CollapseShortElementsRun(1.0);
        });
      }
      else LogSkip("STAGE_03");

      if (_inspectOpt.ActiveStages.HasFlag(ProcessingStage.Stage03_5_DuplicateMerge))
        RunStage("STAGE_03_5", () => ElementDuplicateMergeRun(_inspectOpt.DebugMode));
      else LogSkip("STAGE_03_5");

      if (_inspectOpt.ActiveStages.HasFlag(ProcessingStage.Stage04_Extension))
      {
        RunStage("STAGE_04", () => {
          var extendOpt = new ElementExtendToBBoxIntersectAndSplitModifier.Options
          { SearchRatio = 1.2, DefaultSearchDist = 50.0, IntersectionTolerance = 1.0, GridCellSize = 50.0, Debug = _inspectOpt.DebugMode };
          var result = ElementExtendToBBoxIntersectAndSplitModifier.Run(_context, extendOpt, Console.WriteLine);
          Console.WriteLine($"[Stage 04] Extended: {result.SuccessConnections} elements.");
          CollapseShortElementsRun(1.0);
        });
      }
      else LogSkip("STAGE_04");

      if (_inspectOpt.ActiveStages.HasFlag(ProcessingStage.Stage05_MeshRefinement))
      {
        RunStage("STAGE_05", () => {
          var meshOpt = new ElementMeshRefinementModifier.Options { TargetMeshSize = 500.0, Debug = _inspectOpt.DebugMode };
          var result = ElementMeshRefinementModifier.Run(_context, meshOpt, Console.WriteLine);
          Console.WriteLine($"[Stage 05] Meshing Completed. {result.ElementsRefined} elements refined.");
        });
      }
      else LogSkip("STAGE_05");

      // [수정] Stage 06 (최종 단계)
      if (_inspectOpt.ActiveStages.HasFlag(ProcessingStage.Stage06_LoadGeneration))
      {
        // [핵심 변경] _modelName을 customExportName으로 전달하여 파일명 변경
        RunStage("STAGE_06", () =>
        {
          var loadReporter = new LoadCalculationReporter();

          // 1. Rigid 생성
          _rigidMap = MooringFittingConnectionModifier.Run(_context, _rawStructureData.MfList, _lastSpcList, Console.WriteLine);

          Console.WriteLine(">>> Generating Loads...");
          _forceLoads.Clear();

          // 2. MF 하중
          int mfStartID = 2;
          var mfLoads = MooringLoadGenerator.Generate(
              _context, _rawStructureData.MfList, _rigidMap, Console.WriteLine, mfStartID, loadReporter);
          _forceLoads.AddRange(mfLoads);

          // 3. Winch 하중
          int winchStartID = mfStartID;
          if (mfLoads.Count > 0) winchStartID = mfLoads.Max(l => l.LoadCaseID) + 1;

          var winchLoads = WinchLoadGenerator.Generate(
              _context, _winchData, Console.WriteLine, winchStartID, loadReporter);
          _forceLoads.AddRange(winchLoads);

          // 리포트 저장
          Console.WriteLine(">>> Exporting Load Calculation Reports...");
          loadReporter.ExportReports(_csvPath);

          // 요약 출력
          Console.WriteLine($"   -> Load Generation Summary: MF({mfLoads.Count}), Winch({winchLoads.Count})");

        }, _modelName); // <--- [중요] 여기에 모델 이름을 넘겨줌
      }
      else LogSkip("STAGE_06");
    }

    private void LogSkip(string stageName)
    {
      Console.ForegroundColor = ConsoleColor.DarkGray;
      Console.WriteLine($"--- Skipping {stageName} (Disabled in Options) ---");
      Console.ResetColor();
    }

    // ... (FeModelProcessPipeline.cs 내부)

    // [수정] RunStage 메서드 전체
    private void RunStage(string stageName, Action action, string customExportName = null)
    {
      Console.WriteLine($"================ {stageName} =================");
      action();

      List<int> spcList;
      if (stageName.Equals("STAGE_06", StringComparison.OrdinalIgnoreCase))
      {
        Console.WriteLine("   -> [Info] Skipping Inspector for STAGE_06 to preserve Rigid Independent Nodes.");
        spcList = _lastSpcList;
      }
      else
      {
        spcList = StructuralSanityInspector.Inspect(_context, _inspectOpt);
        _lastSpcList = spcList;
      }

      // 1. BDF 파일명 결정 (커스텀 이름 우선)
      string finalFileName = !string.IsNullOrEmpty(customExportName) ? customExportName : stageName;
      string bdfFullPath = Path.Combine(_csvPath, finalFileName + ".bdf");

      // 2. BDF 내보내기
      BdfExporter.Export(_context, _csvPath, finalFileName, spcList, _rigidMap, _forceLoads);
      Console.WriteLine($"   -> Exported: {finalFileName}.bdf");

      // 3. [수정됨] Solver 실행 및 결과 처리 로직 (STAGE_06 전용)
      if (stageName.Equals("STAGE_06", StringComparison.OrdinalIgnoreCase))
      {
        string f06Path = Path.Combine(_csvPath, finalFileName + ".f06");
        bool f06Exists = false;

        // (A) Solver 실행 모드인 경우
        if (_runSolver)
        {
          Console.WriteLine(">>> [Solver] Launching Nastran Solver...");
          NastranSolverService.RunNastran(bdfFullPath, Console.WriteLine);
          f06Exists = File.Exists(f06Path); // 실행 후 생성되었는지 확인
        }
        // (B) Solver 스킵 모드인 경우
        else
        {
          Console.WriteLine(">>> [Solver] Skipped (Option is false). Checking for existing .f06 file...");
          f06Exists = File.Exists(f06Path); // 기존 파일이 있는지 확인
        }

        // 4. 결과 파일(.f06) 검사 (있을 경우에만)
        if (f06Exists)
        {
          Console.WriteLine($">>> [Result] Scanning {Path.GetFileName(f06Path)} for errors...");

          if (F06ResultScanner.HasFatalError(f06Path, out string errorMsg))
          {
            // Fatal 에러 발견 시
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine(errorMsg);
            Console.ResetColor();
            Console.WriteLine("!!! SOLVER TERMINATED WITH FATAL ERRORS. RESULT PARSING SKIPPED. !!!");
          }
          else
          {
            // 정상 (다음 단계 준비)
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("   -> No Fatal Errors found. Proceeding to result parsing...");
            Console.ResetColor();

            // TODO: 여기서 결과 파싱 로직 호출 (ResultParser)
            // ParseResults(f06Path);
          }
        }
        else
        {
          // 파일이 없는 경우 (경고 출력)
          Console.ForegroundColor = ConsoleColor.Yellow;
          Console.WriteLine($"   [Warning] Result file not found: {Path.GetFileName(f06Path)}");
          Console.WriteLine("   -> Skipping result parsing. Run solver or check file path.");
          Console.ResetColor();
        }
      }
    }


    // --- Private Logic Methods ---
    private void ElementCollinearOverlapGroupRun(bool isDebug)
    {
      var overlapGroup = ElementCollinearOverlapGroupInspector.FindSegmentationGroups(_context, 3e-2, 20.0);
      if (isDebug) Console.WriteLine($"   -> Found {overlapGroup.Count} overlapping groups.");
      ElementCollinearOverlapAlignSplitModifier.Run(_context, overlapGroup, 0.05, 1e-3, isDebug, Console.WriteLine, false);
    }
    private void ElementSplitByExistingNodesRun(bool isDebug)
    {
      var opt = new ElementSplitByExistingNodesModifier.Options(
          DistanceTol: 1.0, GridCellSize: 5.0, SnapNodeToLine: false, DryRun: false, Debug: isDebug);
      var res = ElementSplitByExistingNodesModifier.Run(_context, opt, Console.WriteLine);
      Console.WriteLine($"   -> {res.ElementsActuallySplit} elements split.");
    }
    private void ElementIntersectionSplitRun(bool isDebug)
    {
      var opt = new ElementIntersectionSplitModifier.Options(DistTol: 1.0, GridCellSize: 200.0, DryRun: false, Debug: isDebug);
      ElementIntersectionSplitModifier.Run(_context, opt, Console.WriteLine);
      RemoveDanglingShortElements();
    }
    private void ElementDuplicateMergeRun(bool isDebug)
    {
      string rpt = Path.Combine(_csvPath, "DuplicateMerge_Report.csv");
      var opt = new ElementDuplicateMergeModifier.Options(rpt, isDebug);
      ElementDuplicateMergeModifier.Run(_context, opt, Console.WriteLine);
    }
    private void RemoveDanglingShortElements()
    {
      var nodeDegree = NodeDegreeInspector.BuildNodeDegree(_context);
      var shortEle = new List<int>();
      foreach (var kv in _context.Elements)
      {
        if (kv.Value.NodeIDs.Count < 2) continue;
        int n0 = kv.Value.NodeIDs[0], n1 = kv.Value.NodeIDs[1];
        if (!nodeDegree.TryGetValue(n0, out int d0) || !nodeDegree.TryGetValue(n1, out int d1)) continue;
        if (DistanceUtils.GetDistanceBetweenNodes(n0, n1, _context.Nodes) < 50.0 && (d0 == 1 || d1 == 1)) shortEle.Add(kv.Key);
      }
      foreach (int id in shortEle) _context.Elements.Remove(id);
      if (shortEle.Count > 0) Console.WriteLine($"[Info] Removed {shortEle.Count} dangling elements.");
    }
    private void CollapseShortElementsRun(double tol)
    {
      var elements = _context.Elements; var nodes = _context.Nodes;
      var keys = elements.Keys.ToList();
      foreach (var eid in keys)
      {
        if (!elements.Contains(eid)) continue;
        var ids = elements[eid].NodeIDs;
        if (ids.Count < 2) continue;
        if (DistanceUtils.GetDistanceBetweenNodes(ids[0], ids[1], nodes) < tol)
        {
          int keep = ids[0], remove = ids[1];
          elements.Remove(eid);
          var neighbors = elements.Where(e => e.Value.NodeIDs.Contains(remove)).ToList();
          foreach (var n in neighbors)
          {
            if (n.Value.TryReplaceNode(remove, keep, out var repl))
              elements.AddWithID(n.Key, repl.NodeIDs.ToList(), repl.PropertyID, repl.ExtraData.ToDictionary(k => k.Key, v => v.Value));
          }
          if (nodes.Contains(remove)) nodes.Remove(remove);
        }
      }
    }
  }
}
