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

## 정식 제조 기준: GPU Experiment

정식 제조는 `FluidGpuExperiment`를 기준으로 한다. 기존 Canvas 화면과
`BusinessBartendingBootstrap`의 씬 설치 진입점은 유지하며, 내부 제조를 원본
`FluidExperimentRuntimeRig`로 연결한다. 화면에는 기존 `BartendingViewport`의
RenderTexture/RawImage 경로로 표시한다.

`BusinessBartendingSettings.asset`의 `gpuCrafting`이 게임용 설정과 원본 GPU 런타임을
직접 참조한다. 병·잔·도구·액체·기법 관찰은 GPU Experiment를 사용하고,
주문 판정·결과 대사·보상·에피소드 분기는 기존 게임 흐름을 사용한다.

기존 `LegacyRigidbody2D`, `GpuPbfXpbd`, `Automatic` 설정과 `BartendingSessionBuilder.Build`
경로는 레거시/개발 샌드박스용으로 남긴다. 정식 제조의 백엔드 전환 옵션은 아니다.
기존 Bootstrap 전체 원본도 `docs/legacy/BusinessBartendingBootstrap.before-gpu.cs.txt`에 보존한다.
기존 레거시 풀링 최적화, 기존 PBF/XPBD와 GPU Experiment 구현의 기여 범위는 구분해야 한다.

자세한 구조는 [Canvas와 GPU 제조 연결](docs/gameplay/gpu-crafting-integration.md),
변경 파일과 이유·검증 한계는 [2026-11-04 변경 보고서](docs/gpu-crafting-change-report-20261104.md)를 참고한다.

## 검증

- 정적 참조: `python Tools/validate_gpu_crafting_sources.py`
- 로컬 Unity csproj/DLL 참조 기반 C# 컴파일: `python Tools/compile_gpu_crafting_sources.py`
- Unity 참조 검사: `Slainte > Bartending > Validate GPU Crafting References`
- Unity 실행 검사: `Slainte > Bartending > Validate GPU Crafting Play Mode`

Production BusinessScene에서 실제 Unity Play 검사를 통과했다. 고정 입력으로 GPU 방출·재고·보관·정상 레시피 제출/정산을 확인했고,
실제 UI 클릭으로 주문·재료/잔 선택·빈 잔 제출·Bad 피드백 종료를 확인했다.
손으로 붓기·젓기·흔들기의 사용감, 장시간 성능과 플레이어 빌드는 별도 검증이 필요하다.
