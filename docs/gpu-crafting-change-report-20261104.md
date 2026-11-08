# GPU 제조 연결 변경 보고서

2026-11-04. 기준 머지: `f297c45bc6714e6a5fa7f80050d928d356e43771`.
사용자 지시: Canvas 화면과 Bootstrap 연결은 활용하고 제조 기준을 GPU Experiment로 전환한다.
팀원 파트는 연결에 필요한 부분만 수정한다. 커밋/배포는 하지 않았다.

## 적용 범위

연결 구현은 [설계와 실행 구조](gameplay/gpu-crafting-integration.md)를 따른다.
정식 제조는 기존 Canvas 안에서 GPU Experiment 원본 런타임을 사용하도록 구성했다.
GPU 물리 수치, 셰이더, 병/잔/도구 프리팹 원본은 변경하지 않았다.

## 기존 파일 수정과 이유

| 파일 | 이유 |
| --- | --- |
| `Assets/_Project/Features/Bartending/Runtime/BusinessBartendingBootstrap.cs` | 기존 씬 설치·UI API를 유지하면서 구현을 조립/생명주기 연결로 축소. 레거시 제조 생성·재고·입력의 내부 구현은 원본 보존 자료로 분리. OnDestroy에서는 이미 파괴 중인 UI를 재진입시키는 종료 알림을 발행하지 않음. |
| `Assets/_Project/Features/Bartending/Runtime/BusinessBartendingSettings.cs` | 게임용 GPU 설정의 직접 참조 필드 하나 추가. |
| `Assets/Resources/Bartending/BusinessBartendingSettings.asset` | 위 필드에 실제 GPU 설정 연결. 기존 레거시 필드 값은 보존. |
| `Assets/_Project/Features/Business/Runtime/Flow/BusinessOrderSessionController.cs` | GPU 조성 제출/제출 거절 이벤트 수신과 상태 초기화 추가. 판정·대사·보상 계산 규칙은 유지. |
| `Assets/_Project/Features/Bartending/Runtime/ToolSelection/ToolSelectionUI.cs` | GPU 서비스의 도구 사용 가능 상태 전달과 세션 종료 초기화. 기존 UI 배치와 아트는 유지. |
| `Assets/_Project/Features/Bartending/Runtime/ToolSelection/ToolSlotUI.cs` | 레거시 물리 슬롯 없이도 같은 진열 UI에 도구 사용 가능 여부를 표시. |
| `Assets/_Project/Features/Bartending/Runtime/ToolSelection/ToolReturnZones.cs` | GPU 도구를 레거시 IBartendingItem으로 위장하지 않고 기존 반납 영역/클릭 소비 규칙 사용. |
| `Assets/_Project/Features/Bartending/Runtime/BartendingSessionBuilder.cs` | 기존 Canvas 제출 영역을 GPU 잔의 집기 상태로 표시하는 작은 진입점 추가. 기존 Build 구현은 유지. |
| `Assets/_Project/Features/Bartending/Runtime/BartendingViewport.cs` | 전체 뷰포트의 경계 포함 월드 사각형 계산 추가. Rect.Contains가 최댓값 경계를 제외해 게임 공간 초기화가 실패하던 문제 수정. |
| `Assets/_Project/Features/Bartending/FluidGpuExperiment/Runtime/FluidExperimentInteractor.cs` | 게임의 좌표 변환·제출/반납 클릭 경로와 카메라 전환 입력 정지 연결. |
| `Assets/_Project/Features/Bartending/FluidGpuExperiment/Runtime/FluidExperimentInteractor.Pointer.cs` | 회전/복귀 포인터 역변환, 게임 영역 가드, 겹친 진열 UI의 선택 우선순위 연결. |
| `Assets/_Project/Features/Bartending/FluidGpuExperiment/Runtime/FluidExperimentBody.cs` | 보관 비활성화 시 소유권 유지, 병 소비/가니쉬 사용의 즉시 알림 추가. 물리 계산식은 유지. |
| `Assets/_Project/Features/Bartending/FluidGpuExperiment/Runtime/FluidExperimentWorld.cs` | 카메라 연출 중 명시적으로 GPU 틱 정지. |
| `Assets/_Project/Features/Bartending/FluidGpuExperiment/Runtime/FluidExperimentCocktailSession.cs` | 게임에서 진단 UI를 끄면 Enter 진단 판정도 비활성화. 기법 관찰은 유지. |
| `Assets/_Project/Features/Bartending/FluidGpuExperiment/Runtime/FluidExperimentDispenser.cs` | 디스펜서 실험용 OnGUI 안내도 기존 진단 표시 설정을 따르게 함. 샌드박스 기본 표시는 유지. |
| `README.md`, `docs/gameplay/bartending-systems.md` | 정식 GPU 제조와 레거시 설명의 적용 범위 구분. |

