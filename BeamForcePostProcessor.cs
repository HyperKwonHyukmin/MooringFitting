using MooringFitting2026.Model.Entities;
using MooringFitting2026.Parsers;
using MooringFitting2026.Pipeline;
using MooringFitting2026.Services.SectionProperties;
using System;
using System.Collections.Generic;
using System.Linq;

namespace MooringFitting2026.Services.Analysis
{
  public static class BeamForcePostProcessor
  {
    public static void CalculateStresses(F06Parser.F06ResultData result, FeModelContext context)
    {
      Console.WriteLine(">>> [Post-Processor] Calculating Beam Stresses...");

      var propCache = new Dictionary<int, BeamSectionCalculator.BeamProperties>();
      var dimStringCache = new Dictionary<int, string>();

      foreach (var subcase in result.Subcases.Values)
      {
        foreach (var force in subcase.BeamForces)
        {
          if (!context.Elements.TryGetValue(force.ElementID, out var element)) continue;
          int propID = element.PropertyID;

          if (!propCache.TryGetValue(propID, out var sectionProps))
          {
            if (context.Properties.TryGetValue(propID, out var propEntity))
            {
              sectionProps = new BeamSectionCalculator.BeamProperties();
              var d = propEntity.Dim;

              // 1. PBEAM / EQUIV_PBEAM 직접 매핑 (등가 물성)
              if (IsDirectPropertyType(propEntity.Type))
              {
                // 순서: [0]Ax, [1]Iy(Strong), [2]Iz(Weak), [3]J, 
                //       [4]Wy_min, [5]Wz_min, [6]Ay, [7]Az, [8]Wx
                if (d.Count > 0) sectionProps.Ax = d[0];
                if (d.Count > 1) sectionProps.Iy = d[1];
                if (d.Count > 2) sectionProps.Iz = d[2];
                if (d.Count > 3) sectionProps.Ix = d[3]; // J

                if (d.Count > 8)
                {
                  sectionProps.Wyt = d[4]; sectionProps.Wyb = d[4]; // Wy
                  sectionProps.Wzt = d[5]; sectionProps.Wzb = d[5]; // Wz
                  sectionProps.Ay = d[6];
                  sectionProps.Az = d[7];
                  sectionProps.Wx = d[8];
                }
                // 기존 PBEAM(데이터 4개뿐)일 경우 안전장치
                else
                {
                  // 면적의 50%를 전단면적으로 가정
                  sectionProps.Ay = sectionProps.Ax * 0.5;
                  sectionProps.Az = sectionProps.Ax * 0.5;
                  // W는 0으로 둠 (계산 불가)
                }
                propCache[propID] = sectionProps;
              }
              // B. I/T Type (계산기 호출)
              else
              {
                var dims = MapPropertyToDimensions(propEntity);
                if (dims != null && dims.Hw > 0)
                {
                  sectionProps = BeamSectionCalculator.Calculate(dims);
                  propCache[propID] = sectionProps;
                }
              }
            }
          }

          // -------------------------------------------------------------
          // [최종] 응력 계산 (이미지 수식 적용)
          // -------------------------------------------------------------
          if (sectionProps != null && sectionProps.Ax > 1e-9)
          {
            // -------------------------------------------------------------
            // [디버깅] 중간 변수값 추적 (Tracing Variables)
            // -------------------------------------------------------------
            // 1. 강축(Strong) 관련 변수
            double Wyb = sectionProps.Wyb;
            double Wyt = sectionProps.Wyt;
            double minW_Strong = Math.Min(Wyb, Wyt); // 강축 단면계수 (Mz용)

            // 2. 약축(Weak) 관련 변수 (여기가 의심되는 부분)
            double Wzb = sectionProps.Wzb;
            double Wzt = sectionProps.Wzt;
            double minW_Weak = Math.Min(Wzb, Wzt);   // 약축 단면계수 (My용)

            // 3. 디버깅 정보 출력 (Debug_DimInfo 컬럼에 모두 기록)
            // 형식: "W_Strong(T/B):{값}/{값} | W_Weak(T/B):{값}/{값} | BM1:{값} | BM2:{값}"
            force.Debug_DimInfo = $"Strong[{Wyt:F0}/{Wyb:F0}] Weak[{Wzt:F0}/{Wzb:F0}]";

            // 4. 검증용 데이터 기록
            force.Debug_Wy_Min = minW_Strong;
            force.Debug_Wz_Min = minW_Weak;

            // -------------------------------------------------------------
            // [응력 계산] 사용자 요청 로직 반영
            // -------------------------------------------------------------

            // 1. Axial Stress
            force.Debug_Area = sectionProps.Ax;
            force.Calc_Nx = Math.Round(force.Axial / sectionProps.Ax, 2);

            // 2. Torsion
            force.Debug_Wx = sectionProps.Wx;
            force.Calc_Mx = (sectionProps.Wx > 1e-9)
                ? Math.Round(force.TotalTorque / sectionProps.Wx, 2) : 0.0;

            // 3. Shear (강제 스왑 적용 유지)
            // Shear1(강축전단) -> Ay(약축면적) / Shear2(약축전단) -> Az(강축면적)
            force.Debug_Ay = sectionProps.Ay;
            force.Calc_Qy = (sectionProps.Ay > 1e-9)
                 ? Math.Round(force.Shear1 / sectionProps.Ay, 2) : 0.0;

            force.Debug_Az = sectionProps.Az;
            force.Calc_Qz = (sectionProps.Az > 1e-9)
                 ? Math.Round(force.Shear2 / sectionProps.Az, 2) : 0.0;

            // 4. Bending Stress (변수 추적 적용)

            // Calc_My (약축 굽힘): BM2 / minW_Strong (강축 계수로 나눔)
            force.Calc_My = (minW_Weak > 1e-9)
                ? Math.Round(force.BM2 / minW_Weak, 2) : 0.0;

            // Calc_Mz (강축 굽힘): BM1 / minW_Weak (약축 계수로 나눔)
            // ★ 사용자가 의심하는 부분 ★
            force.Calc_Mz = (minW_Strong > 1e-9)
                ? Math.Round(force.BM1 / minW_Strong, 2) : 0.0;
          }
        }
      }
      Console.WriteLine("   -> Stress calculation completed.");
    }

