# PhysicsLab boundary search performance — 2026-09-22

정밀 콜라이더 적용 후 각 GPU 입자가 씬 전체 선분을 반복 검사하던 병목을 줄였다. 현재 샌드박스의 9개 오브젝트는 727개 경계 선분을 제공한다. 이전 단순 형상의 126개보다 약 5.77배 많다. 형상 단순화나 입자 수 감소 없이 검색 범위만 좁혔다.

## 변경

- 오브젝트마다 연속된 선분 범위, 현재 AABB, 이동·회전 전체를 포함한 AABB를 별도 버퍼에 저장한다. 가까운 범위에 대해서만 기존 선분 검사를 수행한다.
- 용기 내부 판정과 밀폐 용기 보정은 해당 용기의 내부 윤곽 범위만 읽는다. 등록 ID를 배열 인덱스로 사용하지 않으므로 오브젝트를 삭제하거나 다시 등록해도 동작한다.
- 이동 경계 검사에는 여러 바퀴 회전하는 경로 전체를 포함한다. 압력 보정 검사에는 위치 보정 길이까지 포함하여 벽 반대편으로 넘어간 경우를 놓치지 않는다.
- 물줄기 가림 검사에도 같은 범위 정보를 사용한다. 선분 순서, 개별 접촉 계산, 입자 수·반경·ml, 서브스텝, solver 반복 횟수, 표면 해상도는 유지한다.
- `PHYSICSLAB_LINEAR_BOUNDARIES` 로컬 셰이더 변형은 검증용 전체 검색 기준이다. 일반 실행은 범위 검색을 사용하며, 별도 게임플레이 설정은 추가하지 않았다.

수정된 실행 코드는 PhysicsLab 안에만 있다. 기존 Bartending GPU 시스템, 사용자 변경 중이던 씬·폰트·ProjectSettings·조작 코드는 수정하거나 커밋하지 않았다. `preexisting-sha256.json`과 `preservation-audit.json`에 작업 전후 일치를 기록했다.

## 최종 측정

Unity 6000.3.5f2, RTX 3070, Direct3D11. 실제 씬을 복사한 프로젝트에서 9개 오브젝트를 고정하고 용기 내부 액체와 낙하 중인 연속 물줄기를 생성했다. 각 측정 전 동일한 입자·물줄기·조성 스냅샷을 복원했다. 용량 4096, 입자 반경 0.065, 입자당 0.5ml, 서브스텝 2, solver 반복 5다.

`48-boundary-linear-reference`와 `49-boundary-optimized-final`은 같은 코드에서 전체 검색 변형과 범위 검색을 비교한다. 2회 준비 측정 후 9개 표본을 기록했다. Step 표본은 0.02초 시뮬레이션을 3회 실행하고 GPU 버퍼 읽기로 완료를 기다린 전체 시간을 3으로 나눈 값이다. 표는 중앙값이다.

| 활성 입자 수 | 전체 검색 | 범위 검색 | 처리 시간 감소 |
|---:|---:|---:|---:|
| 100 | 20.167ms | 4.078ms | 79.8% |
| 300 | 19.469ms | 4.884ms | 74.9% |
| 600 | 20.517ms | 5.405ms | 73.7% |
| 1000 | 20.371ms | 5.673ms | 72.2% |

**CPU 명령 제출과 GPU 완료 대기를 합친 벽시계 시간이다. GPU 타임스탬프, 실제 게임 FPS, 사용자 플레이 중 최악 프레임 시간으로 해석하면 안 된다.** 초기 `45`/`46`의 23.44ms→5.42ms는 예비 측정이며 최종 수치는 위의 통제된 비교를 사용한다.

1000입자에서 독립 커널을 반복한 진단 측정은 다음과 같다. 커널마다 동일 입력을 복원한 뒤 12회 실행하므로 정상 한 프레임의 시간 분해로 합산하지 않는다.

| 작업 | 전체 검색 | 범위 검색 |
|---|---:|---:|
| 이동 경계 충돌 | 0.496ms | 0.077ms |
| 밀도/lambda | 0.503ms | 0.073ms |
| 위치 보정 계산 | 0.502ms | 0.067ms |
| 위치 보정 및 경계 적용 | 0.786ms | 0.071ms |
| 물줄기 표면 생성 | 0.253ms | 0.038ms |
| 속도 갱신 | 0.485ms | 0.510ms |
| 조성 혼합 | 0.020ms | 0.031ms |

