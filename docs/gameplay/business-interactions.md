# 영업 씬 상호작용

## 영업 주문 기본 흐름

통합 주문 흐름은 `Assets/Scripts/Business/`에 구현되어 있습니다.

- `BusinessFlowBootstrap`는 씬 안의 의존성을 연결하고, 진행 중인 에피소드가 없으면 시간 기반 영업을 시작합니다.
- `BusinessShiftController`는 기본 180초의 남은 영업시간, 손님별 쿨다운, 필수 손님·인카운터, 정산 진입을 관리합니다. 주문 중에는 시간이 흐르고 영업 인카운터 중에는 영업시간과 쿨다운이 함께 멈춥니다.
- `BusinessOrderSessionController`는 주문 제시, 자동 제조 진입, 판정, 반응 대사, 보상, 정리, 완료 처리를 담당합니다.
- `BusinessOrderSessionUI`는 조작 없는 상태·결과 문구만 표시하며 주문 행동 버튼을 만들지 않습니다.
- `BusinessOrderFlowSettings`에는 영업 제한시간, 손님 풀, 필수 영업 액션, Good·Mid·Bad 판정 기준, 보상(돈·명성), 기본 반응 대사가 있습니다.
- `BusinessBartendingBootstrap`는 도구만 놓인 상태로 시작합니다. `LiquorBottleSlotUI`를 왼쪽 클릭하면 같은 ID의 `ItemDef`를 찾아 가장 오른쪽의 빈 테이블 슬롯부터 술병을 배치합니다.
- 씬의 `TableSlots`는 숨겨진 배치 틀입니다. 제조 중에만 화면상 테이블 영역에 맞춘 임시 배치와 월드 충돌 슬롯을 만들고, 제조가 끝나면 함께 제거합니다.
- `GlassController`는 플레이어가 잔을 끌어 전방 기준선을 넘겼을 때만 제출을 요청합니다. 주문 대사가 끝나면 바로 제조로 진입하며 거절, 포기, 버리기, 제출 버튼은 제공하지 않습니다.
- 술병 오브젝트는 현재 병의 잔량을 추적하고 `GameProgress`는 전체 재고를 저장합니다. 제출해도 사용한 양은 복구되지 않습니다.

### 시간 기반 손님 진행

`BusinessShiftController`는 영업 시작 시 진행 조건을 통과한 일반 손님 풀을 고정하고, 주문 사이마다 쿨다운이 끝난 손님 중 하나를 가중치로 추첨합니다. 모든 유효 손님이 쿨다운 중이면 영업이 멈추지 않도록 쿨다운을 이번 추첨에만 무시하고 전체 유효 후보 중 하나를 같은 가중치 방식으로 선택합니다. 손님 수 제한은 없으며 제한시간 동안 주문이 계속 이어집니다. 필수 손님과 필수 인카운터는 `BusinessOrderFlowSettings.requiredActions`에서 일차·진행 조건과 `priority`를 지정합니다. 제한시간이 끝나면 일반 손님은 더 생성하지 않지만 진행 중인 주문과 남은 필수 액션은 모두 마친 뒤 정산합니다.

영업 인카운터는 `EpisodeRunner`를 같은 `BusinessScene` 안에서 실행합니다. 시작 전에 주문 세션이 끝난 상태임을 보장하고, 완료 시 일반 `DayFlowController.OnEpisodeCompleted()` 경로를 타지 않고 남아 있는 영업시간으로 복귀합니다.

에피소드 제조 노드에 직렬화된 `craftingRecipeId`가 있으면 `EpisodeCraftingBridge`가 일반 영업과 같은 실제 제조·판정 세션을 실행합니다. 실제 제조 중에는 기존 수동 `CraftingJudgeUI`를 숨기며, 레시피·제조 화면·잔 추적기 등 기술적인 초기화가 실패한 경우에만 같은 노드의 수동 판정으로 폴백합니다. `craftingRecipeId`가 없는 기존 제조 노드는 처음부터 수동 판정 경로를 유지합니다. 상세 Mid 분기가 없으면 기존 Bad 분기를 사용하며, 에피소드 제조는 판매 수익이나 손님 쿨다운에 포함되지 않습니다.

