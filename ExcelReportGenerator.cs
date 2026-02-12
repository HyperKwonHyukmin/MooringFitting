using ClosedXML.Excel;
using MooringFitting2026.Model.Entities;
using MooringFitting2026.Parsers;
using MooringFitting2026.RawData;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace MooringFitting2026.Services.Reporting
{
  public class ExcelReportGenerator
  {
    private readonly FeModelContext _context;
    private readonly List<MFData> _mfList;
    private readonly WinchData _winchData;
    private readonly F06Parser.F06ResultData _f06Result;
    private readonly string _outputFolder;

    private Dictionary<string, (double Fx, double Fy, double Fz)> _calculatedMfLoads
        = new Dictionary<string, (double, double, double)>();

    public ExcelReportGenerator(
        FeModelContext context,
        List<MFData> mfList,
        WinchData winchData,
        F06Parser.F06ResultData f06Result,
        string outputFolder)
    {
      _context = context;
      _mfList = mfList;
      _winchData = winchData;
      _f06Result = f06Result;
      _outputFolder = outputFolder;

      LoadMfCalculationResults();
    }

    public void Generate(string fileName = "Final_Report.xlsx")
    {
      string filePath = Path.Combine(_outputFolder, fileName);
      Console.WriteLine($"\n>>> [Report] Generating Excel Report: {fileName}...");

      using (var wb = new XLWorkbook())
      {
        CreateSheet_Model(wb);
        CreateSheet_ElementInfo(wb);
        CreateSheet_LoadMF(wb);
        CreateSheet_LoadWinch(wb);
        CreateSheet_StressResults(wb);

        wb.SaveAs(filePath);
      }
      Console.WriteLine("   -> Report Generation Completed.");
    }

    private double ParseDoubleSafe(string value)
    {
      if (string.IsNullOrWhiteSpace(value)) return 0.0;
      if (value.Trim() == "_") return 0.0;
      if (double.TryParse(value, out double result)) return result;
      return 0.0;
    }

    // ====================================================================
    // Tab 1: Model
    // ====================================================================
    private void CreateSheet_Model(XLWorkbook wb)
    {
      var ws = wb.Worksheets.Add("Model");
      ws.Column(2).Width = 80;

      // [설정] 이미지 크기 조절
      double fullModelScale = 0.5;
      double detailModelScale = 0.35;

      var pngFiles = Directory.GetFiles(_outputFolder, "*.png");

      if (pngFiles.Length == 0)
      {
        ws.Cell(2, 2).Value = $"[Warning] No PNG files found in: {_outputFolder}";
        return;
      }

      var sortedFiles = pngFiles.OrderBy(f =>
      {
        string name = Path.GetFileName(f);
        if (name.Contains("Full_Model")) return "000_Full";
        return name;
      }).ToList();

      int currentRow = 2;
      foreach (var file in sortedFiles)
      {
        string name = Path.GetFileNameWithoutExtension(file);
        bool isFullModel = name.Contains("Full_Model");

        var cell = ws.Cell(currentRow, 2);
        cell.Value = name;
        cell.Style.Font.Bold = true;
        cell.Style.Font.FontSize = 14;
        cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        currentRow++;

        try
        {
          var pic = ws.AddPicture(file).MoveTo(ws.Cell(currentRow, 2));

          if (isFullModel)
          {
            pic.Scale(fullModelScale);
            currentRow += 45;
          }
          else
          {
            pic.Scale(detailModelScale);
            currentRow += 30;
          }
        }
        catch (Exception ex)
        {
          ws.Cell(currentRow, 2).Value = $"[Image Error] {ex.Message}";
          currentRow += 2;
        }
      }
    }

    // ====================================================================
    // Tab 2: Element_Info (★ 정렬 적용됨)
    // ====================================================================
    private void CreateSheet_ElementInfo(XLWorkbook wb)
    {
      var ws = wb.Worksheets.Add("Element_Info");
      string[] headers = { "Element ID", "Type", "Bt", "Tt", "Hw", "Tw", "Bb", "Tb" };

      for (int i = 0; i < headers.Length; i++)
      {
        var cell = ws.Cell(1, i + 1);
        cell.Value = headers[i];
        ApplyHeaderStyle(cell);
      }

      int row = 2;

      // ★ [수정] Element ID(Key) 기준으로 오름차순 정렬
      var sortedElements = _context.Elements.OrderBy(x => x.Key);

      foreach (var kv in sortedElements)
      {
        var elem = kv.Value;
        int eid = kv.Key;

        if (!_context.Properties.TryGetValue(elem.PropertyID, out var prop)) continue;

        var dim = prop.Dim;
        double Bt = 0, Tt = 0, Hw = 0, Tw = 0, Bb = 0, Tb = 0;

        if (prop.Type == "I" && dim.Count >= 6)
        {
          double H = dim[0];
          Bb = dim[1]; Bt = dim[2];
          Tt = dim[3]; Tb = dim[4]; Tw = dim[5];
          Hw = H - Tt - Tb;
        }
        else if (prop.Type == "T" && dim.Count >= 4)
        {
          Bt = dim[0]; double H = dim[1];
          Tt = dim[2]; Tw = dim[3];
          Hw = H - Tt;
        }
        else if (dim.Count > 0)
        {
          Hw = dim[0];
        }

        ws.Cell(row, 1).Value = eid;
        ws.Cell(row, 2).Value = prop.Type;
        ws.Cell(row, 3).Value = Bt;
        ws.Cell(row, 4).Value = Tt;
        ws.Cell(row, 5).Value = Hw;
        ws.Cell(row, 6).Value = Tw;
        ws.Cell(row, 7).Value = Bb;
        ws.Cell(row, 8).Value = Tb;

        ws.Range(row, 1, row, 8).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        row++;
      }
    }

    // ====================================================================
    // Tab 3: Load_MF
    // ====================================================================
    private void CreateSheet_LoadMF(XLWorkbook wb)
    {
      var ws = wb.Worksheets.Add("Load_MF");

      ws.Cell("A1").Value = "Name";
      ws.Cell("B1").Value = "Type";
      ws.Range("C1:E1").Merge().Value = "Location";
      ws.Cell("C2").Value = "X"; ws.Cell("D2").Value = "Y"; ws.Cell("E2").Value = "Z";

      ws.Cell("F1").Value = "SWL";
      ws.Cell("G1").Value = "a";
      ws.Cell("H1").Value = "b";
      ws.Cell("I1").Value = "c";

      ws.Range("J1:L1").Merge().Value = "Calculated Force";
      ws.Cell("J2").Value = "Force X"; ws.Cell("K2").Value = "Force Y"; ws.Cell("L2").Value = "Force Z";

      ApplyHeaderStyle(ws.Range("A1:L2"));

      int row = 3;
      foreach (var mf in _mfList)
      {
        ws.Cell(row, 1).Value = mf.ID;
        ws.Cell(row, 2).Value = mf.Type;
        ws.Cell(row, 3).Value = mf.Location[0];
        ws.Cell(row, 4).Value = mf.Location[1];
        ws.Cell(row, 5).Value = mf.Location[2];
        ws.Cell(row, 6).Value = mf.SWL;
        ws.Cell(row, 7).Value = mf.a;
        ws.Cell(row, 8).Value = mf.b;
        ws.Cell(row, 9).Value = mf.c;

        if (_calculatedMfLoads.TryGetValue(mf.ID, out var forces))
        {
          ws.Cell(row, 10).Value = Math.Round(forces.Fx, 1);
          ws.Cell(row, 11).Value = Math.Round(forces.Fy, 1);
          ws.Cell(row, 12).Value = Math.Round(forces.Fz, 1);
        }
        else
        {
          ws.Cell(row, 10).Value = "-";
        }

        ws.Range(row, 1, row, 12).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        ws.Range(row, 1, row, 12).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        ws.Range(row, 10, row, 12).Style.Fill.BackgroundColor = XLColor.FromHtml("#E2EFDA");

        row++;
      }
    }

    // ====================================================================
    // Tab 4: Load_Winch
    // ====================================================================
    private void CreateSheet_LoadWinch(XLWorkbook wb)
    {
      var ws = wb.Worksheets.Add("Load_Winch");

      var loadCases = new List<(string Name, Dictionary<string, List<string>> Data)>
            {
                ("Forward", _winchData.Forward_LC),
                ("Backward", _winchData.Backward_LC),
                ("GreenSea Front Left", _winchData.GreenSea_FrongLeft_LC),
                ("GreenSea Front Right", _winchData.Forward_GreenSea_FrongRight_LC),
                ("Test", _winchData.Test_LC)
            };

      // Header
      ws.Cell(1, 1).Value = "Location";
      ws.Range(1, 1, 2, 1).Merge().Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;

      int col = 2;
      foreach (var lc in loadCases)
      {
        ws.Cell(1, col).Value = lc.Name;
        ws.Range(1, col, 1, col + 2).Merge();

        ws.Cell(2, col).Value = "X";
        ws.Cell(2, col + 1).Value = "Y";
        ws.Cell(2, col + 2).Value = "Z";

        col += 3;
      }
      ApplyHeaderStyle(ws.Range(1, 1, 2, col - 1));

      // Data
      var allWinchIds = _winchData.WinchLocation.Keys.OrderBy(k => k).ToList();

      int row = 3;
      foreach (var wid in allWinchIds)
      {
        ws.Cell(row, 1).Value = wid;

        int dataCol = 2;
        foreach (var lc in loadCases)
        {
          if (lc.Data.TryGetValue(wid, out var values) && values.Count >= 3)
          {
            ws.Cell(row, dataCol).Value = ParseDoubleSafe(values[0]);
            ws.Cell(row, dataCol + 1).Value = ParseDoubleSafe(values[1]);
            ws.Cell(row, dataCol + 2).Value = ParseDoubleSafe(values[2]);
          }
          else if (lc.Data.TryGetValue(wid, out var valSingle) && valSingle.Count == 1)
          {
            ws.Cell(row, dataCol + 2).Value = ParseDoubleSafe(valSingle[0]);
          }
          else
          {
            ws.Cell(row, dataCol).Value = 0;
            ws.Cell(row, dataCol + 1).Value = 0;
            ws.Cell(row, dataCol + 2).Value = 0;
          }
          dataCol += 3;
        }

        ws.Range(row, 1, row, col - 1).Style.Border.BottomBorder = XLBorderStyleValues.Thin;
        ws.Range(row, 1, row, col - 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        row++;
      }
    }

    // ====================================================================
    // Tab 5: Stress Results
    // ====================================================================
    private void CreateSheet_StressResults(XLWorkbook wb)
    {
      if (_f06Result == null || !_f06Result.IsParsedSuccessfully)
      {
        var ws = wb.Worksheets.Add("No_Results");
        ws.Cell("A1").Value = "No Analysis Results Found";
        return;
      }

      foreach (var kv in _f06Result.Subcases)
      {
        int subcaseId = kv.Key;
        var subcaseData = kv.Value;

        string sheetName = $"Results_LC{subcaseId}";
        var ws = wb.Worksheets.Add(sheetName);

        // 1. Load Case Title
        ws.Cell("A1").Value = $"Load Case {subcaseId}";
        ws.Cell("A1").Style.Font.Bold = true;
        ws.Cell("A1").Style.Font.FontSize = 14;

        // 2. Legend
        var legendStyle = ws.Range("A2:B8");
        legendStyle.Style.Font.FontSize = 10;

        ws.Cell(2, 1).Value = "Nx"; ws.Cell(2, 2).Value = "Axial Stress";
        ws.Cell(3, 1).Value = "Mx"; ws.Cell(3, 2).Value = "Torsional Stress";
        ws.Cell(4, 1).Value = "Qy"; ws.Cell(4, 2).Value = "Shear Stress in local Y direction";
        ws.Cell(5, 1).Value = "Qz"; ws.Cell(5, 2).Value = "Shear Stress in local Z direction";
        ws.Cell(6, 1).Value = "My"; ws.Cell(6, 2).Value = "Bending Stress about local Y axis";
        ws.Cell(7, 1).Value = "Mz"; ws.Cell(7, 2).Value = "Bending Stress about local Z axis";

        ws.Range("A2:A7").Style.Font.Bold = true;
        ws.Range("A2:A7").Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

        // 3. Headers
        string[] headers = {
                    "Element", "Node",
                    "Bending_1", "Bending_2", "Shear_1", "Shear_2", "Axial", "Torque",
                    "Nx", "Mx", "Qy", "Qz", "My", "Mz"
                };

        for (int i = 0; i < headers.Length; i++)
        {
          ws.Cell(9, i + 1).Value = headers[i];
        }
        ApplyHeaderStyle(ws.Range(9, 1, 9, headers.Length));

        // 4. Data
        int row = 10;
        foreach (var force in subcaseData.BeamForces)
        {
          ws.Cell(row, 1).Value = force.ElementID;
          ws.Cell(row, 2).Value = force.GridID;
          ws.Cell(row, 3).Value = force.BM1;
          ws.Cell(row, 4).Value = force.BM2;
          ws.Cell(row, 5).Value = force.Shear1;
          ws.Cell(row, 6).Value = force.Shear2;
          ws.Cell(row, 7).Value = force.Axial;
          ws.Cell(row, 8).Value = force.TotalTorque;

          ws.Cell(row, 9).Value = force.Calc_Nx;
          ws.Cell(row, 10).Value = force.Calc_Mx;
          ws.Cell(row, 11).Value = force.Calc_Qy;
          ws.Cell(row, 12).Value = force.Calc_Qz;
          ws.Cell(row, 13).Value = force.Calc_My;
          ws.Cell(row, 14).Value = force.Calc_Mz;

          row++;
        }
      }
    }

    private void ApplyHeaderStyle(IXLRange range)
    {
      range.Style.Font.Bold = true;
      range.Style.Fill.BackgroundColor = XLColor.FromHtml("#FFE699");
      range.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
      range.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
      range.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
    }

    private void ApplyHeaderStyle(IXLCell cell)
    {
      ApplyHeaderStyle(cell.AsRange());
    }

    private void LoadMfCalculationResults()
    {
      string csvPath = Path.Combine(_outputFolder, "Report_LoadCalculation_MF.csv");
      if (!File.Exists(csvPath)) return;

      try
      {
        var lines = File.ReadAllLines(csvPath);
        foreach (var line in lines)
        {
          if (line.StartsWith("Type") || line.StartsWith("=") || string.IsNullOrWhiteSpace(line)) continue;
          var parts = line.Split(',');
          if (parts.Length < 10) continue;

          if (parts[0] == "MF")
          {
            string id = parts[1];
            double fx = double.Parse(parts[7]);
            double fy = double.Parse(parts[8]);
            double fz = double.Parse(parts[9]);
            _calculatedMfLoads[id] = (fx, fy, fz);
          }
        }
      }
      catch { }
    }
  }
}
