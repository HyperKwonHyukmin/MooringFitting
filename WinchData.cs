using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MooringFitting2026.RawData
{
  public class WinchData
  {
    public Dictionary<string, List<string>> Forward_LC { get; set; } = new();
    public Dictionary<string, List<string>> Backward_LC { get; set; } = new();
    public Dictionary<string, List<string>> GreenSea_FrongLeft_LC { get; set; } = new();
    public Dictionary<string, List<string>> Forward_GreenSea_FrongRight_LC { get; set; } = new();
    public Dictionary<string, List<string>> Test_LC { get; set; } = new();
    public Dictionary<string, List<string>> WinchLocation { get; set; } = new();


    public WinchData(
      Dictionary<string, List<string>> forward_LC,
      Dictionary<string, List<string>> backward_LC,
      Dictionary<string, List<string>> greenSea_FrongLeft_LC,
      Dictionary<string, List<string>> forward_GreenSea_FrongRight_LC,
      Dictionary<string, List<string>> test_LC,
      Dictionary<string, List<string>> winchLocation
    )
    {
      Forward_LC = forward_LC;
      Backward_LC = backward_LC;
      GreenSea_FrongLeft_LC = greenSea_FrongLeft_LC;
      Forward_GreenSea_FrongRight_LC = forward_GreenSea_FrongRight_LC;
      Test_LC = test_LC;
      WinchLocation = winchLocation;
    }
  }
}