`BusinessFlowBootstrap`, `EpisodeCraftingBridge`, `EpisodeRunner`, `GameModeManager`, `InputRouter`,
재료 선택 UI, 배송 결제/연출, GameProgress/저장 구조, 정산, 콘텐츠 CSV, Production Scene은 수정하지 않았다.
위 목록은 이번 변경 범위다. 개인/팀원의 기여 귀속을 새로 판단한 목록은 아니다.

## 새 파일과 이유

| 파일 | 이유 |
| --- | --- |
| `Runtime/Integration/GpuCraftingSession.cs` (Bartending 기능 아래) | GPU 제조와 기존 Canvas의 렌더/입력/제출/정리를 연결. GPU 초기화 후 공급/도구 UI 바인딩까지 완료한 시점만 Ready로 노출하고, 디스펜서는 게임 화면 비율 좌표에 배치. |
| `Runtime/Integration/CraftingSupplyService.cs` | GPU 재료·도구 공급, 반납과 GameProgress 재고 동기화. |
| `Runtime/Integration/GpuCraftingSettings.cs`, `Content/GpuCraftingSettings.asset` | 원본 RuntimeRig와 두 가니쉬의 명시적 연결, 게임용 바닥 두께/디스펜서 화면 위치. |
| `FluidGpuExperiment/Runtime/FluidExperimentInteractor.Mapping.cs` | 게임만 매핑을 주입하고 샌드박스 기본 카메라 동작 유지. |
| `FluidGpuExperiment/Runtime/FluidExperimentStorage.cs` | 기존 suspension 커널을 사용해 도구의 ID·액체·담긴 고체를 보관/복원. 비활성 Rigidbody 복원 때 Transform도 동기화해 재활성화 후 예전 위치로 돌아가 집기 기준점이 어긋나는 문제 수정. |
| `Runtime/Integration/GpuCraftingIntegrationProbe.cs` | 에디터 전용 고정 조건 통합 검사. 플레이어 빌드에서는 제외. |
| `Editor/GpuCraftingIntegrationValidator.cs` | Unity 참조/Play 검사 메뉴와 배치 진입점. |
| 위 에셋/스크립트와 `Integration` 폴더의 `.meta` | 새 Unity 에셋의 고유 참조 유지. 기존 GUID는 변경하지 않음. |
| `Tools/validate_gpu_crafting_sources.py` | Unity 실행과 구분되는 읽기 전용 직렬화 참조/원본 보존 점검. |
| `Tools/compile_gpu_crafting_sources.py` | 현재 생성된 Unity 프로젝트 참조를 사용한 Roslyn 컴파일 우회 경로. |
| `docs/legacy/BusinessBartendingBootstrap.before-gpu.cs.txt` | 기존 Bootstrap 전체 원본 보존. 실행되는 소스 파일이 아니며 기존 샌드박스/Builder는 남아 있음. |
| 이 보고서와 `docs/gameplay/gpu-crafting-integration.md` | 수정 파일/이유와 새 연결 구조를 분리 기록. |
| `PlaytestEvidence/GpuCraftingIntegration20261104/` | 정적 참조 결과, C# 컴파일 결과와 Unity 실행 시도 로그 보존. |

## 확인한 것

- 정적 검사 통과: 병 15종, 잔 4종, 가니쉬 2종의 GPU 대응, 새 설정/원본 프리팹의 GUID·fileID·셰이더 참조.
- 기존 Bootstrap 원본 SHA-256: `c104b6661c041a8607591fd4b180a2af0cbb353649da959e688a8848bb2b3cdd`.
- Roslyn C# 컴파일: 런타임 297개 소스, 에디터 95개 소스, 플레이어 심볼 구성 모두 오류 0.
- 런타임/플레이어 심볼 구성의 `CS0649` 경고 238개는 로그에 그대로 보존했다. 에디터 컴파일 경고는 0개다.
- 이번 수정 파일의 `git diff --check`: 통과. 사용자가 저장한 `FluidGpuComparison.unity`의 기존 공백 변경은 제외하고 보존했다.

