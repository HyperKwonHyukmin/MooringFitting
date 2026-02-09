using MooringFitting2026.Exporters;
using MooringFitting2026.Inspector;
using MooringFitting2026.Parsers;
using MooringFitting2026.Pipeline;
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
      string Data = @"C:\Coding\Csharp\Projects\MooringFitting\TestCSV\Part1\2026Ver\MooringFittingData4235.csv";
      string DataLoad = @"C:\Coding\Csharp\Projects\MooringFitting\TestCSV\Part1\2026Ver\MooringFittingDataLoad4235.csv";
      string CsvFolderPath = Path.GetDirectoryName(Data);
      string inputFileName = Path.GetFileName(Data);

      // [설정] 해석 실행 여부
      bool RUN_NASTRAN_SOLVER = false;

      // [설정] 해석 결과를 CSV로 저장할지 여부
      bool EXPORT_RESULT_CSV = true;

      var globalOptions = InspectorOptions.Create()
          .RunUntil(ProcessingStage.All)
          .DisableDebug()
          .WriteLogToFile(false)
          .SetAllChecks(true)
          .SetThresholds(shortElemDist: 1.0, equivTol: 0.1)
          .Build();

      // 2. 파이프라인 실행 (Execution)
      // =======================================================================
      // 파이프라인은 모델 생성 및 BDF Export, (옵션에 따라) Solver 실행을 담당합니다.
      RunPipeline(Data, DataLoad, CsvFolderPath, inputFileName, globalOptions, RUN_NASTRAN_SOLVER);

      // 3. 결과 탐색 (Post-Processing)
      // =======================================================================
      // [수정] Solver 실행 여부와 관계없이 F06 파일이 존재하면 탐색기를 엽니다.
      string f06Name = Path.GetFileNameWithoutExtension(inputFileName) + ".f06";
      string f06Path = Path.Combine(CsvFolderPath, f06Name);

      if (File.Exists(f06Path))
      {
        Console.WriteLine($"\n[Analysis] Processing F06 file: {f06Name}");

        // 1) FATAL 에러 먼저 체크
        if (F06ResultScanner.HasFatalError(f06Path, out string fatalMsg))
        {
          Console.ForegroundColor = ConsoleColor.Red;
          Console.WriteLine(fatalMsg);
          Console.ResetColor();
          Console.WriteLine(">>> Parsing aborted due to FATAL ERROR.");
        }
        else
        {
          Console.WriteLine(">>> fatal 없음, 정상 Nastran 해석 완료.");
          // 2) 파서 실행
          var parser = new F06Parser();

          // 파싱 수행 (로그는 콘솔에 바로 출력)
          var result = parser.Parse(f06Path, Console.WriteLine);

          // 3) 결과 데이터 확인 (검증용 출력)
          if (result.IsParsedSuccessfully)
          {
            Console.WriteLine("\n------------------------------------------------");
            Console.WriteLine($"[Parsing Summary] Total Subcases: {result.Subcases.Count}");
            Console.WriteLine("------------------------------------------------");

            //foreach (var subcase in result.Subcases.Values)
            //{
            //  Console.ForegroundColor = ConsoleColor.Cyan;
            //  Console.WriteLine($"[Subcase {subcase.SubcaseID}]");
            //  Console.ResetColor();

            //  // 변위 데이터 개수 확인
            //  var subcaseResult = subcase.BeamStresses;         

            //  // 샘플 데이터 5개만 출력해보기
            //  if (subcaseResult.Count > 0)
            //  {
            //    foreach (var d in subcaseResult.Take(10))
            //    {
            //      // ToString() 메서드가 F06Parser.cs에 정의되어 있다고 가정
            //      Console.WriteLine($"     {d}");            
            //    }
            //  }    
            //}
            //Console.WriteLine("------------------------------------------------");

            // 4) CSV 내보내기 실행 (요청하신 기능)
            if (EXPORT_RESULT_CSV)
            {
              // 파일명 베이스: "MooringFittingData4235_Result" 등으로 저장됨
              string exportBaseName = Path.GetFileNameWithoutExtension(inputFileName) + "_Result";

              F06ResultExporter.ExportAll(result, CsvFolderPath, exportBaseName);

              Console.WriteLine("\n>>> [Success] All results have been exported to CSV.");
            }
          }
        }
      }
      else
      {
        Console.WriteLine($"\n[Warning] F06 file not found: {f06Path}");
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
