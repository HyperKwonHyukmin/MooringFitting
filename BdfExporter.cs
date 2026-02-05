using MooringFitting2026.Exporters;
using MooringFitting2026.Model.Entities;
using MooringFitting2026.Modifier.ElementModifier;
using MooringFitting2026.Services.Load;
using System.Collections.Generic;
using System.IO;
using System.Linq;

public static class BdfExporter
{
  public static void Export(
      FeModelContext context,
      string CsvPath,
      string stageName,
      List<int> spcList = null,
      Dictionary<int, MooringFittingConnectionModifier.RigidInfo> rigidMap = null,
      List<ForceLoad> forceLoads = null)
  {
    // [수정] 하중 리스트에서 최대 Load Case ID 계산
    int maxLoadCaseID = 1;
    if (forceLoads != null && forceLoads.Count > 0)
    {
      maxLoadCaseID = forceLoads.Max(f => f.LoadCaseID);
    }

    // [수정] 계산된 maxLoadCaseID를 생성자에 전달
    var bdfBuilder = new BdfBuilder(101, context, spcList, rigidMap, forceLoads, maxLoadCaseID);

    bdfBuilder.Run();
    string newBdfName = stageName + ".bdf";
    string BdfName = Path.Combine(CsvPath, newBdfName);
    File.WriteAllLines(BdfName, bdfBuilder.BdfLines);
  }
}
