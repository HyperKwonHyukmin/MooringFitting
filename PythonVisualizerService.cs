using System;
using System.Diagnostics;
using System.IO;

namespace MooringFitting2026.Services.Visualization
{
  public static class PythonVisualizerService
  {
    /// <summary>
    /// 파이썬 스크립트를 실행하여 시각화를 수행합니다.
    /// </summary>
    /// <param name="csvFolderPath">데이터가 있는 CSV 폴더 경로</param>
    /// <param name="pythonScriptPath">실행할 파이썬 스크립트 파일 경로</param>
    /// <param name="interpreterPath">파이썬 실행 파일 경로 (기본값: python)</param>
    public static void RunVisualization(string csvFolderPath, string pythonScriptPath, string interpreterPath = "python")
    {
      if (!File.Exists(pythonScriptPath))
      {
        Console.WriteLine($"[Visualizer] Error: Script not found at {pythonScriptPath}");
        return;
      }

      Console.WriteLine("\n==================================================");
      Console.WriteLine("[Visualizer] Starting Python Visualization...");
      Console.WriteLine($"   -> Interpreter: {interpreterPath}");
      Console.WriteLine($"   -> Script: {Path.GetFileName(pythonScriptPath)}");
      Console.WriteLine("==================================================");

      var psi = new ProcessStartInfo
      {
        FileName = interpreterPath,      // 전달받은 파이썬 경로 사용 (가상환경)
        Arguments = $"\"{pythonScriptPath}\" \"{csvFolderPath}\"",
        UseShellExecute = false,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        CreateNoWindow = true
      };

      try
      {
        using (var process = new Process { StartInfo = psi })
        {
          process.OutputDataReceived += (s, e) => {
            if (!string.IsNullOrWhiteSpace(e.Data)) Console.WriteLine($"  [Py] {e.Data}");
          };
          process.ErrorDataReceived += (s, e) => {
            if (!string.IsNullOrWhiteSpace(e.Data)) Console.WriteLine($"  [Py Err] {e.Data}");
          };

          process.Start();
          process.BeginOutputReadLine();
          process.BeginErrorReadLine();
          process.WaitForExit();
        }
        Console.WriteLine("[Visualizer] Process Finished.");
      }
      catch (Exception ex)
      {
        Console.WriteLine($"[Visualizer] Execution Failed: {ex.Message}");
        Console.WriteLine("Ensure the python path is correct and packages are installed.");
      }
    }
  }
}
