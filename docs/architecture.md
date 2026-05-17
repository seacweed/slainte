# 아키텍처 개요

## 핵심 시스템

**1. 게임 모드 관리 (`Assets/Scripts/Core/`)**

`GameMode` 열거형으로 씬 상태를 구분합니다:

| 모드 | 설명 | 활성 패널 |
|---|---|---|
| `OrderMode` | 기본 영업 상태 (초기 모드) | FrontWorldPanel + DialoguePanel + OrderTicketPanel |
| `EpisodeMode` | 에피소드 실행 중 | FrontWorldPanel + DialoguePanel |
| `CraftingMode` | 에피소드 중 칵테일 제조 | FrontWorldPanel + DialoguePanel + OrderTicketPanel + CraftingJudgePanel |

`GameModeManager`가 `RequestModeChange(GameMode)`를 통해 패널 show/hide를 처리하고 `OnModeChanged` 이벤트를 발행합니다. **씬 전환은 없습니다.**

**2. 입력 처리 (`Assets/Scripts/Input/`)**

`InputRouter` 단 하나가 모든 `Update()` 입력을 수신해 현재 `GameMode`에 따라 라우팅합니다:

| 키 | OrderMode | EpisodeMode | CraftingMode |
|---|---|---|---|
| Space / LMB | `DialogueController.Advance()` | `EpisodeRunner.OnAdvanceInput()` | `EpisodeRunner.OnAdvanceInput()` |
| S / W / D / A | `FrontCameraRig` 이동 (대화 중 불가) | — | `FrontCameraRig` 이동 |
| E | `OrderTicketUI.Toggle()` | — | — |
| 1 (테스트) | `CustomerSpawner.ShowCustomers("yukari")` | — | — |
| 2 (테스트) | `testEpisode` 조건 없이 즉시 실행 | — | — |

수신자 인터페이스:
- `IDialogueAdvanceHandler` — `CanReceiveAdvanceInput`, `OnAdvanceInput()`
- `ICameraInputHandler` — `IsAnimating`, `OnCameraInput(CameraDirection)`

**3. 캐릭터 표시 (`Assets/Scripts/Presentation/`)**

- `CharacterView` — 프리팹 루트에 부착. `Setup(Sprite, Sprite?)` + `SwapSprite(Sprite, Sprite?)` + `PlayAppearAnimation()` / `PlayDisappearAnimation()` 제공. fade+rise+pop 애니메이션 처리. 슬롯 하단 기준으로 배치.
  - `ApplySlotLayout(slot)` — 슬롯 높이(1440, 화면 전체)를 채우고 `HeightControlsWidth` ARF로 스프라이트 원본 비율 유지하며 너비 자동 결정. 슬롯의 x 위치가 캐릭터 수평 중심.
  - `AttachOverlayToFrontContainer(frontContainer)` — `VisualOverlay` 자식을 `frontContainer`로 reparent(`worldPositionStays: true`). 이후 애니메이션/alpha는 `Visual`과 동기화됨. `OnDestroy` 시 overlay GameObject 자동 정리.
  - `GetVisualWorldBoundsX(out float left, out float right)` — `_visualRT.GetWorldCorners()`로 스프라이트의 실제 화면 공간 X 경계를 반환. `HeightControlsWidth` ARF가 너비를 확정하려면 1프레임이 필요하므로, 생성 직후 호출 시 부정확할 수 있음.
  - 프리팹 자식 구조: `Visual`(바 테이블 뒤) + `VisualOverlay`(바 테이블 앞). overlay sprite가 null이면 `VisualOverlay`는 비활성화됨.
- `CharacterStage` — 슬롯 배열을 관리하고 `CharacterView`를 생성. `_activeViews`(key→view)와 `_activeSlotIndices`(key→슬롯 인덱스)로 현재 스테이지 상태를 추적합니다.
  - `ShowCharacters(IReadOnlyList<CharacterSlotEntry>, onAllShown)` — 신규 캐릭터는 입장 애니메이션, 기존 캐릭터는 스프라이트 교체만 수행. 점유된 슬롯을 추적해 신규 캐릭터는 지정 슬롯 또는 빈 슬롯에 배정합니다.
  - `SwapExpression(characterKey, expressionKey)` — 이미 스테이지에 있는 캐릭터의 표정만 교체.
  - `GetActiveGroupCenterWorldX()` — 활성 캐릭터 전체의 스프라이트 좌우 끝 X값(world space) 평균을 반환. 캐릭터가 없으면 `Screen.width * 0.5f` 반환.
  - `CustomerSpawner`(영업 씬 손님)와 `EpisodeRunner`(에피소드) 모두 `CustomerStage` 하나를 공유합니다. 슬롯 5개.
