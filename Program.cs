using MooringFitting2026.Exporters;
using MooringFitting2026.Inspector;
using MooringFitting2026.Model.Entities;
using MooringFitting2026.Parsers;
using MooringFitting2026.Pipeline;
using MooringFitting2026.Services.Analysis;
using MooringFitting2026.Services.Initialization;
using MooringFitting2026.Services.Logging;
using MooringFitting2026.Services.Solver;
using System;
using System.IO;

namespace MooringFitting2026
{
  class MainApp
  {
    static void Main(string[] args)
    {
      // 1. 설정 (Configuration)
      // =======================================================================
      // [경로 설정] 테스트 환경에 맞게 경로를 확인해주세요.
      //string Data = @"C:\Coding\Csharp\Projects\MooringFitting\TestCSV\Part1\2026Ver\MooringFittingData4235.csv";
      //string DataLoad = @"C:\Coding\Csharp\Projects\MooringFitting\TestCSV\Part1\2026Ver\MooringFittingDataLoad4235.csv";
      string Data = @"C:\Coding\Csharp\Projects\MooringFitting\TestCSV\26_03\MooringFittingData.csv";
      string DataLoad = @"C:\Coding\Csharp\Projects\MooringFitting\TestCSV\26_03\MooringFittingDataLoad.csv";
      string CsvFolderPath = Path.GetDirectoryName(Data);
      string inputFileName = Path.GetFileName(Data);

      // [설정] 해석 실행 여부
      bool RUN_NASTRAN_SOLVER = true;

      // [설정] 해석 결과를 CSV로 저장할지 여부
      bool EXPORT_RESULT_CSV = true;

      // [설정] 모델 CenterLine에 SPC 설정
      bool EnableAutoBottomSPC = true;

      var globalOptions = InspectorOptions.Create()
          .RunUntil(ProcessingStage.All)
          .DisableDebug()
          .WriteLogToFile(true)
          .SetAllChecks(true)
          .SetThresholds(shortElemDist: 1.0, equivTol: 0.1)
          .Build();

      StreamWriter logFileWriter = null;
      TextWriter originalConsoleOut = Console.Out;

      try
      {
        // [변경] 로그 설정 및 모델 로딩을 Main에서 수행
        if (globalOptions.EnableFileLogging && !string.IsNullOrEmpty(CsvFolderPath))
        {
          string logPath = Path.Combine(CsvFolderPath, $"Log_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
          logFileWriter = new StreamWriter(logPath, false, System.Text.Encoding.UTF8) { AutoFlush = true };
          Console.SetOut(new MultiTextWriter(originalConsoleOut, logFileWriter));
        }

        // [중요] 여기서 feModelContext를 생성해야 나중에 PostProcessor에서 쓸 수 있음
        var (feModelContext, rawStructureData, winchData) =
                FeModelLoader.LoadAndBuild(Data, DataLoad, debugMode: globalOptions.DebugMode);

        // 2. 파이프라인 실행 (Context 전달)
        var pipeline = new FeModelProcessPipeline(
            feModelContext, rawStructureData, winchData, globalOptions, CsvFolderPath, inputFileName, RUN_NASTRAN_SOLVER,
            EnableAutoBottomSPC);
        pipeline.Run();

        // 3. F06 파싱 및 후처리
        string f06Name = Path.GetFileNameWithoutExtension(inputFileName) + ".f06";
        string f06Path = Path.Combine(CsvFolderPath, f06Name);

        if (File.Exists(f06Path))
        {
          Console.WriteLine($"\n[Analysis] Processing F06 file: {f06Name}");

          if (!F06ResultScanner.HasFatalError(f06Path, out string fatalMsg))
          {
            var parser = new F06Parser();
            var result = parser.Parse(f06Path, Console.WriteLine);

            if (result.IsParsedSuccessfully)
            {
              // [수정 완료] 이제 feModelContext에 접근 가능하므로 에러가 사라짐
              BeamForcePostProcessor.CalculateStresses(result, feModelContext);

              if (EXPORT_RESULT_CSV)
              {
                string exportBaseName = Path.GetFileNameWithoutExtension(inputFileName) + "_Result";
                F06ResultExporter.ExportAll(result, CsvFolderPath, exportBaseName);
                Console.WriteLine("\n>>> [Success] All results have been exported to CSV.");
              }
            }
          }
          else
          {
            Console.WriteLine(fatalMsg);
          }
        }
      }
      catch (Exception ex)
      {
        Console.WriteLine($"\n[Critical Error] {ex.Message}\n{ex.StackTrace}");
      }
      finally
      {
        if (logFileWriter != null)
        {
          Console.SetOut(originalConsoleOut);
          logFileWriter.Close();
          logFileWriter.Dispose();
        }
      }
    }



    // -----------------------------------------------------------------------
    // Helper Method: RunPipeline
    // -----------------------------------------------------------------------
    private static void RunPipeline(
        string dataPath, string loadPath, string outPath, string fileName,
        InspectorOptions options, bool runSolver)
    {
      StreamWriter logFileWriter = null;
      TextWriter originalConsoleOut = Console.Out;

      try
      {
        // 로그 설정
        if (options.EnableFileLogging && !string.IsNullOrEmpty(outPath))
        {
          string logPath = Path.Combine(outPath, $"Log_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
          logFileWriter = new StreamWriter(logPath, false, System.Text.Encoding.UTF8) { AutoFlush = true };
          Console.SetOut(new MultiTextWriter(originalConsoleOut, logFileWriter));
          Console.WriteLine($"[System] Log capture started: {logPath}");
        }

        // 로딩 및 모델 빌드
        var (feModelContext, rawStructureData, winchData) =
            FeModelLoader.LoadAndBuild(dataPath, loadPath, debugMode: options.DebugMode);

        // 파이프라인 실행
        var pipeline = new FeModelProcessPipeline(
            feModelContext, rawStructureData, winchData, options, outPath, fileName, runSolver
        );

        pipeline.Run();
      }
      catch (Exception ex)
      {
        Console.WriteLine($"\n[Critical Error] {ex.Message}\n{ex.StackTrace}");
      }
      finally
      {
        // 리소스 정리
        if (logFileWriter != null)
        {
          Console.WriteLine("[System] Log file closed.");
          Console.SetOut(originalConsoleOut);
          logFileWriter.Close();
          logFileWriter.Dispose();
        }
      }
    }
  }
}
