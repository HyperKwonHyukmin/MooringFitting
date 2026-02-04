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
      sbReport.AppendLine("Description,Calculates equivalent stiffness (A/I/J) for overlapping elements.");
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

      // CSV 데이터 헤더 (엑셀에서 필터 걸기 좋게 한 줄로 구성)
      // GroupID, Nodes, ElementID, PropID, Type, Area, Izz, Iyy, J, Note
      sbReport.AppendLine("GroupID,Nodes,ElementID,PropID,Type,Area,Izz,Iyy,J,Note");

      // 2. 각 그룹별 병합 수행
      int groupIndex = 1;
      foreach (var groupIDs in duplicateGroups)
      {
        if (groupIDs == null || groupIDs.Count < 2) continue;

        // [수정] ID 리스트를 그대로 넘김 (Element 객체가 아닌 ID를 넘겨야 추적 가능)
        // ---------------------------------------------------------
        // [핵심] 등가 물성 계산 (Equivalent Property Calculation)
        // ---------------------------------------------------------
        var calcResult = CalculateEquivalentProperty(
            groupIndex, groupIDs, elements, properties, sbReport);

        // 3. 새 Property 생성 및 등록
        int firstEleID = groupIDs[0];
        int baseMatID = 1;
        if (elements.Contains(firstEleID) && properties.Contains(elements[firstEleID].PropertyID))
        {
          baseMatID = properties[elements[firstEleID].PropertyID].MaterialID;
        }

        // 새 물성 추가 (이름: EQUIV_PBEAM)
        var newDims = new List<double> { calcResult.Area, calcResult.Izz, calcResult.Iyy, calcResult.J };
        int newPropID = properties.AddOrGet("EQUIV_PBEAM", newDims, baseMatID);
        newPropCount++;

        // 4. 모델 수정 (첫 번째 요소만 남기고 나머지 삭제 + 속성 교체)
        var primaryEle = elements[firstEleID];

        // 기존 요소 업데이트 (PropertyID 교체)
        var newExtra = new Dictionary<string, string>(primaryEle.ExtraData);
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
        // Encoding.UTF8은 기본적으로 BOM을 포함하지 않을 수 있으므로 명시적 생성
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
    /// 실제 등가 강성 계산 로직 (CSV 포맷 출력)
    /// </summary>
    private static MergedSectionResult CalculateEquivalentProperty(
        int groupIdx,
        List<int> elementIDs,
        Elements allElements, // [수정] 전체 요소 딕셔너리 전달
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
        int pid = ele.PropertyID;

        if (props.Contains(pid))
        {
          var p = props[pid];
          type = p.Type;
          var dim = p.Dim;

          if (dim.Count > 0) a = dim[0];
          if (dim.Count > 1) izz = dim[1];
          if (dim.Count > 2) iyy = dim[2];
          if (dim.Count > 3) j = dim[3];
        }

        // [수정] CSV 행 추가 (ElementID 포함)
        // Format: GroupID, Nodes, ElementID, PropID, Type, Area, Izz, Iyy, J, Note
        csv.AppendLine($"{groupIdx},{nodeInfo},{eid},{pid},{type},{a:F4},{izz:F4},{iyy:F4},{j:F4},Source");

        sumArea += a;
        sumIzz += izz;
        sumIyy += iyy;
        sumJ += j;
      }

      // 합계 행 추가
      csv.AppendLine($"{groupIdx},{nodeInfo},MERGED,NEW,EQUIV,{sumArea:F4},{sumIzz:F4},{sumIyy:F4},{sumJ:F4},Total Sum (d=0 assumed)");

      // 그룹 간 구분을 위한 빈 줄 (엑셀에서는 빈 행으로 보임)
      // csv.AppendLine(",,,,,,,,,"); 

      return new MergedSectionResult(sumArea, sumIzz, sumIyy, sumJ);
    }

    private record MergedSectionResult(double Area, double Izz, double Iyy, double J);
  }
}
