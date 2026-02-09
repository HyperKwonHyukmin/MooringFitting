using System.Collections.Generic;

namespace MooringFitting2026.Results
{
  /// <summary>
  /// F06 해석 결과를 전체적으로 담는 컨테이너입니다.
  /// </summary>
  public class F06AnalysisResult
  {
    // Key: LoadCaseID (Subcase ID), Value: 해당 케이스의 결과 데이터
    public Dictionary<int, LoadCaseResult> CaseResults { get; set; } = new Dictionary<int, LoadCaseResult>();

    /// <summary>
    /// 특정 LoadCase의 결과를 가져오거나, 없으면 새로 생성합니다.
    /// </summary>
    public LoadCaseResult GetOrCreateCaseResult(int loadCaseId)
    {
      if (!CaseResults.TryGetValue(loadCaseId, out var result))
      {
        result = new LoadCaseResult { LoadCaseID = loadCaseId };
        CaseResults[loadCaseId] = result;
      }
      return result;
    }
  }

  /// <summary>
  /// 하나의 Load Case(Subcase)에 대한 결과 모음
  /// </summary>
  public class LoadCaseResult
  {
    public int LoadCaseID { get; set; }

    // [Displacement] NodeID -> (X, Y, Z, Rx, Ry, Rz)
    // 여기선 Translation(T1, T2, T3)만 저장 예시
    public Dictionary<int, (double X, double Y, double Z)> Displacements { get; set; }
        = new Dictionary<int, (double, double, double)>();

    // [Beam Force] ElementID -> Force Data
    public Dictionary<int, BeamForceResult> BeamForces { get; set; }
        = new Dictionary<int, BeamForceResult>();

    // [Beam Stress] ElementID -> Stress Data
    public Dictionary<int, BeamStressResult> BeamStresses { get; set; }
        = new Dictionary<int, BeamStressResult>();
  }

  public class BeamForceResult
  {
    public int ElementID { get; set; }
    public double AxialForce { get; set; }
    public double TotalTorque { get; set; }
    public double MomentA { get; set; } // Plane 1
    public double MomentB { get; set; } // Plane 2
    public double ShearA { get; set; }
    public double ShearB { get; set; }
  }

  public class BeamStressResult
  {
    public int ElementID { get; set; }
    // 빔 응력은 Grid A, Grid B 등 여러 지점이 나오지만, 
    // 여기선 "Max Combined Stress" (보통 sxc, sxd... 중 최대값) 등을 저장한다고 가정
    public double MaxStressCombined { get; set; }
    public double MinStressCombined { get; set; }
    public double MarginOfSafety { get; set; } // M.S.
  }
}
