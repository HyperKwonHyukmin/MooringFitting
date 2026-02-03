using MooringFitting2026.Model;
using MooringFitting2026.Model.Entities;
using MooringFitting2026.Parsers;
using MooringFitting2026.RawData;
using System;
using System.IO;

namespace MooringFitting2026.Services.Initialization
{
  /// <summary>
  /// FE 모델의 초기 데이터 로딩 및 생성을 전담하는 서비스 클래스입니다.
  /// CSV 파싱부터 초기 모델 빌드까지의 과정을 캡슐화합니다.
  /// </summary>
  public static class FeModelLoader
  {
    /// <summary>
    /// CSV 파일 경로를 받아 파싱을 수행하고, 초기 FE 모델 컨텍스트를 생성하여 반환합니다.
    /// </summary>
    /// <param name="structureCsvPath">구조 데이터 CSV 경로</param>
    /// <param name="winchCsvPath">윈치 하중 데이터 CSV 경로</param>
    /// <param name="debugMode">디버그 출력 여부</param>
    /// <returns>생성된 모델 컨텍스트, 원본 구조 데이터, 윈치 데이터 튜플</returns>
    public static (FeModelContext Context, RawStructureData RawStructure, WinchData WinchData)
        LoadAndBuild(string structureCsvPath, string winchCsvPath, bool debugMode = false)
    {
      // 1. 경로 유효성 검사
      if (!File.Exists(structureCsvPath))
        throw new FileNotFoundException($"Structure CSV not found: {structureCsvPath}");
      if (!File.Exists(winchCsvPath))
        throw new FileNotFoundException($"Winch CSV not found: {winchCsvPath}");

      // 2. CSV 데이터 파싱
      if (debugMode) Console.WriteLine("\n[Loader] Parsing CSV Data...");

      var csvParser = new CsvRawDataParser(structureCsvPath, winchCsvPath, debugPrint: debugMode);
      var (rawStructureData, winchData) = csvParser.Run();

      // 3. FE 모델 컨텍스트 생성 (빈 객체)
      var context = FeModelContext.CreateEmpty();

      // 4. RawData를 이용해 FE 엔티티 빌드
      if (debugMode) Console.WriteLine("[Loader] Building Initial FE Model...");

      var builder = new RawFeModelBuilder(rawStructureData, context, debugPrint: false);

      // NOTE: RawFeModelBuilder 클래스의 메서드명을 'Builder'에서 'Build'나 'Run'으로 변경하는 것을 권장합니다.
      // 현재는 기존 코드 호환성을 위해 Builder()를 호출합니다.
      builder.Build();

      if (debugMode)
      {
        Console.WriteLine($"[Loader] Model Built Successfully.");
        Console.WriteLine($"   - Nodes: {context.Nodes.GetNodeCount()}");
        Console.WriteLine($"   - Elements: {context.Elements.Count}");
      }

      return (context, rawStructureData, winchData);
    }
  }
}