정적 참조 점검 결과는 `PlaytestEvidence/GpuCraftingIntegration20261104/static-reference-results.json`,
C# 결과는 같은 폴더의 `compile-report.json`과 컴파일 로그에 있다.
생성된 csproj와 기존 로컬 Unity 참조 DLL을 사용한 컴파일이며 Unity 에셋 임포트나 플레이어 빌드 통과를 의미하지 않는다.

## Production BusinessScene 실행 확인

실제 열려 있는 Unity 6000.3.5f2, DX12 에디터에서 Production BusinessScene을 실행했다.
초기 별도 배치 실행의 라이선스 실패는 과거 시도이며, 이후 실제 에디터의 참조 검사와 Play 검사가 통과했다.

- Unity 참조 검사: 병 15종, 잔 4종과 GPU/셰이더/가니쉬 연결 통과.
- 자동 Play: 실제 고객 도착·주문 대화에서 제조 진입, 레거시 미생성, Canvas 좌표 변환, GPU 방출에 따른 재고 차감/반납/배송, 채운 잔의 ID·ml 보관/복원 통과.
- 고정 레시피 입력: GPU 액체 표면 렌더, 손님 서빙 영역 제출, Good 평가·피드백·판매/보상 1회 적용·중복 제출 차단 통과.
- 에피소드 요청/결과 변환, 무보상 정책과 반복 세션 정리 통과. 에피소드 그래프 전체 검증은 아니다.
- 실제 UI 클릭: 주문 대화 진행 → 재료 병 선택 → 잔 선택 → 서빙 영역 클릭 → 빈 잔의 Bad 판정과 손님 피드백 종료 통과. 사람이 정상 칵테일을 손으로 완성한 검증으로 해석하지 않는다.

실행 중 수정한 결함은 뷰포트 최댓값 경계 매핑 실패, GPU 초기화와 도구 UI 준비 사이의 준비 완료 경쟁,
씬 종료 중 세션 종료 이벤트가 파괴된 RecipeBookUI를 다시 열던 문제다.
게임용 디스펜서 위치와 실험 문구도 정리했다. 검증 대기는 프레임 수 대신 실제 시간으로 바꿨다.

근거는 `PlaytestEvidence/GpuCraftingIntegration20261104/ProductionRecheck/`의 최종 자동/직접 UI 결과와 화면에 있다.
실패한 초기 검증 시도도 보존했다. 검사 중 저장을 억제하고 진행 상태를 복원하며, 사용자 저장 파일과 두 씬의 해시 보존을 별도 확인한다.
최종 자동 결과는 `playmode-automated-final-pass.txt`, 직접 클릭 결과는 `manual-ui-pass.txt`,
세 파일의 해시 불변 확인은 `after-protected-hashes.json`이다. 최종 자동 실행 이후 새 런타임 오류는 없었다.
`production-good-cocktail.png`는 고정 조성 제출 직전 GPU 렌더 화면이며, 의도적으로 검사한 생성 대기 거절 문구와 방출 시험의 잔여 액체도 포함한다.
원본 GPU 셰이더의 컴파일 경고 8개는 남아 있으며 이번에 셰이더를 수정하지 않았다.

손으로 붓기·젓기·흔들기 사용감, 전체 에피소드/하루 정산·휴식·저장 후 재실행,
다른 해상도, 장시간 성능과 플레이어 빌드는 미검증이다.

## 후속 확인: 병과 잔 사이의 상호작용

사용자 지적에 따라 조성 직접 주입과 분리한 실제 전달 검사를 추가했다.
Production 씬의 공급 서비스로 잔을 꺼내 native 물리로 테이블에 놓고, 실제 병을 기울여 방출한 뒤 GPU에서 잔 안의 양을 읽었다.
최종 실행의 대표 병·락 글라스 조합은 **방출 22ml / 수용 22ml**였다. 이 구간은 `Fill`을 호출하지 않았다.
성분 ID와 제출용 조성의 양, 같은 양의 재고 차감도 확인했다.

