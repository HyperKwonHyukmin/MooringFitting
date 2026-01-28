using MooringFitting2026.RawData;
using MooringFitting2026.RawData;
using System.Collections.Generic;
using System.IO;

namespace MooringFitting2026.Parsers
{
  /// <summary>
  /// 윈치(Winch) 하중 데이터를 CSV에서 읽어 WinchData 객체를 생성하는 클래스입니다.
  /// </summary>
  public class WinchCsvParser
  {
    /// <summary>
    /// 지정된 경로의 CSV 파일을 읽어 윈치 데이터 객체로 변환합니다.
    /// </summary>
    /// <param name="filePath">CSV 파일 경로</param>
    /// <returns>파싱된 WinchData</returns>
    public WinchData Parse(string filePath)
    {
      if (!File.Exists(filePath))
        throw new FileNotFoundException($"Load CSV 파일을 찾을 수 없습니다: {filePath}");

      // 데이터 컨테이너 초기화
      var forwardLC = new Dictionary<string, List<string>>();
      var backwardLC = new Dictionary<string, List<string>>();
      var greenSeaFrontLeftLC = new Dictionary<string, List<string>>();
      var greenSeaFrontRightLC = new Dictionary<string, List<string>>();
      var testLC = new Dictionary<string, List<string>>();
      var winchLocation = new Dictionary<string, List<string>>();

      var lines = File.ReadAllLines(filePath);

      // Header 건너뛰기 (index 1부터 시작)
      for (int i = 1; i < lines.Length; i++)
      {
        string line = lines[i];
        if (string.IsNullOrWhiteSpace(line)) continue;

        var values = line.Split(',');
        if (values.Length < 18) continue; // 데이터 무결성 검증

        string winchLoadID = values[1];

        // 데이터 매핑
        forwardLC[winchLoadID] = new List<string> { values[2], values[3], values[4] };
        backwardLC[winchLoadID] = new List<string> { values[5], values[6], values[7] };
        greenSeaFrontLeftLC[winchLoadID] = new List<string> { values[8], values[9], values[10] };
        greenSeaFrontRightLC[winchLoadID] = new List<string> { values[11], values[12], values[13] };
        testLC[winchLoadID] = new List<string> { values[14] };
        winchLocation[winchLoadID] = new List<string> { values[15], values[16], values[17] };
      }

      return new WinchData(forwardLC, backwardLC, greenSeaFrontLeftLC, greenSeaFrontRightLC, testLC, winchLocation);
    }
  }
}
