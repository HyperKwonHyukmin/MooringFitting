using MooringFitting2026.Exporters;
using MooringFitting2026.Model.Entities;
using MooringFitting2026.Modifier.ElementModifier;

public static class BdfExporter
{
  public static void Export(
      FeModelContext context,
      string CsvPath,
      string stageName,
      List<int> spcList = null,
      Dictionary<int, MooringFittingConnectionModifier.RigidInfo> rigidMap = null,
      List<ForceLoad> forceLoads = null) // [추가] 인자
  {
    // [수정] forceLoads 전달
    var bdfBuilder = new BdfBuilder(101, context, spcList, rigidMap, forceLoads);

    bdfBuilder.Run();
    string newBdfName = stageName + ".bdf";
    string BdfName = Path.Combine(CsvPath, newBdfName);
    File.WriteAllLines(BdfName, bdfBuilder.BdfLines);
  }
}
