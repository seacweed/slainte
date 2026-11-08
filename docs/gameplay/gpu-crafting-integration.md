# Canvas와 GPU 제조 연결

기준: 2026-11-04, 머지 커밋 `f297c45b` 이후 작업 트리.

정식 칵테일 제조 기준은 `FluidGpuExperiment`다. 게임 씬과 UI는 기존 Canvas 구조를 유지한다.
아래는 구현된 연결 구조이며 실제 Unity/GPU 실행 통과 여부는 별도의 [변경 보고서](../gpu-crafting-change-report-20261104.md)를 따른다.

## 실행 구조

```text
BusinessScene 로드 → BusinessBartendingBootstrap → GPU/RenderTexture 사전 준비
GameModeManager → CraftingMode → 준비된 GpuCraftingSession 활성화
    → GpuCraftingSession
      → FluidExperimentRuntimeRig (원본 프리팹)
      → 제조 카메라 → RenderTexture → BartendingViewport의 RawImage
      → CraftingSupplyService → GameProgress 재고
```

`BusinessBartendingBootstrap`의 기존 씬 설치 진입점과 공개 선택 UI API를 유지한다.
이 클래스는 서비스를 조립하고 모드·카메라 이동·세션 종료를 연결한다.
기존 Bootstrap 원본은 [레거시 소스](../legacy/BusinessBartendingBootstrap.before-gpu.cs.txt)로 보존한다.
기존 `BartendingSessionBuilder.Build`와 기존 액체 백엔드는 정식 경로에서 호출하지 않는다.
카메라/뷰포트 생성과 Canvas 서빙 영역 표시는 기존 도우미를 재사용한다.

## 설정

`Assets/Resources/Bartending/BusinessBartendingSettings.asset`의 `gpuCrafting`이
`Content/GpuCraftingSettings.asset`을 직접 참조한다. 이 설정은 GPU RuntimeRig 원본과
가니쉬 재고 ID → GPU 프리팹 대응과 게임 화면용 바닥 두께·디스펜서 배치 비율을 갖는다.
병과 잔·도구는 RuntimeRig의 원본 카탈로그에서 찾는다.
셰이더·재질은 원본 프리팹/설정 에셋의 직접 참조를 통해 빌드 의존성에 포함된다.

기존 설정의 `LiquidSimulationBackend`와 PBF/XPBD 수치는 레거시 경로용이다.
정식 게임의 제조 구현 선택 스위치가 아니다. 새 경로는 GPU 실패 시 레거시 물리를 생성하지 않는다.
기존 EpisodeRunner의 수동 판정 복구 정책 자체는 이번 변경에서 수정하지 않았다.

## 화면과 입력

제작 공간(`CraftingSpace`)에 기존 RawImage 뷰포트를 생성한다.
화면 입력은 `BartendingViewport`를 통해 GPU 월드로 변환하고, 회전 포인터와 커서 복원은 역변환한다.
레터박스 밖은 입력 대상으로 삼지 않는다. GPU 샌드박스에서는 매핑 주입이 없으므로 기존 직접 카메라 좌표를 사용한다.

재료·도구 선택 UI는 유지한다. 물체와 진열 아이콘이 겹치면 월드 물체를 우선하고,
다른 UI는 기존 EventSystem 차단을 유지한다. 반납으로 소비된 누름은 원래 등록소에 기록한다.
기존 배경 Image의 RaycastTarget은 씬에서 수정하지 않는다. 게임 연결 코드가 배경 히트만 통과시키고
도감·주문서·버튼·입력/드래그 핸들러와 다른 팝업은 차단한다. UI 클릭으로 잔이 제출되지 않게 한다.
게임의 Experiment 진단 패널과 P/Enter 판정은 비활성화하며, Stir/Shake 관찰은 유지한다.
디스펜서의 실험용 OnGUI 문구도 같은 진단 표시 설정을 따른다.
디스펜서는 원본 실험 좌표 대신 현재 뷰포트 비율 좌표에 배치해 화면 잘림과 주문서 겹침을 피한다.

카메라 연출 중에는 GPU 틱·입력·native Rigidbody 시뮬레이션을 정지하고 이후 복원한다.
주문 종료 시 제조 물체·내용물·커서 상태와 재고 예약을 정리하고 뷰포트 표시/입력 등록을 끈다.
같은 BusinessScene 안에서는 GPU 버퍼·카메라·RenderTexture를 대기 상태로 보관한다.
다음 제조는 이 자원을 재사용하며, 씬 종료 시 실제 해제와 메인 카메라 컬링 복원을 수행한다.
준비 중에는 화면과 입력을 노출하지 않고, 대기 중에는 GPU 틱과 제조 카메라를 정지한다.

