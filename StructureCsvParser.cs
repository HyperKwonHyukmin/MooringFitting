using MooringFitting2026.RawData;
using MooringFitting2026.Utils.Geometry;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace MooringFitting2026.Parsers
{
  public class StructureCsvParser
  {
    public RawStructureData Parse(string filePath)
    {
      if (!File.Exists(filePath))
        throw new FileNotFoundException($"Structure CSV 파일을 찾을 수 없습니다: {filePath}");

      var mfList = new List<MFData>();
      var plateList = new List<PlateData>();
      var flatbarList = new List<FlatbarData>();
      var angleList = new List<AngleData>();
      var tBarList = new List<TbarData>();

      var lines = File.ReadAllLines(filePath);

      foreach (var line in lines)
      {
        if (string.IsNullOrWhiteSpace(line)) continue;

        string[] parts = line.Split(',');
        if (parts.Length == 0) continue;

        string type = parts[0].Trim().ToUpper();

        try
        {
          switch (type)
          {
            case "MF": mfList.Add(ParseMF(parts)); break;
            case "PLATE": plateList.Add(ParsePlate(parts)); break;
            case "FLATBAR": flatbarList.Add(ParseFlatbar(parts)); break;
            case "ANGLE": angleList.Add(ParseAngle(parts)); break;
            case "TBAR": tBarList.Add(ParseTbar(parts)); break;
          }
        }
        catch (Exception)
        {
          // 파싱 에러 발생 시 해당 라인 무시 (로그 추가 권장)
          continue;
        }
      }

      return new RawStructureData(mfList, plateList, flatbarList, angleList, tBarList);
    }

    // =================================================================
    // [중요] 아래 Private 메서드들은 기존 코드의 컬럼 순서와 일치해야 합니다.
    // 기존 로직이 있다면 여기 내용을 교체하세요.
    // =================================================================

    private MFData ParseMF(string[] parts)
    {
      string id = parts[1];
      string type = parts[5];

      double[] location = {
            double.Parse(parts[2]),
            double.Parse(parts[3]),
            double.Parse(parts[4])
        };

      // 1. SWL 파싱 (기본값)
      double swl = double.Parse(parts[6]);

      // 2. a, b, c 파싱 (인덱스 7, 8, 9)
      // 값이 실수면 해당 값, '_'이거나 파싱 불가면 0.0으로 설정
      double a = TryParseNullableDouble(parts[7]) ?? 0.0;
      double b = TryParseNullableDouble(parts[8]) ?? 0.0;
      double c = TryParseNullableDouble(parts[9]) ?? 0.0;

      // 3. Tow 파싱 (인덱스 10) 및 SWL 비교
      double tow = TryParseNullableDouble(parts[10]) ?? 0.0;

      // Tow 값이 존재하고(0보다 크고) SWL보다 크면 SWL을 덮어씀
      if (tow > swl)
      {
        swl = tow;
      }

      // 4. Rigid Range 파싱 (11~14번 인덱스)
      var rigidRange = new List<(double, double, double)>();
      // parts 길이를 체크하여 인덱스 초과 방지
      for (int i = 11; i <= 14 && i < parts.Length; i++)
      {
        if (string.IsNullOrWhiteSpace(parts[i])) continue;

        string[] rangeParts = parts[i].Split('/');
        if (rangeParts.Length >= 3)
        {
          rigidRange.Add((
              double.Parse(rangeParts[0]),
              double.Parse(rangeParts[1]),
              double.Parse(rangeParts[2])
          ));
        }
      }

      return new MFData(id, type, location, rigidRange, swl, a, b, c, tow) { /* SWL 등을 추가로 할당해야 한다면 여기에 */ };
      // *참고: 원본 코드에는 SWL 변수만 만들고 MFData 생성자에는 넣지 않았습니다.
      // 만약 MFData에 SWL 필드가 있다면 생성자에 추가해야 합니다.
    }

    private PlateData ParsePlate(string[] parts)
    {
      string id = parts[1];
      string locationType = parts[2].ToLower(); // above / below

      if (locationType == "below")
      {
        double height = double.Parse(parts[4]);
        double thickness = double.Parse(parts[5]);

        double[] nodeA = { double.Parse(parts[7]), double.Parse(parts[8]), double.Parse(parts[9]) };
        double[] nodeB = { double.Parse(parts[10]), double.Parse(parts[11]), double.Parse(parts[12]) };

        return new PlateData(id, locationType, height, thickness, nodeA, nodeB);
      }
      else // "above"
      {
        double[] nodeA = { double.Parse(parts[3]), double.Parse(parts[4]), double.Parse(parts[5]) };
        double[] nodeB = { double.Parse(parts[6]), double.Parse(parts[7]), double.Parse(parts[8]) };

        // 좌표 간 거리 계산 (Height)
        // parts[3,4,5] 와 parts[12,13,14] 사이의 거리
        double x1 = double.Parse(parts[3]), y1 = double.Parse(parts[4]), z1 = double.Parse(parts[5]);
        double x2 = double.Parse(parts[12]), y2 = double.Parse(parts[13]), z2 = double.Parse(parts[14]);

        double height = DistanceUtils.GetDistanceBetweenPoints(x1, y1, z1, x2, y2, z2);
        double thickness = double.Parse(parts[15]);

        return new PlateData(id, locationType, height, thickness, nodeA, nodeB);
      }
    }

    private FlatbarData ParseFlatbar(string[] parts)
    {
      string id = parts[1];
      double sizeA = double.Parse(parts[2]);
      double sizeB = double.Parse(parts[3]);
      double sizeC = double.Parse(parts[4]);
      double sizeD = double.Parse(parts[5]);

      double[] nodeA = { double.Parse(parts[7]), double.Parse(parts[8]), double.Parse(parts[9]) };
      double[] nodeB = { double.Parse(parts[10]), double.Parse(parts[11]), double.Parse(parts[12]) };

      // 유효폭 계산 (Effective Width)
      double x1 = double.Parse(parts[13]), y1 = double.Parse(parts[14]), z1 = double.Parse(parts[15]);
      double x2 = double.Parse(parts[16]), y2 = double.Parse(parts[17]), z2 = double.Parse(parts[18]);

      double effectiveWidth = DistanceUtils.GetDistanceBetweenPoints(x1, y1, z1, x2, y2, z2);

      return new FlatbarData(id, sizeA, sizeB, sizeC, sizeD, nodeA, nodeB, effectiveWidth);
    }

    private AngleData ParseAngle(string[] parts)
    {
      string id = parts[1];
      double sizeA = double.Parse(parts[2]);
      double sizeB = double.Parse(parts[3]);
      double sizeC = double.Parse(parts[4]);
      double sizeD = double.Parse(parts[5]);

      double[] nodeA = { double.Parse(parts[7]), double.Parse(parts[8]), double.Parse(parts[9]) };
      double[] nodeB = { double.Parse(parts[10]), double.Parse(parts[11]), double.Parse(parts[12]) };

      // 유효폭 계산
      double x1 = double.Parse(parts[13]), y1 = double.Parse(parts[14]), z1 = double.Parse(parts[15]);
      double x2 = double.Parse(parts[16]), y2 = double.Parse(parts[17]), z2 = double.Parse(parts[18]);

      double effectiveWidth = DistanceUtils.GetDistanceBetweenPoints(x1, y1, z1, x2, y2, z2);

      return new AngleData(id, sizeA, sizeB, sizeC, sizeD, nodeA, nodeB, effectiveWidth);
    }

    private TbarData ParseTbar(string[] parts)
    {
      // TBAR는 ANGLE과 구조가 거의 같음
      string id = parts[1];
      double sizeA = double.Parse(parts[2]);
      double sizeB = double.Parse(parts[3]);
      double sizeC = double.Parse(parts[4]);
      double sizeD = double.Parse(parts[5]);

      double[] nodeA = { double.Parse(parts[7]), double.Parse(parts[8]), double.Parse(parts[9]) };
      double[] nodeB = { double.Parse(parts[10]), double.Parse(parts[11]), double.Parse(parts[12]) };

      // 유효폭 계산
      double x1 = double.Parse(parts[13]), y1 = double.Parse(parts[14]), z1 = double.Parse(parts[15]);
      double x2 = double.Parse(parts[16]), y2 = double.Parse(parts[17]), z2 = double.Parse(parts[18]);

      double effectiveWidth = DistanceUtils.GetDistanceBetweenPoints(x1, y1, z1, x2, y2, z2);

      return new TbarData(id, sizeA, sizeB, sizeC, sizeD, nodeA, nodeB, effectiveWidth);
    }


    // Nullable Double 파싱 (_ 또는 공백 처리)
    private double? TryParseNullableDouble(string text)
    {
      if (string.IsNullOrWhiteSpace(text) || text.Trim() == "_")
      {
        return null;
      }
      if (double.TryParse(text, out double result))
      {
        return result;
      }
      return null;
    }
  }
}
