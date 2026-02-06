using MooringFitting2026.Debug;
using MooringFitting2026.Inspector;
using MooringFitting2026.Model;
using MooringFitting2026.Model.Entities;
using MooringFitting2026.Parsers;
using MooringFitting2026.Pipeline;
using MooringFitting2026.RawData;
using MooringFitting2026.Services.Initialization;
using MooringFitting2026.Services.Logging;
using System;


namespace MooringFitting2026
{
  class MainApp
  {
    static void Main(string[] args)
    {
      // 3431호선
      //string Data = @"C:\Coding\Csharp\Projects\MooringFitting\TestCSV\Part2\MooringFittingData_3431.csv";
      //string DataLoad = @"C:\Coding\Csharp\Projects\MooringFitting\TestCSV\Part2\MooringFittingDataLoad_3431.csv";

      // 4235호선
      string Data = @"C:\Coding\Csharp\Projects\MooringFitting\TestCSV\Part1\2026Ver\MooringFittingData4235.csv";
      string DataLoad = @"C:\Coding\Csharp\Projects\MooringFitting\TestCSV\Part1\2026Ver\MooringFittingDataLoad4235.csv";

      // 3414
      //string Data = @"C:\Coding\Csharp\Projects\MooringFitting\TestCSV\Part3\MooringFittingData_3414.csv";
      //string DataLoad = @"C:\Coding\Csharp\Projects\MooringFitting\TestCSV\Part3\MooringFittingDataLoad_3414.csv";


      bool RUN_NASTRAN_SOLVER = true;

      // 본 코드 진행
      string CsvFolderPath = Path.GetDirectoryName(Data);
      string inputFileName = Path.GetFileName(Data);

      // [옵션 설정 가이드]
      // .Create()로 시작하여 필요한 설정을 체이닝(Chaining) 하세요.
      var globalOptions = InspectorOptions.Create()
        .RunUntil(ProcessingStage.All) // 여기서 실행 단계 지정
        // 실행 STAGE 지정 목록
        //Stage01_CollinearOverlap | Stage02_SplitByNodes | Stage03_IntersectionSplit |
        //Stage03_5_DuplicateMerge | Stage04_Extension | Stage05_MeshRefinement | Stage06_LoadGeneration

        // [1] 디버깅 설정
        //.EnableDebug(printAllNodes: true)   // 상세 로그 켜기 (노드 ID 목록 포함)
        .DisableDebug()                  // (또는) 로그 끄기

        .WriteLogToFile(true) // 로그 내용 출력

        // [2] 검사 범위 설정 (기본값은 모두 true)
        .SetAllChecks(true)                 // 일단 다 켜고 시작 (추천)
                                            // .SetAllChecks(false)             // (또는) 다 끄고 필요한 것만 켜기
                                            // .WithTopology(false)             // 특정 검사만 끄기 가능
                                            // .WithDuplicate(true)             // 특정 검사만 켜기 가능

        // [3] 기준값(Threshold) 설정
        .SetThresholds(
            shortElemDist: 1.0,             // 요소 길이가 1.0 미만이면 경고
            equivTol: 0.1                   // 0.1 거리 이내 점은 겹친 것으로 간주
        )

        .Build(); // [4] 최종 확정


      // =======================================================================
      // [변경] 로그 리다이렉션 (콘솔 + 파일 동시 출력)
      // =======================================================================

      StreamWriter logFileWriter = null;
      TextWriter originalConsoleOut = Console.Out; // 종료 시 복원용

      if (globalOptions.EnableFileLogging && !string.IsNullOrEmpty(CsvFolderPath))
      {
        // 파일명: Log_YYYYMMDD_HHMMSS.txt
        string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string logFileName = $"Log_{timestamp}.txt";
        string logPath = Path.Combine(CsvFolderPath, logFileName);

        try
        {
          // 파일 스트림 생성 (AutoFlush=true로 실시간 기록)
          logFileWriter = new StreamWriter(logPath, append: false, System.Text.Encoding.UTF8)
          {
            AutoFlush = true
          };

          // 기존의 MultiTextWriter 활용 (화면과 파일 양쪽에 씀)
          var multiWriter = new MultiTextWriter(originalConsoleOut, logFileWriter);

          // ★ 핵심: 시스템의 콘솔 출력을 가로챔
          Console.SetOut(multiWriter);

          Console.WriteLine($"[System] Log file capture started: {logPath}");
        }
        catch (Exception ex)
        {
          Console.WriteLine($"[System Warning] Failed to create log file: {ex.Message}");
          globalOptions = InspectorOptions.Create().WriteLogToFile(false).Build(); // 실패 시 옵션 끄기
        }
      }



      // =======================================================================
      // [변경 3] 데이터 로딩 및 파이프라인 실행
      // =======================================================================
      try
      {
        // 이제부터 모든 Console.WriteLine은 파일에도 저장됩니다.
        var (feModelContext, rawStructureData, winchData) =
            FeModelLoader.LoadAndBuild(Data, DataLoad, debugMode: globalOptions.DebugMode);

        var pipeline = new FeModelProcessPipeline(
          feModelContext,
          rawStructureData,
          winchData,
          globalOptions,
          CsvFolderPath,
          inputFileName,
          RUN_NASTRAN_SOLVER
        );

        pipeline.Run();
      }
      catch (Exception ex)
      {
        Console.WriteLine($"\n[Critical Error] Program terminated unexpectedly: {ex.Message}");
        Console.WriteLine(ex.StackTrace);
      }
      finally
      {
        // =======================================================================
        // [변경 4] 리소스 정리 (파일 닫기)
        // =======================================================================
        if (logFileWriter != null)
        {
          Console.WriteLine("[System] Closing log file.");
          Console.SetOut(originalConsoleOut); // 콘솔 원상 복구
          logFileWriter.Close();
          logFileWriter.Dispose();
        }
      }
    }  
  }
}
