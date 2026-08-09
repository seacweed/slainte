# 씬 구조

씬은 `BusinessScene` 하나로 통합되어 있습니다. (`EpisodeScene`은 제거됨)

```
[Scene: BusinessScene]
├── GameSystems
│   ├── GameProgress          (DontDestroyOnLoad 싱글톤)
│   ├── DragManager           (싱글톤)
│   ├── GameModeManager       (패널 show/hide 오케스트레이터)
│   └── InputRouter           (단일 입력 처리)
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
    │   └── LiquorShelfPanel             (LiquorShelfUI, 우측 슬라이드 — R키 토글, EpisodeMode에서 잠금·슬라이드 닫힘)
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
        └── DragGhostImage
```

**VerticalCameraFollowGroup**: 씬 파일에는 `OrderTicketPanel`/`RecipeBookPanel`/`LiquorShelfPanel`이 Canvas 직계 자식으로 저장돼 있지만, `FrontCameraRig.Awake()`가 `verticalFollowPanels`에 지정된 이 3개 패널을 런타임에 새로 생성한 `VerticalCameraFollowGroup`(풀스트레치 RectTransform) 밑으로 재부모시킨다. 이 그룹은 `frontWorld`의 anchoredPosition.y만 매 프레임 따라가고 x는 항상 0으로 고정되므로, 서랍 열기/닫기(세로 이동)에는 배경과 함께 움직이지만 에피소드 모드의 캐릭터 포커스 팬(가로 이동)에는 영향받지 않는다. 각 패널 자체의 열기/닫기 슬라이드는 이 그룹 기준 로컬 좌표로 그대로 동작하므로 `OrderTicketUI`/`RecipeBookUI`/`LiquorShelfUI` 쪽 코드는 변경이 없다.
