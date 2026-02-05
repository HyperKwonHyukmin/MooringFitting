using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace MooringFitting2026.Services.Solver
{
  public static class F06ResultScanner
  {
    /// <summary>
    /// .f06 파일에서 치명적 오류(FATAL MESSAGE)가 있는지 검사합니다.
    /// </summary>
    /// <param name="f06Path">.f06 파일 경로</param>
    /// <param name="errorMessage">발견된 에러 메시지 (없으면 null)</param>
    /// <returns>에러가 있으면 true, 없으면 false</returns>
    public static bool HasFatalError(string f06Path, out string errorMessage)
    {
      errorMessage = null;

      if (!File.Exists(f06Path))
      {
        errorMessage = "File not found.";
        return true; // 파일이 없는 것도 에러로 간주
      }

      try
      {
        // 대용량 파일일 수 있으므로 ReadLines로 한 줄씩 스트리밍
        var lines = File.ReadLines(f06Path);
        bool foundFatal = false;
        StringBuilder sbError = new StringBuilder();
        int linesToCapture = 0;

        foreach (var line in lines)
        {
          // Nastran의 표준 에러 키워드: "FATAL MESSAGE"
          if (line.Contains("FATAL MESSAGE", StringComparison.OrdinalIgnoreCase))
          {
            foundFatal = true;
            linesToCapture = 10; // 에러 발생 지점부터 10줄 정도를 추가로 수집하여 문맥 파악
            sbError.AppendLine("--------------------------------------------------");
            sbError.AppendLine($"[FATAL ERROR DETECTED]");
          }

          if (foundFatal && linesToCapture > 0)
          {
            sbError.AppendLine(line);
            linesToCapture--;
          }
        }

        if (foundFatal)
        {
          errorMessage = sbError.ToString();
          return true;
        }
      }
      catch (Exception ex)
      {
        errorMessage = $"Error reading f06 file: {ex.Message}";
        return true;
      }

      return false; // 에러 없음
    }
  }
}