집힌 병의 고체 겹침 허용, 놓은 뒤 충돌 복원, 잔 위에 병을 놓을 때 원본 물체 교환,
채운 잔 재집기·보관·다른 위치 복원·제출/정산까지 최종 검사에 포함해 통과했다.
검사 도중 보관 복원 좌표 결함을 발견해 위 Storage 파일을 수정했다.
고정 검사의 포인터는 에디터 전용 Probe에서만 업데이트를 꺼 OS 입력과 섞이지 않게 했다. 실제 게임 입력은 변경하지 않았다.

이번 후속 변경은 `FluidExperimentStorage.cs`, 에디터 전용 `GpuCraftingIntegrationProbe.cs`, 이 보고서/설계 문서/검증 근거뿐이다.
팀원 로직, Production 씬, 원본 GPU 물리 수치·셰이더·프리팹은 추가 변경하지 않았다.
근거: `ProductionRecheck/bottle-glass-final-pass.txt`, `production-physical-pour.png`, `after-pour-protected-hashes.json`.
모든 병×잔 조합이나 사람의 연속 마우스 붓기 조작감을 검사한 결과는 아니다.

## 후속 수정: 제조 진입 끊김과 액체 표시 영역

사용자 요청 이후 컴퓨터 유즈를 중단했다. 이후 검증은 창 전환/입력 없이 격리 Unity 배치 프로젝트에서 실행했다.
원본 Production 씬과 같은 소스/에셋을 사용하고 검증 복사본의 회사명/제품명을 분리했다.
초기 샌드박스 실행의 라이선스 IPC 실패 후, 설치된 공식 Unity를 일반 사용자 권한으로 실행해 GPU 자동 검사를 완료했다.

이번 추가 수정 파일과 이유:

| 파일 | 추가 변경 이유 |
| --- | --- |
| `Runtime/BusinessBartendingBootstrap.cs` | 씬 로드 후 GPU를 미리 준비하고 제조 시 활성화. 주문 종료와 씬 자원 폐기를 분리해 주문마다 재생성하던 비용 제거. |
| `Runtime/Integration/GpuCraftingSession.cs` | 준비/활성 제조/대기/폐기 수명주기 추가. 주문 내용은 비우되 GPU 버퍼·카메라·출력 타깃을 같은 씬에서 재사용. 대기 중 화면·입력·GPU 틱은 끔. |
| `Runtime/BartendingViewport.cs` | 대기 시 전역 입력 등록을 해제하는 API 추가. Canvas 알림 때 같은 크기의 RenderTexture를 다시 해제/생성하지 않게 함. |
| `FluidGpuExperiment/Runtime/FluidExperimentGpuLiquid.Rendering.cs` | 액체 합성 시작 시 scissor 해제, 밀도/색상/MRT/합성 타깃별 전체 viewport 명시. 외부 렌더 상태가 액체만 좁은 영역으로 자르던 경로 차단. 물리/셰이더 계산은 유지. |
| `Runtime/Integration/GpuCraftingIntegrationProbe.cs` | 제조 진입 프레임 계측, 이전 패스의 작은 scissor 상태 주입, QHD 자유/잔 안 액체 위치별 픽셀 검사, 동일 Rig/타깃 재사용 검사 추가. 에디터 검증 전용. |
| `Tools/validate_gpu_crafting_sources.py` | 새로 수정한 렌더 소스를 해시 근거에 포함. |
| 설계 문서, 이 보고서, `TransitionRecheck/` | 수명주기 변경, 재현 조건과 검증 한계를 기록. |

클리핑 상태가 없는 기본 배치 환경에서는 처음부터 QHD 9개 위치가 통과했다.
다른 패스가 작은 scissor 영역을 남기는 조건을 명시적으로 주입하자 수정 전 자유/잔 안 액체 **0/18**이 보였고,
위 렌더 상태 수정 후 동일 조건에서 **18/18**이 보였다. 이는 문제 경로의 회귀 재현이며 사용자 원본 프레임의 렌더 상태를 직접 채집한 결과는 아니다.

최종 GPU Play 검사는 주문·방출/재고·물체 교환·보관 복원·Good 제출/정산·에피소드 매핑까지 통과했고,
영업 주문과 에피소드 제조가 같은 RuntimeRig와 같은 RenderTexture를 사용하는 것을 확인했다.
최종 계측은 주문 연출 포함 최대 135.8ms, 제조 모드 구간 최대 31.02ms였다.
수정 전 반복 실행의 전체 진입 최대는 약 351~449ms였다. 최초 임포트 직후의 20.4초 셰이더 준비 표본은 따로 보존하며 일반 플레이 수치로 사용하지 않는다.
이 수치는 격리 배치 검사의 해당 실행 표본이며, 사용자가 다른 게임을 실행 중인 환경의 실측 FPS 보장이나 모든 끊김 해소를 의미하지 않는다.

