# 아키텍처 개요

## 핵심 시스템

**1. 게임 모드 관리 (`Assets/Scripts/Core/`)**

`GameMode` 열거형으로 씬 상태를 구분합니다:

| 모드 | 설명 | 활성 패널 |
|---|---|---|
| `OrderMode` | 기본 영업 상태 (초기 모드) | FrontWorldPanel + DialoguePanel + OrderTicketPanel |
| `EpisodeMode` | 에피소드 실행 중 | FrontWorldPanel + DialoguePanel |
| `CraftingMode` | 에피소드 중 칵테일 제조 | FrontWorldPanel + DialoguePanel |

`GameModeManager`가 `RequestModeChange(GameMode)`를 통해 패널 show/hide를 처리하고 `OnModeChanged` 이벤트를 발행합니다. **씬 전환은 없습니다.**

**2. 입력 처리 (`Assets/Scripts/Input/`)**

`InputRouter` 단 하나가 모든 `Update()` 입력을 수신해 현재 `GameMode`에 따라 라우팅합니다:

| 키 | OrderMode | EpisodeMode | CraftingMode |
|---|---|---|---|
| Space / LMB | `DialogueController.Advance()` | `EpisodeRunner.OnAdvanceInput()` | `EpisodeRunner.OnAdvanceInput()` |
| S / W / D / A | `FrontCameraRig` 이동 (대화 중 불가) | — | `FrontCameraRig` 이동 |
| E | `OrderTicketUI.Toggle()` | — | — |

수신자 인터페이스:
- `IDialogueAdvanceHandler` — `CanReceiveAdvanceInput`, `OnAdvanceInput()`
- `ICameraInputHandler` — `IsAnimating`, `OnCameraInput(CameraDirection)`

**3. 캐릭터 표시 (`Assets/Scripts/Presentation/`)**

- `CharacterView` — 프리팹 루트에 부착. `Setup(Sprite)` + `PlayAppearAnimation()` / `PlayDisappearAnimation()` 제공. fade+rise+pop 애니메이션 처리. 슬롯 하단 기준으로 배치.
- `CharacterStage` — 슬롯 배열을 관리하고 `CharacterView`를 생성. `ShowCharacters(keys, onAllShown)` / `Clear()`.
  - `CustomerSpawner`(영업 씬 손님)와 `EpisodeRunner`(에피소드) 모두 FrontCameraRig 내 `CustomerStage` 하나를 공유합니다. 슬롯 5개.
- `CharacterData` — 표정 1개 = 파일 1개. 같은 캐릭터의 여러 표정은 별도 파일로 관리 (key 예: `"yukari_mid"`, `"yukari_good"`). 영업 시스템은 `CustomerOrderData.characterKeyMid/Good/Bad`로 참조. 대화 시스템은 임의 key 사용.

**4. 에피소드 (`Assets/Scripts/Conversation/Episode/`)**

`EpisodeRunner`가 `EpisodeData` 기반 에피소드를 오케스트레이션합니다:
1. `EpisodeTriggerManager.CheckAndLaunchEpisode()` → 조건 검사 후 `GameModeManager.RequestModeChange(EpisodeMode)` + `EpisodeRunner.Begin(episode)` 호출
2. `EpisodeRunner`가 `CustomerStage`, `DialogueController`, 선택지 UI를 구동
3. 에피소드 종료 시 `GameModeManager.RequestModeChange(OrderMode)` 자동 복귀

`EpisodeNode`에 `requiresCrafting: bool` + `craftingTicketKey: string` 필드가 있어 노드 진입 시 `CraftingMode`로 전환하고 `EpisodeRunner.NotifyCraftingCompleted()` 호출 시 복귀합니다.

**5. 영업 씬 손님 & 주문 (`Assets/Scripts/Conversation/Sell/`, `Assets/Scripts/OrderTicket/`)**

- `CustomerSpawner` — `CustomerOrderData.characterKeyMid`로 `CharacterStage`에 캐릭터 표시를 위임하고, 등장 완료 후 `DialogueController.StartDialogue()` 호출
- `OrderTicketManager` — `DialogueClosed` 이벤트 수신. 단, `GameMode.OrderMode`일 때만 티켓을 표시 (EncounterMode에서는 미표시). `EncounterMode` 진입 시 티켓 초기화.
- `OrderTicketUI` — `Show(data)` / `HideImmediate()` / `Toggle()`. E키 처리는 `InputRouter`가 담당.

