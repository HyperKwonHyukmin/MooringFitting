using MooringFitting2026.Debug;
using MooringFitting2026.RawData;

namespace MooringFitting2026.Parsers
{
  public class CsvRawDataParser
  {
    private readonly string _structureCsvPath;
    private readonly string _winchCsvPath;
    bool _debugPrint;

    public CsvRawDataParser(string structureCsvPath, string winchCsvPath, bool debugPrint=false)
    {
      _structureCsvPath = structureCsvPath;
      _winchCsvPath = winchCsvPath;
      _debugPrint = debugPrint;
    }

    public (RawStructureData, WinchData) Run()
    {

      // 1. Structure 파싱
      var structureParser = new StructureCsvParser();
      RawStructureData structureData = structureParser.Parse(_structureCsvPath);

      // 2. Winch 파싱
      var winchParser = new WinchCsvParser();
      WinchData winchData = winchParser.Parse(_winchCsvPath);

      if (_debugPrint)
      {
        RawDataDebugger.Run(structureData, winchData);
      }

      return (structureData, winchData);
    }
  }
}
