# 씬 구조

기준일: 2026-08-09

## 1. Build Settings

| 순서 | 씬 | 역할 |
|---:|---|---|
| 0 | `Assets/_Project/Scenes/Production/MainMenuScene.unity` | 새 게임 진입 |
| 1 | `Assets/_Project/Scenes/Production/CoreScene.unity` | 영속 서비스와 페이드 |
| 2 | `Assets/_Project/Scenes/Production/BusinessScene.unity` | 에피소드·영업·제조 공용 |
| 3 | `Assets/_Project/Scenes/Production/RestScene.unity` | 보드·상점·현황 |

`CoreSceneAutoLoader`가 플레이 전에 CoreScene을 추가 방식으로 로드한다. 전체 흐름 QA는 `MainMenuScene`, 주문·제조 단독 QA는 `BusinessScene`, 휴식 UI QA는 `RestScene`에서 시작한다.

에디터 도구의 제품·개발 씬 경로는 `Assets/_Project/Shared/Editor/ProjectScenePaths.cs`에서 한곳에 관리한다.

## 2. CoreScene

```text
CoreScene
├── GameManager
├── SceneTransitionManager
│   └── Fade Canvas / CanvasGroup
├── EpisodeManager
├── DataManager
├── AudioManager
└── 런타임 생성 가능
    ├── DayFlowController
    └── GameProgress
```

`MonoSingleton<T>.Instance`는 해당 컴포넌트가 없으면 새 GameObject를 만든다. Hierarchy에 보이지 않더라도 런타임에 존재할 수 있다.

## 3. BusinessScene의 정적 구조

```text
BusinessScene
├── GameSystems
│   ├── GameModeManager
│   ├── InputRouter
│   ├── DragManager
│   └── BusinessBartendingBootstrap
│
└── Canvas [ScreenSpace-Overlay]
    ├── FrontWorldPanel        (모든 모드에서 표시)
    │   ├── FrontCameraRig     (이 RectTransform 전체가 이동하는 단위 — 아래 순서가 그리기 순서)
    │   │   ├── (바 배경)
    │   │   ├── CustomerStage  (CharacterStage — 영업/에피소드 공용)
    │   │   ├── (바테이블 배경)
    │   │   ├── BarCounter     (손님 바 테이블, 비물리 — TableSlots: 런타임 슬롯 간격·배율 가이드, 지우지 말 것)
    │   │   ├── IngredientSelection  (IngredientSelectionUI — 제조 모드에서만 올라옴, A/D 대분류 전환)
    │   │   ├── DrawerArea     (DrawerUI, 아래쪽 오프스크린 — S키로 이동해서 노출)
    │   │   └── CraftingSpace  (제작 공간 — 가림 배경 + CraftingSlotRow + 런타임 BartendingViewport)
    │   └── EpisodeRunner      (FrontCameraRig 밖 — 카메라 이동 영향 없음)
    ├── DialoguePanel          (모든 모드에서 표시)
    │   ├── DialogueController (공용 대화 렌더러)
    │   └── ChoiceContainer    (선택지 버튼 동적 생성)
    ├── VerticalCameraFollowGroup   (런타임 생성, FrontCameraRig.Awake — 씬 파일에는 없음)
    │   ├── OrderTicketPanel   (OrderTicketUI, OrderMode/CraftingMode 진입 시 자동 open, EpisodeMode에서 잠금·슬라이드 닫힘)
    │   └── RecipeBookPanel    (RecipeBookUI, OrderTicketPanel과 동일한 open/lock 패턴)
    ├── CraftingJudgePanel     (CraftingJudgeUI, CraftingMode에서만 표시 — 임시 판정 버튼 6개, goodjob/badjob/midjob 4종)
    │   ├── GoodJobButton
    │   ├── MidIceButton
    │   ├── MidGlassButton
    │   ├── MidIceGlassButton
    │   ├── MidWrongMenuButton
    │   └── BadJobButton
    └── HUD
```

`CraftingJudgePanel`은 이전 수동 Good/Bad 디버그 UI다. 공용 주문 세션이 초기화되면 `BusinessFlowBootstrap`이 비활성화한다.

## 4. BusinessScene의 런타임 생성 구조

### 영업 조립

`BusinessFlowBootstrap`은 씬 로드 후 `BusinessFlow` GameObject를 만들고 다음 컴포넌트를 추가한다.

```text
BusinessFlow (런타임)
├── BusinessFlowBootstrap
├── BusinessOrderSessionController
├── BusinessOrderSessionUI
└── BusinessShiftController
```

씬에서 `GameModeManager`, `CustomerSpawner`, `DialogueController`, `OrderTicketManager`, `BusinessBartendingBootstrap`, 루트 Canvas를 찾아 연결한다.