`CustomerOrderData` 구조:
```
CustomerOrderData
 ├── key                  (주문 고유 식별자)
 ├── characterKeyMid      (CharacterDatabase key — 기본 표정)
 ├── characterKeyGood     (CharacterDatabase key — 제조 성공 표정)
 ├── characterKeyBad      (CharacterDatabase key — 제조 실패 표정)
 ├── lines                (주문 대사)
 ├── feedbackLinesGood    (제조 성공 피드백 대사)
 └── feedbackLinesBad     (제조 실패 피드백 대사)
```
한 캐릭터가 여러 `CustomerOrderData`를 가질 수 있습니다 (랜덤 주문 선택 예정).

**6. 게임 진행 관리 (`Assets/Scripts/GameProgress.cs`)**

`DontDestroyOnLoad` 싱글톤. 씬 전환과 무관하게 유지됩니다:
- 스토리 플래그: `SetFlag` / `HasFlag` / `ClearFlag`
- 에피소드 완료 기록: `MarkEpisodeCompleted` / `IsEpisodeCompleted`
- 현재 게임 내 일 진행: `SetCurrentDay` / `CurrentDay`

**7. 드래그-드롭 바텐딩 (`Assets/Scripts/DragandDrop/`)**

- `ItemDef` (ScriptableObject): `ItemType`(Bottle, Glass, Tool), `dragMovesObject` 플래그
- `DragManager.Instance` 싱글톤: 드래그 상태와 고스트 이미지 관리
- `UIItemDraggable`: **Move** 모드(술병 — 원본 이동) / **Copy** 모드(잔/도구 — 원본 유지, 복사본 배치)
- `UIDropSlot`: 유효 드롭 타겟. Tool은 `toolPlaceableOnTable=true`인 경우만 허용
- `ShelfUI` / `DrawerUI`: `ItemDef` 배열에서 아이템 UI 생성
- `FrontCameraRig`: `ICameraInputHandler` 구현. `InputRouter`로부터 `CameraDirection` 명령 수신

**8. 대화 렌더링 (`Assets/Scripts/Conversation/DialogueController.cs`)**

TMPro 타이핑 애니메이션. 모든 모드에서 공유하는 단일 컴포넌트입니다:
- `StartDialogue(List<DialogueLine>)` — 손님 대화용 배치 큐 방식
- `ShowSingleLine(speakerName, text)` — 에피소드 노드별 단일 출력
- `SkipTypingIfNeeded()` — 타이핑 스킵 (InputRouter → EncounterRunner → DialogueController 경로)
- `DialogueClosed` 이벤트 — 대화 종료 시 발행

## 데이터 패턴

모든 컨텐츠는 `Assets/Data/`에 ScriptableObject 데이터베이스로 저장됩니다. 런타임에 string key로 조회합니다. 새 컨텐츠를 추가하려면 ScriptableObject 에셋을 만들고 해당 Database 에셋의 리스트에 등록하세요.

| 데이터 | 파일 | Database |
|---|---|---|
| 캐릭터 | `CharacterData` | `CharacterDatabase` |
| 에피소드 | `EpisodeData` | `EpisodeTriggerManager.episodes` 리스트 |
| 손님 주문 | `CustomerOrderData` | `CustomerOrderDatabase` |
| 주문표 | `OrderTicketData` | `OrderTicketDatabase` |
| 아이템 | `ItemDef` | `ShelfUI.items` / `DrawerUI.items` 배열 |

## 주요 설계 패턴

- **단일 씬 + GameMode 상태머신**: 씬 전환 없이 `GameModeManager`가 패널 활성/비활성으로 상태 전환
- **DontDestroyOnLoad 싱글톤**: `GameProgress`, `DragManager`
- **이벤트 기반 연결**: `DialogueController.DialogueClosed`, `EncounterRunner.OnEncounterCompleted`, `EpisodeDialogueRunner.OnEpisodeCompleted`, `GameModeManager.OnModeChanged`
- **ScriptableObject 데이터베이스**: 모든 게임 컨텐츠를 에디터에서 구성, 하드코딩 없음
- **인터페이스 기반 입력**: `IDialogueAdvanceHandler`, `ICameraInputHandler` — 수신자가 InputRouter에 의존하지 않음
