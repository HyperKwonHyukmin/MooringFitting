using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using MooringFitting2026.Model.Geometry;

namespace MooringFitting2026.Services.Reporting
{
  /// <summary>
  /// 하중 계산 과정의 상세 데이터(입력 파라미터 및 결과 벡터)를 수집하고
  /// 이를 보고서 형태(CSV)로 출력하는 서비스 클래스입니다.
  /// </summary>
  public class LoadCalculationReporter
  {
    private readonly StringBuilder _mfReportBuffer = new StringBuilder();
    private readonly StringBuilder _winchReportBuffer = new StringBuilder();

    public LoadCalculationReporter()
    {
      // MF 헤더 작성
      _mfReportBuffer.AppendLine("Type,MF_ID,LoadCaseID,NodeID,SWL(Ton),Angle_H(deg),Angle_V(deg),Calc_Fx,Calc_Fy,Calc_Fz,Result");

      // Winch 헤더 작성
      _winchReportBuffer.AppendLine("CaseName,WinchID,LoadCaseID,NodeID,Input_Fx(Ton),Input_Fy(Ton),Input_Fz(Ton),Final_Fx,Final_Fy,Final_Fz,Location");
    }

    /// <summary>
    /// Mooring Fitting 하중 계산 결과를 기록합니다. (파라미터 'type' 추가됨)
    /// </summary>
    public void AddMfEntry(string type, string mfId, int loadCaseId, int nodeId, double swl, double angleH, double angleV, Vector3D finalForce, string resultMsg)
    {
      // CSV 포맷: Type, ID, LC, Node, SWL, A, B, Fx, Fy, Fz, Note
      string line = $"{type},{mfId},{loadCaseId},{nodeId},{swl:F3},{angleH:F1},{angleV:F1}," +
                    $"{finalForce.X:F3},{finalForce.Y:F3},{finalForce.Z:F3},{resultMsg}";
      _mfReportBuffer.AppendLine(line);
    }

    /// <summary>
    /// Winch 하중 계산 결과를 기록합니다.
    /// </summary>
    public void AddWinchEntry(string caseName, string winchId, int loadCaseId, int nodeId,
                              double inFx, double inFy, double inFz,
                              Vector3D finalForce, string locationStr)
    {
      // CSV 포맷: Case, ID, LC, Node, InX, InY, InZ, OutX, OutY, OutZ, Loc
      string line = $"{caseName},{winchId},{loadCaseId},{nodeId}," +
                    $"{inFx:F3},{inFy:F3},{inFz:F3}," +
                    $"{finalForce.X:F3},{finalForce.Y:F3},{finalForce.Z:F3},{locationStr.Replace(",", " ")}";
      _winchReportBuffer.AppendLine(line);
    }

    /// <summary>
    /// 수집된 데이터를 CSV 파일로 저장합니다.
    /// </summary>
    /// <param name="directoryPath">저장할 폴더 경로</param>
    public void ExportReports(string directoryPath)
    {
      try
      {
        // MF 보고서 저장
        string mfPath = Path.Combine(directoryPath, "Report_LoadCalculation_MF.csv");
        File.WriteAllText(mfPath, _mfReportBuffer.ToString(), Encoding.UTF8);
        Console.WriteLine($"   -> [Report] MF Load Report saved: {Path.GetFileName(mfPath)}");

        // Winch 보고서 저장
        string winchPath = Path.Combine(directoryPath, "Report_LoadCalculation_Winch.csv");
        File.WriteAllText(winchPath, _winchReportBuffer.ToString(), Encoding.UTF8);
        Console.WriteLine($"   -> [Report] Winch Load Report saved: {Path.GetFileName(winchPath)}");
      }
      catch (Exception ex)
      {
        Console.WriteLine($"   [Error] Failed to save load reports: {ex.Message}");
      }
    }
  }
}