`TransitionRecheck/final-pass.txt`, `final-entry-frames.csv`, `final-liquid-coverage.txt`가 최종 근거다.
초기에 깨끗했던 TMP 폰트 두 파일의 자동 생성 캐시 변화는 원래 Git 내용으로 복원했으며 코드 변경 목록에 포함하지 않았다.
Production/비교 씬과 최신 저장 파일 보존 결과는 `after-protected-hashes.json`에 기록한다.

## 11월 6일 후속 수정 — 잔을 화면 아래로 내릴 때 액체 소실

포인터가 제조 뷰포트 안에 있어도 윗부분을 잡은 잔은 작업대와 화면 하단을 통과할 수 있었다.
한 번 부어 담은 20ml를 같은 잔으로 내렸다 올리는 검사에서 화면 최하단의 액체 픽셀이 0으로 떨어졌다.
작업대에 살짝 겹치는 조건에서는 액체가 보였다. 이전 고정 위치 검사는 잔의 하단 이동을 포함하지 않았다.

| 파일 | 변경 이유 |
| --- | --- |
| `FluidGpuExperiment/Runtime/FluidExperimentWorld.Environment.cs` | 기존 집기 위치 제한에 선택적 작업대 하한을 추가했다. 회전한 용기의 실제 외곽이 하한을 넘지 않게 한다. |
| `Runtime/Integration/GpuCraftingSession.cs` | Production 제조 세션의 작업대 콜라이더를 용기의 하한으로 연결했다. |
| `Runtime/Integration/CraftingSupplyService.cs` | 보관한 용기와 내용물을 복원하기 전에 같은 위치 제한을 적용한다. |
| `Runtime/Integration/GpuCraftingIntegrationProbe.cs` | 실제로 부어 담은 잔을 화면 끝까지 이동하고, 각 위치의 잔 내부 용량과 카메라 출력 픽셀을 읽는 회귀 검사를 추가했다. |
| `Tools/validate_gpu_crafting_sources.py` | 환경 위치 제한 소스를 검증 해시에 추가했다. |

격리한 Production Unity DX12 자동검사에서 수정 전 최하단 액체 픽셀은 0이었다.
수정 후에는 포인터가 화면 끝에 도달해도 잔 바닥이 작업대 위에서 멈췄다.
왕복 12개 위치에서 20ml를 유지했고 액체 픽셀은 105~129개였다.
기존 QHD 렌더 검사, 붓기, 보관 복원, 주문 제출과 정산도 통과했다.
검증 기록은 `LowerEdgeRecheck/baseline-bottom-sweep.txt`, `fixed-bottom-sweep.txt`, `fixed-pass.txt`에 있다.

## 11월 8일 후속 조사와 수정 — 실제 에디터 프레임의 자유 액체 잘림

사용자가 직접 실행한 진단 캡처 `Library/GpuLiquidDiagnostics/20261108-193930-902`에서
물리 입자와 표면 도형이 각각 313개 존재했다. 물리 입자 304개가 화면 높이의 25% 아래에 있었지만
밀도와 색상 누적 텍스처에는 해당 부분이 없었다. 최종 합성 이전부터 액체가 누락된 상태였다.
캡처의 작업대와 다른 GPU 마스크 경계는 잘림 위치와 일치하지 않았다.

텍스처 전환마다 전체 viewport와 scissor를 명시한 첫 수정 후에도 실제 캡처
`20261108-194207-369`에서 증상이 남았다. 이 변경만으로 해결됐다고 판정하지 않는다.
메인 카메라 QHD 출력과 DX12 SplitJobs 모드를 맞춘 격리 검사에서는 수정 전에도
전체 술줄기가 표시돼, 실제 에디터의 잘림 자체를 격리 실행에서 재현하지는 못했다.

