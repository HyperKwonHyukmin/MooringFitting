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
      if (data == null) data = "";
      if (data.Length > 8) data = data.Substring(0, 8);

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

    // double 지원 (기본)
    static public string FormatField(double data, string direction = "left")
    {
      return FormatField(data, direction, false);
    }

    // [핵심] 3개 인수를 받는 오버로드 복구 (CS1501 해결)
    static public string FormatField(double data, string direction, bool isRho)
    {
      if (isRho)
      {
        return FormatField(ConvertScientificNotation(data), direction);
      }
      // 0.0일 경우 깔끔하게 처리
      if (Math.Abs(data) < 1e-9) return FormatField("0.0", direction);

      return FormatField(data.ToString("0.0"), direction);  // 기본적으로 소수점 1자리
    }

    // 지수 표기법 변환 (E-표기법 → "-지수" 형태, Nastran 호환)
    static public string ConvertScientificNotation(double data)
    {
      if (Math.Abs(data) < 1e-9) return "0.0";

      string scientific = data.ToString("0.00E+00", CultureInfo.InvariantCulture);
      // 예: "7.85E-09" -> "7.85-9" (Nastran Short Field)
      if (scientific.Contains("E"))
      {
        string[] parts = scientific.Split('E');
        return $"{parts[0]}{int.Parse(parts[1])}";
      }
      return scientific;
    }

    static public string FormatNastranField(double value)
    {
      // "0.00E+00" 은 총 9자리 → 8칸 맞춤
      if (Math.Abs(value) < 1e-9) return FormatField("0.0", "right");

      string formatted = value.ToString("0.0E+00", CultureInfo.InvariantCulture);
      if (formatted.Length > 8)
      {
        formatted = value.ToString("0.#E+00", CultureInfo.InvariantCulture);
      }
      return FormatField(formatted, "right");
    }
  }
}
