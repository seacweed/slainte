# 영업 씬 상호작용

## 영업 주문 기본 흐름

통합 주문 흐름은 `Assets/_Project/Features/Business/Runtime/Flow/`에 구현되어 있습니다.

- `BusinessFlowBootstrap`는 씬 안의 의존성을 연결하고, 진행 중인 에피소드가 없으면 시간 기반 영업을 시작합니다.
- `BusinessShiftController`는 기본 180초의 남은 영업시간, 손님별 쿨다운, 필수 손님·인카운터, 정산 진입을 관리합니다. 주문 중에는 시간이 흐르고 영업 인카운터 중에는 영업시간과 쿨다운이 함께 멈춥니다.
- `BusinessOrderSessionController`는 주문 제시, 자동 제조 진입, 판정, 반응 대사, 보상, 정리, 완료 처리를 담당합니다.
- `BusinessOrderSessionUI`는 조작 없는 상태·결과 문구만 표시하며 주문 행동 버튼을 만들지 않습니다.
- `BusinessOrderFlowSettings`에는 영업 제한시간, 손님 풀, 필수 영업 액션, Good·Mid·Bad 판정 기준, 보상(돈·명성), 기본 반응 대사가 있습니다.
- `BusinessBartendingBootstrap`는 도구만 놓인 상태로 시작합니다. `LiquorBottleSlotUI`를 왼쪽 클릭하면 같은 ID의 `ItemDef`를 찾아 가장 오른쪽의 빈 테이블 슬롯부터 술병을 배치합니다.
- 씬의 `TableSlots`는 숨겨진 배치 틀입니다. 제조 중에만 화면상 테이블 영역에 맞춘 임시 배치와 월드 충돌 슬롯을 만들고, 제조가 끝나면 함께 제거합니다.
- `GlassController`는 플레이어가 잔을 끌어 전방 기준선을 넘겼을 때만 제출을 요청합니다. 주문 대사가 끝나면 바로 제조로 진입하며 거절, 포기, 버리기, 제출 버튼은 제공하지 않습니다.
- 술병 오브젝트는 현재 병의 잔량을 추적하고 `GameProgress`는 전체 재고를 저장합니다. 제출해도 사용한 양은 복구되지 않습니다.

### 판매 보상 공식 (`BusinessOrderRewardCalculator.Calculate`)

`listedPrice`(주문 레시피의 정가)를 기준으로 Good·Mid·Bad 모두 판매 수익 자체는 항상 전액이 즉시 지급됩니다. Bad일 때만 추가로 실수 페널티가 차감되어 순손실이 납니다.

| 등급 | 판매 수익 | 팁 | 실수 페널티 | 순수익 |
|---|---|---|---|---|
| Good | `listedPrice` | `round(listedPrice × satisfiedTipRate)`(기본 30%) | — | 약 130% |
| Mid | `listedPrice` | 없음(`neutralTipRate` 기본 0%) | — | 100% |
| Bad | `listedPrice` | 없음 | `round(listedPrice × badPenaltyRate)`(기본 130%) | 약 -30% |

