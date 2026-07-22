# 영업 씬 상호작용

## Business order vertical slice

The first integrated order flow is implemented under `Assets/Scripts/Business/`.

- `BusinessFlowBootstrap` resolves scene-local dependencies and starts the saved business sequence when no episode is active.
- `BusinessSequencePlanner` creates the fixed day snapshot. `BusinessSequenceRunner` advances it and calls `DataManager.Save()` only at order boundaries.
- `BusinessOrderSessionController` owns presentation, decision, crafting, evaluation, feedback, reward, cleanup, and completion.
- `BusinessOrderSessionUI` creates Accept, Reject, Serve, Discard, and Abandon controls at runtime.
- `BusinessOrderFlowSettings` contains fixed orders, Good/Mid/Bad thresholds, rewards, and fallback feedback.
- `BusinessBartendingBootstrap` creates recipe bottles from authoritative `ItemDef` assets. Bottle amounts update `GameProgress`; discarding does not restore used ingredients.
- `BusinessFlowSceneSetup` creates the settings/sample assets and connects `BusinessFlowBootstrap` to `BusinessScene` through the Unity Editor API.

Current sample: `vertical_slice_vodka_lemon` requests `vodka_lemon` with Breeze Vodka 50 ml and Lemon Juice 30 ml.

## 영업 씬 손님 & 주문 (`Assets/Scripts/Conversation/Sell/`, `Assets/Scripts/OrderTicket/`)

- `CustomerSpawner` — `CustomerOrderData.characterKey` + `expressionKeyMid`로 `CharacterStage`에 캐릭터 표시를 위임하고, 등장 완료 후 `DialogueController.StartDialogue()` 호출. `ShowFeedbackExpression(bool isGood)`으로 결과에 따라 표정 교체.
- `OrderTicketManager` — 티켓 데이터를 채워 표시하는 두 경로. ① `OrderMode`: `DialogueClosed` 이벤트 수신 후 `Show(data)`. ② `CraftingMode`: `OnModeChanged` 이벤트로 모드 전환 시점에 즉시 `Show(data)`. 두 경우 모두 `dialogue.HideImmediate()`를 먼저 호출해 대화창을 닫습니다. `EpisodeMode` 진입 시 `_pendingTicketKey` 초기화 + `ticketUI.HideAnimated()` 호출(슬라이드로 닫힘, 비활성화는 아님). `ToggleTicket()` — E키/버튼 토글 진입점, `_pendingTicketKey`가 없으면 무시(빈 티켓 노출 방지).
- `OrderTicketUI` / `RecipeBookUI` — 동일한 구조. `gameObject.SetActive`를 쓰지 않고 `anchoredPosition` 슬라이드 + `CanvasGroup`(또는 sprite) 전환만으로 open/close를 표현해 패널이 화면에서 완전히 사라지지 않습니다. 내부 상태는 `Closed`/`Open` 2-state. 공개 API: `Toggle()`(슬라이드 중에는 무시), `Open()`(이미 열려있으면 무시), `SetInteractable(bool)`(EpisodeMode 잠금 — `false`면 버튼 비활성화 + 열려있으면 닫기). 둘 다 `spriteOpen`/`spriteClosed`를 토글 버튼의 `buttonImage`(토글 버튼 자식의 Image, 패널 배경 Image와는 별개)에 적용해 open/close 상태를 표시. 패널 배경 Image는 상태와 무관하게 고정 스프라이트이며, 실제 그림 영역만큼 트림되어 있어야 함(풀스크린 anchor-stretch로 두면 raycastTarget이 화면 전체의 클릭을 막아버림). 에셋: `Sprites/UI/recipe_book/recipe_off.png`(닫힘) / `recipe_on.png`(열림), `Sprites/UI/order_ticket/order_screen.png`(패널 배경).

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
