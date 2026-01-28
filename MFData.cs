using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MooringFitting2026.RawData
{
  public class MFData
  {
    public string mfID;
    public string mfType;
    public double[] mfLocation = new double[3];
    public List<(double, double, double)> RigidRange = new List<(double, double, double)> { };

    public MFData(string mfID, string mfType, double[] mfLocation, List<(double, double, double)> rigidRange)
    {
      this.mfID = mfID;
      this.mfType = mfType;
      this.mfLocation = mfLocation;
      RigidRange = rigidRange;
    }
  }
}