- `CharacterSlotEntry` — `{ characterKey, expressionKey, slotIndex }` 세 필드. `slotIndex`가 0 이상이면 해당 인덱스 슬롯에 직접 배치, `-1`(기본값)이면 빈 슬롯에 자동 배정. 슬롯 인덱스: 0=Center, 1=Left, 2=Right, 3=Left2, 4=Right2, 5~8=Interaction0~3(통합 스프라이트 전용).
- `CharacterData` — 캐릭터 1명 = 파일 1개. `defaultSprite` + `defaultOverlaySprite` + `List<ExpressionEntry>` (`{ key, sprite, overlaySprite }`)로 모든 표정을 하나의 에셋에 보관.
  - `nameColor` — 대화창 이름 텍스트 색상. Inspector에서 캐릭터별로 지정. `overrideSpeakerName`이 있어도 항상 `speakerKey` 기준 색상이 적용됨.
  - `GetSprite(expressionKey)` — 표정 키로 스프라이트 조회(없으면 defaultSprite 반환).
  - `GetOverlaySprite(expressionKey)` — overlay 스프라이트 조회(없으면 defaultOverlaySprite 반환). overlay가 불필요한 캐릭터는 모든 overlay 필드를 비워두면 됨.
  - **주인공은 1인칭 시점이므로 스프라이트 없음.** `CharacterData`는 화자 이름 표시용으로만 사용하고, `EpisodeNode.characters`에는 포함하지 않습니다.

**4. 에피소드 (`Assets/Scripts/Conversation/Episode/`)**

`EpisodeRunner`가 `EpisodeData` 기반 에피소드를 오케스트레이션합니다:
1. `EpisodeBoardManager`에서 에피소드 선택 → `EpisodeManager.StartEpisode(id)` 호출
2. `GameManager.ChangeState(GameState.Episode)` → `SceneTransitionManager`가 BusinessScene 로드
3. 씬 로드 완료 콜백에서 `EpisodeRunner.Begin(EpisodeData)` 호출
4. `EpisodeRunner`가 `CustomerStage`, `DialogueController`, 선택지 UI를 구동
5. 에피소드 종료 시 `EpisodeManager.ClearEpisode(id)` → `GameManager.ChangeState(GameState.Rest)`

에피소드 조건 체크는 `EpisodeManager.CanStart(EpisodeData, GameProgress)` 에서 처리합니다 (`EpisodeTriggerCondition` 기반).

`EpisodeRunner` 입력 차단 플래그 (모두 `CanReceiveAdvanceInput`에 포함):
- `_waitingForCharacterAnim` — 캐릭터 등장 애니메이션 중 (오프닝 및 노드별)
- `_waitingForChoice` — 선택지 대기 중
- `_isTransitioning` — 선택지 슬라이드 업/다운 및 버튼 페이드 아웃 중
- `IsWaitingForChoice` (public) — `_waitingForChoice || _isTransitioning`. `InputRouter`가 이 값으로 `dialogue.Advance()` 폴스루를 차단함

캐릭터 포커스 pan: `ShowCharacters` 호출 직후 `StartPanCoroutine()`으로 1프레임 지연 코루틴을 시작해 `FrontCameraRig.PanToWorldCenterX(CharacterStage.GetActiveGroupCenterWorldX())`를 호출. 1프레임 지연은 ARF가 너비를 확정한 후 world bounds를 읽기 위함. 에피소드 종료 시 `cameraRig.ResetPan()` 호출.

선택지 표시 흐름: 말풍선 슬라이드 업 완료 후 버튼 생성. 버튼 크기 1500×80, 간격 20px. 선택 시 나머지 버튼은 즉시 투명 처리 후, 선택 버튼만 페이드 아웃(0.35s) → 슬라이드 백 → 다음 노드 진행.

`EpisodeData.openingCharacters: List<CharacterSlotEntry>` — 에피소드 시작 시 표시할 캐릭터+표정.
`EpisodeNode.characters: List<CharacterSlotEntry>` — 해당 노드에서 표시할 캐릭터+표정. 비어 있으면 스테이지 변경 없음.
`EpisodeNode` 제조 관련 필드:
- `requiresCrafting: bool` + `craftingTicketKey: string` — 노드 진입 시 `CraftingMode`로 전환
- `nextNodeIdGood: string` / `nextNodeIdBad: string` — 제조 결과에 따른 분기 노드 (비어 있으면 `nextNodeId` 사용)

`EpisodeNode` 분기 필드:
- `flagBranches: List<NodeFlagBranch>` — 플래그 조건 분기. `{ requiredFlag, nextNodeId }` 목록을 순서대로 확인해 처음 맞는 노드로 이동
- `varBranches: List<NodeVarBranch>` — 수치 변수 조건 분기. `{ VarCondition(varName, op, threshold), nextNodeId }` 목록을 순서대로 확인. `flagBranches` 이후에 평가됨
- 두 조건 모두 맞지 않으면 `nextNodeId`(기본 흐름) 사용

