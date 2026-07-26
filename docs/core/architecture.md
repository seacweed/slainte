# 아키텍처 개요

## 업데이트 규칙
architecture.md 업데이트 시 관련 문서에 입력할 사항들은 해당 문서에 작성. 필요시 새로운 문서 생성하고 관련 문서에 링크 추가.

## 핵심 시스템

**1. 게임 모드 관리 (`Assets/Scripts/Core/`)**

`GameMode` 열거형으로 씬 상태를 구분합니다:

| 모드 | 설명 | 활성 패널 |
|---|---|---|
| `OrderMode` | 기본 영업 상태 (초기 모드) | FrontWorldPanel + DialoguePanel + OrderTicketPanel(open) + RecipeBookPanel(open) |
| `EpisodeMode` | 에피소드 실행 중 | FrontWorldPanel + DialoguePanel (OrderTicketPanel/RecipeBookPanel 토글 잠금·슬라이드 닫힘) |
| `CraftingMode` | 에피소드 중 칵테일 제조 | FrontWorldPanel + DialoguePanel + OrderTicketPanel(open) + RecipeBookPanel(open) + CraftingJudgePanel |

`GameModeManager`가 `RequestModeChange(GameMode)`를 통해 모드 전환을 처리하고 `OnModeChanged` 이벤트를 발행합니다. **씬 전환은 없습니다.** 패널 제어는 두 방식으로 나뉩니다:
- `FrontWorldPanel` / `DialoguePanel` / `ChoiceContainer` / `CraftingJudgePanel` — `CanvasGroup` alpha/interactable 즉시 on-off
- `OrderTicketPanel` / `RecipeBookPanel` — 자체 슬라이드 애니메이션으로 open/close. `GameModeManager`는 `SetInteractable(bool)`(EpisodeMode 잠금)과 `Open()`(OrderMode/CraftingMode 진입 시 강제 open)만 호출

**2. 입력 처리 (`Assets/Scripts/Input/`)**

`InputRouter` 단 하나가 모든 `Update()` 입력을 수신해 현재 `GameMode`에 따라 라우팅합니다:

| 키 | OrderMode | EpisodeMode | CraftingMode |
|---|---|---|---|
| Space / LMB | `DialogueController.Advance()` | `EpisodeRunner.OnAdvanceInput()` | `EpisodeRunner.OnAdvanceInput()` |
| S / W | `FrontCameraRig` 서랍 열기/닫기 (대화 중 불가) | — | `FrontCameraRig` 서랍 열기/닫기 |
| Tab | `RecipeBookUI.Toggle()` | — | `RecipeBookUI.Toggle()` |
| E | `OrderTicketManager.ToggleTicket()` | — | `OrderTicketManager.ToggleTicket()` |
| 1 (테스트) | `CustomerSpawner.ShowCustomers("yukari")` | — | — |
| 2 (테스트) | `testEpisode` 조건 없이 즉시 실행 | — | — |

수신자 인터페이스:
- `IDialogueAdvanceHandler` — `CanReceiveAdvanceInput`, `OnAdvanceInput()`
- `ICameraInputHandler` — `IsAnimating`, `OnCameraInput(CameraDirection)`

`CameraDirection` 열거형: `DrawerOpen` / `DrawerClose` (S/W 키). 술장(Shelf) 제거로 `ShelfOpen` / `ShelfClose` 삭제됨.
`FrontCameraRig` 수평 이동은 캐릭터 포커스 전용 `PanToWorldCenterX()` / `ResetPan()`만 남음.

**3. 게임 진행 관리 (`Assets/Scripts/GameProgress.cs`)**

`MonoSingleton<GameProgress>`. 씬 전환과 무관하게 유지됩니다:
- 스토리 플래그: `SetFlag` / `HasFlag` / `ClearFlag`
- 수치 변수: `GetVar` / `SetVar` / `AddVar` — 호감도 등 정수형 전역 변수 관리
- 에피소드 완료 기록: `MarkEpisodeCompleted` / `IsEpisodeCompleted`
- 현재 게임 내 일 진행: `SetCurrentDay` / `CurrentDay`

## 데이터 패턴

모든 컨텐츠는 `Assets/Data/`에 ScriptableObject 데이터베이스로 저장됩니다. 런타임에 string key로 조회합니다. 새 컨텐츠를 추가하려면 ScriptableObject 에셋을 만들고 해당 Database 에셋의 리스트에 등록하세요.

| 데이터 | 파일 | 로드 방식 |
|---|---|---|
| 캐릭터 | `CharacterData` | `CharacterDatabase` (string key 조회) |
| 에피소드 | `EpisodeData` | `Resources.LoadAll<EpisodeData>("EpisodeData")` — `EpisodeManager`가 시작 시 일괄 로드 |
| 손님 주문 | `CustomerOrderData` | `CustomerOrderDatabase` |
| 주문표 | `OrderTicketData` | `OrderTicketDatabase` |
| 아이템 | `ItemDef` | `ShelfUI.items` / `DrawerUI.items` 배열 |

## 공용 UI 유틸리티 (`Assets/Scripts/Tools/`)

특정 기능에 묶이지 않는 범용 UI 컴포넌트는 `Assets/Scripts/Tools/`에 둡니다.

| 컴포넌트 | 역할 |
|---|---|
| `ContentHeightToBackground` | ScrollView Content 높이를 배경 이미지(AspectRatioFitter) 높이에 맞춤 |
| `HollowRectangle` | 크기(RectTransform)와 테두리 두께(`Thickness`)를 독립적으로 조절 가능한 속이 빈 사각형. 상/하/좌/우 4개의 `Image`를 자동 생성해 테두리만 그리는 방식(배경에 의존하지 않음) |

## 주요 설계 패턴

- **하루 흐름 + GameState 상태머신**: `DayFlowManager`가 Episode → Business → Rest 순서를 조율하고, `GameManager.ChangeState()`가 필요한 씬 전환을 처리. Episode와 Business는 같은 BusinessScene을 공유
- **씬 내 GameMode 상태머신**: `GameModeManager`가 패널 활성/비활성으로 씬 내 모드 전환 (씬 전환 없음)
- **MonoSingleton<T>**: `GameProgress`, `GameModeManager`, `EpisodeManager`, `DataManager`, `GameManager`, `AudioManager` 모두 통일
- **이벤트 기반 연결**: `DialogueController.DialogueClosed`, `GameModeManager.OnModeChanged`
- **ScriptableObject 데이터베이스**: 모든 게임 컨텐츠를 에디터에서 구성, 하드코딩 없음
- **인터페이스 기반 입력**: `IDialogueAdvanceHandler`, `ICameraInputHandler` — 수신자가 InputRouter에 의존하지 않음

## 관련 문서

| 문서 | 내용 |
|---|---|
| [../gameplay/character-presentation.md](../gameplay/character-presentation.md) | 캐릭터 표시(`CharacterView`/`CharacterStage`/`CharacterData`), 대화 렌더링(`DialogueController`) |
| [../narrative/episode-engine.md](../narrative/episode-engine.md) | 에피소드 오케스트레이션(`EpisodeRunner`, 분기, 제조 트리거), 오디오/BGM(`AudioManager`) |
| [../gameplay/business-interactions.md](../gameplay/business-interactions.md) | 영업 씬 손님&주문(`CustomerSpawner`/`OrderTicketManager`/`OrderTicketUI`), 드래그-드롭 바텐딩(`ItemDef`/`DragManager`/`FrontCameraRig`) |
