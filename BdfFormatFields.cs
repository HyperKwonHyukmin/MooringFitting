using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MooringFitting.Exporters
{
  static public class BdfFormatFields
  {
    // 하나의 문자열을 8칸에 넣어서 문자열을 반환하는 메써드
    static public string FormatField(string data, string direction = "left")
    {
      if (direction == "right")
      {
        return data.PadLeft(8).Substring(0, 8);
      }

      return data.PadRight(8).Substring(0, 8);
    }

    // int 지원
    static public string FormatField(int data, string direction = "left")
    {
      return FormatField(data.ToString(), direction);
    }

    static public string FormatField(double data, string direction = "left", bool isRho = false)
    {
      if (isRho)
      {
        return FormatField(ConvertScientificNotation(data), direction);
      }
      return FormatField(data.ToString("0.0"), direction);  // 기본적으로 소수점 2자리
    }


    // 지수 표기법 변환 (E-표기법 → "-지수" 형태)
    static public string ConvertScientificNotation(double data)
    {
      string scientific = data.ToString("0.00E+0");  // "7.85E-09" 형식
      if (scientific.Contains("E"))
      {
        string[] parts = scientific.Split('E'); // ["7.85", "-09"]
        return $"{parts[0]}{int.Parse(parts[1])}"; // "7.85-9"
      }
      return scientific;
    }

    static public string FormatNastranField(double value)
    {
      // "0.00E+00" 은 총 9자리 → "0.0####E##"로 줄여야 8칸 맞춤 가능
      string formatted = value.ToString("0.0E+00", CultureInfo.InvariantCulture); // "9.6E+05"
      if (formatted.Length > 8)
      {
        // 자르기 전에 음수/지수 등 고려해서 줄여야 함 → 아래 로직으로 8칸 안전 보장
        formatted = value.ToString("0.#E+00", CultureInfo.InvariantCulture);
      }
      return FormatField(formatted, "right");
    }
  }
}
