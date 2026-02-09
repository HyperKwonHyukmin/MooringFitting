using MooringFitting2026.Inspector.ElementInspector;
using MooringFitting2026.Model.Entities;
using MooringFitting2026.Services.SectionProperties;
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
                int groupIdx, List<int> elementIDs, Elements allElements, Properties props, StringBuilder csv)
    {
      double sumArea = 0.0;
      double sumIzz = 0.0;
      double sumIyy = 0.0;
      double sumJ = 0.0;

      // [NEW] 응력 계산용 합산 변수
      double sumAy = 0.0;
      double sumAz = 0.0;
      double sumWx = 0.0; // Torsion Modulus Sum

      // 단면계수 W는 단순 합산이 안되지만, 안전측 설계를 위해
      // "전체 I / 최대 거리" 개념으로 접근하거나, 개별 W의 합으로 근사
      // 여기서는 개별 형상의 W를 구해서 합산하는 방식을 택함 (보수적 접근)
      double sumWy = 0.0;
      double sumWz = 0.0;

      foreach (var eid in elementIDs)
      {
        if (!allElements.Contains(eid)) continue;
        var ele = allElements[eid];
        int pid = ele.PropertyID;

        if (props.Contains(pid))
        {
          var p = props[pid];
          var dim = p.Dim;

          // 형상별 계산기 호출
          BeamSectionCalculator.BeamDimensions d = null;

          if (p.Type == "I" && dim.Count >= 6)
          {
            d = new BeamSectionCalculator.BeamDimensions
            {
              Hw = dim[0] - dim[3] - dim[5], // H - Tt - Tb
              Bb = dim[1],
              Bt = dim[2],
              Tt = dim[3],
              Tw = dim[4],
              Tb = dim[5]
            };
          }
          else if (p.Type == "T" && dim.Count >= 4)
          {
            d = new BeamSectionCalculator.BeamDimensions
            {
              Bt = dim[0],
              Hw = dim[1] - dim[2], // H - Tt
              Tt = dim[2],
              Tw = dim[3],
              Bb = 0,
              Tb = 0
            };
          }
          // TODO: Angle, Flatbar도 필요하면 Dimensions 매핑 추가

          if (d != null)
          {
            var res = BeamSectionCalculator.Calculate(d);

            sumArea += res.Ax;
            sumIzz += res.Iy; // Strong Axis
            sumIyy += res.Iz; // Weak Axis
            sumJ += res.Ix;

            // [NEW] 응력 파라미터 합산
            sumAy += res.Ay;
            sumAz += res.Az;
            sumWx += res.Wx;
            sumWy += Math.Min(res.Wyb, res.Wyt); // 보수적으로 작은 값 사용
            sumWz += Math.Min(res.Wzb, res.Wzt);
          }
          else if (p.Type == "PBEAM" || p.Type == "EQUIV_PBEAM")
          {
            // 이미 PBEAM인 경우 있는 값만 더함
            if (dim.Count > 0) sumArea += dim[0];
            if (dim.Count > 1) sumIzz += dim[1];
            if (dim.Count > 2) sumIyy += dim[2];
            if (dim.Count > 3) sumJ += dim[3];
            // W, Ay, Az 정보가 있다면 더함 (재귀적 병합 대응)
            if (dim.Count > 8)
            {
              sumWy += dim[4]; sumWz += dim[5];
              sumAy += dim[6]; sumAz += dim[7]; sumWx += dim[8];
            }
          }
        }
      }

      // 만약 형상 정보가 없어서 W가 0이면, 근사식 사용 (안전장치)
      // W = I / (Estimated_H / 2) -> 형상 정보가 다 날아갔으므로 정확하진 않음
      if (sumWy < 1e-9 && sumIzz > 0) sumWy = sumIzz / 100.0; // 임의 값 방지용 (경고 필요)
      if (sumWz < 1e-9 && sumIyy > 0) sumWz = sumIyy / 100.0;

      return new MergedSectionResult(sumArea, sumIzz, sumIyy, sumJ, sumWy, sumWz, sumAy, sumAz, sumWx);
    }

    private record MergedSectionResult(
        double Area, double Izz, double Iyy, double J,
        double Wy_min, double Wz_min, double Ay, double Az, double Wx);
  }
}
