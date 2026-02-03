using MooringFitting2026.Model.Entities;
using MooringFitting2026.Exporters;
using MooringFitting2026.Modifier.ElementModifier; // RigidInfo 사용을 위해 추가
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO; // Path, File 사용을 위해

namespace MooringFitting2026.Exporters
{
  public static class BdfExporter
  {
    // [수정] rigidMap 인자 추가 (기본값 null)
    public static void Export(
        FeModelContext context,
        string CsvPath,
        string stageName,
        List<int> spcList = null,
        Dictionary<int, MooringFittingConnectionModifier.RigidInfo> rigidMap = null) // <--- 추가됨
    {
      // [수정] BdfBuilder 생성자에 rigidMap 전달
      var bdfBuilder = new BdfBuilder(101, context, spcList, rigidMap);

      bdfBuilder.Run();
      string newBdfName = stageName + ".bdf";
      string BdfName = Path.Combine(CsvPath, newBdfName);
      File.WriteAllLines(BdfName, bdfBuilder.BdfLines);
    }
  }
}
