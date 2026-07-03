# 씬 구조

씬은 `BusinessScene` 하나로 통합되어 있습니다. (`EpisodeScene`은 제거됨)

```
[Scene: BusinessScene]
├── GameSystems
│   ├── GameProgress          (DontDestroyOnLoad 싱글톤)
│   ├── DragManager           (싱글톤)
│   ├── GameModeManager       (패널 show/hide 오케스트레이터)
│   ├── InputRouter           (단일 입력 처리)
│   └── EpisodeTriggerManager
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
    ├── OrderTicketPanel       (OrderTicketUI, OrderMode/CraftingMode 진입 시 자동 open, EpisodeMode에서 잠금·슬라이드 닫힘)
    ├── RecipeBookPanel        (RecipeBookUI, OrderTicketPanel과 동일한 open/lock 패턴)
    ├── LiquorShelfPanel             (LiquorShelfUI, 우측 슬라이드 — R키 토글, EpisodeMode에서 잠금·슬라이드 닫힘)
    │   ├── CategoryButtonsArea  (ScrollRect, 카테고리 탭 — 닫힌 상태에서도 화면 우측에 노출)
    │   └── LiquorShelf      (vertical 스크롤, 술장 배경+슬롯)
    │       └── Viewport
    │           └── Content      (ContentHeightToBackground)
    │               ├── ShelfBGImage              (Image + AspectRatioFitter, 카테고리 전환 시 sprite 교체)
    │               └── [CategoryContainer × N]   (카테고리별, SetActive로 전환 — LiquorBottleSlotUI 수동 배치)
    ├── CraftingJudgePanel     (CraftingJudgeUI, CraftingMode에서만 표시 — 임시 판정 버튼)
    │   ├── GoodJobButton      (화면 왼쪽)
    │   └── BadJobButton       (화면 오른쪽)
    └── HUD
        └── DragGhostImage
```
