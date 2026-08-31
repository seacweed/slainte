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
    ├── DayFlowManager
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
    │   ├── FrontCameraRig     (이 RectTransform 전체가 이동하는 단위)
    │   │   ├── BarCounter     (UIDropSlot들, 기본 뷰)
    │   │   ├── CustomerStage  (CharacterStage — 영업/에피소드 공용)
    │   │   └── DrawerArea     (DrawerUI, 아래쪽 오프스크린 — S키로 이동해서 노출)
    │   └── EpisodeRunner      (FrontCameraRig 밖 — 카메라 이동 영향 없음)
    ├── DialoguePanel          (모든 모드에서 표시)
    │   ├── DialogueController (공용 대화 렌더러)
    │   └── ChoiceContainer    (선택지 버튼 동적 생성)
    ├── VerticalCameraFollowGroup   (런타임 생성, FrontCameraRig.Awake — 씬 파일에는 없음)
    │   ├── OrderTicketPanel   (OrderTicketUI, OrderMode/CraftingMode 진입 시 자동 open, EpisodeMode에서 잠금·슬라이드 닫힘)
    │   ├── RecipeBookPanel    (RecipeBookUI, OrderTicketPanel과 동일한 open/lock 패턴)
    │   └── LiquorShelfPanel             (LiquorShelfUI, 우측 슬라이드 — D키 토글, EpisodeMode에서 잠금·슬라이드 닫힘)
    │       ├── CategoryButtonsArea  (ScrollRect, 카테고리 탭 — 닫힌 상태에서도 화면 우측에 노출)
    │       └── LiquorShelf      (vertical 스크롤, 술장 배경+슬롯)
    │           └── Viewport
    │               └── Content      (ContentHeightToBackground)
    │                   ├── ShelfBGImage              (Image + AspectRatioFitter, 카테고리 전환 시 sprite 교체)
    │                   └── [CategoryContainer × N]   (카테고리별, SetActive로 전환 — LiquorBottleSlotUI 수동 배치)
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
└── BusinessSequenceRunner
```

씬에서 `GameModeManager`, `CustomerSpawner`, `DialogueController`, `OrderTicketManager`, `BusinessBartendingBootstrap`, 루트 Canvas를 찾아 연결한다.

### 제조 세션

`CraftingMode`에 들어가면 `BusinessBartendingBootstrap`이 별도 물리 월드를 만든다.

```text
BartendingRuntime (런타임)
├── 전용 Orthographic Camera
├── RenderTexture / BartendingViewport
├── LiquidPool
├── 물리 슬롯들
├── 선택한 BottleController들
├── BeakerController
├── CobblerShaker
│   └── CobblerShakerTechniqueController
├── StirringRodController
└── GlassController
    ├── VesselLiquidTracker
    └── GlassSteamEmitter
```

제조 모드를 벗어나면 이 루트를 제거한다. 병 재고는 오브젝트가 사라져도 `GameProgress`에 남는다.

## 5. 카메라 추종 UI

`FrontCameraRig.Awake()`는 다음 패널을 런타임 `VerticalCameraFollowGroup` 아래로 옮긴다.

- `OrderTicketPanel`
- `RecipeBookPanel`
- `LiquorShelfPanel`

이 그룹은 `frontWorld`의 세로 이동만 따라간다. 서랍을 열 때 배경과 함께 이동하지만 캐릭터 포커스용 가로 이동에는 영향을 받지 않는다.

## 6. 모드별 표시

| UI | OrderMode | EpisodeMode | CraftingMode |
|---|---:|---:|---:|
| FrontWorldPanel | 표시 | 표시 | 표시 |
| DialoguePanel | 표시 | 표시 | 표시 |
| ChoiceContainer | 숨김 | 표시 | 숨김 |
| 주문서 | 조작 가능 | 잠금·닫힘 | 조작 가능 |
| 레시피북 | 조작 가능 | 잠금·닫힘 | 조작 가능 |
| 술장 | 조작 가능 | 잠금·닫힘 | 조작 가능 |
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
- `Sample_*` 씬과 `_Recovery` 씬은 빌드 흐름이 아니라 QA·복구 자료다.
