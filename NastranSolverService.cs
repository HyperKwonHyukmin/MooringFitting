using System;
using System.Diagnostics;
using System.IO;

namespace MooringFitting2026.Services.Solver
{
  /// <summary>
  /// Nastran Solver를 실행하는 서비스 클래스입니다.
  /// 외부 프로세스(CMD)를 호출하여 BDF 해석을 수행합니다.
  /// </summary>
  public static class NastranSolverService
  {
    /// <summary>
    /// 지정된 BDF 파일에 대해 Nastran 해석을 실행합니다.
    /// (cmd 명령어: nastran filename.bdf)
    /// </summary>
    /// <param name="bdfFilePath">실행할 BDF 파일의 절대 경로</param>
    /// <param name="log">로그 출력 액션</param>
    public static void RunNastran(string bdfFilePath, Action<string> log)
    {
      if (!File.Exists(bdfFilePath))
      {
        log($"[Nastran Error] BDF file not found: {bdfFilePath}");
        return;
      }

      string workingDirectory = Path.GetDirectoryName(bdfFilePath);
      string fileName = Path.GetFileName(bdfFilePath);

      log($"[Nastran] Starting solver for: {fileName}");

      try
      {
        // CMD 프로세스 설정
        ProcessStartInfo psi = new ProcessStartInfo
        {
          FileName = "cmd.exe",
          // /C : 명령어 실행 후 종료
          // nastran "{fileName}" : 파일명에 공백이 있을 수 있으므로 따옴표 처리
          Arguments = $"/C nastran \"{fileName}\"",
          WorkingDirectory = workingDirectory, // 중요: 결과 파일이 이 폴더에 생성됨
          UseShellExecute = false,
          RedirectStandardOutput = true,
          RedirectStandardError = true,
          CreateNoWindow = true
        };

        using (Process process = new Process())
        {
          process.StartInfo = psi;

          // 비동기 출력 캡처 (Nastran 로그를 콘솔에 표시하려면)
          process.OutputDataReceived += (sender, e) => { if (e.Data != null) log($"  [Nastran Output] {e.Data}"); };
          process.ErrorDataReceived += (sender, e) => { if (e.Data != null) log($"  [Nastran Error] {e.Data}"); };

          process.Start();

          process.BeginOutputReadLine();
          process.BeginErrorReadLine();

          process.WaitForExit(); // 해석 완료될 때까지 대기 (필요시 제거 가능)

          log($"[Nastran] Solver finished with ExitCode: {process.ExitCode}");
        }
      }
      catch (Exception ex)
      {
        log($"[Nastran Exception] Failed to run solver: {ex.Message}");
        log("Make sure 'nastran' command is recognized in CMD (Check Environment PATH).");
      }
    }
  }
}