## 에피소드 제조 노드 (현재: 수동 판정)

- 에피소드 제조 노드는 `EpisodeRunner.HandleCraftingStart()`에서 처리되며, 기존 방식대로 `OrderTicketManager.Prepare(node.craftingTicketKey)` + `GameModeManager.RequestModeChange(GameMode.CraftingMode)`로 진입해 `CraftingJudgeUI`의 버튼 6개로 수동 판정합니다.
- 영업과 같은 `BusinessOrderSessionController`(레시피 기반 자동 판정, `BusinessFlowBootstrap.StartEpisodeOrder()` 경유)로 연동해서 수동 판정을 대체하려던 시도가 있었으나(그래프 노드에 `craftingRecipeId` 컬럼과 `CraftingRecipeId` 필드 추가) 컴파일 문제로 되돌려졌습니다. 재통합 예정입니다.
- 그 흔적으로 `StrangeCoin_0.asset`/`StrangeCoin_1.asset` 그래프에는 `CraftingRecipeId: vodka_lemon`/`whiskey_neat` 값이 여전히 남아 있고, `EpisodeCsvImporter.cs`도 CSV의 `craftingRecipeId` 컬럼을 계속 파싱합니다. 하지만 `EpisodeNode`/`EpisodeEventData` 클래스엔 이 필드가 없어서 지금은 그래프를 저장할 때마다 버려지는 고아 값입니다. 재통합 시 `EpisodeNode.craftingRecipeId` 필드부터 다시 추가해야 합니다.

## 영업 씬 손님 & 주문 (`Assets/Scripts/Conversation/Sell/`, `Assets/Scripts/OrderTicket/`)

- `CustomerSpawner` — `CustomerVisitData.members` 전원을 `CharacterStage`에 표시하고, 등장 완료 후 선택된 `CustomerOrderData`의 대사를 시작합니다. 결과가 나오면 각 구성원의 Good·Mid·Bad 표정으로 교체합니다. 방문 키가 없는 이전 데이터는 `CustomerOrderData.characterKey`를 호환 경로로 사용합니다.
- `OrderTicketManager` — 티켓 데이터를 채워 표시하는 두 경로. ① `OrderMode`: `DialogueClosed` 이벤트 수신 후 `Show(data)`. ② `CraftingMode`: `OnModeChanged` 이벤트로 모드 전환 시점에 즉시 `Show(data)`. 두 경우 모두 `dialogue.HideImmediate()`를 먼저 호출해 대화창을 닫습니다. `EpisodeMode` 진입 시 `_pendingTicketKey` 초기화 + `ticketUI.HideAnimated()` 호출(슬라이드로 닫힘, 비활성화는 아님). `ToggleTicket()` — E키/버튼 토글 진입점, `_pendingTicketKey`가 없으면 무시(빈 티켓 노출 방지).
- `OrderTicketUI` / `RecipeBookUI` — 동일한 구조. `gameObject.SetActive`를 쓰지 않고 `anchoredPosition` 슬라이드 + `CanvasGroup`(또는 sprite) 전환만으로 open/close를 표현해 패널이 화면에서 완전히 사라지지 않습니다. 내부 상태는 `Closed`/`Open` 2-state. 공개 API: `Toggle()`(슬라이드 중에는 무시), `Open()`(이미 열려있으면 무시), `SetInteractable(bool)`(EpisodeMode 잠금 — `false`면 버튼 비활성화 + 열려있으면 닫기). 둘 다 `spriteOpen`/`spriteClosed`를 토글 버튼의 `buttonImage`(토글 버튼 자식의 Image, 패널 배경 Image와는 별개)에 적용해 open/close 상태를 표시. 패널 배경 Image는 상태와 무관하게 고정 스프라이트이며, 실제 그림 영역만큼 트림되어 있어야 함(풀스크린 anchor-stretch로 두면 raycastTarget이 화면 전체의 클릭을 막아버림). 에셋: `Sprites/UI/recipe_book/recipe_off.png`(닫힘) / `recipe_on.png`(열림), `Sprites/UI/order_ticket/order_screen.png`(패널 배경).

