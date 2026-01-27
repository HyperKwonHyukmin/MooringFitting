using MooringFitting2026.Debug;
using MooringFitting2026.Model;
using MooringFitting2026.Model.Entities;
using MooringFitting2026.Parsers;
using MooringFitting2026.RawData;
using MooringFitting2026.Inspector;
using MooringFitting2026.Pipeline;

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
      string CsvFolderPath = Path.GetDirectoryName(Data);

      // 01. Structure 및 WinchLoad CSV 데이터 파싱
      CsvRawDataParser csvParse = new CsvRawDataParser(Data, DataLoad, debugPrint:false);
      (RawStructureData rawStructureData, WinchData winchData) = csvParse.Run();

      // 02. 객체 초기화(깡통 데이터 클래스생성), rawContext에 FE 인스턴드 모두 들어감
      FeModelContext feModelContext = FeModelContext.CreateEmpty();
      RawFeModelBuilder rawFeModelBuilder = 
        new RawFeModelBuilder(rawStructureData, feModelContext, debugPrint:false);
      rawFeModelBuilder.Builder();


      // 전처리 파이프라인 실행
      var opt = new InspectorOptions
      {
        PrintNodeIds = true,
        PrintAllNodeIds = true,          
        ShortElementDistanceThreshold = 1,
        EquivalenceTolerance = 0.1,
        NearNodeTolerance = 1
      };
      var feModelPreprocessPipeline = new FeModelProcessPipeline(feModelContext, opt, CsvFolderPath);
      feModelPreprocessPipeline.Run();

    }
  }
}

