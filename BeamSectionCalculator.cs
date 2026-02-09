using System;

namespace MooringFitting2026.Services.SectionProperties
{
  public static class BeamSectionCalculator
  {
    public class BeamDimensions
    {
      public double Bt { get; set; }
      public double Tt { get; set; }
      public double Hw { get; set; }
      public double Tw { get; set; }
      public double Bb { get; set; }
      public double Tb { get; set; }
    }

    public class BeamProperties
    {
      public double Area { get; set; }
      public double I_Strong { get; set; }  // Iy 
      public double I_Weak { get; set; }    // Iz 
      public double J { get; set; }         // Ix (Torsional)
      public double ShearArea_Strong { get; set; } // Az 
      public double ShearArea_Weak { get; set; }   // Ay 
      public double Naz { get; set; }

      // [복구됨] Section Modulus Properties
      public double Wx { get; set; }
      public double Wz_plus { get; set; }
      public double Wz_minus { get; set; }
      public double Wyb { get; set; }
      public double Wyt { get; set; }
    }

    public static BeamProperties Calculate(BeamDimensions d)
    {
      double H = d.Hw + d.Tb + d.Tt;
      double Ax = (d.Bt * d.Tt) + (d.Hw * d.Tw) + (d.Bb * d.Tb);

      // Naz (Neutral Axis)
      double term1 = d.Bt * d.Tt * (H - d.Tt / 2.0);
      double term2 = d.Hw * d.Tw * (d.Hw / 2.0 + d.Tb);
      double term3 = d.Bb * d.Tb * (d.Tb / 2.0);
      double Naz = (term1 + term2 + term3) / Ax;

      // Sy, Sz (First Moments)
      double Sy_bottom = (d.Bb * d.Tb * (Naz - d.Tb / 2.0)) + (Math.Pow(Naz - d.Tb, 2) * d.Tw / 2.0);
      double Sy_top = (d.Bt * d.Tt * (H - d.Tt / 2.0 - Naz)) + (Math.Pow(d.Hw + d.Tb - Naz, 2) * d.Tw / 2.0);
      double Sy = (Sy_bottom + Sy_top) / 2.0;

      double Sz = (d.Tt * Math.Pow(d.Bt, 2) + d.Tb * Math.Pow(d.Bb, 2) + d.Hw * Math.Pow(d.Tw, 2)) / 8.0;

      // Inertia & Torsion
      double J = (d.Bb * Math.Pow(d.Tb, 3) + d.Hw * Math.Pow(d.Tw, 3) + d.Bt * Math.Pow(d.Tt, 3)) / 3.0;
      double I_Weak = (d.Tb * Math.Pow(d.Bb, 3) + d.Tt * Math.Pow(d.Bt, 3) + d.Hw * Math.Pow(d.Tw, 3)) / 12.0;

      double I_Strong =
          (d.Bb * Math.Pow(d.Tb, 3) / 12.0) + (d.Bb * d.Tb * Math.Pow(Naz - d.Tb / 2.0, 2)) +
          (d.Tw * Math.Pow(d.Hw, 3) / 12.0) + (d.Hw * d.Tw * Math.Pow(d.Hw / 2.0 + d.Tb - Naz, 2)) +
          (d.Bt * Math.Pow(d.Tt, 3) / 12.0) + (d.Bt * d.Tt * Math.Pow(H - d.Tt / 2.0 - Naz, 2));

      // Shear Areas
      double ShearArea_Strong = (Sy != 0) ? (I_Strong * d.Tw / Sy) : 0.0;
      double ShearArea_Weak = (Sz != 0) ? (I_Weak * (d.Tb + d.Tt) / Sz) : 0.0;

      // [추가 계산] Section Moduli
      double maxThickness = Math.Max(d.Tt, Math.Max(d.Tw, d.Tb));
      double Wx = (maxThickness != 0) ? J / maxThickness : 0.0;

      double maxFlangeWidth = Math.Max(d.Bt, d.Bb);
      double Wz_plus = (maxFlangeWidth != 0) ? I_Weak / (maxFlangeWidth / 2.0) : 0.0;
      double Wz_minus = Wz_plus;

      double Wyb = (Naz != 0) ? I_Strong / Naz : 0.0;
      double Wyt = ((H - Naz) != 0) ? I_Strong / (H - Naz) : 0.0;

      return new BeamProperties
      {
        Area = Math.Round(Ax, 2),
        I_Strong = Math.Round(I_Strong, 2),
        I_Weak = Math.Round(I_Weak, 2),
        J = Math.Round(J, 2),
        ShearArea_Strong = Math.Round(ShearArea_Strong, 2),
        ShearArea_Weak = Math.Round(ShearArea_Weak, 2),
        Naz = Math.Round(Naz, 2),

        // Assign restored properties
        Wx = Math.Round(Wx, 2),
        Wz_plus = Math.Round(Wz_plus, 2),
        Wz_minus = Math.Round(Wz_minus, 2),
        Wyb = Math.Round(Wyb, 2),
        Wyt = Math.Round(Wyt, 2)
      };
    }
  }
}