`EpisodeChoice` 필드:
- `setFlags` / `clearFlags` — 선택 시 플래그 변경
- `varChanges: List<VarChange>` — 선택 시 수치 변수 증감. `{ varName, delta }`

`EpisodeTriggerCondition` 필드:
- `requiredVars: List<VarCondition>` — 수치 변수 조건이 모두 충족되어야 에피소드 발동. `VarCondition`은 `varName`, `op`(`CompareOp` 열거형), `threshold` 보유

제조 완료는 `EpisodeRunner.NotifyCraftingCompleted(bool isGood)` 호출로 처리합니다. 현재는 `CraftingJudgeUI`의 GoodJob/BadJob 버튼으로 수동 판정합니다 (실제 제조 판정 미구현 상태의 임시 구현).

**5. 오디오 (`Assets/Scripts/Audio/`)**

`AudioManager` (`MonoSingleton<AudioManager>`). BGM 크로스페이드를 담당합니다:
- `PlayBgm(string clipName, float fadeDuration)` — `Resources/BGM/{clipName}` 클립을 로드해 재생. 이미 같은 클립이 재생 중이면 무시. 두 `AudioSource`(bgmSourceA/B)를 교대로 사용해 크로스페이드 처리
- `StopBgm(float fadeDuration)` — 현재 재생 중인 BGM을 페이드아웃 후 정지
- Inspector에서 `bgmSourceA` / `bgmSourceB`를 직접 연결하거나, 비워두면 자동 생성

`EpisodeNode` BGM 필드:
- `bgmCommand: BgmCommand` — `None`(변경 없음, 기본값) / `Play`(재생) / `Stop`(정지)
- `bgmClipName: string` — `Play`일 때만 사용. 확장자 없는 파일명 (`Resources/BGM/` 기준)

`EpisodeRunner`가 노드 진입 시 `ApplyBgmCommand()`를 호출해 `AudioManager`에 위임합니다.

BGM 클립은 `Assets/Resources/BGM/` 폴더에 배치해야 합니다.

**6. 영업 씬 손님 & 주문 (`Assets/Scripts/Conversation/Sell/`, `Assets/Scripts/OrderTicket/`)**

- `CustomerSpawner` — `CustomerOrderData.characterKey` + `expressionKeyMid`로 `CharacterStage`에 캐릭터 표시를 위임하고, 등장 완료 후 `DialogueController.StartDialogue()` 호출. `ShowFeedbackExpression(bool isGood)`으로 결과에 따라 표정 교체.
- `OrderTicketManager` — 두 가지 경로로 티켓을 표시. ① `OrderMode`: `DialogueClosed` 이벤트 수신 후 표시. ② `CraftingMode`: `OnModeChanged` 이벤트로 모드 전환 시점에 즉시 표시. 두 경우 모두 `dialogue.HideImmediate()`를 먼저 호출해 대화창을 닫습니다. `EpisodeMode` 진입 시 티켓 초기화.
- `OrderTicketUI` — `Show(data)` / `HideImmediate()` / `Toggle()`. E키 처리는 `InputRouter`가 담당.

`CustomerOrderData` 구조:
```
CustomerOrderData
 ├── key                  (주문 고유 식별자)
 ├── characterKey         (CharacterDatabase key)
 ├── expressionKeyMid     (기본 표정 key)
 ├── expressionKeyGood    (제조 성공 표정 key)
 ├── expressionKeyBad     (제조 실패 표정 key)
 ├── lines                (주문 대사)
 ├── feedbackLinesGood    (제조 성공 피드백 대사)
 └── feedbackLinesBad     (제조 실패 피드백 대사)
```
한 캐릭터가 여러 `CustomerOrderData`를 가질 수 있습니다 (랜덤 주문 선택 예정).

**6. 게임 진행 관리 (`Assets/Scripts/GameProgress.cs`)**

`MonoSingleton<GameProgress>`. 씬 전환과 무관하게 유지됩니다:
- 스토리 플래그: `SetFlag` / `HasFlag` / `ClearFlag`
- 수치 변수: `GetVar` / `SetVar` / `AddVar` — 호감도 등 정수형 전역 변수 관리
- 에피소드 완료 기록: `MarkEpisodeCompleted` / `IsEpisodeCompleted`
- 현재 게임 내 일 진행: `SetCurrentDay` / `CurrentDay`

**7. 드래그-드롭 바텐딩 (`Assets/Scripts/DragandDrop/`)**