| 파일 | 추가 변경 이유 |
| --- | --- |
| `FluidGpuExperiment/Runtime/FluidExperimentGpuLiquid.Rendering.cs` | 각 렌더 타깃의 전체 raster 영역을 명시한다. 전역 카메라 행렬 설정을 제거하고 액체 누적 셰이더에 해당 카메라의 GPU 투영 행렬을 직접 전달한다. |
| `FluidGpuExperiment/Shaders/FluidExperimentImprovedAccumulation.shader` | 전용 `_SurfaceWorldToClip` 행렬로 표면 입자를 변환해 에디터나 다른 카메라의 전역 행렬에 의존하지 않게 한다. |
| `FluidGpuExperiment/Shaders/FluidExperimentImprovedComposite.shader` | 화면 전체 쿼드를 clip 좌표로 직접 출력하고 그래픽 API의 텍스처 방향을 처리한다. |
| `Editor/GpuCraftingIntegrationValidator.cs` | 사용자가 Ctrl+Shift+F10으로 현재 액체/카메라 출력, 밀도/색상, 표면 도형, 물리 입자, 실제 GPU 마스크와 앞쪽 Canvas UI 경계를 함께 저장하도록 보강했다. |

Unity 6000.3 공식 문서는 `CommandBuffer.SetViewProjectionMatrices`의 URP 비호환을 명시한다.
기존 코드는 GPU용으로 미리 변환한 행렬을 이 전역 설정 API에 넘기고 있었다.
새 누적 경로는 해당 API를 호출하지 않는다.

전용 행렬 변경은 격리 프로젝트에서 먼저 검사했다. 텍스처 방향 누락을 발견해 수정한 뒤
QHD 자유/소유 액체 18개 위치, 실제 20ml 붓기, 하단 잔 이동, 보관 복원,
주문 제출·평가·정산·세션 재사용 검사가 통과했다.
`FreeStreamRecheck/explicit-projection-oriented-pass.txt`와
`explicit-projection-oriented-stream.txt`가 해당 실행 기록이다.
이 자동 검사와 실제 사용자 화면의 수정 후 확인은 구분한다.

사용자가 전용 좌표 변환 적용 후 정상 표시를 확인했고,
수정 후 실제 에디터 캡처 `Library/GpuLiquidDiagnostics/20261108-194832-551`도 검사했다.
기존 캡처에서는 카메라 출력의 액체가 위에서 약 1084픽셀 지점까지만 있었지만,
수정 후에는 실제 작업대 위치인 약 1341픽셀까지 그려졌다.
중앙 화면 범위(x UV 0.05~0.95)에서 하단 액체 122개 모두의 위치 인근에
합성 텍스처와 카메라 출력 픽셀이 있었다.
우측 화면 끝에 있는 두 입자의 인근 픽셀 대조는 일치하지 않아
해당 한 장으로 모든 가장자리 입자의 표시까지 통과했다고 해석하지 않는다.
원본 Production/비교 씬의 해시는 작업 전과 일치했다.
사용자 플레이 중인 원본 저장 파일은 19:48:22에 갱신돼 작업 전 해시와 달라졌다.
격리 검증은 별도 회사명/제품명의 저장 경로를 사용했고, 원본 저장 파일은 현재 내용을 유지했다.
분석 기록은 `FreeStreamRecheck/live-frame-analysis-20261108.json`과
`live-fixed-frame-analysis-20261108.json`에 보존했다.

이번 후속 변경에서 팀원 소유 Business 흐름이나 공유 도구 선택 UI를 추가 수정하지 않았다.

## 11월 8일 — 술장 기준 크기와 액체·얼음 낙하

술장 Image의 preserveAspect와 부모 배율을 반영한 표시 높이를 제조 월드로 환산한다.
꺼낸 물체는 종횡비를 유지하는 단일 배율을 Awake 이전에 적용해 그림·충돌체·병 입구·셰이커 부품을 함께 맞춘다.
이미 생성해 보관한 잔과 도구는 같은 인스턴스를 복원하므로 크기가 누적 변경되지 않는다.
진열 용기와 실제 낱조각이 다른 가니쉬는 기존 낱조각 크기를 사용한다.

크기 축소 후에도 레시피 ml가 담기도록 진열 잔의 배율에 따라 복제된 런타임 설정의
액체 입자 반경·평활화 길이·면적 환산을 보정한다. 원본 설정과 ml 재고 단위는 유지한다.
얼음도 같은 길이 배율로 생성하며, 재등록 시 배율이 누적되지 않게 했다.
Production 테이블의 GPU 액체 충돌을 해제했다. 얼음은 기존 native 테이블 충돌 제외를 활용하고,
시뮬레이션 하단까지 떨어진 잔 밖 얼음은 정리한다.
스터 조작·완료 판정은 사용자 요청에 따라 사용자가 직접 검증한다.