액체 합성은 이전 카메라/에디터 패스의 scissor 상태를 해제하고 각 중간 렌더 타깃의 전체 viewport를 명시한다.
화면 일부의 클리핑 상태가 남아도 액체만 좁은 영역에 잘리지 않게 한다.

## 재료와 도구

`GameProgress` 총량은 술장과 꺼낸 병의 잔량을 합한 값이다.
`CraftingSupplyService`는 꺼낸 양만 세션에서 예약한다. 실제 GPU 방출이 받아들여져 병의 양이 줄면
같은 호출에서 총재고에 반영한다. 생성 거절에는 차감하지 않는다.
반납과 제조 종료는 남은 양의 예약만 해제한다. 배송은 기존 결제/연출 완료 경로에서 총량을 더한다.

가니쉬는 꺼내면 한 개를 예약하고, 처음 놓을 때 한 개를 소비한다.
사용 전 술장 반납은 소비하지 않는다. 샌드박스의 무한 공급 통은 정식 세션에 생성하지 않는다.

도구는 정의당 한 인스턴스다. 첫 선택 때 원본 GPU 프리팹으로 만들고, 반납은 동일한 객체/용기 ID와
성분·기법 이력·담긴 고체를 보관한다. 보관 중에는 기존 GPU suspension 커널로 액체 적분/표시를 멈춘다.
다시 선택하면 같은 인스턴스를 복원한다. 분해된 셰이커는 반납을 거절한다.
얼음은 GPU 디스펜서를 사용하며 기존 진열 UI의 얼음 버킷 제외 정책을 유지한다.

## 제출과 주문

온전한 GPU 잔을 들고 기존 Canvas 서빙 영역에서 클릭하면 제출한다.
`TryBuildCocktailComposition`이 완료 상태의 재료별 ml·온도·잔 ID·얼음·기법 이력을 추출한다.
생성 예약 중에는 제출을 거절하고 제조를 유지한다. 같은 세션의 중복 제출은 거절한다.

`BusinessOrderSessionController`는 GPU 객체 대신 `CocktailComposition`을 수신한다.
기존 `CocktailOrderEvaluator`와 Good/Mid/Bad 판정·판매 기록·보상·대사·에피소드 결과 매핑을 사용한다.
EpisodeCraftingBridge/EpisodeRunner의 정상 진행 로직은 수정하지 않았다.

## 검증

- 정적 직렬화 참조: `python Tools/validate_gpu_crafting_sources.py`
- 생성된 Unity csproj와 로컬 참조 DLL을 사용하는 C# 컴파일: `python Tools/compile_gpu_crafting_sources.py`
- Unity 참조/기존 보상 규칙 검사: `Slainte.EditorTools.GpuCraftingIntegrationValidator.RunBatchReferences`
- 에디터 메뉴: `Slainte > Bartending > Validate GPU Crafting References`
- 그래픽 장치가 있는 Play 검사: `Slainte > Bartending > Validate GPU Crafting Play Mode`
- 저장을 억제한 직접 조작 점검: `Slainte > Bartending > Inspect GPU Crafting In Production`

Play 검사는 저장을 억제하고 진행 상태를 복원한다. 씬의 미저장 변경이 있으면 실행을 거절한다.
2026-11-04 실제 Production BusinessScene의 Play 검사와 직접 UI 점검을 통과했다.
주문/피드백은 실제 고객 대사를 사용한다. Good 경로는 고정 조성을 주입하고 GPU 렌더·제출·정산을 검사하며,
직접 UI 점검에서는 재료/잔 선택과 빈 잔 제출 후 Bad 피드백 종료를 확인했다.
후속 물리 전달 검사에서는 조성 직접 주입 없이 대표 병이 방출한 22ml가 놓인 락 글라스에 22ml 들어갔다.
성분/재고 반영, 집기 중 겹침 허용·놓기 후 충돌 복원·겹친 물체 교환과 채운 잔 재집기도 통과했다.
보관 복원은 재활성화 전에 Rigidbody와 Transform 위치를 함께 맞춰 집기 기준점을 보존한다.
제조 진입/잘림 후속 검사는 컴퓨터 유즈 없이 같은 Production 씬과 코드를 복사한 격리 Unity 배치 프로젝트에서 실행했다.
QHD 타깃의 9개 비대칭 위치를 하나씩 검사해 자유 액체/잔 안 액체의 총 18개 결과를 확인한다.
작은 scissor 상태를 주입한 회귀 조건은 수정 전 0/18, 수정 후 18/18이었다. 실제 사용자 화면의 원래 scissor 상태를 채집한 것은 아니다.
고정된 조성을 주입하는 검사는 손으로 붓기·젓기·흔들기, 고객 대화 전체, 하루 정산/휴식,
재실행 저장/로드, 시각적 자연스러움이나 성능을 입증하지 않는다.
실제 입력·해상도별 배치·장시간 동작·플레이어 빌드는 별도로 확인해야 한다.