속도 갱신과 혼합에는 개선을 주장하지 않는다. 둘 다 일부 입자가 집중된 이웃 탐색 비용을 포함하며 이번 작업에서 그 알고리즘은 변경하지 않았다. 추가 진단에서 오브젝트 선분 업로드는 약 0.99ms/회, 1600×900 전체 카메라의 동기 완료 시간은 약 0.79ms/회였다. CPU 형상 업로드, 입자 밀집도와 여러 재료 혼합은 후속 프로파일링 대상으로 남는다. 이번 측정은 단일 재료의 통제된 워크로드이며 장시간 플레이의 프레임 분포를 대신하지 않는다.

## 검증과 실패 기록

- `50-boundary-scene-regression`: **350 PASS / 2 SKIP**, 종료 0. 실제 프리팹의 붓기, 충돌, 밀폐·열린 용기, 물줄기 가림, 액체 소유권, 보존량, 표면 픽셀, 기존 조작과 렌더러 재활성화 검사. SKIP 2개는 복사 프로젝트에서 검사할 수 없는 기존 시스템의 존재 여부다. 원본 게임 씬 전체 Play Mode 회귀를 주장하지 않는다. `pour-stream.png`도 직접 확인했다.
- `51-boundary-isolation`: **10 PASS**, 종료 0. 원거리 이동·회전 시 액체 위치/속도 RMS 차이 0, 서브스텝 2 유지, 빠른 고체 충돌, 밀폐 용기 855도 회전, 열린 용기 쏟아짐, 국소 충돌 처리 예산 초과 0.
- `52-boundary-differential-clean`: **25 PASS**, 종료 0. 4096입자 × 4가지 상태 × 6커널의 전체 검색/범위 검색 비교와 모든 오브젝트 제거 후 빈 경계 검사. 큰 위치 보정, 지연 생성, 855도 및 역방향 회전, 밀폐, 300회 재등록한 희소 ID를 포함한다. 충돌 위치·소유권과 물줄기 출력은 일치했고 이웃 연산의 작은 부동소수 오차는 보고서에 그대로 기록했다.
- `compile-restored`: 원본 C# 프로젝트 전체 빌드 **오류 0 / 기존 경고 8**, 종료 0. 앞선 `compile`은 SDK 디렉터리 접근 거부, `compile-sdk-access`는 비어 있던 Temp의 `project.assets.json` 누락으로 실패했다. 두 실패의 명령/로그/종료 코드를 보존하고, 접근 조건 수정 및 빌드 메타데이터 복원 후 별도 실행했다.
- `45`/`46`/`47`: 검사 자체는 통과했지만 `UnityEditor.Search.SearchDatabase.EnumerateAll → GetDefaultSearchDatabase → SearchInit.IndexationOnStartup`에서 에디터 시작 예외가 있었다. **깨끗한 검증 실행으로 취급하지 않는다.** 원본 설정 대신 복사 프로젝트의 `UserSettings/Search.settings`에서 `indexOnEditorStartup`을 껐으며, 이후 `48`~`52`에는 해당 예외와 런타임/셰이더 컴파일 예외가 없다. 설정 이름은 [Unity 공식 참조 코드](https://github.com/Unity-Technologies/UnityCsReference/blob/master/Modules/QuickSearch/Editor/SearchSettings.cs)로 확인했다. 이번 실행에는 네이티브 충돌이 없었다.

실행별 `command.json`, `process.json`, `exit.json`, `log-audit.json`과 측정 원자료를 보존했다. 큰 원시 Editor 로그·stdout/stderr·이미지는 기존 규칙에 따라 로컬에 남고, 검증 보고서·CSV·소스 해시는 커밋한다.

## 재현

`HarnessSource/prepare_scene_harness.py`로 복사 프로젝트를 준비한다. 원본 Unity 프로젝트에서 직접 벤치마크를 실행하지 않는다. `Run-Unity.ps1`에 사용하지 않은 Attempt 이름과 복사 프로젝트 경로를 지정한다.

- 전체 검색 측정: `-Method BoundaryBenchmark.BeginReference`
- 범위 검색 측정: `-Method BoundaryBenchmark.Begin`
- GPU 직접 대조: `-Method BoundaryBenchmark.BeginDifferential`
- 실제 씬 회귀: `-Method Slainte.Bartending.PhysicsLab.Editor.PhysicsLabValidator.Begin`

원거리 독립성 검사는 기존 `Prepare-Harness.ps1`로 별도 프로젝트를 준비하고 `IsolationHarness.Begin`을 실행한다. 실패 시 같은 실행 폴더를 재사용하거나 로그를 덮어쓰지 않는다.
