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
    ├── FrontWorldPanel        (OrderMode / EncounterMode / CraftingMode에서 표시)
    │   └── FrontCameraRig     (이 RectTransform 전체가 이동하는 단위)
    │       ├── BarCounter     (UIDropSlot들, 기본 뷰)
    │       ├── CustomerStage  (CharacterStage, 기본 뷰)
    │       ├── ShelfArea      (ShelfUI, 오른쪽 오프스크린 — D키로 이동해서 노출)
    │       └── DrawerArea     (DrawerUI, 아래쪽 오프스크린 — S키로 이동해서 노출)
    ├── EncounterPanel         (EncounterMode / CraftingMode에서 표시)
    │   ├── EpisodeCharacterStage  (CharacterStage)
    │   ├── EpisodeDialogueRunner
    │   └── EncounterRunner
    ├── DialoguePanel          (OrderMode / EncounterMode / CraftingMode에서 표시)
    │   ├── DialogueController (공용 대화 렌더러)
    │   └── ChoiceContainer    (선택지 버튼 동적 생성)
    ├── OrderTicketPanel       (OrderTicketUI, OrderMode에서만 표시)
    └── HUD
        └── DragGhostImage
```
