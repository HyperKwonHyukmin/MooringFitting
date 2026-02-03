using System;

namespace MooringFitting2026.Inspector
{
  /// <summary>
  /// 파이프라인의 실행 단계를 정의하는 비트 플래그(Flags) 열거형입니다.
  /// 원하는 단계만 조합(OR 연산)하여 실행할 수 있습니다.
  /// </summary>
  [Flags]
  public enum ProcessingStage
  {
    None = 0,

    /// <summary>Stage 01: 공선 및 중복 요소 정렬/분할</summary>
    Stage01_CollinearOverlap = 1 << 0,

    /// <summary>Stage 02: 기존 노드에 의한 요소 분할</summary>
    Stage02_SplitByNodes = 1 << 1,

    /// <summary>Stage 03: 요소 교차점 생성 및 분할</summary>
    Stage03_IntersectionSplit = 1 << 2,

    /// <summary>Stage 03.5: 중복 요소 병합 (등가 물성)</summary>
    Stage03_5_DuplicateMerge = 1 << 3,

    /// <summary>Stage 04: 자유단 연장 및 연결</summary>
    Stage04_Extension = 1 << 4,

    /// <summary>Stage 05: 메쉬 세분화</summary>
    Stage05_MeshRefinement = 1 << 5,

    /// <summary>Stage 06: MF Rigid 및 하중 생성</summary>
    Stage06_LoadGeneration = 1 << 6,

    /// <summary>모든 단계 실행</summary>
    All = Stage01_CollinearOverlap | Stage02_SplitByNodes | Stage03_IntersectionSplit |
          Stage03_5_DuplicateMerge | Stage04_Extension | Stage05_MeshRefinement | Stage06_LoadGeneration
  }
}