- `ItemDef` (ScriptableObject): `ItemType`(Bottle, Glass, Tool), `dragMovesObject` 플래그
- `DragManager.Instance` 싱글톤: 드래그 상태와 고스트 이미지 관리
- `UIItemDraggable`: **Move** 모드(술병 — 원본 이동) / **Copy** 모드(잔/도구 — 원본 유지, 복사본 배치)
- `UIDropSlot`: 유효 드롭 타겟. Tool은 `toolPlaceableOnTable=true`인 경우만 허용
- `ShelfUI` / `DrawerUI`: `ItemDef` 배열에서 아이템 UI 생성
- `FrontCameraRig`: `ICameraInputHandler` 구현. `InputRouter`로부터 `CameraDirection` 명령 수신. `frontWorld` RectTransform을 이동시켜 화면 전체를 pan.
  - **에피소드 캐릭터 포커스**: `_focusX` 필드로 현재 X 오프셋(canvas units)을 추적. `PanToWorldCenterX(worldX)` — world X 좌표가 화면 중앙에 오도록 `_focusX`를 계산 후 이동. `ResetPan()` — `_focusX = 0`, 원점 복귀.
  - **서랍 열기/닫기**: `SetDrawer(true)` — `drawerArea.anchoredPosition.x = -_focusX`로 drawer를 화면 중앙에 배치 후 카메라는 Y축만 이동(`_focusX` 유지). `SetDrawer(false)` — pan 완료 콜백에서 `drawerArea.anchoredPosition.x = 0` 복원. pan 완료 콜백은 `BeginMove`의 `onComplete` 파라미터로 전달.
  - **주의**: `VisualOverlay`가 `frontContainer`(FrontCameraRig 자식)에 reparent된 이후에는 CharacterStage나 슬롯을 이동하면 Visual/Overlay가 desynced됨. 수평 pan은 반드시 `FrontCameraRig` 이동으로만 처리할 것. 배경/바 테이블 에셋은 화면보다 넓어야 함(와이드 에셋 필요, 현재 미완).

**8. 대화 렌더링 (`Assets/Scripts/Conversation/DialogueController.cs`)**

TMPro 타이핑 애니메이션. 모든 모드에서 공유하는 단일 컴포넌트입니다:
- `StartDialogue(List<DialogueLine>)` — 손님 대화용 배치 큐 방식
- `ShowSingleLine(speakerName, text, nameColor)` — 에피소드 노드별 단일 출력. `nameColor`는 `CharacterData.nameColor`에서 전달됨
- `SkipTypingIfNeeded()` — 타이핑 스킵 (InputRouter → EncounterRunner → DialogueController 경로)
- `SlideUpForChoices(float amount, Action onComplete)` — 선택지 표시 시 말풍선을 위로 이동 (smoothstep)
- `SlideBackToOrigin(Action onComplete)` — 선택지 해제 후 말풍선 원위치 복귀
- `DialogueClosed` 이벤트 — 대화 종료 시 발행
- Inspector: `bubbleRect` — 슬라이드 애니메이션 대상 RectTransform. `ChoiceContainer`는 `bubbleRect`의 자식으로 배치해야 함 (bubble 이동 시 함께 이동)

## 데이터 패턴

모든 컨텐츠는 `Assets/Data/`에 ScriptableObject 데이터베이스로 저장됩니다. 런타임에 string key로 조회합니다. 새 컨텐츠를 추가하려면 ScriptableObject 에셋을 만들고 해당 Database 에셋의 리스트에 등록하세요.

| 데이터 | 파일 | 로드 방식 |
|---|---|---|
| 캐릭터 | `CharacterData` | `CharacterDatabase` (string key 조회) |
| 에피소드 | `EpisodeData` | `Resources.LoadAll<EpisodeData>("EpisodeData")` — `EpisodeManager`가 시작 시 일괄 로드 |
| 손님 주문 | `CustomerOrderData` | `CustomerOrderDatabase` |
| 주문표 | `OrderTicketData` | `OrderTicketDatabase` |
| 아이템 | `ItemDef` | `ShelfUI.items` / `DrawerUI.items` 배열 |

## 주요 설계 패턴

- **씬 분리 + GameState 상태머신**: `GameManager.ChangeState()`가 `SceneTransitionManager`를 통해 씬 전환 처리. BusinessScene(에피소드) ↔ RestScene 전환
- **씬 내 GameMode 상태머신**: `GameModeManager`가 패널 활성/비활성으로 씬 내 모드 전환 (씬 전환 없음)
- **MonoSingleton<T>**: `GameProgress`, `GameModeManager`, `EpisodeManager`, `DataManager`, `GameManager`, `AudioManager` 모두 통일
- **이벤트 기반 연결**: `DialogueController.DialogueClosed`, `GameModeManager.OnModeChanged`
- **ScriptableObject 데이터베이스**: 모든 게임 컨텐츠를 에디터에서 구성, 하드코딩 없음
- **인터페이스 기반 입력**: `IDialogueAdvanceHandler`, `ICameraInputHandler` — 수신자가 InputRouter에 의존하지 않음
