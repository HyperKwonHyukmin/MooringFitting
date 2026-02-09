using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace MooringFitting2026.Parsers
{
  public class F06Parser
  {
    // =================================================================
    // 1. 데이터 구조 정의
    // =================================================================

    public class F06ResultData
    {
      public bool IsParsedSuccessfully { get; set; } = false;
      public Dictionary<int, SubcaseResult> Subcases { get; set; } = new Dictionary<int, SubcaseResult>();

      public SubcaseResult GetOrAddSubcase(int id)
      {
        if (!Subcases.ContainsKey(id))
          Subcases[id] = new SubcaseResult { SubcaseID = id };
        return Subcases[id];
      }
    }

    public class SubcaseResult
    {
      public int SubcaseID { get; set; }
      public List<DisplacementData> Displacements { get; set; } = new List<DisplacementData>();
      public List<BeamForceData> BeamForces { get; set; } = new List<BeamForceData>();
      public List<BeamStressData> BeamStresses { get; set; } = new List<BeamStressData>();
    }

    public class DisplacementData
    {
      public int NodeID { get; set; }
      public string Type { get; set; }
      public double T1 { get; set; }
      public double T2 { get; set; }
      public double T3 { get; set; }
      public double R1 { get; set; }
      public double R2 { get; set; }
      public double R3 { get; set; }

      public override string ToString()
          => $"Node {NodeID}: [{T1:0.0E+0}, {T2:0.0E+0}, {T3:0.0E+0}]";
    }

    public class BeamForceData
    {
      // ... 기존 필드 (ElementID, Forces, Calculated Stresses 등) 유지 ...
      public int ElementID { get; set; }
      public int GridID { get; set; }
      public double Pos { get; set; }

      // Raw Forces
      public double BM1 { get; set; }
      public double BM2 { get; set; }
      public double Shear1 { get; set; }
      public double Shear2 { get; set; }
      public double Axial { get; set; }
      public double TotalTorque { get; set; }
      public double WarpingTorque { get; set; }

      // Calculated Stresses
      public double Calc_Nx { get; set; }
      public double Calc_Mx { get; set; }
      public double Calc_Qy { get; set; }
      public double Calc_Qz { get; set; }
      public double Calc_My { get; set; }
      public double Calc_Mz { get; set; }

      // [NEW] 검증용 추가 필드 (2차 단면 모멘트)
      public double Debug_I_Strong { get; set; } // Izz (Strong Axis)
      public double Debug_I_Weak { get; set; }   // Iyy (Weak Axis)

      // [NEW] 검증을 위한 참조 데이터 (Calculation References)
      // 어떤 치수가 들어갔는지 확인 (예: "H=300, Tw=10...")
      public string Debug_DimInfo { get; set; } = "";

      // 계산에 사용된 핵심 물성치 (분모 값들)
      public double Debug_Area { get; set; }
      public double Debug_Wx { get; set; }
      public double Debug_Ay { get; set; }
      public double Debug_Az { get; set; }
      public double Debug_Wy_Min { get; set; } // min(Wyb, Wyt)
      public double Debug_Wz_Min { get; set; } // min(Wz+, Wz-)

      public override string ToString()
          => $"Elem {ElementID}: Nx={Calc_Nx:F2} (F={Axial:F0}/A={Debug_Area:F0})";
    }

    public class BeamStressData
    {
      public int ElementID { get; set; }
      public int GridID { get; set; }
      public double Station { get; set; } // Station along beam

      // Stress at Recovery Points (C, D, E, F)
      public double S_C { get; set; }
      public double S_D { get; set; }
      public double S_E { get; set; }
      public double S_F { get; set; }

      public double MaxStress { get; set; }
      public double MinStress { get; set; }

      public override string ToString()
          => $"Elem {ElementID} (G{GridID}): Max={MaxStress:0.0E+0}, Min={MinStress:0.0E+0}";
    }

    // =================================================================
    // 2. 파싱 로직
    // =================================================================

    public F06ResultData Parse(string f06FilePath, Action<string> log)
    {
      var result = new F06ResultData();

      if (!File.Exists(f06FilePath))
      {
        log($"[F06 Parser] Error: File not found - {f06FilePath}");
        return result;
      }

      log($"[F06 Parser] Parsing started: {Path.GetFileName(f06FilePath)}");

      try
      {
        using (var reader = new StreamReader(f06FilePath))
        {
          string line;
          int currentSubcaseId = 0;

          while ((line = reader.ReadLine()) != null)
          {
            // A. Subcase ID 감지
            // (ParseBeamForceBlock 내부에서 ref로 업데이트된 경우를 위해 여기서도 체크하지만,
            //  보통은 내부에서 break하고 나오면 다음 줄을 읽으면서 자연스럽게 처리됨)
            if (line.Contains("SUBCASE", StringComparison.OrdinalIgnoreCase))
            {
              int newId = ExtractSubcaseId(line);
              if (newId > 0) currentSubcaseId = newId;
            }

            // B. Displacement Vector 감지
            if (line.Contains("D I S P L A C E M E N T   V E C T O R"))
            {
              var subcaseData = result.GetOrAddSubcase(currentSubcaseId);
              ParseDisplacementBlock(reader, subcaseData);
            }

            // C. Beam Force 감지
            if (line.Contains("F O R C E S   I N   B E A M   E L E M E N T S"))
            {
              // [핵심 변경] ref currentSubcaseId를 전달하여 내부에서 ID 변경 시 반영되도록 함
              // 파싱 전에 현재 ID로 객체를 가져오지만, 
              // 내부에서 ID가 바뀌면 result 객체에서 새로 GetOrAdd 해야 하므로
              // 메서드 내부 로직을 조금 수정하여 'result' 자체를 넘기는 게 안전함.
              // 하지만 기존 구조 유지를 위해, ID가 바뀌면 즉시 리턴하게 설계.

              var subcaseData = result.GetOrAddSubcase(currentSubcaseId);
              ParseBeamForceBlock(reader, subcaseData, ref currentSubcaseId);
            }

            // D. Beam Stress (CBEAM) [New]
            if (line.Contains("S T R E S S E S   I N   B E A M   E L E M E N T S") && line.Contains("C B E A M"))
            {
              var subcaseData = result.GetOrAddSubcase(currentSubcaseId);
              ParseBeamStressBlock(reader, subcaseData, ref currentSubcaseId);
            }
          }
        }

        result.IsParsedSuccessfully = true;
        log($"[F06 Parser] Completed.");
        foreach (var kv in result.Subcases)
        {
          log($"   -> Subcase {kv.Key}: {kv.Value.Displacements.Count} Disp, {kv.Value.BeamForces.Count} BeamForces, " +
            $"{kv.Value.BeamStresses.Count} BeamStresses");
        }
      }
      catch (Exception ex)
      {
        log($"[F06 Parser] Exception: {ex.Message}\n{ex.StackTrace}");
      }

      return result;
    }

    private int ExtractSubcaseId(string line)
    {
      try
      {
        int idx = line.LastIndexOf("SUBCASE", StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return 0;
        string afterKeyword = line.Substring(idx + 7).Trim();
        string[] tokens = afterKeyword.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length > 0 && int.TryParse(tokens[0], out int id))
        {
          return id;
        }
      }
      catch { }
      return 0;
    }

    private void ParseDisplacementBlock(StreamReader reader, SubcaseResult subcaseData)
    {
      reader.ReadLine(); // 빈 줄
      reader.ReadLine(); // 헤더

      string line;
      while ((line = reader.ReadLine()) != null)
      {
        if (IsEndOfSection(line)) break;
        if (ShouldSkipLine(line)) continue;

        var parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 8) continue;
        if (!int.TryParse(parts[0], out int nodeId)) continue;

        try
        {
          subcaseData.Displacements.Add(new DisplacementData
          {
            NodeID = nodeId,
            Type = parts[1],
            T1 = ParseNastranDouble(parts[2]),
            T2 = ParseNastranDouble(parts[3]),
            T3 = ParseNastranDouble(parts[4]),
            R1 = ParseNastranDouble(parts[5]),
            R2 = ParseNastranDouble(parts[6]),
            R3 = ParseNastranDouble(parts[7])
          });
        }
        catch { continue; }
      }
    }

    // =================================================================
    // [핵심 수정] Beam Force 파싱 (Subcase 전환 감지 추가)
    // =================================================================
    private void ParseBeamForceBlock(StreamReader reader, SubcaseResult subcaseData, ref int currentSubcaseId)
    {
      int currentElementID = 0;

      string line;
      while ((line = reader.ReadLine()) != null)
      {
        // 1. 섹션 종료 체크
        if (IsEndOfSection(line)) break;

        // 2. SUBCASE 변경 감지 (ShouldSkipLine에서 제거하고 여기서 직접 처리)
        if (line.Contains("SUBCASE", StringComparison.OrdinalIgnoreCase))
        {
          int newId = ExtractSubcaseId(line);

          // 현재 읽고 있는 Subcase ID와 다르면
          // "다음 Subcase가 시작되었다"는 뜻이므로 여기서 탈출!
          if (newId != 0 && newId != currentSubcaseId)
          {
            currentSubcaseId = newId; // [중요] 메인 루프 변수 업데이트
            break; // 현재 블록 파싱 종료 -> 메인 루프로 복귀
          }

          // ID가 같으면(페이지 넘김 등) 그냥 Skip
          continue;
        }

        // 3. 페이지 넘김/헤더 체크
        if (ShouldSkipLine(line)) continue;

        if (string.IsNullOrWhiteSpace(line)) continue;

        // 4. 제어 문자 '0' 처리
        if (line.Length > 0 && line[0] == '0')
        {
          char[] chars = line.ToCharArray();
          chars[0] = ' ';
          line = new string(chars);
        }

        int leadingSpaces = 0;
        foreach (char c in line)
        {
          if (c == ' ') leadingSpaces++;
          else break;
        }

        string trimmed = line.Trim();
        if (trimmed.Length == 0) continue;
        var parts = trimmed.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

        if (!char.IsDigit(parts[0][0]) && parts[0][0] != '-') continue;

        // Case A: ID만 있는 줄
        if (parts.Length == 1 && int.TryParse(parts[0], out int onlyId))
        {
          currentElementID = onlyId;
          continue;
        }

        // Case B: 데이터 라인
        if (parts.Length < 8) continue;

        try
        {
          int tokenIdx = 0;
          bool hasElementID = (leadingSpaces < 14);

          if (hasElementID)
          {
            if (int.TryParse(parts[tokenIdx], out int eid))
            {
              currentElementID = eid;
              tokenIdx++;
            }
          }

          if (tokenIdx >= parts.Length) continue;
          int gridId = int.Parse(parts[tokenIdx++]);

          var force = new BeamForceData
          {
            ElementID = currentElementID,
            GridID = gridId,
            Pos = ParseNastranDouble(parts[tokenIdx++]),
            BM1 = ParseNastranDouble(parts[tokenIdx++]),
            BM2 = ParseNastranDouble(parts[tokenIdx++]),
            Shear1 = ParseNastranDouble(parts[tokenIdx++]),
            Shear2 = ParseNastranDouble(parts[tokenIdx++]),
            Axial = ParseNastranDouble(parts[tokenIdx++]),
            TotalTorque = ParseNastranDouble(parts[tokenIdx++]),
            WarpingTorque = (tokenIdx < parts.Length) ? ParseNastranDouble(parts[tokenIdx]) : 0.0
          };

          subcaseData.BeamForces.Add(force);
        }
        catch { continue; }
      }
    }

    // =================================================================
    // [New] Beam Stress Parsing Logic (CBEAM)
    // =================================================================
    private void ParseBeamStressBlock(StreamReader reader, SubcaseResult subcaseData, ref int currentSubcaseId)
    {
      int currentElementID = 0;
      string line;

      while ((line = reader.ReadLine()) != null)
      {
        if (IsEndOfSection(line)) break;
        if (CheckSubcaseChange(line, ref currentSubcaseId)) break;
        if (ShouldSkipLine(line)) continue;

        // 제어 문자 '0' 처리
        if (line.Length > 0 && line[0] == '0') line = " " + line.Substring(1);

        string trimmed = line.Trim();
        if (trimmed.Length == 0) continue;
        var parts = trimmed.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

        // 헤더 스킵
        if (!char.IsDigit(parts[0][0]) && parts[0][0] != '-') continue;

        // Case A: Element ID만 있는 줄
        if (parts.Length == 1 && int.TryParse(parts[0], out int onlyId))
        {
          currentElementID = onlyId;
          continue;
        }
        //Console.WriteLine($"parts: {string.Join(",", parts)}, length:{parts.Length} ");
        // Case B: 데이터 라인 (최소 9개 컬럼: Grid, Station, S-C, S-D, S-E, S-F, Max, Min, MST, MSC)
        try
        {
          int tokenIdx = 0;

          // Element ID가 있는지 확인 (들여쓰기 기준)
          int leadingSpaces = 0;
          foreach (char c in line) { if (c == ' ') leadingSpaces++; else break; }

          bool hasElementID = (leadingSpaces < 14); // 임의 기준 (파일 포맷에 따라 조정 가능)

          if (hasElementID)
          {
            if (int.TryParse(parts[tokenIdx], out int eid))
            {
              currentElementID = eid;
              tokenIdx++;
            }
          }

          // Grid ID 파싱
          int gridId = 0;
          if (tokenIdx < parts.Length && int.TryParse(parts[tokenIdx], out int gid))
          {
            gridId = gid;
            tokenIdx++;
          }
          else
          {
            // Grid ID가 '0.000' 처럼 실수 형태(Station)일 수도 있고, 그냥 생략될 수도 있음
            // Nastran CBEAM Stress: ElemID, GridID, Station, S-C, ...
            // 만약 GridID 자리에 숫자가 아니거나 범위가 이상하면 Station일 수도 있음
            // 일단은 정수로 시도하고 안되면 Station으로 간주하거나 스킵
          }

          // Station
          double station = ParseNastranDouble(parts[tokenIdx++]);

          var stress = new BeamStressData
          {
            ElementID = currentElementID,
            GridID = gridId,
            Station = station,
            S_C = ParseNastranDouble(parts[tokenIdx++]),
            S_D = ParseNastranDouble(parts[tokenIdx++]),
            S_E = ParseNastranDouble(parts[tokenIdx++]),
            S_F = ParseNastranDouble(parts[tokenIdx++]),
            MaxStress = ParseNastranDouble(parts[tokenIdx++]),
            MinStress = ParseNastranDouble(parts[tokenIdx++]),
          };         

          subcaseData.BeamStresses.Add(stress);
        }
        catch { continue; }
      }
    }

    private bool CheckSubcaseChange(string line, ref int currentSubcaseId)
    {
      if (line.Contains("SUBCASE", StringComparison.OrdinalIgnoreCase))
      {
        int newId = ExtractSubcaseId(line);
        if (newId != 0 && newId != currentSubcaseId)
        {
          currentSubcaseId = newId;
          return true; // 탈출 신호
        }
      }
      return false;
    }

    private bool IsEndOfSection(string line)
    {
      if (string.IsNullOrWhiteSpace(line)) return false;
      if (line.Contains("D I S P L A C E M E N T")) return true;
      if (line.Contains("S T R E S S")) return true;
      if (line.Contains("E I G E N V A L U E")) return true;
      if (line.Contains("S P C   F O R C E S")) return true;
      return false;
    }

    private bool ShouldSkipLine(string line)
    {
      if (string.IsNullOrWhiteSpace(line)) return false;

      if (line.StartsWith("1") && line.Contains("PAGE")) return true;
      if (line.Contains("MSC Nastran")) return true;
      if (line.Contains("LOAD CASE")) return true;

      if (line.Contains("F O R C E S   I N   B E A M")) return true;
      if (line.Contains("ELEMENT-ID") && line.Contains("GRID")) return true;
      if (line.Contains("STAT") && line.Contains("DIST/")) return true;

      // [중요] SUBCASE는 여기서 체크하지 않고, ParseBeamForceBlock 내부에서 ID 변경 여부를 판단함!
      // if (line.Contains("SUBCASE")) return true; 

      return false;
    }

    private double ParseNastranDouble(string val)
    {
      if (double.TryParse(val, out double result)) return result;
      return 0.0;
    }
  }
}
