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
      string Data = @"C:\Coding\Csharp\Projects\MooringFitting\TestCSV\Part1\MooringFittingData4235.csv";
      string DataLoad = @"C:\Coding\Csharp\Projects\MooringFitting\TestCSV\Part1\MooringFittingDataLoad4235.csv";

      // 3414
      //string Data = @"C:\Coding\Csharp\Projects\MooringFitting\TestCSV\Part3\MooringFittingData_3414.csv";
      //string DataLoad = @"C:\Coding\Csharp\Projects\MooringFitting\TestCSV\Part3\MooringFittingDataLoad_3414.csv";


      // 본 코드 진행
      string CsvFolderPath = Path.GetDirectoryName(Data);

      var (feModelContext, rawStructureData, winchData) =
          FeModelLoader.LoadAndBuild(Data, DataLoad, debugMode: true);


      // 전처리 파이프라인 실행
      var opt = new InspectorOptions
      {
        DebugMode = true,
        PrintAllNodeIds = true,
        ShortElementDistanceThreshold = 1,
        EquivalenceTolerance = 0.1,
        NearNodeTolerance = 1
      };
      var feModelPreprocessPipeline = new FeModelProcessPipeline(feModelContext, rawStructureData, winchData, opt, CsvFolderPath);
      feModelPreprocessPipeline.Run();
    }
  }
}

