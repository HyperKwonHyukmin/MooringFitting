using MooringFitting2026.Exporters;
using MooringFitting2026.Extensions;
using MooringFitting2026.Inspector;
using MooringFitting2026.Inspector.ElementInspector;
using MooringFitting2026.Inspector.NodeInspector;
using MooringFitting2026.Model.Entities;
using MooringFitting2026.Modifier.ElementModifier;
using MooringFitting2026.Modifier.NodeModifier;
using MooringFitting2026.Parsers; // [추가] F06Parser 사용
using MooringFitting2026.RawData;
using MooringFitting2026.Services.Analysis;
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
    private readonly string _inputCsvFileName; // [추가] 원본 CSV 파일명 저장
    private readonly bool _runSolver;

    private Dictionary<int, MooringFittingConnectionModifier.RigidInfo> _rigidMap
        = new Dictionary<int, MooringFittingConnectionModifier.RigidInfo>();
    private List<ForceLoad> _forceLoads = new List<ForceLoad>();
    private List<int> _lastSpcList = new List<int>();

    /// <summary>
    /// 파이프라인 생성자 (파일명 인자 추가됨)
    /// </summary>
    public FeModelProcessPipeline(
        FeModelContext context,
        RawStructureData rawStructureData,
        WinchData winchData,
        InspectorOptions inspectOpt,
        string CsvFolderPath,
        string inputCsvFileName, // [수정] 원본 파일명 인자 추가
        bool runSolver = true)
    {
      _context = context ?? throw new ArgumentNullException(nameof(context));
      _rawStructureData = rawStructureData;
      _winchData = winchData;
      _inspectOpt = inspectOpt ?? InspectorOptions.Default;
      _csvPath = CsvFolderPath;
      _inputCsvFileName = inputCsvFileName;
      _runSolver = runSolver;
    }

    public void Run()
    {
      Console.WriteLine("\n[Pipeline Started] Processing FE Model...");

      Console.WriteLine(">>> [Preprocessing] Normalizing Z-Plane (2D Conversion)...");
      NodeZPlaneNormalizeModifier.Run(_context);

      ExportBaseline();
      RunStagedPipeline();

      foreach(var ele in _context.Elements)
      {
        Console.WriteLine(ele);
      }
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

      // [Stage 06] 하중 생성 및 최종 해석 (파일명 커스텀 적용)
      if (_inspectOpt.ActiveStages.HasFlag(ProcessingStage.Stage06_LoadGeneration))
      {
        // 원본 파일명(확장자 제외) 추출: 예) "MooringFittingData4235"
        string customBdfName = Path.GetFileNameWithoutExtension(_inputCsvFileName);

        RunStage("STAGE_06", () =>
        {
          var loadReporter = new LoadCalculationReporter();

          // 1. Rigid 생성
          _rigidMap = MooringFittingConnectionModifier.Run(_context, _rawStructureData.MfList, _lastSpcList, Console.WriteLine);

          Console.WriteLine(">>> Generating Loads...");
          _forceLoads.Clear();

          // 2. MF 하중 생성
          int mfStartID = 2;
          var mfLoads = MooringLoadGenerator.Generate(
              _context, _rawStructureData.MfList, _rigidMap, Console.WriteLine, mfStartID, loadReporter);
          _forceLoads.AddRange(mfLoads);

          // 3. Winch 하중 생성
          int winchStartID = mfLoads.Count > 0 ? mfLoads.Max(l => l.LoadCaseID) + 1 : mfStartID;
          var winchLoads = WinchLoadGenerator.Generate(
              _context, _winchData, Console.WriteLine, winchStartID, loadReporter);
          _forceLoads.AddRange(winchLoads);

          // 4. 리포트 내보내기
          Console.WriteLine(">>> Exporting Load Calculation Reports...");
          loadReporter.ExportReports(_csvPath);

        }, customBdfName); // [중요] 커스텀 파일명 전달
      }
      else LogSkip("STAGE_06");
    }

    private void LogSkip(string stageName)
    {
      Console.ForegroundColor = ConsoleColor.DarkGray;
      Console.WriteLine($"--- Skipping {stageName} (Disabled in Options) ---");
      Console.ResetColor();
    }

    /// <summary>
    /// 각 스테이지 공통 실행 로직 (검사 -> BDF 출력 -> [옵션] Solver -> Scanner -> Parser)
    /// </summary>
    private void RunStage(string stageName, Action action, string customExportName = null)
    {
      Console.WriteLine($"================ {stageName} =================");
      action(); // 실제 작업 수행

      // Stage 06은 Rigid Independent Node 보존을 위해 Inspector 생략
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

      // [변경] BDF 파일명 결정 (custom이 있으면 그것 사용, 없으면 stageName 사용)
      string finalBdfName = !string.IsNullOrEmpty(customExportName) ? customExportName : stageName;

      // BDF 내보내기
      BdfExporter.Export(_context, _csvPath, finalBdfName, spcList, _rigidMap, _forceLoads);
      Console.WriteLine($"   -> [File] BDF Exported: {finalBdfName}.bdf");

      // [변경] Stage 06이고 Solver 옵션이 켜져있을 때만 해석 및 파싱 수행
      if (stageName.Equals("STAGE_06", StringComparison.OrdinalIgnoreCase) && _runSolver)
      {
        string bdfFullPath = Path.Combine(_csvPath, finalBdfName + ".bdf");

        // 1. Nastran Solver 실행
        Console.WriteLine(">>> [Solver] Launching Nastran Solver...");
        NastranSolverService.RunNastran(bdfFullPath, Console.WriteLine);

        // 2. F06 파일 스캔 (FATAL 에러 확인)
        string f06FullPath = Path.ChangeExtension(bdfFullPath, ".f06");

        // F06ResultScanner를 이용해 FATAL 체크
        if (F06ResultScanner.HasFatalError(f06FullPath, out string fatalMessage))
        {
          Console.ForegroundColor = ConsoleColor.Red;
          Console.WriteLine(fatalMessage); // 전후 5줄 포함된 에러 로그 출력
          Console.ResetColor();
          Console.WriteLine(">>> [Stop] Fatal Error detected in F06. Parsing skipped.");
        }
        else
        {
          // 3. F06 파싱 (에러가 없을 때만)
          Console.WriteLine(">>> [Parser] No Fatal Error. Starting F06 Parsing...");
          var parser = new F06Parser();
          var parseResult = parser.Parse(f06FullPath, Console.WriteLine);

          if (parseResult.IsParsedSuccessfully)
          {
            Console.WriteLine("   -> [Result] F06 Data Extraction Complete.");
            BeamForcePostProcessor.CalculateStresses(parseResult, _context);
          }
        }
      }
    }

    // --- Private Logic Methods (기존과 동일) ---
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
