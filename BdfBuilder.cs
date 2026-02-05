using MooringFitting.Exporters;
using MooringFitting2026.Exporters;
using MooringFitting2026.Inspector;
using MooringFitting2026.Model.Entities;
using MooringFitting2026.Modifier.ElementModifier;
using MooringFitting2026.Services.Load;
using MooringFitting2026.Utils;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MooringFitting2026.Exporters
{
  public class BdfBuilder
  {
    public int sol;
    public FeModelContext feModelContext;
    int LoadCase;
    public List<int> SpcList = new List<int>();
    public Dictionary<int, MooringFittingConnectionModifier.RigidInfo> RigidMap = null;
    public List<ForceLoad> ForceLoads = new List<ForceLoad>();

    // BDF에 입력된 텍스트 라인모음 리스트
    public List<String> BdfLines = new List<String>();

    // 생성자 함수
    public BdfBuilder(
      int Sol,
      FeModelContext FeModelContext,
      List<int> spcList = null,
      Dictionary<int, MooringFittingConnectionModifier.RigidInfo> rigidMap = null,
      List<ForceLoad> forceLoads = null, // [추가] 인자
      int loadCase = 1)
    {
      this.sol = Sol;
      this.feModelContext = FeModelContext;
      this.SpcList = spcList ?? new List<int>();
      this.RigidMap = rigidMap ?? new Dictionary<int, MooringFittingConnectionModifier.RigidInfo>();
      this.ForceLoads = forceLoads ?? new List<ForceLoad>(); // [추가] 초기화
      this.LoadCase = loadCase;
    }

    public void Run()
    {
      // 01. Nastran 솔버 입력
      ExecutiveControlSection();

      // 02. 출력결과 종류 설정, LoadCase 설정
      CaseControlSection();

      // 03. Node, Element 데이터 입력
      NodeElementSection();

      // 04. Property, Material 데이터 입력
      PropertyMaterialSection();

      RigidElementSection();

      // 05. 경계 조건 데이터 입력
      BoundaryConditionSection();

      LoadBulkSection();

    }

    // 01. Nastran 솔버 입력
    public void ExecutiveControlSection()
    {
      BdfLines.Add(BdfFormatFields.FormatField($"SOL {this.sol}"));
      BdfLines.Add(BdfFormatFields.FormatField($"CEND"));
    }

    // 02. 출력결과 종류 설정, LoadCase 설정
    // [수정된 부분] 02. 출력결과 종류 설정, LoadCase 설정
    public void CaseControlSection()
    {
      BdfLines.Add("DISPLACEMENT = ALL");
      BdfLines.Add("FORCE = ALL");
      BdfLines.Add("SPCFORCES = ALL");
      BdfLines.Add("STRESS = ALL");

      // 1. 실제로 존재하는 Load ID 목록 추출 (중복 제거 및 정렬)
      List<int> activeLoadIds;

      if (this.ForceLoads != null && this.ForceLoads.Count > 0)
      {
        // ForceLoads에 데이터가 있다면 그 ID들을 사용 (예: 2, 3, 4...)
        activeLoadIds = this.ForceLoads
                        .Select(f => f.LoadCaseID)
                        .Distinct()
                        .OrderBy(id => id)
                        .ToList();
      }
      else
      {
        // 하중 데이터가 없는 경우(Shape만 볼 때 등), 생성자에서 받은 개수만큼 1부터 생성
        activeLoadIds = Enumerable.Range(1, this.LoadCase).ToList();
      }

      // 2. Subcase 생성
      // subcaseIndex는 1부터 순차적으로 증가 (SUBCASE 1, SUBCASE 2...)
      // targetLoadId는 실제 데이터의 ID 사용 (LOAD = 2, LOAD = 3...)
      for (int i = 0; i < activeLoadIds.Count; i++)
      {
        int subcaseId = i + 1;
        int targetLoadId = activeLoadIds[i];

        BdfLines.Add($"SUBCASE {subcaseId}");
        BdfLines.Add("    ANALYSIS = STATICS");
        BdfLines.Add($"    LABEL = Load Case {targetLoadId}");
        BdfLines.Add("    SPC = 1");             // SPC는 1번 고정
        BdfLines.Add($"    LOAD = {targetLoadId}"); // 실제 하중 ID (2부터 시작)
      }

      BdfLines.Add("BEGIN BULK");
      BdfLines.Add("PARAM,POST,-1");
    }

    // 03. Node, Element 데이터 입력
    public void NodeElementSection()
    {
      foreach (var node in this.feModelContext.Nodes)
      {
        string nodeText = $"{BdfFormatFields.FormatField("GRID")}"
          + $"{BdfFormatFields.FormatField(node.Key, "right")}"
          + $"{BdfFormatFields.FormatField("")}"
          + $"{BdfFormatFields.FormatField(node.Value.X, "right")}"
          + $"{BdfFormatFields.FormatField(node.Value.Y, "right")}"
          + $"{BdfFormatFields.FormatField(node.Value.Z, "right")}";
        BdfLines.Add(nodeText);
      }

      foreach (var element in this.feModelContext.Elements)
      {
        //Console.WriteLine(element);
        //int nodeA = element.Value.NodeIDs[0];
        //int nodeB = element.Value.NodeIDs[1];
        //var directionVector3D = Vector3dUtils.Direction(nodeA, nodeB, feModelContext.Nodes);
        //double[] dirctionVector = { Math.Round(directionVector3D.X, 1),
        //  Math.Round(directionVector3D.Y, 1), Math.Round(directionVector3D.Z, 1)};
        //Console.WriteLine($"{dirctionVector[0]}, {dirctionVector[1]}, {dirctionVector[2]}");
        string elementText = $"{BdfFormatFields.FormatField("CBEAM")}"
         + $"{BdfFormatFields.FormatField(element.Key, "right")}"
         + $"{BdfFormatFields.FormatField(element.Value.PropertyID, "right")}"
         + $"{BdfFormatFields.FormatField(element.Value.NodeIDs[0], "right")}"
         + $"{BdfFormatFields.FormatField(element.Value.NodeIDs[1], "right")}"
         + $"{BdfFormatFields.FormatField(0.0, "right")}"
         + $"{BdfFormatFields.FormatField(0.0, "right")}"
         + $"{BdfFormatFields.FormatField(1.0, "right")}"
         + $"{BdfFormatFields.FormatField("BGG", "right")}";
        BdfLines.Add(elementText);
      }
    }

    public void PropertyMaterialSection()
    {
      foreach (var property in this.feModelContext.Properties)
      {
        string type = property.Value.Type.ToUpper(); // 대소문자 무시

        // 1. PBEAM (직접 입력형 / 등가 물성)
        // 병합 로직에서 생성한 "EQUIV_PBEAM"도 여기서 처리
        if (type == "PBEAM" || type == "EQUIV_PBEAM")
        {
          // Data Mapping (Dim 리스트 순서: [0]Area, [1]I1(Izz), [2]I2(Iyy), [3]J)
          // 데이터가 부족할 경우 0.0 처리
          double A = property.Value.Dim.Count > 0 ? property.Value.Dim[0] : 0.0;
          double I1 = property.Value.Dim.Count > 1 ? property.Value.Dim[1] : 0.0; // Izz
          double I2 = property.Value.Dim.Count > 2 ? property.Value.Dim[2] : 0.0; // Iyy
          double J = property.Value.Dim.Count > 3 ? property.Value.Dim[3] : 0.0;
          double I12 = 0.0; // 대칭 단면 가정 시 0

          // Format: PBEAM, PID, MID, A, I1, I2, I12, J
          string propertyText = $"{BdfFormatFields.FormatField("PBEAM")}"
            + $"{BdfFormatFields.FormatField(property.Key, "right")}"
            + $"{BdfFormatFields.FormatField(property.Value.MaterialID, "right")}"
            + $"{BdfFormatFields.FormatNastranField(A)}"    // 8자리 지수표기
            + $"{BdfFormatFields.FormatNastranField(I1)}"   // 8자리 지수표기
            + $"{BdfFormatFields.FormatNastranField(I2)}"   // 8자리 지수표기
            + $"{BdfFormatFields.FormatNastranField(I12)}"  // 0.0
            + $"{BdfFormatFields.FormatNastranField(J)}";   // 8자리 지수표기

          BdfLines.Add(propertyText);
        }
        // 2. PBEAML (파라메트릭 - I Beam)
        else if (type == "I")
        {
          string propertyText = $"{BdfFormatFields.FormatField("PBEAML")}"
            + $"{BdfFormatFields.FormatField(property.Key, "right")}"
            + $"{BdfFormatFields.FormatField(property.Value.MaterialID, "right")}"
            + $"{BdfFormatFields.FormatField("", "right")}"
            + $"{BdfFormatFields.FormatField("I", "right")}";
          BdfLines.Add(propertyText);

          propertyText = $"{BdfFormatFields.FormatField("")}"
            + $"{BdfFormatFields.FormatField(property.Value.Dim[0], "right")}"
            + $"{BdfFormatFields.FormatField(property.Value.Dim[1], "right")}"
            + $"{BdfFormatFields.FormatField(property.Value.Dim[2], "right")}"
            + $"{BdfFormatFields.FormatField(property.Value.Dim[3], "right")}"
            + $"{BdfFormatFields.FormatField(property.Value.Dim[4], "right")}"
            + $"{BdfFormatFields.FormatField(property.Value.Dim[5], "right")}";

          BdfLines.Add(propertyText);
        }
        // 3. PBEAML (파라메트릭 - T Bar)
        else if (type == "T")
        {
          string propertyText = $"{BdfFormatFields.FormatField("PBEAML")}"
            + $"{BdfFormatFields.FormatField(property.Key, "right")}"
            + $"{BdfFormatFields.FormatField(property.Value.MaterialID, "right")}"
            + $"{BdfFormatFields.FormatField("", "right")}"
            + $"{BdfFormatFields.FormatField("T", "right")}";
          BdfLines.Add(propertyText);

          propertyText = $"{BdfFormatFields.FormatField("")}"
            + $"{BdfFormatFields.FormatField(property.Value.Dim[0], "right")}"
            + $"{BdfFormatFields.FormatField(property.Value.Dim[1], "right")}"
            + $"{BdfFormatFields.FormatField(property.Value.Dim[2], "right")}"
            + $"{BdfFormatFields.FormatField(property.Value.Dim[3], "right")}";

          BdfLines.Add(propertyText);
        }
      }

      // Material 출력
      foreach (var material in this.feModelContext.Materials)
      {
        string materialText = $"{BdfFormatFields.FormatField("MAT1")}"
            + $"{BdfFormatFields.FormatField(material.Key, "right")}"
            + $"{BdfFormatFields.FormatField(material.Value.E, "right")}"
            + $"{BdfFormatFields.FormatField("")}"
            + $"{BdfFormatFields.FormatField(material.Value.Nu, "right")}"
            + $"{BdfFormatFields.FormatField(material.Value.Rho, "right", true)}";

        BdfLines.Add(materialText);
      }
    }

    public void RigidElementSection()
    {
      if (this.RigidMap == null || this.RigidMap.Count == 0) return;

      foreach (var kv in this.RigidMap)
      {
        int eid = kv.Key;
        var info = kv.Value;

        // Dependent Node가 없으면 RBE2 생성 불가하므로 스킵
        if (info.DependentNodeIDs == null || info.DependentNodeIDs.Count == 0) continue;

        // -----------------------------------------------------------------------
        // Nastran RBE2 Format Logic (Small Field)
        // Row 1: [RBE2][EID][GN][CM][GM1][GM2][GM3][GM4][GM5][+]
        // Row 2: [+][GM6][GM7]...
        // -----------------------------------------------------------------------
        var sb = new StringBuilder();

        // 1. 첫 번째 줄 헤더 작성 (필드 1~4 사용)
        sb.Append(BdfFormatFields.FormatField("RBE2"));
        sb.Append(BdfFormatFields.FormatField(eid, "right"));
        sb.Append(BdfFormatFields.FormatField(info.IndependentNodeID, "right"));
        sb.Append(BdfFormatFields.FormatField("123456", "right")); // DOF 고정

        // 현재 줄에서 사용된 필드 수 (1~4번 필드 사용됨)
        int fieldsUsed = 4;

        // 2. Dependent Nodes (GMi) 순회
        for (int i = 0; i < info.DependentNodeIDs.Count; i++)
        {
          int depNodeID = info.DependentNodeIDs[i];

          // 필드 9번까지 꽉 찼다면 줄바꿈 처리 (필드 10은 연속 마크용)
          if (fieldsUsed >= 9)
          {
            sb.Append(BdfFormatFields.FormatField("+")); // 필드 10: Continuation Mark
            BdfLines.Add(sb.ToString());

            // StringBuilder 리셋 및 다음 줄 초기화
            sb.Clear();
            sb.Append(BdfFormatFields.FormatField("+")); // 다음 줄 필드 1: Continuation Mark Match
            fieldsUsed = 1; // 필드 1 사용됨
          }

          // 노드 ID 추가
          sb.Append(BdfFormatFields.FormatField(depNodeID, "right"));
          fieldsUsed++;
        }

        // 마지막 줄이 남아있다면 리스트에 추가
        if (sb.Length > 0)
        {
          BdfLines.Add(sb.ToString());
        }
      }
    }

    public void BoundaryConditionSection()
    {
      // 리스트가 비어있거나 null이면 작성하지 않음
      if (this.SpcList == null || this.SpcList.Count == 0) return;

      foreach (int nodeId in this.SpcList)
      {
        // ---------------------------------------------------------
        // Nastran SPC Card Format (Small Field)
        // 1. Keyword : "SPC"
        // 2. SID     : Set ID (1로 고정)
        // 3. G       : Grid ID (Node ID)
        // 4. C       : Component (자유도, 123456 고정)
        // 5. D       : Enforced Displacement (0.0 고정)
        // ---------------------------------------------------------

        string spcLine = $"{BdfFormatFields.FormatField("SPC")}"
                       + $"{BdfFormatFields.FormatField(1, "right")}"          // SID = 1
                       + $"{BdfFormatFields.FormatField(nodeId, "right")}"     // Node ID
                       + $"{BdfFormatFields.FormatField("123456", "right")}"  // DOF
                       + $"{BdfFormatFields.FormatField(0.0, "right")}";      // Value

        BdfLines.Add(spcLine);
      }
    }

    public void LoadBulkSection()
    {
      if (this.ForceLoads == null || this.ForceLoads.Count == 0) return;
      foreach (var load in this.ForceLoads)
      {
        // FORCE Card Format:
        // FORCE, SID, G, CID, F, N1, N2, N3
        // SID: Load Case ID
        // G: Grid ID
        // CID: Coord System (0 = Basic)
        // F: Scale Factor (1.0으로 두고 N1~N3에 실제 힘 성분 입력)
        // N1, N2, N3: Vector components
       

        string line = $"{BdfFormatFields.FormatField("FORCE")}" +
                      $"{BdfFormatFields.FormatField(load.LoadCaseID, "right")}" +
                      $"{BdfFormatFields.FormatField(load.NodeID, "right")}" +
                      $"{BdfFormatFields.FormatField(0, "right")}" +     // CID=0
                      $"{BdfFormatFields.FormatField(1.0, "right")}" +   // F=1.0 (Scale)
                      $"{BdfFormatFields.FormatNastranField(load.Vector.X)}" +
                      $"{BdfFormatFields.FormatNastranField(load.Vector.Y)}" +
                      $"{BdfFormatFields.FormatNastranField(load.Vector.Z)}";

        BdfLines.Add(line);
      }
    }

  }
}
