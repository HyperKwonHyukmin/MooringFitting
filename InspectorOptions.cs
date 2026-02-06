using System;

namespace MooringFitting2026.Inspector
{
  /// <summary>
  /// 검사기(Inspector)의 동작을 제어하는 설정 옵션입니다.
  /// <br/>사용법: InspectorOptions.Create().EnableDebug().Build();
  /// </summary>
  public class InspectorOptions
  {
    // ... (속성 정의는 기존과 동일) ...
    public bool DebugMode { get; private set; }
    public bool PrintAllNodeIds { get; private set; }
    public bool CheckTopology { get; private set; }
    public bool CheckGeometry { get; private set; }
    public bool CheckEquivalence { get; private set; }
    public bool CheckDuplicate { get; private set; }
    public bool CheckIntegrity { get; private set; }
    public bool CheckIsolation { get; private set; }
    public double ShortElementDistanceThreshold { get; private set; }
    public double EquivalenceTolerance { get; private set; }
    public double NearNodeTolerance { get; private set; }
    public ProcessingStage ActiveStages { get; private set; }
    public bool EnableFileLogging { get; private set; }

    private InspectorOptions() { }

    public static Builder Create() => new Builder();
    public static InspectorOptions Default => Create().Build();

    public class Builder
    {
      private readonly InspectorOptions _options;

      public Builder()
      {
        _options = new InspectorOptions
        {
          DebugMode = false,
          PrintAllNodeIds = false,
          CheckTopology = true,
          CheckGeometry = true,
          CheckEquivalence = true,
          CheckDuplicate = true,
          CheckIntegrity = true,
          CheckIsolation = true,
          ShortElementDistanceThreshold = 1.0,
          EquivalenceTolerance = 0.1,
          NearNodeTolerance = 1.0,
          ActiveStages = ProcessingStage.All,
          EnableFileLogging = false
        };
      }

      // =============================================================
      // 1. 디버깅 설정 (Debugging)
      // =============================================================

      /// <summary>
      /// [디버그 모드] 상세 로그를 출력하도록 설정합니다.
      /// </summary>
      /// <param name="printAllNodes">
      /// true일 경우, 검출된 모든 노드 ID 목록을 콘솔에 출력합니다. 
      /// (노드가 많을 경우 콘솔이 느려질 수 있음)
      /// </param>
      public Builder EnableDebug(bool printAllNodes = false)
      {
        _options.DebugMode = true;
        _options.PrintAllNodeIds = printAllNodes;
        return this;
      }

      /// <summary>
      /// 디버그 로그를 끕니다. (오류 메시지만 출력됨)
      /// </summary>
      public Builder DisableDebug()
      {
        _options.DebugMode = false;
        _options.PrintAllNodeIds = false;
        return this;
      }

      // =============================================================
      // 2. 검사 항목 스위치 (Check Switches)
      // =============================================================

      /// <summary>
      /// [일괄 설정] 모든 검사 항목(Topology, Geometry, Duplicate 등)을 켜거나 끕니다.
      /// <br/>보통 .SetAllChecks(false)로 초기화한 뒤 필요한 것만 .WithXxx(true)로 켭니다.
      /// </summary>
      public Builder SetAllChecks(bool isEnabled)
      {
        _options.CheckTopology = isEnabled;
        _options.CheckGeometry = isEnabled;
        _options.CheckEquivalence = isEnabled;
        _options.CheckDuplicate = isEnabled;
        _options.CheckIntegrity = isEnabled;
        _options.CheckIsolation = isEnabled;
        return this;
      }

      /// <summary>
      /// [위상 검사] 자유단(Free End) 및 고립 노드(Isolated Node)를 검사할지 설정합니다.
      /// </summary>
      public Builder WithTopology(bool enabled) { _options.CheckTopology = enabled; return this; }

      /// <summary>
      /// [형상 검사] 너무 짧은 요소(Short Element) 등을 검사할지 설정합니다.
      /// </summary>
      public Builder WithGeometry(bool enabled) { _options.CheckGeometry = enabled; return this; }

      /// <summary>
      /// [등가 노드] 위치가 거의 동일한(겹친) 노드를 검사할지 설정합니다.
      /// </summary>
      public Builder WithEquivalence(bool enabled) { _options.CheckEquivalence = enabled; return this; }

      /// <summary>
      /// [중복 요소] 동일한 노드 구성을 가진 중복 Element를 검사할지 설정합니다.
      /// </summary>
      public Builder WithDuplicate(bool enabled) { _options.CheckDuplicate = enabled; return this; }

      /// <summary>
      /// [무결성] 존재하지 않는 노드/프로퍼티를 참조하는 요소를 검사할지 설정합니다.
      /// </summary>
      public Builder WithIntegrity(bool enabled) { _options.CheckIntegrity = enabled; return this; }

      // =============================================================
      // 3. 임계값 설정 (Thresholds)
      // =============================================================

      /// <summary>
      /// 검사에 사용될 허용 오차(Tolerance) 및 기준값을 설정합니다.
      /// </summary>
      /// <param name="shortElemDist">이 길이보다 짧으면 'Short Element'로 간주 (기본 1.0)</param>
      /// <param name="equivTol">이 거리 이내면 '동일 노드'로 간주 (기본 0.1)</param>
      /// <param name="nearNodeTol">인접 노드 검색 범위 (기본 1.0)</param>
      public Builder SetThresholds(double shortElemDist = 1.0, double equivTol = 0.1, double nearNodeTol = 1.0)
      {
        _options.ShortElementDistanceThreshold = shortElemDist;
        _options.EquivalenceTolerance = equivTol;
        _options.NearNodeTolerance = nearNodeTol;
        return this;
      }

      // =============================================================
      // 4. 파이프라인 단계 설정 (Pipeline Flow)
      // =============================================================

      /// <summary>
      /// 실행할 파이프라인 단계를 명시적으로 설정합니다.
      /// 예: .SetStages(ProcessingStage.Stage01 | ProcessingStage.Stage06)
      /// </summary>
      public Builder SetStages(ProcessingStage stages)
      {
        _options.ActiveStages = stages;
        return this;
      }

      /// <summary>
      /// 특정 단계까지만 실행하고 멈추도록 설정합니다. (디버깅용)
      /// </summary>
      /// <param name="limitStage">마지막으로 실행할 단계 (포함)</param>
      public Builder RunUntil(ProcessingStage limitStage)
      {
        // Enum 값 비교를 위해 정수형 변환 등을 사용할 수도 있지만,
        // 여기서는 비트 연산으로 간단히 구현 (순서 의존적)
        ProcessingStage mask = ProcessingStage.None;
        foreach (ProcessingStage stage in Enum.GetValues(typeof(ProcessingStage)))
        {
          if (stage == ProcessingStage.None || stage == ProcessingStage.All) continue;

          mask |= stage;
          if (stage == limitStage) break;
        }
        _options.ActiveStages = mask;
        return this;
      }

      /// <summary>
      /// 콘솔 출력을 파일로도 저장할지 설정합니다.
      /// </summary>
      /// <param name="enabled">true일 경우 CSV 폴더에 로그 파일 생성</param>
      public Builder WriteLogToFile(bool enabled)
      {
        _options.EnableFileLogging = enabled;
        return this;
      }

      /// <summary>
      /// 설정을 완료하고 InspectorOptions 불변 객체를 생성합니다.
      /// </summary>
      public InspectorOptions Build()
      {
        return _options;
      }
    }
  }
}
