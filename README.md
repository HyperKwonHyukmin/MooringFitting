# MooringFitting2026

선박 계류 장비(Mooring Fitting)의 구조 건전성 검토를 위한 **FE 모델 자동 생성 및 Nastran 해석 파이프라인**입니다.

CSV 형식의 구조 설계 데이터를 입력받아 기하학적 전처리, BDF 파일 생성, Nastran 해석 실행, 결과 후처리까지 전 과정을 자동화합니다.

---

## 주요 기능

- CSV 입력 데이터로부터 빔 요소 FE 모델 자동 생성
- 7단계 기하학적 전처리 (공선 정렬 → 교차점 분할 → 메쉬 세분화)
- Nastran SOL 101 BDF 파일 자동 생성 (8자 필드 규격 준수)
- RBE2 강체 요소 및 계류 하중 자동 생성
- Nastran F06 결과 파싱 및 빔 응력 계산
- Excel 최종 보고서 자동 생성 (`Final_Analysis_Report.xlsx`)
- Python 기반 3D 모델 시각화 연동

---

## 요구 사항

| 항목 | 버전 |
|---|---|
| .NET SDK | 8.0 이상 |
| Nastran | MSC Nastran (선택, 해석 실행 시 필요) |
| Python | 3.9 이상 (선택, 시각화 시 필요) |
| Python 패키지 | `pandas`, `pyvista`, `numpy` |

---

## 빌드 및 실행

```bash
# 빌드
dotnet build

# 실행
dotnet run
```

### 해석 케이스 선택

`PathManager.cs`에서 실행할 케이스를 변경합니다.

```csharp
// PathManager.cs
public static (string Data, string Load) Current = Case1;  // ← 원하는 케이스로 변경
// public static (string Data, string Load) Current = Case6;
```

### 주요 실행 옵션

`Program.cs` 상단의 플래그로 동작을 제어합니다.

```csharp
bool RUN_NASTRAN_SOLVER = false;  // true: Nastran 솔버 직접 실행
bool EXPORT_RESULT_CSV  = true;   // true: F06 파싱 후 CSV 결과 저장
bool EnableAutoBottomSPC = true;  // true: 하부 노드 자동 SPC 설정
```

---

## 입력 파일 형식

케이스 폴더(`csv/Case_XX/`) 안에 두 개의 CSV 파일이 필요합니다.

### 구조 데이터 (`MooringFittingData.csv`)

```
MF,  {ID},{X},{Y},{Z},{타입},{SWL},{a},{b},{c},_,{Dep1},{Dep2},{Dep3},{Dep4},...
PLATE,{ID},{위치},{X1},{Y1},{Z1},{X2},{Y2},{Z2},...,{두께},{등급}
FLATBAR,{ID},{X1},{Y1},{Z1},{X2},{Y2},{Z2},{폭},{두께}
ANGLE, {ID},{X1},{Y1},{Z1},{X2},{Y2},{Z2},{H},{Bt},{Tt},{Tw}
TBAR,  {ID},{X1},{Y1},{Z1},{X2},{Y2},{Z2},{H},{Bt},{Tt},{Tw}
```

### 하중 데이터 (`MooringFittingDataLoad.csv`)

```
LOADCASE,{ID},{Fx_Fore},{Fy_Fore},{Fz_Fore},{Fx_Aft},{Fy_Aft},{Fz_Aft},...,{X},{Y},{Z}
```

---

## 파이프라인 처리 단계

| 스테이지 | 내용 |
|---|---|
| Stage 00 | Z-평면 정규화 (2D 변환), 기준 BDF 출력 |
| Stage 01 | 공선·중복 요소 정렬 및 분할 |
| Stage 02 | 기존 노드 위치에서 요소 분할 |
| Stage 03 | 요소 교차점 탐색 및 분할 |
| Stage 03.5 | 중복 요소 병합 → `EQUIV_PBEAM` 등가 단면 생성 |
| Stage 04 | 자유단 노드 연장 및 연결 |
| Stage 05 | 목표 크기(500mm) 기준 메쉬 세분화 |
| Stage 06 | RBE2 강체 생성, 계류·윈치 하중 생성, 최종 BDF 출력 |

---

## 출력 파일

실행 완료 후 케이스 폴더에 다음 파일이 생성됩니다.

```
csv/Case_XX/
├── STAGE_00.bdf ~ STAGE_06.bdf     # 각 전처리 단계별 중간 BDF
├── {이름}.bdf                       # 최종 Nastran 해석 입력 파일
├── {이름}.f06                       # Nastran 해석 결과 (솔버 실행 시)
├── Final_Analysis_Report.xlsx       # 최종 결과 보고서 (Excel)
├── Report_LoadCalculation_MF.csv    # MF 하중 계산 리포트
├── Report_LoadCalculation_Winch.csv # Winch 하중 계산 리포트
├── Final_Nodes_Check.csv            # 최종 노드 목록
├── Final_Elements_Check.csv         # 최종 요소 목록
├── DuplicateMerge_Report.csv        # 중복 요소 병합 리포트 (해당 시)
├── View_*.png                       # 시각화 결과 이미지 (Python 실행 시)
└── Log_{날짜시간}.txt               # 실행 로그
```

---

## 시각화

Python 가상환경이 프로젝트 루트의 `.venv`에 설치되어 있으면 파이프라인 완료 후 자동으로 시각화됩니다.

수동 실행도 가능합니다.

```bash
python visualize_model.py "csv/Case_XX"
```

---

## 프로젝트 구조

```
MooringFitting2026/
├── Pipeline/               # 파이프라인 실행 제어
├── Model/                  # FE 모델 엔티티 (Nodes, Elements, Properties)
├── Modifier/               # 기하학적 전처리 (Stage 01~06)
├── Inspector/              # 위상·기하 품질 검사
├── Parsers/                # CSV 및 F06 파서
├── Exporters/              # BDF 생성 및 결과 내보내기
├── Services/               # 단면 계산, 하중 생성, 응력 후처리
├── Utils/                  # 기하학 유틸리티, UnionFind
├── RawData/                # 입력 데이터 모델
└── csv/                    # 케이스별 입력 데이터 및 결과
    ├── Case_01/
    ├── Case_02/
    └── ...
```

---

## 기술 스택

- **언어 / 프레임워크**: C# 12, .NET 8.0
- **Excel 출력**: [ClosedXML](https://github.com/ClosedXML/ClosedXML) v0.105.0
- **해석 솔버**: MSC Nastran (SOL 101 정적 해석)
- **시각화**: Python / PyVista

---

## 라이선스

이 프로젝트는 내부 업무용으로 작성된 코드입니다.
