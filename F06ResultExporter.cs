using MooringFitting2026.Parsers;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace MooringFitting2026.Exporters
{
  /// <summary>
  /// 파싱된 F06 결과를 CSV 파일로 내보내는 클래스입니다.
  /// </summary>
  public static class F06ResultExporter
  {
    public static void ExportAll(F06Parser.F06ResultData result, string outputFolder, string baseFileName)
    {
      if (!result.IsParsedSuccessfully) return;

      Console.WriteLine("\n>>> [Exporter] Starting CSV Export...");

      // 1. 변위 (Displacement) 내보내기
      ExportDisplacements(result, outputFolder, baseFileName);

      // 2. 빔 요소력 (Beam Forces) 내보내기
      ExportBeamForces(result, outputFolder, baseFileName);

      // 3. 빔 응력 (Beam Stresses) 내보내기
      ExportBeamStresses(result, outputFolder, baseFileName);
    }

    private static void ExportDisplacements(F06Parser.F06ResultData result, string folder, string baseName)
    {
      string path = Path.Combine(folder, $"{baseName}_Displacements.csv");
      bool hasData = false;

      using (var sw = new StreamWriter(path, false, Encoding.UTF8))
      {
        // Header
        sw.WriteLine("SubcaseID,NodeID,Type,T1,T2,T3,R1,R2,R3");

        foreach (var subcase in result.Subcases.Values)
        {
          if (subcase.Displacements.Count == 0) continue;
          hasData = true;

          foreach (var d in subcase.Displacements)
          {
            sw.WriteLine($"{subcase.SubcaseID},{d.NodeID},{d.Type},{d.T1},{d.T2},{d.T3},{d.R1},{d.R2},{d.R3}");
          }
        }
      }

      if (hasData) Console.WriteLine($"   -> Exported: {Path.GetFileName(path)}");
      else File.Delete(path); // 데이터 없으면 빈 파일 삭제
    }

    private static void ExportBeamForces(F06Parser.F06ResultData result, string folder, string baseName)
    {
      string path = Path.Combine(folder, $"{baseName}_BeamForces.csv");
      bool hasData = false;

      using (var sw = new StreamWriter(path, false, Encoding.UTF8))
      {
        // Header
        sw.WriteLine("SubcaseID,ElementID,GridID,Position,BM1,BM2,Shear1,Shear2,Axial,TotalTorque,WarpingTorque");

        foreach (var subcase in result.Subcases.Values)
        {
          if (subcase.BeamForces.Count == 0) continue;
          hasData = true;

          foreach (var f in subcase.BeamForces)
          {
            sw.WriteLine($"{subcase.SubcaseID},{f.ElementID},{f.GridID},{f.Pos}," +
                         $"{f.BM1},{f.BM2},{f.Shear1},{f.Shear2},{f.Axial},{f.TotalTorque},{f.WarpingTorque}");
          }
        }
      }

      if (hasData) Console.WriteLine($"   -> Exported: {Path.GetFileName(path)}");
      else File.Delete(path);
    }

    private static void ExportBeamStresses(F06Parser.F06ResultData result, string folder, string baseName)
    {
      string path = Path.Combine(folder, $"{baseName}_BeamStresses.csv");
      bool hasData = false;

      using (var sw = new StreamWriter(path, false, Encoding.UTF8))
      {
        // Header
        sw.WriteLine("SubcaseID,ElementID,GridID,Station,S_C,S_D,S_E,S_F,MaxStress,MinStress");

        foreach (var subcase in result.Subcases.Values)
        {
          if (subcase.BeamStresses.Count == 0) continue;
          hasData = true;

          foreach (var s in subcase.BeamStresses)
          {
            sw.WriteLine($"{subcase.SubcaseID},{s.ElementID},{s.GridID},{s.Station}," +
                         $"{s.S_C},{s.S_D},{s.S_E},{s.S_F},{s.MaxStress},{s.MinStress}");
          }
        }
      }

      if (hasData) Console.WriteLine($"   -> Exported: {Path.GetFileName(path)}");
      else File.Delete(path);
    }
   
  }
}
