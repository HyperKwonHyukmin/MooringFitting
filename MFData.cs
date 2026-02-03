using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MooringFitting2026.RawData
{
  public class MFData
  {
    public string ID;
    public string Type;
    public double[] Location = new double[3];
    public List<(double, double, double)> RigidRange = new List<(double, double, double)> { };
    public double Mass;
    public double a;
    public double b;
    public double c;
    public double tow;

    public MFData(string mfID, string mfType, double[] mfLocation, 
      List<(double, double, double)> rigidRange, double mass, double a, double b, double c, double tow)
    {
      this.ID = mfID;
      this.Type = mfType;
      this.Location = mfLocation;
      RigidRange = rigidRange;
      Mass = mass;
      this.a = a;
      this.b = b;
      this.c = c;
      this.tow = tow;
    }
  }
}