| 수정 파일 | 이유 |
| --- | --- |
| `Runtime/BartendingViewport.cs` | 실제 Image 표시 크기를 월드 크기로 변환. 입력이 꺼진 사전 준비 단계에서도 명시한 뷰포트로 계산. |
| `Runtime/IngredientSelection/IngredientSlotUI.cs`, `IngredientSelectionUI.cs` | 진열 크기 조회 메서드만 추가. |
| `Runtime/ToolSelection/ToolSlotUI.cs`, `ToolSelectionUI.cs` | 잔·도구 진열 크기 조회 메서드만 추가. |
| `Runtime/BusinessBartendingBootstrap.cs` | UI 크기 조회를 GPU 공급 서비스로 연결. |
| `Runtime/Integration/CraftingSupplyService.cs` | 첫 생성 시 진열 배율을 적용하고 보관 복원 시 같은 크기 유지. |
| `Runtime/Integration/GpuCraftingSession.cs` | 액체·얼음 길이 단위 보정, 테이블의 액체 충돌 해제, 낙하한 얼음 정리. |
| `FluidGpuExperiment/Runtime/FluidExperimentRuntimeRig.cs` | Awake 전에 생성 배율을 적용하는 선택적 인자 추가. 기본값은 기존 크기. |
| `FluidGpuExperiment/Runtime/FluidExperimentWorld.cs`, `FluidExperimentBody.cs` | Production 얼음 배율 적용 및 재등록 중복 배율 방지. 기본값 1로 기존 실험 동작 유지. |
| `FluidGpuExperiment/Runtime/FluidExperimentDispenser.Emission.cs` | 얼음 출구의 여유 검사도 생성 얼음 크기에 맞춤. |
| `Runtime/Integration/GpuCraftingIntegrationProbe.cs` | 술장 크기 일치·반납 복원·낙하와 정리 검사 추가. 렌더 검사 잔도 Production 크기로 생성. |
| `Tools/validate_gpu_crafting_sources.py` | 변경한 연결 소스를 검증 해시에 포함. |

검증 기록은 `PlaytestEvidence/GpuCraftingIntegration20261104/ShelfAndFalls20261108/`에 저장한다.

최종 격리 DX12 SplitJobs 검사는 통과했다. 진열 병 14종과 잔·도구 7종의 크기 일치,
반납·재집기 후 같은 인스턴스와 배율 유지, 액체·얼음의 테이블 통과와 하단 정리를 확인했다.
실제 20ml 붓기에서 20ml 수령, 내용물 보관 복원, 주문 제출·평가·정산과 세션 재사용도 통과했다.
크기 단위 보정 전의 레시피 채움 실패와 검증용 미축소 잔의 범위 검사 실패는 시도 기록에 보존했다.
최종 결과는 `final-pass.txt`, 크기 측정은 `shelf-sizes.txt`에 있다.
Production/비교 씬의 해시는 작업 전과 일치하며, 실제 화면의 조작감과 스터는 사용자 검증 범위다.

## 11월 8일 — 제조 영역을 게임 화면 전체에 고정

제조 뷰포트를 대화용 확대·이동 리그의 자식에서 전체 `GameArea16x9` 기준으로 옮겼다.
기존 배경 레이어 바로 위에 배치해 화면 전체를 채우고, 대화/HUD 계층과 함께 렌더링한다.
작업대 높이는 제조 레이아웃의 정규화 좌표로 계산한다.
사전 준비 중 대화 확대가 남아 있어도 진열 크기 계산에서 그 임시 배율을 제거한다.

| 파일 | 이유 |
| --- | --- |
| `Runtime/Integration/GpuCraftingSession.cs` | 전체 게임 영역에 뷰포트 고정, 배경 위 그리기 순서 지정, 배경 확대와 독립적인 작업대 좌표 계산. |
| `Runtime/BusinessBartendingBootstrap.cs` | 대화 확대 배율을 제거한 전체 보기 기준으로 술장 물체 크기를 계산. |
| `Runtime/Integration/GpuCraftingIntegrationProbe.cs` | 실제 제조 영역과 게임 영역의 경계 일치 검사. 배경을 확대·이동해도 경계가 유지되는지 확인. |

