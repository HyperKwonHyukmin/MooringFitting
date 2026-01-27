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
    public static void Export(FeModelContext context, string CsvPath, string stageName)
    {
      var bdfBuilder = new BdfBuilder(101, context);
      bdfBuilder.Run();
      string newBdfName = stageName + ".bdf";
      string BdfName = Path.Combine(CsvPath, newBdfName);
      File.WriteAllLines(BdfName, bdfBuilder.BdfLines);
    }
  }
}
