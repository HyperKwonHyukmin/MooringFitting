using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MooringFitting2026.RawData
{
  public class RawStructureData
  {
    public List<MFData> MfList { get; init; }
    public List<PlateData> PlateList { get; init; }
    public List<FlatbarData> FlatbarList { get; init; }
    public List<AngleData> AngleList { get; init; }
    public List<TbarData> TbarList { get; init; }

    public RawStructureData(
      List<MFData> mfList,
      List<PlateData> plateList,
      List<FlatbarData> flatbarList,
      List<AngleData> angleList,
      List<TbarData> tbarList
    )
    {
      MfList = mfList;
      PlateList = plateList;
      FlatbarList = flatbarList;
      AngleList = angleList;
      TbarList = tbarList;
    }
  }
}




