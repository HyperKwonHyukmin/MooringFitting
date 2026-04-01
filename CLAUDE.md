# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

---

이 문서는 **MooringFitting2026** 프로젝트(선박 계류 시스템 FE 모델링 및 구조 해석 도구)의 아키텍처, 코딩 표준 및 공학적 제약 사항을 정의합니다.

---

## 1. 주요 명령어

```bash
dotnet build                                        # 빌드
dotnet run                                          # 실행 (Program.cs의 MainApp)
python visualize_model.py [결과_폴더_경로]           # 시각화 (pandas, pyvista, numpy 필요)
```

---

## 2. 전체 파이프라인 흐름

```
CSV 파일 (csv/Case_XX/)
  → CsvRawDataParser → RawStructureData / WinchData
  → RawFeModelBuilder → FeModelContext
  → FeModelProcessPipeline.Run()
      ├─ Stage 00: Z-평면 정규화 (NodeZPlaneNormalizeModifier) → 기준 BDF 출력
      ├─ Stage 01: 공선·중복 요소 정렬 및 분할 (CollinearOverlapAlignSplit)
      ├─ Stage 02: 기존 노드 위치에서 요소 분할 (SplitByExistingNodes)
      ├─ Stage 03: 교차점 분할 (IntersectionSplit)
      ├─ Stage 03.5: 중복 요소 병합 (DuplicateMerge → EQUIV_PBEAM 생성)
      ├─ Stage 04: 자유단 노드 연장·연결 (ExtendToBBoxIntersectAndSplit)
      ├─ Stage 05: 메쉬 세분화 (MeshRefinement, 목표 크기 500mm)
      └─ Stage 06: RBE2 강체 생성 + 하중/SPC 생성 → BDF 출력
  → Nastran 솔버 (.bdf → .f06)
  → F06Parser → BeamForcePostProcessor → 응력 계산
  → ExcelReportGenerator + CSV 출력 (ClosedXML)
```

**파이프라인 제어 플래그** (`Program.cs`):
- `RUN_NASTRAN_SOLVER`: Nastran 솔버 호출 여부
- `EXPORT_RESULT_CSV`: F06 파싱 및 결과 CSV 출력 여부
- `EnableAutoBottomSPC`: 하부 노드 자동 SPC 설정 여부

**ProcessingStage** (`Inspector/ProcessingStage.cs`): `[Flags]` enum으로 특정 스테이지까지만 실행 가능.

```csharp
InspectorOptions.Create()
    .RunUntil(ProcessingStage.All)
    .WriteLogToFile(true)
    .SetThresholds(shortElemDist: 1.0, equivTol: 0.1)
    .Build();
```

---

## 3. 핵심 아키텍처

### 주요 파일
| 파일 | 역할 |
|---|---|
| `Pipeline/FeModelProcessPipeline.cs` | 모든 스테이지 실행 순서 제어, rigidMap/forceLoads/spcList 상태 관리 |
| `Model/Entities/FeModelContext.cs` | Nodes, Elements, Properties, Materials 통합 저장소 (모든 Modifier가 공유) |
| `Exporters/BdfBuilder.cs` | Nastran BDF 섹션 생성 (Executive/CaseControl/Bulk) |
| `Exporters/BdfFormatFields.cs` | BDF 8자 필드 포맷터 (모든 BDF 출력에 반드시 사용) |
| `Services/Analysis/BeamForcePostProcessor.cs` | F06 결과로부터 빔 응력 계산 |
| `Services/SectionProperties/BeamSectionCalculator.cs` | 단면 형상별 Ax, Iy, Iz, J, W 계산 |
| `Inspector/StructuralSanityInspector.cs` | 위상·기하학적 품질 검사 및 SPC 노드 목록 반환 |

### 공간 인덱싱
성능을 위해 `SpatialHash`, `LocalSpatialHash`, `ElementSpatialHash`를 사용. 노드/요소 근접 검색 시 반드시 활용.

### 연결 요소 분석
`UnionFind.cs` — 연결된 요소 그룹 감지.

---

## 4. 코딩 및 공학 표준

### 개발 규칙
- **네임스페이스**: `MooringFitting2026.*`
- **명명 규칙**: 클래스·메서드는 `PascalCase`, 지역 변수는 `camelCase`
- **주석**: public 멤버에 XML 주석(`/// <summary>`) 권장

### FE 모델링 제약
- **노드 생성**: 반드시 `Nodes.AddOrGet()` 사용 (좌표 중복 방지, 소수점 첫째 자리 반올림)
- **Nastran 포맷**: BDF 각 필드는 **8자** 너비 엄수 → `BdfFormatFields.cs` 전용 포맷터 사용
- **요소 속성**: `I`, `T`, `PBEAM`, `EQUIV_PBEAM` 형식 지원
- **요소 생성 전** 참조 노드가 `FeModelContext`에 존재하는지 반드시 확인

### 응력 계산 공식
| 응력 | 공식 |
|---|---|
| 축응력 (Nx) | 축방향 하중 / 단면적 (Ax) |
| 비틀림 (Mx) | 비틀림 모멘트 / 비틀림 단면계수 (Wx) |
| 약축 굽힘 (My) | Moment 2 / 약축 단면계수 (Wz_Min) |
| 강축 굽힘 (Mz) | Moment 1 / 강축 단면계수 (Wy_Min) |
| 전단 (Qy, Qz) | 전단력 / 전단 면적 (Ay, Az) |

### Stage 03.5 — 중복 요소 병합
동일 노드 쌍을 가진 요소를 단일 요소로 병합할 때 `EquivalentPropertyMerger`를 사용하여 합산된 Ax, Iy, Iz, J를 가진 `EQUIV_PBEAM` 속성을 생성. 강성 보존이 핵심.

---

## 5. BDF 생성 규칙

`BdfBuilder.Run()` 섹션 순서:
1. Executive Control: `SOL 101` (정적 해석)
2. Case Control: 서브케이스별 출력 요청 (DISPLACEMENT, FORCE, STRESS)
3. Bulk Data: GRID, CBEAM, PBEAM/EQUIV_PBEAM, MAT1, RBE2, SPC, FORCE 카드

BDF 출력 파일명: `{stageName}.bdf` (케이스 폴더 내)

---

## 6. 필수 준수 사항
- 코드 수정 시 기능의 완전성을 위해 전체 소스 코드를 제공합니다.
- Nastran 솔버 호환성을 위해 8자 칸 맞춤 규칙을 엄격히 준수합니다.
- 새 Modifier 작성 시 `FeModelProcessPipeline`에 스테이지를 등록하고, `ProcessingStage` enum에 플래그를 추가합니다.
