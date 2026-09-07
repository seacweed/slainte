# Slainte

Unity 6000.3.5f2와 URP 17.3.0으로 제작 중인 에피소드 기반 바텐딩 게임입니다.
게임의 큰 흐름은 `MainMenu → Episode → Business → Settlement → Rest → Episode`이며,
Episode와 Business는 같은 `BusinessScene`을 공유합니다.

## 개발 환경과 시작 지점

- Unity: `6000.3.5f2`
- Render Pipeline: URP `17.3.0`, 2D Renderer
- 주요 런타임 데이터: ScriptableObject와 CSV 임포트 결과
- 빌드 및 프로젝트 검증: [Unity 빌드 안내](docs/core/unity-build.md)

프로젝트를 처음 살펴볼 때는 다음 문서를 순서대로 확인합니다.

1. [프로젝트 폴더 구조 규약](docs/refactoring/folder-structure-conventions.md) — Core·Shared·Feature 소유권과 새 파일 배치 기준
2. [아키텍처](docs/core/architecture.md) — 씬, 상태, 주문 세션, 데이터 흐름
3. [바텐딩 시스템](docs/gameplay/bartending-systems.md) — 도구 상호작용, 액체 백엔드, 조성 및 판정
4. [구현 계획](docs/implementation-plan.md) — 현재 완료 범위와 다음 작업

## 액체 시뮬레이션 백엔드

바텐딩 액체는 세션 설정으로 두 구현을 선택합니다. GPU 구현은
`ILiquidSimulationBackend`를 직접 구현하고, 레거시 `LiquidPool`은 원본 코드를 수정하지 않은 채
`LegacyLiquidSimulationBackend` 어댑터가 세션 계약에 연결합니다.

| 모드 | 구현 | 용도 |
|---|---|---|
| `LegacyRigidbody2D` | `LiquidPool` 기반 Unity Physics2D 입자 | 기존 풀링·재활용·따르기·젓기 경로를 그대로 사용 |
| `GpuPbfXpbd` | Compute Shader 기반 GPU PBF/XPBD | 별도 GPU 입자 시뮬레이션을 우선 사용하고, 초기화 실패 시 레거시로 폴백 |
| `Automatic` | 실행 환경에 따라 선택 | GPU 실행 조건을 만족하면 GPU, 그렇지 않으면 레거시 사용 |

### 모드 전환

1. Unity에서 `Assets/Resources/Bartending/BusinessBartendingSettings.asset`을 선택합니다.
2. Inspector의 `Liquid > Liquid Simulation Backend`를 원하는 모드로 설정합니다.
3. 바텐딩 세션을 다시 생성하거나 씬을 다시 시작합니다.

설정은 세션 생성 시 읽습니다. 실행 중인 입자를 다른 백엔드로 변환하는 런타임 핫스왑은
지원하지 않습니다. 현재 저장된 기본값은 `Automatic`입니다.

레거시 입자 부피(`water_particle.prefab`의 `2 ml`)와 GPU 입자 부피
(`gpuLiquidParticleVolumeMl`, 기본 `0.5 ml`)는 서로 독립된 설정입니다.

레거시 모드에서는 기존 90도 임계값·고정 `pourMlPerSecond`·가로 스폰 지터·초기속도 0인
`LiquidPool.GetParticle()` 따르기 경로와 평균 조성 편차 10% 이하인 젓기 완료 기준을 유지합니다.
기울기별 유량·병 입구 방향 초기속도·입구 이동속도 상속과 모든 GPU 입자의 최대 조성 편차
검사는 GPU 모드에서만 사용합니다. GPU 부하 패널과 단축키도 GPU 세션에만 붙습니다.

### 구현 및 기여 경계

- 레거시 경로의 입자 풀 캐시, 화면 경계 계산, 자동 반환, 중복 혼합 방지 및 수면 처리는 기존 `LiquidPool`/`LiquidReaction` 최적화 구현을 유지합니다. `LiquidPool.cs`는 GPU 인터페이스를 직접 구현하지 않습니다.
- GPU PBF/XPBD는 `Infrastructure/GpuFluid`와 `Runtime/Liquid/Gpu`에 분리된 신규 백엔드입니다.
- `BottleController`, `VesselLiquidTracker`, 젓기·셰이킹 처리는 백엔드 경계에서 명시적으로 분기하며, 레거시 분기는 GPU 도입 전 계산과 호출 순서를 유지합니다.
- 개인 포트폴리오에서는 레거시 풀링 최적화와 GPU 백엔드 구현을 같은 사람의 단독 기여로 묶지 않고 실제 담당 범위를 구분해야 합니다.

## 액체 검증

- 정적 구성 검증: Unity 메뉴 `Slainte > Bartending > Validate GPU Liquid PBF-XPBD`
- 런타임 컴파일: `dotnet build Assembly-CSharp.csproj`
- 에디터 컴파일: `dotnet build Assembly-CSharp-Editor.csproj`
- GPU 성능 계측: GPU 세션에서만 생성되는 `LiquidStressHarness`의 입자 수와 p95/p99 프레임 시간을 사용

컴파일과 정적 검증은 실제 Unity 플레이모드 동작이나 GPU 성능을 증명하지 않습니다.
포트폴리오에 FPS, 지원 입자 수 또는 절감률을 기재하려면 대상 기기에서 별도로 측정해야 합니다.
