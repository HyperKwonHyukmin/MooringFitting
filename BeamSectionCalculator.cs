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
      public double Ay { get; set; }   // Shear Area Y
      public double Az { get; set; }   // Shear Area Z
      public double Ix { get; set; }   // Torsional Constant (J)
      public double Iy { get; set; }   // Moment of Inertia (Strong Axis, about Y)
      public double Iz { get; set; }   // Moment of Inertia (Weak Axis, about Z)
      public double Wx { get; set; }   // Torsion Modulus
      public double Wyb { get; set; }  // Section Modulus Y Bottom
      public double Wyt { get; set; }  // Section Modulus Y Top
      public double Wzb { get; set; }  // Section Modulus Z Bottom
      public double Wzt { get; set; }  // Section Modulus Z Top
      public double Naz { get; set; }  // Neutral Axis from Bottom
    }

    public static BeamProperties Calculate(BeamDimensions d)
    {
      // 0. 치수 및 기초 변수
      // H = Hw + Tb + Tt (이미지 기준)
      double H = d.Hw + d.Tb + d.Tt;

      // 1. Ax (단면적)
      // Ax = Bt*Tt + Hw*Tw + Bb*Tb
      double Ax = (d.Bt * d.Tt) + (d.Hw * d.Tw) + (d.Bb * d.Tb);

      // 2. Naz (중립축 높이) - 도심 구하기
      // 식: (Bt*Tt*(H-Tt/2) + Hw*Tw*(Hw/2+Tb) + Bb*Tb*Tb/2) / Ax
      double term1 = d.Bt * d.Tt * (H - d.Tt / 2.0);
      double term2 = d.Hw * d.Tw * (d.Hw / 2.0 + d.Tb);
      double term3 = d.Bb * d.Tb * (d.Tb / 2.0);
      double Naz = (Ax > 1e-9) ? (term1 + term2 + term3) / Ax : 0.0;

      // 3. Sy (Strong Axis 1st Moment of Area)
      // 식: (Bb*Tb*(Naz-Tb/2) + (Naz-Tb)^2*Tw/2 + Bt*Tt*(H-Tt/2-Naz) + (Hw+Tb-Naz)^2*Tw/2) / 2
      // 주의: 위아래 단면 1차 모멘트의 평균값 개념을 사용한 수식입니다.
      double Sy_part1 = d.Bb * d.Tb * (Naz - d.Tb / 2.0);
      double Sy_part2 = Math.Pow(Naz - d.Tb, 2) * d.Tw / 2.0;
      double Sy_part3 = d.Bt * d.Tt * (H - d.Tt / 2.0 - Naz);
      double Sy_part4 = Math.Pow(d.Hw + d.Tb - Naz, 2) * d.Tw / 2.0;
      double Sy = (Sy_part1 + Sy_part2 + Sy_part3 + Sy_part4) / 2.0;

      // 4. Sz (Weak Axis 1st Moment of Area)
      // 식: (Tt*Bt^2 + Tb*Bb^2 + Hw*Tw^2) / 8
      double Sz = (d.Tt * Math.Pow(d.Bt, 2) + d.Tb * Math.Pow(d.Bb, 2) + d.Hw * Math.Pow(d.Tw, 2)) / 8.0;

      // 5. Iy (Strong Axis Inertia, 이미지 상 Iy)
      // 평행축 정리 적용
      // Web contribution
      double Iy_web = (d.Tw * Math.Pow(d.Hw, 3)) / 12.0 + (d.Hw * d.Tw * Math.Pow(d.Hw / 2.0 + d.Tb - Naz, 2));
      // Bot Flange
      double Iy_bot = (d.Bb * Math.Pow(d.Tb, 3)) / 12.0 + (d.Bb * d.Tb * Math.Pow(Naz - d.Tb / 2.0, 2));
      // Top Flange
      double Iy_top = (d.Bt * Math.Pow(d.Tt, 3)) / 12.0 + (d.Bt * d.Tt * Math.Pow(H - d.Tt / 2.0 - Naz, 2));
      double Iy = Iy_web + Iy_bot + Iy_top;

      // 6. Iz (Weak Axis Inertia, 이미지 상 Iz)
      // 수직축 기준 대칭 가정
      double Iz = (d.Tb * Math.Pow(d.Bb, 3) + d.Tt * Math.Pow(d.Bt, 3) + d.Hw * Math.Pow(d.Tw, 3)) / 12.0;

      // 7. Ix (Torsional Constant J)
      double Ix;
      // 조건: if Tt == Tw and Tw == Tb (모든 두께가 같으면 단순식)
      bool uniformThickness = (Math.Abs(d.Tt - d.Tw) < 1e-5 && Math.Abs(d.Tw - d.Tb) < 1e-5);
      if (uniformThickness)
      {
        // Ix = Tw^3 * (Hw + Bb + Bt - 1.2 * Tw) / 3
        Ix = Math.Pow(d.Tw, 3) * (d.Hw + d.Bb + d.Bt - 1.2 * d.Tw) / 3.0;
      }
      else
      {
        // Ix = 1.30/3 * (Sum(b*t^3))
        // 계수 1.30은 개단면 비틀림 보정 계수로 보임 (이미지 수식 준수)
        double sum_bt3 = (d.Bb * Math.Pow(d.Tb, 3)) + (d.Hw * Math.Pow(d.Tw, 3)) + (d.Bt * Math.Pow(d.Tt, 3));
        Ix = sum_bt3 / 3.0;
      }

      // 8. Shear Areas (Ay, Az)
      // Az = Iy * Tw / Sy (수직 전단면적)
      double Az = (Sy > 1e-9) ? (Iy * d.Tw / Sy) : 0.0;
      // Ay = Iz * (Tb + Tt) / Sz (수평 전단면적)
      double Ay = (Sz > 1e-9) ? (Iz * (d.Tb + d.Tt) / Sz) : 0.0;

      // 9. Section Moduli (Wx, Wy, Wz)
      // Wx = Ix / max(Tt, Tw, Tb)
      double maxT = Math.Max(d.Tt, Math.Max(d.Tw, d.Tb));
      double Wx = (maxT > 1e-9) ? Ix / maxT : 0.0;

      // Wyb = Iy / Naz
      double Wyb = (Naz > 1e-9) ? Iy / Naz : 0.0;
      // Wyt = Iy / (H - Naz)
      double Wyt = (H - Naz > 1e-9) ? Iy / (H - Naz) : 0.0;

      // Wzb = Iz / (Bb/2)
      double Wzb = (d.Bb > 1e-9) ? Iz / (d.Bb / 2.0) : 0.0;
      // Wzt = Iz / (Bt/2)
      double Wzt = (d.Bt > 1e-9) ? Iz / (d.Bt / 2.0) : 0.0;

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
