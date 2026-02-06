using MooringFitting2026.Inspector;
using MooringFitting2026.Pipeline;
using MooringFitting2026.Services.Initialization;
using MooringFitting2026.Services.Logging;
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

      var globalOptions = InspectorOptions.Create()
          .RunUntil(ProcessingStage.All)
          .DisableDebug()
          .WriteLogToFile(true)
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
        Console.WriteLine($"\n[Analysis] Inspecting {f06Name} line by line...");

        // [핵심] File.ReadLines로 메모리 부하 없이 한 줄씩 가져옵니다.
        int lineIndex = 0;
        foreach (string line in File.ReadLines(f06Path))
        {
          lineIndex++;

          // =========================================================
          // [이곳에 중단점을 걸거나 조건을 추가하여 규칙을 찾으세요]
          // =========================================================

          // 예시 1: 상위 50줄만 찍어보기 (헤더 구조 확인)
          if (lineIndex <= 50)
          {
            Console.WriteLine($"[{lineIndex}] {line}");
          }

          // 예시 2: 특정 키워드가 나오면 멈추기 (디버깅용)
          // if (line.Contains("FATAL MESSAGE") || line.Contains("DISPLACEMENT"))
          // {
          //     Console.WriteLine($"Found at {lineIndex}: {line}");
          //     // 여기에 Breakpoint 설정 (F9)
          // }
        }

        Console.WriteLine($"[Analysis] Finished scanning {lineIndex} lines.");
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