### 제조 세션

`CraftingMode`에 들어가면 `BusinessBartendingBootstrap`이 별도 물리 월드를 만든다.

```text
BartendingRuntime (런타임)
├── 전용 Orthographic Camera
├── RenderTexture / BartendingViewport
├── LiquidPool
├── 물리 슬롯들              (줄 중심 = CraftingSlotRow, 없으면 BarCounter)
├── 술 선택 공간에서 꺼낸 BottleController들
├── BeakerController
├── CobblerShaker
│   └── CobblerShakerTechniqueController
├── StirringRodController
└── GlassController
    ├── VesselLiquidTracker
    └── GlassSteamEmitter
```

제조 모드를 벗어나면 이 루트를 제거한다. 슬롯에 남은 병의 잔량은 자동으로 술장 재고에 합쳐진다([ingredient-selection.md](../ui/ingredient-selection.md#재고-규칙)).

## 5. 카메라 추종 UI

`FrontCameraRig.Awake()`는 다음 패널을 런타임 `VerticalCameraFollowGroup` 아래로 옮긴다.

- `OrderTicketPanel`
- `RecipeBookPanel`

이 그룹은 `frontWorld`의 세로 이동만 따라간다. 서랍을 열 때 배경과 함께 이동하지만 캐릭터 포커스용 가로 이동에는 영향을 받지 않는다. 술 선택 공간과 제작 공간은 이 그룹이 아니라 `FrontCameraRig` 안에 있어 기본적으로 가로·세로 이동을 모두 따라간다.

다만 제조 모드에서는 `FrontCameraRig.horizontalFixedPanels`에 등록된 패널(`CraftingSlotRow`, `IngredientSelection`)이 가로 팬만 역보정되어 화면 중앙에 고정된다. 제작 공간 배경(`CraftingSpace`)은 보정 대상이 아니므로 그대로 팬을 따라간다. 자세한 내용은 [ingredient-selection.md](../ui/ingredient-selection.md#카메라-가로-팬-보정)를 참고한다.

단축키: 도감 Q, 주문서 E, 대분류 전환 A/D(제조 모드), 도구장 서랍 S/W.

## 6. 모드별 표시

| UI | OrderMode | EpisodeMode | CraftingMode |
|---|---:|---:|---:|
| FrontWorldPanel | 표시 | 표시 | 표시 |
| DialoguePanel | 표시 | 표시 | 표시 |
| ChoiceContainer | 숨김 | 표시 | 숨김 |
| 주문서 | 조작 가능 | 잠금·닫힘 | 조작 가능 |
| 레시피북 | 조작 가능 | 잠금·닫힘 | 조작 가능 |
| 술 선택 공간 | 내려가 가려짐 | 내려가 가려짐 | 올라옴·조작 가능 |
| 제조 물리 월드 | 없음 | 없음 | 생성 |

## 7. RestScene

```text
RestScene
├── 배경 상호작용 오브젝트
│   ├── ObjectInteraction
│   └── ObjectInteractionBoard
├── EpisodeBoardManager
│   ├── 보드 슬롯 6개
│   ├── EpisodePhotoTrigger 동적 생성
│   └── EpisodeInfoUI
├── ShopUIManager
│   ├── 카테고리
│   └── ItemSlotUI 동적 생성
├── EpisodeUIManager
├── TooltipManager
└── ResetGameButton
```

에피소드 사진 위치는 `GameProgress`의 board slot 데이터로 유지된다. 상점 구매는 현재 로그만 남기며 돈·재고 저장과 연결되지 않았다.

## 8. 씬 전환 규칙

```text
MainMenu → BusinessScene(Episode)
BusinessScene(Episode) → 같은 BusinessScene(Business)
BusinessScene(Business) → RestScene
RestScene → BusinessScene(Episode)
```

에피소드에서 영업으로 바뀔 때는 같은 씬이 이미 로드되어 있으므로 `SceneTransitionManager`가 재로드하지 않고 `BusinessFlowBootstrap.StartBusinessSequence()` 콜백만 실행한다.

## 9. 리뷰 주의점

- 런타임 생성 컴포넌트는 Edit Mode Hierarchy에 보이지 않는다.
- CoreScene과 서브 씬 양쪽에 같은 `MonoSingleton` 컴포넌트가 있으면 중복 객체가 제거될 수 있다.
- 씬·Prefab YAML에 남은 이전 컴포넌트 참조는 Unity Inspector의 Missing Script로 별도 확인한다.
- `Sample_*` 씬은 빌드 흐름이 아니라 QA 자료다.