    private static bool IsDirectPropertyType(string type)
    {
      if (string.IsNullOrWhiteSpace(type)) return false;
      string t = type.ToUpper();
      return t == "PBEAM" || t == "EQUIV_PBEAM";
    }

    private static BeamSectionCalculator.BeamDimensions MapPropertyToDimensions(Property prop)
    {
      try
      {
        var dims = new BeamSectionCalculator.BeamDimensions();
        var d = prop.Dim;

        if (d == null || d.Count == 0) return null;

        // --------------------------------------------------------------------------------
        // I-Type Mapping
        // Data: [210, 90, 750, 10, 14, 10]
        // --------------------------------------------------------------------------------
        if (prop.Type == "I")
        {
          if (d.Count >= 6)
          {
            double H_total = d[0];  // 210
            dims.Bb = d[1];         // 90
            dims.Bt = d[2];         // 750
            dims.Tt = d[3];         // 10
            dims.Tb = d[4];         // 14
            dims.Tw = d[5];         // 10

            // I형강은 상하부 두께를 빼서 웹 높이 계산
            dims.Hw = H_total - dims.Tt - dims.Tb; // 186
          }
          else if (d.Count >= 4)
          {
            dims.Bt = d[2]; dims.Bb = d[2];
            dims.Tt = d[3]; dims.Tb = d[3];
            dims.Tw = d[1];
            dims.Hw = d[0] - (2 * d[3]);
          }
        }
        // --------------------------------------------------------------------------------
        // T-Type Mapping (수정됨)
        // Data: [750.1, 160.0, 10.0, 20.0] -> Area 10501
        // --------------------------------------------------------------------------------
        else if (prop.Type == "T")
        {
          if (d.Count >= 4)
          {
            dims.Bt = d[0];         // Top Width (750.1)
            double H_total = d[1];  // Total Height (160)
            dims.Tt = d[2];         // Top Thickness (10.0)
            dims.Tw = d[3];         // Web Thickness (20.0)

            // T형강도 전체 높이에서 상부 두께를 뺌
            dims.Hw = H_total - dims.Tt; // 150

            dims.Bb = 0;
            dims.Tb = 0;
          }
        }

        return dims;
      }
      catch
      {
        return null;
      }
    }
  }
}
