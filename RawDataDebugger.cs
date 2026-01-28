using MooringFitting2026.RawData; // 실제 데이터 클래스 네임스페이스
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection; // 리플렉션

namespace MooringFitting2026.Debug
{
  public static class RawDataDebugger
  {
    public static void Run(RawStructureData structure, WinchData winch, bool showDetails = true)
    {
      Console.WriteLine();
      PrintHeader("======= [DEBUG] DETAILED DATA INSPECTION (FULL OUTPUT) =======");

      // 1. Structure Data
      PrintSection("1. Structure Data Details");
      if (structure != null)
      {
        PrintCollectionDetails("MF Data", structure.MfList, showDetails);
        PrintCollectionDetails("Plate Data", structure.PlateList, showDetails);
        PrintCollectionDetails("Flatbar Data", structure.FlatbarList, showDetails);
        PrintCollectionDetails("Angle Data", structure.AngleList, showDetails);
        PrintCollectionDetails("TBar Data", structure.TbarList, showDetails);
      }
      else
      {
        PrintError("RawStructureData is NULL");
      }

      //// 2. Winch Data
      //PrintSection("2. Winch Data Details");
      //if (winch != null)
      //{
      //  PrintDictionaryDetails("Forward LC", winch.forward_LC, showDetails);
      //  PrintDictionaryDetails("Backward LC", winch.backward_LC, showDetails);
      //  PrintDictionaryDetails("GreenSea Front Left LC", winch.GreenSeaFrontLeftLC, showDetails);
      //  PrintDictionaryDetails("GreenSea Front Right LC", winch.GreenSeaFrontRightLC, showDetails);
      //  PrintDictionaryDetails("Test LC", winch.TestLC, showDetails);
      //  PrintDictionaryDetails("Winch Locations", winch.WinchLocation, showDetails);
      //}
      //else
      //{
      //  PrintError("WinchData is NULL");
      //}

      PrintHeader("======= [DEBUG] END OF INSPECTION =======");
      Console.WriteLine();
    }

    // ==================================================================
    //  Helper Methods (제한 없음)
    // ==================================================================

    private static void PrintCollectionDetails(string name, IEnumerable collection, bool showDetails)
    {
      if (collection == null)
      {
        Console.WriteLine($" - {name,-25}: [NULL]");
        return;
      }

      var list = new List<object>();
      foreach (var item in collection) list.Add(item);

      Console.ForegroundColor = ConsoleColor.Cyan;
      Console.Write($" - {name,-25}: ");
      Console.ResetColor();
      Console.WriteLine($"{list.Count} items found.");

      if (!showDetails || list.Count == 0) return;

      Console.ForegroundColor = ConsoleColor.DarkGray;
      Console.WriteLine("   ---------------------------------------------------------------");

      int count = 0;
      foreach (var item in list)
      {
        // [수정] maxPrint 제한 제거: 무조건 출력
        Console.Write($"   [{count + 1:0000}] "); // 인덱스 자리수 4자리로 늘림
        PrintObjectValues(item);
        count++;
      }
      Console.WriteLine("   ---------------------------------------------------------------\n");
      Console.ResetColor();
    }

    private static void PrintDictionaryDetails<TKey, TValue>(string name, Dictionary<TKey, TValue> dict, bool showDetails)
    {
      if (dict == null)
      {
        Console.WriteLine($" - {name,-25}: [NULL]");
        return;
      }

      Console.ForegroundColor = ConsoleColor.Cyan;
      Console.Write($" - {name,-25}: ");
      Console.ResetColor();
      Console.WriteLine($"{dict.Count} keys found.");

      if (!showDetails || dict.Count == 0) return;

      Console.ForegroundColor = ConsoleColor.DarkGray;
      Console.WriteLine("   ---------------------------------------------------------------");

      foreach (var kvp in dict)
      {
        // [수정] maxPrint 제한 제거: 무조건 출력
        Console.Write($"   Key: {kvp.Key,-10} | Val: ");

        if (kvp.Value is IEnumerable<string> listVal)
        {
          Console.WriteLine($"[{string.Join(", ", listVal)}]");
        }
        else
        {
          Console.WriteLine(kvp.Value);
        }
      }
      Console.WriteLine("   ---------------------------------------------------------------\n");
      Console.ResetColor();
    }

    private static void PrintObjectValues(object obj)
    {
      if (obj == null) return;

      Type type = obj.GetType();
      var values = new List<string>();

      // 1. Fields (public 변수)
      FieldInfo[] fields = type.GetFields(BindingFlags.Public | BindingFlags.Instance);
      foreach (var field in fields)
      {
        object val = field.GetValue(obj);
        string valStr = FormatValue(val);
        values.Add($"{field.Name}:{valStr}");
      }

      // 2. Properties (get/set 속성)
      PropertyInfo[] props = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);
      foreach (var prop in props)
      {
        if (prop.CanRead)
        {
          object val = prop.GetValue(obj, null);
          string valStr = FormatValue(val);
          values.Add($"{prop.Name}:{valStr}");
        }
      }

      if (values.Count == 0)
      {
        Console.WriteLine(obj.ToString()); // 필드/속성이 없으면 ToString() 결과 출력
      }
      else
      {
        Console.WriteLine(string.Join(" | ", values));
      }
    }

    private static string FormatValue(object val)
    {
      if (val == null) return "null";

      if (val is IEnumerable enumerable && !(val is string))
      {
        var list = new List<string>();
        foreach (var item in enumerable) list.Add(item.ToString());
        return $"[{string.Join(",", list)}]";
      }
      return val.ToString();
    }

    // --- 스타일 헬퍼 ---
    private static void PrintHeader(string title) { Console.ForegroundColor = ConsoleColor.Green; Console.WriteLine(title); Console.ResetColor(); }
    private static void PrintSection(string title) { Console.ForegroundColor = ConsoleColor.Yellow; Console.WriteLine($"\n[{title}]"); Console.ResetColor(); }
    private static void PrintError(string message) { Console.ForegroundColor = ConsoleColor.Red; Console.WriteLine($"[ERROR] {message}"); Console.ResetColor(); }
  }
}
