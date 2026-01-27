

namespace MooringFitting2026.Utils
{
  public static class MathUtils
  {
    /// <summary>
    /// 정렬된 리스트에서 허용 오차(tol) 내의 인접한 값들을 병합(제거)합니다.
    /// </summary>
    public static List<double> MergeClose(List<double> sortedValues, double tolerance)
    {
      if (sortedValues == null || sortedValues.Count == 0)
        return new List<double>();

      var merged = new List<double>();
      double current = sortedValues[0];
      merged.Add(current);

      for (int i = 1; i < sortedValues.Count; i++)
      {
        // 현재 값과 다음 값의 차이가 오차 범위 내라면 건너뜀 (병합 효과)
        if (Math.Abs(sortedValues[i] - current) <= tolerance)
          continue;

        current = sortedValues[i];
        merged.Add(current);
      }
      return merged;
    }
  }
}
