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
      sbReport.AppendLine("Report Type,Duplicate Element Merge Calculation Report");
      sbReport.AppendLine($"Date,{DateTime.Now}");
      sbReport.AppendLine("Description,Calculates equivalent stiffness (A/I/J) for overlapping elements based on their shape.");
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
      sbReport.AppendLine("GroupID,Nodes,ElementID,PropID,Type,Area(mm2),Izz(mm4),Iyy(mm4),J(mm4),Note");

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
    /// 실제 등가 강성 계산 로직 (형상별 공식 적용)
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

      // 그룹 정보 (노드 정보 추출)
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
        int pid = ele.PropertyID;

        if (props.Contains(pid))
        {
          var p = props[pid];
          type = p.Type.ToUpper(); // 대소문자 무시
          var dim = p.Dim;

          // [수정된 로직] 형상별 물성치 계산
          if (type == "PBEAM" || type == "EQUIV_PBEAM")
          {
            // PBEAM은 이미 A, I, I, J 값이 들어있음 (직접 값)
            if (dim.Count > 0) a = dim[0];
            if (dim.Count > 1) izz = dim[1];
            if (dim.Count > 2) iyy = dim[2];
            if (dim.Count > 3) j = dim[3];
            note = "Direct Value";
          }
          else if (type == "T") // T-Bar
          {
            // Dim: [0]H, [1]W, [2]tw, [3]tf
            if (dim.Count >= 4)
            {
              double H = dim[0]; double W = dim[1];
              double tw = dim[2]; double tf = dim[3];

              // 1. 단면적 (A)
              a = (H - tf) * tw + W * tf;

              // 2. 도심 (Centroid Y from bottom of web)
              // Web height = H - tf
              double h_web = H - tf;
              double area_web = h_web * tw;
              double area_flange = W * tf;

              double y_web = h_web / 2.0;
              double y_flange = h_web + tf / 2.0;

              double y_bar = (area_web * y_web + area_flange * y_flange) / a;

              // 3. 관성모멘트 Izz (Strong Axis - Horizontal bending axis)
              double I_web_own = (tw * Math.Pow(h_web, 3)) / 12.0;
              double I_flange_own = (W * Math.Pow(tf, 3)) / 12.0;

              double I_web_shift = area_web * Math.Pow(y_web - y_bar, 2);
              double I_flange_shift = area_flange * Math.Pow(y_flange - y_bar, 2);

              izz = I_web_own + I_web_shift + I_flange_own + I_flange_shift;

              // 4. 관성모멘트 Iyy (Weak Axis - Vertical bending axis)
              // Web is centered, Flange is centered
              double Iyy_web = (h_web * Math.Pow(tw, 3)) / 12.0;
              double Iyy_flange = (tf * Math.Pow(W, 3)) / 12.0;
              iyy = Iyy_web + Iyy_flange;

              // 5. 비틀림 상수 J (Open Section Approximation)
              j = (1.0 / 3.0) * (W * Math.Pow(tf, 3) + h_web * Math.Pow(tw, 3));

              note = "Calculated_T";
            }
          }
          else if (type == "I") // I-Beam
          {
            // Dim: [0]H, [1]W, [2]tw, [3]tf, [4]W_bot?, [5]tf_bot? (가정)
            if (dim.Count >= 4)
            {
              double H = dim[0]; double W_top = dim[1];
              double tw = dim[2]; double tf_top = dim[3];

              // 하부 플랜지 정보가 없으면 상부와 대칭 가정
              double W_bot = (dim.Count > 4) ? dim[4] : W_top;
              double tf_bot = (dim.Count > 5) ? dim[5] : tf_top;

              double h_web = H - tf_top - tf_bot;

              // 1. 단면적
              a = (W_top * tf_top) + (W_bot * tf_bot) + (h_web * tw);

              // 2. 도심 및 Izz (정밀 계산)
              // Y reference from bottom
              double y_botFlange = tf_bot / 2.0;
              double y_web = tf_bot + h_web / 2.0;
              double y_topFlange = tf_bot + h_web + tf_top / 2.0;

              double A_bot = W_bot * tf_bot;
              double A_web = h_web * tw;
              double A_top = W_top * tf_top;

              double y_bar = (A_bot * y_botFlange + A_web * y_web + A_top * y_topFlange) / a;

              // Izz Calculation
              double Izz_bot = (W_bot * Math.Pow(tf_bot, 3)) / 12.0 + A_bot * Math.Pow(y_botFlange - y_bar, 2);
              double Izz_web = (tw * Math.Pow(h_web, 3)) / 12.0 + A_web * Math.Pow(y_web - y_bar, 2);
              double Izz_top = (W_top * Math.Pow(tf_top, 3)) / 12.0 + A_top * Math.Pow(y_topFlange - y_bar, 2);
              izz = Izz_bot + Izz_web + Izz_top;

              // Iyy Calculation (Symmetric about Y-axis assumed)
              double Iyy_bot = (tf_bot * Math.Pow(W_bot, 3)) / 12.0;
              double Iyy_web = (h_web * Math.Pow(tw, 3)) / 12.0;
              double Iyy_top = (tf_top * Math.Pow(W_top, 3)) / 12.0;
              iyy = Iyy_bot + Iyy_web + Iyy_top;

              // J Calculation
              j = (1.0 / 3.0) * (W_top * Math.Pow(tf_top, 3) + W_bot * Math.Pow(tf_bot, 3) + h_web * Math.Pow(tw, 3));

              note = "Calculated_I";
            }
          }
          else if (type == "ANGLE" || type == "L") // Angle
          {
            if (dim.Count >= 4)
            {
              double H = dim[0]; double W = dim[1];
              double t1 = dim[2]; double t2 = dim[3];

              // 1. Area
              // Assume Leg 1 is H, Leg 2 is W. 
              // Overlap area needs care. Usually (H * t1) + (W - t1) * t2
              a = (H * t1) + ((W - t1) * t2);

              // Approximate Izz/Iyy for generic L (Simplified)
              // For precise calculation, parallel axis theorem is needed for L-shape centroid.
              // Here we use simplified estimation to avoid zero stiffness.
              izz = (t1 * Math.Pow(H, 3)) / 12.0 + ((W - t1) * Math.Pow(t2, 3)) / 12.0; // Very rough
              iyy = (t2 * Math.Pow(W, 3)) / 12.0 + ((H - t2) * Math.Pow(t1, 3)) / 12.0; // Very rough

              j = (1.0 / 3.0) * (H * Math.Pow(t1, 3) + W * Math.Pow(t2, 3)); // Rough
              note = "Calculated_L(Approx)";
            }
          }
          else if (type == "FLATBAR" || type == "BAR") // Flat Bar
          {
            if (dim.Count >= 2)
            {
              double H = dim[0]; double T = dim[1];
              a = H * T;
              izz = (T * Math.Pow(H, 3)) / 12.0;
              iyy = (H * Math.Pow(T, 3)) / 12.0;
              j = (1.0 / 3.0) * H * Math.Pow(T, 3);
              note = "Calculated_FB";
            }
          }
          else
          {
            // Fallback: If type is unknown, just use dimensions as raw properties (Legacy behavior)
            if (dim.Count > 0) a = dim[0];
            if (dim.Count > 1) izz = dim[1];
            if (dim.Count > 2) iyy = dim[2];
            if (dim.Count > 3) j = dim[3];
            note = $"Fallback({type})";
          }
        }

        // CSV 로그 기록
        csv.AppendLine($"{groupIdx},{nodeInfo},{eid},{pid},{type},{a:F4},{izz:F4},{iyy:F4},{j:F4},{note}");

        sumArea += a;
        sumIzz += izz;
        sumIyy += iyy;
        sumJ += j;
      }

      // 합계 행 추가
      csv.AppendLine($"{groupIdx},{nodeInfo},MERGED,NEW,EQUIV,{sumArea:F4},{sumIzz:F4},{sumIyy:F4},{sumJ:F4},Total Sum");

      return new MergedSectionResult(sumArea, sumIzz, sumIyy, sumJ);
    }

    private record MergedSectionResult(double Area, double Izz, double Iyy, double J);
  }
}
