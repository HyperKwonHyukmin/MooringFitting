using MooringFitting2026.Model.Entities;
using MooringFitting2026.Exporters;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MooringFitting2026.Exporters
{
  public static class BdfExporter
  {
    // [수정] spcList 인자 추가 (기본값 null)
    public static void Export(FeModelContext context, string CsvPath, string stageName, List<int> spcList = null)
    {
      // [수정] BdfBuilder 생성자에 spcList 전달
      var bdfBuilder = new BdfBuilder(101, context, spcList);

      bdfBuilder.Run();
      string newBdfName = stageName + ".bdf";
      string BdfName = Path.Combine(CsvPath, newBdfName);
      File.WriteAllLines(BdfName, bdfBuilder.BdfLines);
    }
  }
}
