using System;

namespace MooringFitting2026.Services.SectionProperties
{
  public static class BeamSectionCalculator
  {
    public class BeamDimensions
    {
      public double Bt { get; set; } // Top Flange Width
      public double Tt { get; set; } // Top Flange Thickness
      public double Hw { get; set; } // Web Height (Clear distance)
      public double Tw { get; set; } // Web Thickness
      public double Bb { get; set; } // Bottom Flange Width
      public double Tb { get; set; } // Bottom Flange Thickness
    }

    public class BeamProperties
    {
      public double Ax { get; set; }   // Area
      public double Ay { get; set; }   // Shear Area Y (Weak Axis Shear Area)
      public double Az { get; set; }   // Shear Area Z (Strong Axis Shear Area)
      public double Ix { get; set; }   // Torsional Constant (J)
      public double Iy { get; set; }   // Strong Axis Inertia (Izz in Nastran)
      public double Iz { get; set; }   // Weak Axis Inertia (Iyy in Nastran)
      public double Wx { get; set; }   // Torsion Modulus
      public double Wyb { get; set; }  // Strong Axis Modulus (Bottom)
      public double Wyt { get; set; }  // Strong Axis Modulus (Top)
      public double Wzb { get; set; }  // Weak Axis Modulus (Bottom Flange ref)
      public double Wzt { get; set; }  // Weak Axis Modulus (Top Flange ref)
      public double Naz { get; set; }  // Neutral Axis from Bottom
    }

    public static BeamProperties Calculate(BeamDimensions d)
    {
      // 0. 치수 및 기초 변수
      double H = d.Hw + d.Tb + d.Tt;

      // 1. Ax (단면적)
      double Ax = (d.Bt * d.Tt) + (d.Hw * d.Tw) + (d.Bb * d.Tb);

      // 2. Naz (중립축 높이)
      double term1 = d.Bt * d.Tt * (H - d.Tt / 2.0);
      double term2 = d.Hw * d.Tw * (d.Hw / 2.0 + d.Tb);
      double term3 = d.Bb * d.Tb * (d.Tb / 2.0);
      double Naz = (Ax > 1e-9) ? (term1 + term2 + term3) / Ax : 0.0;

      // 3. Sy (Strong Axis 1st Moment) - 전단면적 계산용
      double Sy_part1 = d.Bb * d.Tb * (Naz - d.Tb / 2.0);
      double Sy_part2 = Math.Pow(Naz - d.Tb, 2) * d.Tw / 2.0;
      double Sy_part3 = d.Bt * d.Tt * (H - d.Tt / 2.0 - Naz);
      double Sy_part4 = Math.Pow(d.Hw + d.Tb - Naz, 2) * d.Tw / 2.0;
      double Sy = (Sy_part1 + Sy_part2 + Sy_part3 + Sy_part4) / 2.0;

      // 4. Sz (Weak Axis 1st Moment)
      double Sz = (d.Tt * Math.Pow(d.Bt, 2) + d.Tb * Math.Pow(d.Bb, 2) + d.Hw * Math.Pow(d.Tw, 2)) / 8.0;

      // 5. Iy (Strong Axis Inertia, Izz)
      double Iy_web = (d.Tw * Math.Pow(d.Hw, 3)) / 12.0 + (d.Hw * d.Tw * Math.Pow(d.Hw / 2.0 + d.Tb - Naz, 2));
      double Iy_bot = (d.Bb * Math.Pow(d.Tb, 3)) / 12.0 + (d.Bb * d.Tb * Math.Pow(Naz - d.Tb / 2.0, 2));
      double Iy_top = (d.Bt * Math.Pow(d.Tt, 3)) / 12.0 + (d.Bt * d.Tt * Math.Pow(H - d.Tt / 2.0 - Naz, 2));
      double Iy = Iy_web + Iy_bot + Iy_top;

      // 6. Iz (Weak Axis Inertia, Iyy)
      double Iz = (d.Tb * Math.Pow(d.Bb, 3) + d.Tt * Math.Pow(d.Bt, 3) + d.Hw * Math.Pow(d.Tw, 3)) / 12.0;

      // 7. Ix (Torsional Constant J)
      double Ix;
      bool uniformThickness = (Math.Abs(d.Tt - d.Tw) < 1e-5 && Math.Abs(d.Tw - d.Tb) < 1e-5);
      if (uniformThickness)
      {
        Ix = Math.Pow(d.Tw, 3) * (d.Hw + d.Bb + d.Bt - 1.2 * d.Tw) / 3.0;
      }
      else
      {
        double sum_bt3 = (d.Bb * Math.Pow(d.Tb, 3)) + (d.Hw * Math.Pow(d.Tw, 3)) + (d.Bt * Math.Pow(d.Tt, 3));
        Ix = sum_bt3 / 3.0;
      }

      // 8. Shear Areas
      double Az = (Sy > 1e-9) ? (Iy * d.Tw / Sy) : 0.0; // Strong Axis Shear Area
      double Ay = (Sz > 1e-9) ? (Iz * (d.Tb + d.Tt) / Sz) : 0.0; // Weak Axis Shear Area

      // 9. Section Moduli (단면계수)

      // Wx (Torsion)
      double maxT = Math.Max(d.Tt, Math.Max(d.Tw, d.Tb));
      double Wx = (maxT > 1e-9) ? Ix / maxT : 0.0;

      // Wy (Strong Axis Bending Modulus) - 상/하부
      double Wyb = (Naz > 1e-9) ? Iy / Naz : 0.0;
      double Wyt = (H - Naz > 1e-9) ? Iy / (H - Naz) : 0.0;

      // [수정된 부분] Wz (Weak Axis Bending Modulus)
      // T-Bar의 경우 Bb=0이므로 Wzb가 0이 되는 문제 해결
      // 약축은 대칭이므로, 가장 넓은 폭을 기준으로 유효 단면계수를 산정합니다.

      double maxFlangeWidth = Math.Max(d.Bt, d.Bb);
      // 만약 Web이 더 넓다면 Web 고려 (일반적이진 않음)
      maxFlangeWidth = Math.Max(maxFlangeWidth, d.Tw);

      // 약축 단면계수 (I / c_max)
      double Wz_calc = (maxFlangeWidth > 1e-9) ? Iz / (maxFlangeWidth / 2.0) : 0.0;

      // 상부/하부 플랜지 폭이 다를 때 각각 계산하기보다, 
      // 약축은 "전체 폭"에 지배되므로 안전하게 최대 폭 기준 값을 할당하거나
      // Bb가 있을 때만 계산하도록 분기해야 합니다. 여기서는 최대 폭 기준 값을 공통 적용합니다.
      double Wzb = (d.Bb > 1e-9) ? Iz / (d.Bb / 2.0) : Wz_calc;
      double Wzt = (d.Bt > 1e-9) ? Iz / (d.Bt / 2.0) : Wz_calc;

      return new BeamProperties
      {
        Ax = Ax,
        Ay = Ay,
        Az = Az,
        Ix = Ix,
        Iy = Iy,
        Iz = Iz,
        Wx = Wx,
        Wyb = Wyb,
        Wyt = Wyt,
        Wzb = Wzb,
        Wzt = Wzt,
        Naz = Naz
      };
    }
  }
}