손님 풀 구조:
```
CustomerVisitData
 ├── visitKey             (방문 고유 식별자)
 ├── tags                 (검색·연출용 보조 정보)
 ├── members[]            (캐릭터 키, 슬롯, 결과별 표정)
 ├── weight               (방문 추첨 가중치)
 ├── condition            (날짜·플래그·변수·완료 에피소드 조건)
 ├── maxDay
 ├── cooldownSeconds       (주문 완료 뒤 재등장까지의 유효 영업시간, 기본 100초)
 └── orders[]             (주문 데이터, 가중치, 조건)

CustomerOrderData
 ├── key                  (주문 고유 식별자)
 ├── requestedRecipeId    (화면에 표시하지 않는 내부 판정 ID)
 ├── orderType            (레시피 지정 등 주문 판정 유형)
 ├── lines                (주문 대사)
 ├── feedbackLinesGood    (제조 성공 피드백 대사)
 ├── feedbackLinesMid     (중간 결과 피드백 대사)
 └── feedbackLinesBad     (제조 실패 피드백 대사)
```

커플과 단체는 별도 유형으로 나누지 않습니다. `members`가 한 명이면 1인 방문, 두 명이면 커플이나 2인 방문, 세 명 이상이면 단체 방문으로 자연스럽게 표현됩니다. 런타임은 인원 유형이 아니라 구성원 목록만 처리합니다.

일반 손님 풀의 진행 조건은 영업 시작 시 평가합니다. 주문이 끝나면 해당 방문에 `cooldownSeconds`를 적용하고, 쿨다운은 인카운터와 게임 일시정지 중에는 흐르지 않습니다. 주문 후보 조건과 필수 액션 조건은 주문 사이의 안전 구간에서 다시 평가하므로 인카운터가 설정한 플래그나 완료 에피소드로 같은 날 후속 필수 액션을 열 수 있습니다.

## 드래그-드롭 바텐딩 (`Assets/Scripts/DragandDrop/`)

- `ItemDef` (ScriptableObject): `ItemType`(Bottle, Glass, Tool), `dragMovesObject` 플래그
- `DragManager.Instance` 싱글톤: 드래그 상태와 고스트 이미지 관리
- `UIItemDraggable`: **Move** 모드(술병 — 원본 이동) / **Copy** 모드(잔/도구 — 원본 유지, 복사본 배치)
- `UIDropSlot`: 유효 드롭 타겟. Tool은 `toolPlaceableOnTable=true`인 경우만 허용
- `ShelfUI` / `DrawerUI`: `ItemDef` 배열에서 아이템 UI 생성
- `FrontCameraRig`: `ICameraInputHandler` 구현. `InputRouter`로부터 `CameraDirection` 명령 수신. `frontWorld` RectTransform을 이동시켜 화면 전체를 pan.
  - **에피소드 캐릭터 포커스**: `_focusX` 필드로 현재 X 오프셋(canvas units)을 추적. `PanToWorldCenterX(worldX)` — world X 좌표가 화면 중앙에 오도록 `_focusX`를 계산 후 이동. `ResetPan()` — `_focusX = 0`, 원점 복귀.
  - **서랍 열기/닫기**: `SetDrawer(true)` — `drawerArea.anchoredPosition.x = -_focusX`로 drawer를 화면 중앙에 배치 후 카메라는 Y축만 이동(`_focusX` 유지). `SetDrawer(false)` — pan 완료 콜백에서 `drawerArea.anchoredPosition.x = 0` 복원. pan 완료 콜백은 `BeginMove`의 `onComplete` 파라미터로 전달.
  - **주의**: `VisualOverlay`가 `frontContainer`(FrontCameraRig 자식)에 reparent된 이후에는 CharacterStage나 슬롯을 이동하면 Visual/Overlay가 desynced됨. 수평 pan은 반드시 `FrontCameraRig` 이동으로만 처리할 것. 배경/바 테이블 에셋은 화면보다 넓어야 함(와이드 에셋 필요, 현재 미완).