검증 결과는 `PlaytestEvidence/GpuCraftingIntegration20261104/Fullscreen20261108/`에 기록한다.
격리 DX12 SplitJobs 검사에서 전체 영역 경계와 배경 확대·이동 독립성이 통과했다.
술장 크기 14+7종, 액체·얼음 낙하, 실제 20ml 붓기와 보관 복원, 주문 제출·정산 검사도 통과했다.
`final-pass.txt`가 최종 실행 결과다.

## 11월 8일 — 지거·셰이커 내용물 ml 표시

사용자가 지정한 지거와 셰이커에 기존 내용물 라벨을 연결했다.
재료별 ml와 합계를 표시하고 빈 용기는 0ml로 표시한다.
들고 이동할 때 용기를 따라가며, 보관·제거된 용기의 라벨은 정리한다.
GPU의 기존 비동기 재료별 집계 결과를 사용하고 표시를 위해 동기 GPU readback을 호출하지 않는다.
일반 술잔은 이 표시의 대상에서 제외한다.

| 파일 | 이유 |
| --- | --- |
| `Runtime/BartendingSessionBuilder.cs` | 기존 오버레이·폰트·배경·숫자 형식을 재사용해 GPU 지거/셰이커 집계와 위치·수명 연결. 기존 레거시 라벨도 같은 경로에서 유지. |
| `Runtime/Integration/GpuCraftingSession.cs` | Production 오버레이에 GPU 월드 연결. |
| `Runtime/Integration/GpuCraftingIntegrationProbe.cs` | 각 용기의 0.5→1→0ml 갱신과 보관 후 라벨 제거 검사 추가. |
| `Editor/GpuCraftingIntegrationValidator.cs` | 같은 배치 실행에서 기존 비커 계열 표시 검증도 수행. |

기록은 `PlaytestEvidence/GpuCraftingIntegration20261104/VolumeLabels20261108/`에 저장한다.
최종 격리 DX12 SplitJobs 검사에서 지거·셰이커 각각 0.5→1→0ml 갱신과 보관 후 라벨 제거가 통과했다.
기존 비커 계열 라벨의 대상·폰트 설정 검사도 수행했다.
전체 제조 영역, 술장 크기, 낙하, 20ml 실제 붓기, 보관 복원, 제출·정산 회귀 검사도 통과했다.
`final-pass.txt`가 최종 결과다. 실제 화면에서의 가독성은 별도의 사용자 확인 범위다.

## 11월 8일 — 회수 영역을 공통 사각형 하나로 통일

병·잔·도구 모두 같은 사각형 안에서 클릭하면 회수하도록 변경했다.
기본 범위는 전체 너비, 작업대 위 진열 구간이다(QHD 화면 아래 원점 기준 y255~447).
`BusinessBartendingSettings`의 `Shelf Return Area`에서 게임 영역 기준 정규화 좌표(x, y, 너비, 높이)를 조절한다.
해상도와 레터박스를 반영하며, 기존 클릭·회전 커서 기준 및 재고·내용물 보관 처리는 유지한다.

| 파일 | 이유 |
| --- | --- |
| `Runtime/BusinessBartendingSettings.cs` | 공통 회수 사각형 설정 추가. |
| `Assets/Resources/Bartending/BusinessBartendingSettings.asset` | Production 기본 회수 범위를 하단 진열 구간으로 지정. |
| `Runtime/BusinessBartendingBootstrap.cs` | 설정을 게임 화면 좌표로 변환해 공통 포함 판정 제공. |
| `Runtime/IngredientSelection/IngredientSelectionUI.cs` | 병 회수 판정을 공통 사각형에 연결. |
| `Runtime/ToolSelection/ToolSelectionUI.cs` | 잔·도구 회수 판정을 같은 사각형에 연결. |
| `Runtime/Integration/GpuCraftingIntegrationProbe.cs` | 영역 안 회수와 영역 밖 유지 검사 추가. |

검증 기록: `PlaytestEvidence/GpuCraftingIntegration20261104/ReturnZones20261108/`.
격리 DX12 SplitJobs 실행에서 병·도구의 영역 안 회수와 영역 밖 유지가 통과했다.
기존 제조·붓기·보관·제출·정산 검사도 통과했으며, 최종 결과는 `final-pass.txt`다.
