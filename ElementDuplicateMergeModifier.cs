using MooringFitting2026.Inspector.ElementInspector;
using MooringFitting2026.Model.Entities;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace MooringFitting2026.Modifier.ElementModifier
{
  /// <summary>
  /// 동일한 노드를 공유하는 중복 요소(Duplicate Elements)들을 찾아
  /// 물성치(단면적, 관성모멘트 등)를 합산하여 하나의 등가 요소(Equivalent Element)로 병합합니다.
  /// 계산의 신뢰성을 위해 상세 리포트를 CSV(Excel 호환) 파일로 저장합니다.
  /// </summary>
  public static class ElementDuplicateMergeModifier
  {
    public sealed record Options(
        string ReportFilePath,   // CSV 리포트 파일 경로
        bool Debug = false
    );

    public sealed record Result(
        int GroupsFound,
        int ElementsMerged,
        int NewPropertiesCreated
    );

    /// <summary>
    /// 실행 진입점
    /// </summary>
    public static Result Run(FeModelContext context, Options opt, Action<string> log)
    {
      if (context == null) throw new ArgumentNullException(nameof(context));
      log ??= Console.WriteLine;

      var elements = context.Elements;
      var properties = context.Properties;

      // CSV 작성을 위한 StringBuilder (Excel 한글 깨짐 방지를 위해 UTF-8 BOM 필요)
      var sbReport = new StringBuilder();

      // CSV 헤더 (메타 데이터)
      sbReport.AppendLine("Report Type,Duplicate Element Merge Calculation Report (Ship Structure Adjusted)");
      sbReport.AppendLine($"Date,{DateTime.Now}");
      sbReport.AppendLine("Description,Calculates stiffness for 'Deck+Stiffener' 1D Idealization.");
      sbReport.AppendLine(); // 빈 줄

      // 1. 중복 그룹 찾기
      var duplicateGroups = ElementDuplicateInspector.FindDuplicateGroups(context);

      if (duplicateGroups.Count == 0)
      {
        if (opt.Debug) log("   -> [병합] 중복된 요소가 없습니다.");
        return new Result(0, 0, 0);
      }

      if (opt.Debug) log($"   -> [병합] 총 {duplicateGroups.Count}개의 중복 그룹을 발견했습니다.");

      int mergedCount = 0;
      int newPropCount = 0;

      // CSV 데이터 헤더
      sbReport.AppendLine("GroupID,Nodes,ElementID,PropID,Type,Mapped_Dimensions,Area(mm2),Izz(mm4),Iyy(mm4),J(mm4),Note");

      // 2. 각 그룹별 병합 수행
      int groupIndex = 1;
      foreach (var groupIDs in duplicateGroups)
      {
        if (groupIDs == null || groupIDs.Count < 2) continue;

        // ---------------------------------------------------------
        // [핵심] 등가 물성 계산 (Equivalent Property Calculation)
        // ---------------------------------------------------------
        var calcResult = CalculateEquivalentProperty(
            groupIndex, groupIDs, elements, properties, sbReport);

        // 3. 새 Property 생성 및 등록
        // 베이스 재질 ID 가져오기 (첫 번째 요소 기준)
        int firstEleID = groupIDs[0];
        int baseMatID = 1;
        if (elements.Contains(firstEleID) && properties.Contains(elements[firstEleID].PropertyID))
        {
          baseMatID = properties[elements[firstEleID].PropertyID].MaterialID;
        }

        // 새 물성 추가 (이름: EQUIV_PBEAM)
        // Dim 순서: [0]Area, [1]Izz, [2]Iyy, [3]J
        var newDims = new List<double> { calcResult.Area, calcResult.Izz, calcResult.Iyy, calcResult.J };
        int newPropID = properties.AddOrGet("EQUIV_PBEAM", newDims, baseMatID);
        newPropCount++;

        // 4. 모델 수정 (첫 번째 요소만 남기고 나머지 삭제 + 속성 교체)
        var primaryEle = elements[firstEleID];

        // 기존 요소 업데이트 (PropertyID 교체)
        var newExtra = (primaryEle.ExtraData != null)
            ? new Dictionary<string, string>(primaryEle.ExtraData)
            : new Dictionary<string, string>();

        newExtra["MergedFrom"] = string.Join("+", groupIDs);

        elements.AddWithID(firstEleID, primaryEle.NodeIDs.ToList(), newPropID, newExtra);

        // 나머지 요소 삭제
        for (int i = 1; i < groupIDs.Count; i++)
        {
          elements.Remove(groupIDs[i]);
        }

        mergedCount++;
        if (opt.Debug)
          log($"   -> [병합 완료] 그룹#{groupIndex} -> E{firstEleID} (P{newPropID})");

        groupIndex++;
      }

      // [추가] Appendix: 선체 구조 매핑 설명
      sbReport.AppendLine();
      sbReport.AppendLine("===================================================================================");
      sbReport.AppendLine("[Appendix] Dimension Mapping Logic (Ship Structure Idealization)");
      sbReport.AppendLine("1. T-Type (Deck + T-Stiffener)");
      sbReport.AppendLine("   - Input Order: [W_top] [H] [tw] [tf_top]");
      sbReport.AppendLine("   - Interpretation: W_top=DeckWidth(750), H=StiffenerHeight");
      sbReport.AppendLine();
      sbReport.AppendLine("2. I-Type (Deck + I-Stiffener / Asymmetric)");
      sbReport.AppendLine("   - Input Order: [H] [W_bot] [W_top] [tf_top] [tw] [tf_bot]");
      sbReport.AppendLine("   - Interpretation: W_top=DeckWidth(750), W_bot=StiffenerFlange, H=TotalHeight");
      sbReport.AppendLine("===================================================================================");

      // 5. 리포트 파일 저장 (UTF-8 BOM 포함)
      try
      {
        var utf8WithBom = new UTF8Encoding(true);
        File.WriteAllText(opt.ReportFilePath, sbReport.ToString(), utf8WithBom);
        log($"   -> [보고서] 엑셀용 CSV 파일이 저장되었습니다: {Path.GetFileName(opt.ReportFilePath)}");
      }
      catch (Exception ex)
      {
        log($"   -> [오류] 보고서 저장 실패: {ex.Message}");
      }

      return new Result(duplicateGroups.Count, mergedCount, newPropCount);
    }

    /// <summary>
    /// 실제 등가 강성 계산 로직 (선체 구조 전용 매핑 적용)
    /// </summary>
    private static MergedSectionResult CalculateEquivalentProperty(
        int groupIdx,
        List<int> elementIDs,
        Elements allElements,
        Properties props,
        StringBuilder csv)
    {
      double sumArea = 0.0;
      double sumIzz = 0.0;
      double sumIyy = 0.0;
      double sumJ = 0.0;

      string nodeInfo = "Unknown";
      if (elementIDs.Count > 0 && allElements.Contains(elementIDs[0]))
      {
        var ids = allElements[elementIDs[0]].NodeIDs;
        nodeInfo = $"[{ids[0]}-{ids[1]}]";
      }

      foreach (var eid in elementIDs)
      {
        if (!allElements.Contains(eid)) continue;

        var ele = allElements[eid];
        double a = 0, izz = 0, iyy = 0, j = 0;
        string type = "Unknown";
        string note = "Raw";
        string dimStr = "";
        int pid = ele.PropertyID;

        if (props.Contains(pid))
        {
          var p = props[pid];
          type = p.Type.ToUpper();
          var dim = p.Dim;

          if (type == "PBEAM" || type == "EQUIV_PBEAM")
          {
            if (dim.Count > 0) a = dim[0];
            if (dim.Count > 1) izz = dim[1];
            if (dim.Count > 2) iyy = dim[2];
            if (dim.Count > 3) j = dim[3];
            dimStr = "Pre-calculated";
            note = "Direct";
          }
          // -----------------------------------------------------------------------
          // [수정] 선체 구조용 T-Type 매핑 (W가 먼저 옴: Deck Width)
          // Data: 750, 228.6, 10, 18 -> W_top, H, tw, tf_top
          // -----------------------------------------------------------------------
          else if (type == "T")
          {
            if (dim.Count >= 4)
            {
              double W_top = dim[0]; // Deck Width (750)
              double H = dim[1];     // Height (228.6)
              double tw = dim[2];    // Web Thickness (10)
              double tf_top = dim[3]; // Deck Thickness (18)

              dimStr = $"W_top={W_top}; H={H}; tw={tw}; tf_top={tf_top}";

              // Area
              a = (W_top * tf_top) + ((H - tf_top) * tw);

              // Centroid (from bottom)
              double h_web = H - tf_top;
              double A_web = h_web * tw;
              double y_web = h_web / 2.0; // Web center

              double A_flange = W_top * tf_top;
              double y_flange = h_web + tf_top / 2.0; // Flange center

              double y_bar = (A_web * y_web + A_flange * y_flange) / a;

              // Izz (Strong Axis)
              double I_web = (tw * Math.Pow(h_web, 3)) / 12.0 + A_web * Math.Pow(y_web - y_bar, 2);
              double I_flange = (W_top * Math.Pow(tf_top, 3)) / 12.0 + A_flange * Math.Pow(y_flange - y_bar, 2);
              izz = I_web + I_flange;

              // Iyy (Weak Axis) - Symmetric about web
              double Iyy_web = (h_web * Math.Pow(tw, 3)) / 12.0;
              double Iyy_flange = (tf_top * Math.Pow(W_top, 3)) / 12.0;
              iyy = Iyy_web + Iyy_flange;

              // J (St. Venant) - Open Section Summation
              j = (1.0 / 3.0) * (W_top * Math.Pow(tf_top, 3) + h_web * Math.Pow(tw, 3));

              note = "Ship_T (Deck+Web)";
            }
          }
          // -----------------------------------------------------------------------
          // [수정] 선체 구조용 I-Type 매핑 (Asymmetric, Deck + I-Stiffener)
          // Data: 154.9, 90, 750, 20... -> H, W_bot, W_top, tf_top, tw, tf_bot
          // -----------------------------------------------------------------------
          else if (type == "I")
          {
            if (dim.Count >= 6) // Ensure we have all dims
            {
              double H = dim[0];      // 154.9
              double W_bot = dim[1];  // 90
              double W_top = dim[2];  // Deck Width (750)
              double tf_top = dim[3]; // Deck Thickness (20)
              double tw = dim[4];     // 10
              double tf_bot = dim[5]; // 10

              dimStr = $"H={H}; Wb={W_bot}; Wt={W_top}; tft={tf_top}; tw={tw}; tfb={tf_bot}";

              double h_web = H - tf_top - tf_bot;
              if (h_web < 0) h_web = 0; // Safety check

              // Area
              double A_top = W_top * tf_top;
              double A_bot = W_bot * tf_bot;
              double A_web = h_web * tw;
              a = A_top + A_bot + A_web;

              // Centroid (from bottom)
              double y_bot = tf_bot / 2.0;
              double y_web = tf_bot + h_web / 2.0;
              double y_top = tf_bot + h_web + tf_top / 2.0;

              double y_bar = (A_bot * y_bot + A_web * y_web + A_top * y_top) / a;

              // Izz (Strong Axis)
              double Izz_bot = (W_bot * Math.Pow(tf_bot, 3)) / 12.0 + A_bot * Math.Pow(y_bot - y_bar, 2);
              double Izz_web = (tw * Math.Pow(h_web, 3)) / 12.0 + A_web * Math.Pow(y_web - y_bar, 2);
              double Izz_top = (W_top * Math.Pow(tf_top, 3)) / 12.0 + A_top * Math.Pow(y_top - y_bar, 2);
              izz = Izz_bot + Izz_web + Izz_top;

              // Iyy (Weak Axis)
              double Iyy_bot = (tf_bot * Math.Pow(W_bot, 3)) / 12.0;
              double Iyy_web = (h_web * Math.Pow(tw, 3)) / 12.0;
              double Iyy_top = (tf_top * Math.Pow(W_top, 3)) / 12.0;
              iyy = Iyy_bot + Iyy_web + Iyy_top;

              // J
              j = (1.0 / 3.0) * (W_top * Math.Pow(tf_top, 3) + W_bot * Math.Pow(tf_bot, 3) + h_web * Math.Pow(tw, 3));

              note = "Ship_I (Asym)";
            }
            else
            {
              // 데이터 부족 시 Fallback (로그에 남김)
              dimStr = $"Missing Data (Count={dim.Count})";
              note = "Error_I (Dim<6)";
            }
          }
          else if (type == "ANGLE" || type == "L")
          {
            // Angle의 경우 표준 매핑 유지 (필요 시 수정 가능)
            if (dim.Count >= 4)
            {
              double H = dim[0]; double W = dim[1];
              double t1 = dim[2]; double t2 = dim[3];
              dimStr = $"H={H}; W={W}; t1={t1}; t2={t2}";
              a = (H * t1) + ((W - t1) * t2);
              izz = (t1 * Math.Pow(H, 3)) / 12.0;
              iyy = (t2 * Math.Pow(W, 3)) / 12.0;
              j = (1.0 / 3.0) * (H * Math.Pow(t1, 3) + W * Math.Pow(t2, 3));
              note = "Angle";
            }
          }
          else if (type == "FLATBAR" || type == "BAR")
          {
            if (dim.Count >= 2)
            {
              double H = dim[0]; double T = dim[1];
              dimStr = $"H={H}; T={T}";
              a = H * T;
              izz = (T * Math.Pow(H, 3)) / 12.0;
              iyy = (H * Math.Pow(T, 3)) / 12.0;
              j = (1.0 / 3.0) * H * Math.Pow(T, 3);
              note = "FlatBar";
            }
          }
          else
          {
            dimStr = "Unknown Type";
            if (dim.Count > 0) a = dim[0];
            if (dim.Count > 1) izz = dim[1];
            if (dim.Count > 2) iyy = dim[2];
            if (dim.Count > 3) j = dim[3];
            note = "Direct_Fallback";
          }
        }

        // CSV 로그 기록
        csv.AppendLine($"{groupIdx},{nodeInfo},{eid},{pid},{type},{dimStr},{a:F4},{izz:F4},{iyy:F4},{j:F4},{note}");

        sumArea += a;
        sumIzz += izz;
        sumIyy += iyy;
        sumJ += j;
      }

      // 합계 행
      csv.AppendLine($"{groupIdx},{nodeInfo},MERGED,NEW,EQUIV,,{sumArea:F4},{sumIzz:F4},{sumIyy:F4},{sumJ:F4},Total Sum");
      return new MergedSectionResult(sumArea, sumIzz, sumIyy, sumJ);
    }

    private record MergedSectionResult(double Area, double Izz, double Iyy, double J);
  }
}