- 모든 소수점은 반올림(`Mathf.RoundToInt`).
- Bad 페널티는 판매 수익이 지급된 직후 잔액을 기준으로 차감되며, 소지금이 페널티보다 적으면 0 밑으로 내려가지 않도록 `min(지급 직후 소지금, 페널티)`로 클램프됩니다.
- 비율은 `Assets/Resources/Business/BusinessOrderFlowSettings.asset`에서 조정합니다.
- 정산 화면에는 "총 판매량"(전체 건수+`listedPrice` 합) / "팁"(Good 건수+팁 합) / "실수"(Bad 건수+`listedPrice`+실제 차감액 합)로 요약 표시됩니다 — 자세한 화면 구성은 [corescene-systems.md](../core/corescene-systems.md#settlementmanager--settlementui-coreScenescriptssettlementmanagercs-settlementuics) 참고.

### 시간 기반 손님 진행

`BusinessShiftController`는 영업 시작 시 진행 조건을 통과한 일반 손님 풀을 고정하고, 주문 사이마다 재등장 제한을 통과한 손님 중 하나를 가중치로 추첨합니다(`BusinessSequencePlanner.PickWeightedSequence`). 재등장 제한은 초 단위 쿨다운이 아니라 **최근 등장 손님 최대 2명**을 `Queue`+`HashSet`(`recentCustomerKeySet`)으로 기억해 다음 추첨 후보에서만 제외하는 방식입니다. 유효 후보가 이 제한 때문에 모두 소진되면(예: 서로 다른 손님이 2명뿐인 풀) 쿨다운을 무시하는 폴백 없이 해당 영업의 무작위 손님 생성을 멈춥니다(`StopRandomCustomerSpawning()`) — 단, 남은 영업시간과 진행 중인 주문·필수 액션은 그대로 이어집니다. 손님 수 제한은 없으며 제한시간 동안 주문이 계속 이어집니다. 필수 손님과 필수 인카운터는 `BusinessOrderFlowSettings.requiredActions`에서 일차·진행 조건과 `priority`를 지정합니다. 제한시간이 끝나면 일반 손님은 더 생성하지 않지만 진행 중인 주문과 남은 필수 액션은 모두 마친 뒤 정산합니다.

영업 인카운터는 `EpisodeRunner`를 같은 `BusinessScene` 안에서 실행합니다. 시작 전에 주문 세션이 끝난 상태임을 보장하고, 완료 시 일반 `DayFlowController.OnEpisodeCompleted()` 경로를 타지 않고 남아 있는 영업시간으로 복귀합니다.

에피소드 제조 노드에 직렬화된 `craftingOrderTarget`(레시피 ID 또는 맛/분위기 태그)이 있으면 `EpisodeCraftingBridge`가 일반 영업과 같은 실제 제조·판정 세션을 실행합니다. 실제 제조 중에는 기존 수동 `CraftingJudgeUI`를 숨기며, 레시피·제조 화면·잔 추적기 등 기술적인 초기화가 실패한 경우에만 같은 노드의 수동 판정으로 폴백합니다. `craftingOrderTarget`이 없는(또는 팔레트에 없는 오타 태그라 레시피 조회에 실패하는) 제조 노드는 이 수동 판정 경로를 탑니다. 상세 Mid 분기가 없으면 기존 Bad 분기를 사용하며, 에피소드 제조는 판매 수익이나 손님 쿨다운에 포함되지 않습니다.

## 에피소드 제조 노드 판정 (`EpisodeCraftingBridge`)

`EpisodeRunner.HandleCraftingNode()`가 노드에 `craftingOrderTarget`이 있고 브리지가 주입돼 있으면 `EpisodeCraftingBridge.TryStart()`를 먼저 시도합니다. 브리지는 `craftingOrderTarget` 값을 `TasteMoodTagPaletteDef`(`Assets/Resources/Bartending/Recipes/TasteMoodPalette.asset`)에 대조해 맛/분위기 태그면 `TasteOrder`/`MoodOrder`, 아니면 레시피 ID로 보고 `EpisodeOrder`로 자동 판별(`ResolveOrderType()`, CSV에 별도 타입 컬럼 없음)한 뒤 `OrderSessionRequest`(`owner = Episode`, `applyProgressRewards = false`, `clearCustomerOnComplete = false` 등 영업과 분리된 옵션)를 만들어 `BusinessFlowBootstrap.StartEpisodeOrder()`로 영업과 동일한 `BusinessOrderSessionController` 판정 세션을 실행합니다.

- **결과 매핑** (`CraftingResultMapper.Map()`): `BusinessOrderSessionResult` → `CraftingJobResult`(`Good` / `Bad` / `MidWrongMenu` / `MidIce` / `MidGlass` / `MidIceGlass`). 요청한 레시피가 아니라 다른 성공 레시피가 감지되면 `MidWrongMenu`, 요청 레시피 계열(`baseRecipeId` 포함)은 맞지만 얼음·잔 조건만 어긋나면 `MidIce`/`MidGlass`/`MidIceGlass`로 세분화합니다.
- **분기 반영**: `EpisodeRunner.GoToNextFromCrafting()`이 `EpisodeNode.GetCraftingFlag(result)`/`GetCraftingVarChanges(result)`/`GetNextNodeId(result)`로 결과별 플래그·변수·다음 노드를 조회합니다. `craftingOutcomes`(신규)가 채워져 있으면 우선 사용하고, 비어 있으면 레거시 필드(`craftingFlagGood/Bad`, `nextNodeIdGood/Bad` 등)로 폴백합니다 — 상세 Mid 분기가 없는 기존 데이터는 자동으로 Bad 분기를 탑니다.
- **수동 판정 폴백**: 레시피·제조 화면·잔 추적기 등 기술적인 초기화가 실패했을 때(`onTechnicalFailure`)만 같은 노드를 `BeginManualCrafting()`으로 전환해 기존 `OrderTicketManager.Prepare(node.craftingTicketKey)` + `GameModeManager.RequestModeChange(GameMode.CraftingMode)` + `CraftingJudgeUI` 6버튼 수동 판정으로 진행합니다. `craftingOrderTarget`이 비어 있는 제조 노드는 처음부터 이 수동 경로만 사용합니다. `CraftingJudgeUI`는 `EpisodeRunner.IsUsingManualCrafting`이 true이고 `CraftingMode`일 때만 노출되는 레거시 디버그 패널이라, 태그 오타로 레시피 조회가 실패해도 같은 패널이 뜬다 — 의도치 않게 이 패널이 보이면 `craftingOrderTarget` 값이 팔레트/레시피 카탈로그에 정확히 등록된 문자열인지부터 확인할 것.
- 에피소드 제조는 `applyProgressRewards = false`이므로 판매 수익이나 손님 재등장 제한에 포함되지 않습니다.

## 영업 씬 손님 & 주문 (`Assets/_Project/Features/Business/Runtime/Conversation/Sell/`, `Assets/_Project/Features/Business/Runtime/OrderTicket/`)

- `CustomerSpawner` — `CustomerVisitData.members` 전원을 `CharacterStage`에 표시하고, 등장 완료 후 선택된 `CustomerOrderData`의 대사를 시작합니다. 결과가 나오면 각 구성원의 Good·Mid·Bad 표정으로 교체합니다. 방문 키가 없는 이전 데이터는 `CustomerOrderData.characterKey`를 호환 경로로 사용합니다.
- `OrderTicketManager` — 티켓 데이터를 채워 표시하는 두 경로. ① `OrderMode`: `DialogueClosed` 이벤트 수신 후 `Show(data)`. ② `CraftingMode`: `OnModeChanged` 이벤트로 모드 전환 시점에 즉시 `Show(data)`. 두 경우 모두 `dialogue.HideImmediate()`를 먼저 호출해 대화창을 닫습니다. `EpisodeMode` 진입 시 `_pendingTicketKey` 초기화 + `ticketUI.HideAnimated()` 호출(슬라이드로 닫힘, 비활성화는 아님). `ToggleTicket()` — Tab키/버튼 토글 진입점, `_pendingTicketKey`가 없으면 무시(빈 티켓 노출 방지).
- `OrderTicketUI` / `RecipeBookUI` — 동일한 구조. `gameObject.SetActive`를 쓰지 않고 `anchoredPosition` 슬라이드 + `CanvasGroup`(또는 sprite) 전환만으로 open/close를 표현해 패널이 화면에서 완전히 사라지지 않습니다. 내부 상태는 `Closed`/`Open` 2-state. 공개 API: `Toggle()`(슬라이드 중에는 무시), `Open()`(이미 열려있으면 무시), `SetInteractable(bool)`(EpisodeMode 잠금 — `false`면 버튼 비활성화 + 열려있으면 닫기). 둘 다 `spriteOpen`/`spriteClosed`를 토글 버튼의 `buttonImage`(토글 버튼 자식의 Image, 패널 배경 Image와는 별개)에 적용해 open/close 상태를 표시. 패널 배경 Image는 상태와 무관하게 고정 스프라이트이며, 실제 그림 영역만큼 트림되어 있어야 함(풀스크린 anchor-stretch로 두면 raycastTarget이 화면 전체의 클릭을 막아버림). 에셋: `Sprites/UI/recipe_book/recipe_off.png`(닫힘) / `recipe_on.png`(열림), `Sprites/UI/order_ticket/order_screen.png`(패널 배경).

손님 풀 구조:
```
CustomerVisitData
 ├── visitKey                  (방문 고유 식별자)
 ├── tags                      (검색·연출용 보조 정보)
 ├── preferredTasteKey         (기획 CSV 원문 보존, 맛 조건 주문의 후보 연결용)
 ├── preferredAtmosphereKey    (기획 CSV 원문 보존, 분위기 조건 주문의 후보 연결용)
 ├── members[]                 (캐릭터 키, 슬롯, 결과별 표정)
 ├── weight                    (방문 추첨 가중치)
 ├── condition                 (날짜·플래그·변수·완료 에피소드 조건)
 ├── initiallyAvailable        (시작 시 활성 여부)
 ├── availabilityTransitions[] (조건 충족 시 활성/비활성으로 전환)
 ├── maxDay
 ├── reappearanceGroupKey      (재등장 제한을 공유할 키, 비어있으면 visitKey 사용 — `GetReappearanceKey()`)
 └── orders[]                  (주문 데이터, 가중치, 조건)

CustomerOrderData
 ├── key                  (주문 고유 식별자)
 ├── requestedRecipeId    (화면에 표시하지 않는 내부 판정 ID)
 ├── orderType            (레시피 지정 등 주문 판정 유형)
 ├── orderDialogueAuthored (켜져 있고 lines가 비어있으면 주문 대사를 의도적으로 생략)
 ├── lines                (주문 대사)
 ├── feedbackLinesGood    (제조 성공 피드백 대사)
 ├── feedbackLinesMid     (중간 결과 피드백 대사)
 └── feedbackLinesBad     (제조 실패 피드백 대사)
```

커플과 단체는 별도 유형으로 나누지 않습니다. `members`가 한 명이면 1인 방문, 두 명이면 커플이나 2인 방문, 세 명 이상이면 단체 방문으로 자연스럽게 표현됩니다. 런타임은 인원 유형이 아니라 구성원 목록만 처리합니다.

일반 손님 풀의 진행 조건(`condition`, `initiallyAvailable`+`availabilityTransitions`)은 영업 시작 시 평가합니다. 주문이 끝나면 `RecordRecentCustomer()`가 `GetReappearanceKey()` 값을 최근 등장 큐에 기록합니다(위 "시간 기반 손님 진행" 참고). 주문 후보 조건과 필수 액션 조건은 주문 사이의 안전 구간에서 다시 평가하므로 인카운터가 설정한 플래그나 완료 에피소드로 같은 날 후속 필수 액션을 열 수 있습니다.

### 주문 유효성 검증

`CocktailOrderGenerator.CanGenerateOrder(recipeId)`는 실제 주문 생성 전에 요청 레시피 ID가 카탈로그에서 해석되는지 확인합니다. 영업 시작 시 이 검증을 통과하지 못하는 방문은 그날의 고정 손님 풀(`frozenCustomerPool`)에서 제외됩니다. `CustomerSpawner.ShowVisit(...)`는 `bool`을 반환해 정상적인 플레이 실패(예: 조건 불충족)와 데이터 누락 같은 기술적 실패를 서로 다른 로그 심각도로 구분합니다.

### 주문표 대사 데이터 흐름

`BusinessOrderSessionController`가 제조 화면(CraftingMode)으로 진입할 때, 현재 주문이 실제 손님 주문(`CustomerOrderData`)인지 확인한 뒤 `OrderTicketMemoFormatter.Build()`로 해당 주문의 `lines`를 주문표 본문으로 조립해 `OrderTicketManager.Prepare(ticketKey, memoOverride)`에 전달합니다. `orderDialogueAuthored`가 켜져 있고 `lines`가 비어있는 주문은 칵테일명 등으로 폴백하지 않고 빈 본문을 그대로 유지하는 것이 현재 데이터 계약입니다 — 대사가 의도적으로 비어있는 주문임을 뜻합니다.

## 드래그-드롭 바텐딩 (`Assets/_Project/Features/Bartending/Runtime/Interaction/`)

- `ItemDef` (ScriptableObject): `ItemType`(Bottle, Glass, Tool), `dragMovesObject` 플래그
- `DragManager.Instance` 싱글톤: 드래그 상태와 고스트 이미지 관리
- `UIItemDraggable`: **Move** 모드(술병 — 원본 이동) / **Copy** 모드(잔/도구 — 원본 유지, 복사본 배치)
- `UIDropSlot`: 유효 드롭 타겟. Tool은 `toolPlaceableOnTable=true`인 경우만 허용
- `ShelfUI` / `DrawerUI`: `ItemDef` 배열에서 아이템 UI 생성
- `FrontCameraRig`: `ICameraInputHandler` 구현. `InputRouter`로부터 `CameraDirection` 명령 수신. `frontWorld` RectTransform을 이동시켜 화면 전체를 pan.
  - **에피소드 캐릭터 포커스**: `_focusX` 필드로 현재 X 오프셋(canvas units)을 추적. `PanToWorldCenterX(worldX)` — world X 좌표가 화면 중앙에 오도록 `_focusX`를 계산 후 이동. `ResetPan()` — `_focusX = 0`, 원점 복귀.
  - **서랍 열기/닫기**: `SetDrawer(true)` — `drawerArea.anchoredPosition.x = -_focusX`로 drawer를 화면 중앙에 배치 후 카메라는 Y축만 이동(`_focusX` 유지). `SetDrawer(false)` — pan 완료 콜백에서 `drawerArea.anchoredPosition.x = 0` 복원. pan 완료 콜백은 `BeginMove`의 `onComplete` 파라미터로 전달.
  - **주의**: `VisualOverlay`가 `frontContainer`(FrontCameraRig 자식)에 reparent된 이후에는 CharacterStage나 슬롯을 이동하면 Visual/Overlay가 desynced됨. 수평 pan은 반드시 `FrontCameraRig` 이동으로만 처리할 것. 배경/바 테이블 에셋은 화면보다 넓어야 함(와이드 에셋 필요, 현재 미완).
