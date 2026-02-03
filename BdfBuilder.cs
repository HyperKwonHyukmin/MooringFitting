using MooringFitting.Exporters;
using MooringFitting2026.Exporters;
using MooringFitting2026.Inspector;
using MooringFitting2026.Model.Entities;
using MooringFitting2026.Modifier.ElementModifier;
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

    // BDF에 입력된 텍스트 라인모음 리스트
    public List<String> BdfLines = new List<String>();

    // 생성자 함수
    public BdfBuilder(
          int Sol,
          FeModelContext FeModelContext,
          List<int> spcList = null,
          Dictionary<int, MooringFittingConnectionModifier.RigidInfo> rigidMap = null, // 추가
          int loadCase = 1)
    {
      this.sol = Sol;
      this.feModelContext = FeModelContext;
      this.SpcList = spcList ?? new List<int>();
      this.RigidMap = rigidMap ?? new Dictionary<int, MooringFittingConnectionModifier.RigidInfo>();
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

    }

    // 01. Nastran 솔버 입력
    public void ExecutiveControlSection()
    {
      BdfLines.Add(BdfFormatFields.FormatField($"SOL {this.sol}"));
      BdfLines.Add(BdfFormatFields.FormatField($"CEND"));
    }

    // 02. 출력결과 종류 설정, LoadCase 설정
    public void CaseControlSection()
    {
      BdfLines.Add("DISPLACEMENT = ALL");
      BdfLines.Add("FORCE = ALL");
      BdfLines.Add("SPCFORCES = ALL");
      BdfLines.Add("STRESS = ALL");

      for (int i = 1; i <= LoadCase; i++)
      {
        BdfLines.Add($"SUBCASE {i}");
        BdfLines.Add("    ANALYSIS = STATICS");
        BdfLines.Add($"    LABEL = Load Case {i}");
        BdfLines.Add("    SPC = 1");
        BdfLines.Add($"    LOAD = {i}");
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

        // -----------------------------------------------------------------------
        // Nastran RBE2 Format (Small Field)
        // RBE2, EID, GN, CM, GM1, GM2, GM3, GM4
        // +   , GM5, GM6 ...
        // -----------------------------------------------------------------------

        // 첫 줄 헤더 작성: [RBE2][EID][GN(Independent)][CM(DOF)]
        // CM(Degrees of Freedom)은 보통 123456 (전체 고정)
        var line = $"{BdfFormatFields.FormatField("RBE2")}" +
                   $"{BdfFormatFields.FormatField(eid, "right")}" +
                   $"{BdfFormatFields.FormatField(info.IndependentNodeID, "right")}" +
                   $"{BdfFormatFields.FormatField("123456", "right")}";

        // Dependent Nodes (GMi) 작성
        // 한 줄에 8개 필드(헤더 제외하면 데이터는 4개 혹은 8개)가 들어가므로 줄바꿈 처리 필요
        // 첫 줄에는 이미 4개 필드(키워드, EID, GN, CM)를 썼으므로 4개의 GM만 더 들어갈 수 있음

        int fieldCount = 4; // 현재 줄에 사용된 필드 수

        foreach (var depNode in info.DependentNodeIDs)
        {
          // 줄이 꽉 찼으면(8칸 이상) Continuation Mark 찍고 다음 줄로
          if (fieldCount >= 8)
          {
            BdfLines.Add(line + "+"); // 현재 줄 마무리
            line = "+       ";        // 다음 줄 시작 (Continuation)
            fieldCount = 1;           // Continuation 마크도 1개 필드 차지
          }

          line += $"{BdfFormatFields.FormatField(depNode, "right")}";
          fieldCount++;
        }

        // 마지막 줄 추가
        BdfLines.Add(line);
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

  }
}
