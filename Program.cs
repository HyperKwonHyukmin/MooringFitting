using MooringFitting2026.Debug;
using MooringFitting2026.Inspector;
using MooringFitting2026.Model;
using MooringFitting2026.Model.Entities;
using MooringFitting2026.Parsers;
using MooringFitting2026.Pipeline;
using MooringFitting2026.RawData;
using MooringFitting2026.Services.Initialization;
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

      var (feModelContext, rawStructureData, winchData) =
          FeModelLoader.LoadAndBuild(Data, DataLoad, debugMode: false);


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



      // 파이프라인 생성 및 실행
      var pipeline = new FeModelProcessPipeline(
        feModelContext,
        rawStructureData,
        winchData,
        globalOptions,
        CsvFolderPath,
        RUN_NASTRAN_SOLVER 
             );
      pipeline.Run();
    }
  }
}
