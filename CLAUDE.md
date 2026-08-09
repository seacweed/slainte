# CLAUDE.md

이 파일은 Claude Code(claude.ai/code)가 이 저장소에서 작업할 때 참고하는 안내 문서입니다.

## 반드시 지켜야 할 점

- 코드 내에 한글 사용 금지(주석 제외)
- 답변은 무조건 한국어로 할 것
- 후에 다양한 기능이 추가될 수 있으므로 OOP 기반 설계, 확장성 고려한 코드 작성
- 계획부터 말하고 승인 받은 후에 작업 진행
- 요구사항이 명확하지 않거나 부족한 점이 있는 경우 반드시 질문 후 답변
- 최적화를 고려한 코드 작성
- claude.md 업데이트 시 프로젝트 전체를 아우르는 중심 내용만 이 파일에 작성(docs에 추가될 내용은 작성하지 말 것), 세부 사항들은 docs의 개별 문서에 작성. 필요시 새로운 문서 생성하고 claude.md에 링크 추가.

## 프로젝트 개요

**Slainte**는 Unity 2023.3.5f2(URP)로 제작 중인 내러티브 바텐딩 게임입니다. 이름은 아일랜드어로 "건배"를 뜻합니다. 에피소드 기반의 비주얼 노벨식 스토리텔링과 드래그-드롭 바텐딩 메커니즘을 결합한 게임입니다.

**게임 진행 흐름**: MainMenu → (Episode 또는 Business, BusinessScene) → Settlement(정산) → Rest(RestScene) → 다음 날 → ...

## 아키텍처 핵심

- **싱글톤**: 전역 상태는 `MonoSingleton<T>`(DontDestroyOnLoad 유지, `GameProgress`, `EpisodeManager`, `DataManager`, `GameManager`, `AudioManager`, `SceneTransitionManager`, `DayFlowController`, `SettlementManager`), 씬 종속 UI 매니저는 `SceneSingleton<T>`(DontDestroyOnLoad 없음, `GameModeManager`, `NotificationManager`, `LiquorBottleInfoCard`, `IngredientUnlockTooltip`) — `OnDestroy()`에서 static `Instance`를 null로 정리하므로 씬 재로드 후에도 `?.` 호출이 Unity의 fake-null 예외 없이 안전. BusinessScene처럼 Additive로 매 에피소드 언로드/재로드되는 씬에서 씬 로컬 UI를 직접 참조하는 매니저가 전역 싱글톤이면 재로드 시 낡은 인스턴스를 참조하는 버그가 생기므로 분리. `DontDestroyOnLoad`는 루트 GameObject에서만 유효하므로 모든 `MonoSingleton` 스크립트는 CoreScene의 단일 루트 오브젝트 `Managers`에 컴포넌트로 함께 부착(자식 오브젝트로 분리하지 않음)
- **에피소드 해금/플레이/선택 조건**: `EpisodeData.triggerCondition`(해금, 작전판 노출)과 `playCondition`(플레이, Play 버튼 활성화)이 분리되어 있음. `EpisodeManager.IsUnlocked()`/`IsPlayable()`가 각각 평가. `selectConditions`(선택, 리스트)는 위 둘과 완전히 독립된 세 번째 조건군 — 작전판 툴팁에서 옵션 중 하나만 켤 수 있는 라디오 버튼처럼 동작하며 Play 버튼 활성화엔 영향 없음. 옵션별로 조건 하나+플래그+등장인물 override를 가지고, 실제 `GameProgress` 플래그 반영은 Play 버튼 클릭 시점에 이루어짐
- **런타임 상태 Source of Truth**: `GameProgress` (flags, completedEpisodeIds, affinityVars, boardSlots, currentDay, currentChapterId, currentMoney, 당일 정산 집계, 손님 등장 횟수)
- **Day/챕터**: `DayFlowController`의 Rest 진입점(`StartBusinessDay()`/`StartDefaultEpisode()`)에서 `GameProgress.AdvanceDay()`로 currentDay 증가. `GameProgress.SetCurrentChapter()`는 챕터가 실제로 바뀔 때만 currentDay를 1로 리셋. 최초 게임 시작 시 `MainMenuManager`가 currentChapterId가 비어있으면 `ChapterData.LoadFirst()`(chapterIndex 최솟값)로 첫 챕터를 설정 — 챕터 전환 트리거 자체는 아직 미구현(훅만 존재)
- **에피소드 데이터**: `EpisodeData` 단일 SO — `Resources/EpisodeData/`에 배치, `EpisodeManager`가 일괄 로드. `episodeType`(Default/Mandatory)+`mandatorySlot`(Before/AfterBusiness)+`chapterId`로 하루 흐름에서의 역할 구분
- **Canvas**: ScreenSpace-Overlay, Canvas Scaler Reference Resolution **2560×1440 (QHD)**. 1 canvas unit = 1px at QHD
- **씬 전환**: `GameManager` + `SceneTransitionManager` (Additive, 페이드). `TransitionToSubScene()`은 화면이 완전히 검게 된 직후(씬 언로드 전) 호출되는 `onFadeOutComplete` 콜백을 제공 — `SettlementManager`가 이를 이용해 정산 UI(셔터/모니터)를 페이드아웃 끝날 때까지 유지했다가 화면이 안 보이는 시점에 원위치로 리셋
- **게임 상태**: `GameState.None`(MainMenu 초기) / `Episode` / `Business` / `Settlement` / `Rest` — 하루 진행 순서(필수 에피소드 큐, 영업 전/후, 정산)는 `DayFlowController`가 전담하며 각 씬/매니저는 `GameManager`를 직접 호출하지 않고 이 컨트롤러를 거침
- **에피소드 종료**: `EpisodeRunner.EndEncounter()` → `EpisodeManager.ClearEpisode()` → `DayFlowController.OnEpisodeCompleted()` (다음 단계가 영업인지 정산인지는 필수 에피소드 큐 상태에 따라 결정)
- **제조 판정 분기**: `requiresCrafting` 노드는 `CraftingJobResult`(Good/MidIce/MidGlass/MidIceGlass/MidWrongMenu/Bad 6종) 결과별로 `EpisodeNode.craftingOutcomes`(`List<CraftingOutcome>`)에서 다음 노드·플래그·변수 변경을 분기. 실제 재료/얼음/잔 비교로 결과를 자동 판정하는 로직은 아직 없어 `CraftingJudgeUI`의 버튼 6개로 수동 판정
- **UI 패널 open/close**: `GameModeManager`가 단순 표시용 패널은 `CanvasGroup` 즉시 on-off로, 사용자 토글이 필요한 패널(주문서, 도감, 술장)은 `SetInteractable()`/`Open()` 호출만 하고 실제 슬라이드 애니메이션은 각 UI가 자체 관리
- **카메라 이동**: `FrontCameraRig`가 `frontWorld` anchoredPosition으로 배경을 가로(에피소드 캐릭터 포커스)·세로(서랍 열기, S/W키)로 이동. 주문서/도감/술장 패널은 세로 이동만 따라가고 가로 팬에는 화면 고정 (`verticalFollowPanels`)
- **술장**: `LiquorShelfUI` — 우측 슬라이드 토글(R키), 카테고리별 컨테이너 show/hide, `GameProgress` 해금 플래그로 슬롯 표시 제어. 버튼 이미지는 버튼이 아닌 패널 자체의 Image 컴포넌트(`panelImage`)를 교체하는 방식 (`RecipeBookUI`, `OrderTicketUI` 공통 적용). 병 잔여량은 `GameProgress`가 소스오브트루스, 호버 시 `LiquorBottleInfoCard`가 `LiquorStockLevelPalette`(empty/10단계/full 총 12단계 스프라이트) 기반으로 병 개수만큼 상태 아이콘 표시(5개씩 2줄, 6병 이상일 때만 2번째 줄 활성화)
- **상점**: `ShopUIManager`가 홈(재료/업그레이드/레시피북 3버튼) → 재료 대분류(`LiquorCategoryDef`) → 재료 소분류(`LiquorBottleDef`) 3단 구조로 동작. 재료 마스터 데이터는 임시 `ItemData`(`Resources/Items`)가 아니라 술장과 동일한 `LiquorBottleDef`/`LiquorCategoryDef`를 그대로 사용 — 상점에서 산 재료 잔량과 술장에 표시되는 잔량이 같은 `GameProgress` 저장소(id 키)를 공유해 자동 동기화됨. 구매는 1병(unitVolume) 단위 충전, 잠금 판정은 술장과 동일한 `unlockFlagKey` 재사용(표시용 해금 힌트만 레시피북/에피소드로 분리). 업그레이드는 별도 `UpgradeDef`(단계별 가격 배열)와 `GameProgress` 레벨 저장소로 관리
- **알림 시스템**: `GameProgress.OnAffinityChanged` 이벤트 → `NotificationManager` 수신 → 화면 우상단 순차 표시. `AnimatedSpriteUI`(PNG 프레임 배열 코루틴 재생)로 방향 애니메이션 처리
- **에피소드 그래프 편집**: `NarrativeGraphSO`(그래프 SO) → `EpisodeDataCompiler` → `EpisodeData`(런타임). 역방향: `EpisodeDataImporter`. 노드 ID 자동 할당: `NarrativeNodeIdAssigner`. CSV와 양방향 호환 유지
- **메인메뉴 인트로**: `MainMenuIntroController`가 팀 로고 → 타이틀 로고(중앙, 커스터마이즈 가능한 다단계 깜박임에 발광 레이어가 동기화되었다가 배경과 함께 독립적으로 페이드아웃) → 배경(레이어별 fade, 무한 스크롤 구름 `InfiniteHorizontalScroller`) → 펍 조명 깜박임 → 메뉴 순으로 순차 진행, 임의 입력으로 스킵 가능. `MainMenuCharacterSpawner`/`MainMenuCharacterWalker`는 이 시퀀스와 완전히 독립적으로 씬 로드 즉시 시작해 배경 위를 오가는 배경 캐릭터를 프리팹 없이 런타임 생성으로 상시 스폰

## 문서

필요한 섹션만 읽어 컨텍스트 부하를 줄이세요.

| 문서 | 내용 |
|---|---|
| **core** |||
| [docs/core/architecture.md](docs/core/architecture.md) | GameMode/패널 구조, 입력 처리, GameProgress, 데이터 패턴, 공용 UI 유틸리티 |
| [docs/core/scene-structure.md](docs/core/scene-structure.md) | 씬 계층 구조 (Canvas, Panel, GameObject) |
| [docs/core/corescene-systems.md](docs/core/corescene-systems.md) | CoreScene 매니저 구조, 게임 흐름, 저장/로드 |
| [docs/core/game-flow-design.md](docs/core/game-flow-design.md) | Day 흐름 설계(영업/에피소드/정산), 필수 에피소드 큐, 챕터·해금 데이터 모델 |
| [docs/core/unity-build.md](docs/core/unity-build.md) | Unity 버전, 빌드 방법, 개발 환경 |
| **narrative** |||
| [docs/narrative/episode-engine.md](docs/narrative/episode-engine.md) | 에피소드 오케스트레이션(EpisodeRunner, 분기, 제조 트리거), 오디오/BGM |
| [docs/narrative/episode-csv-guide.md](docs/narrative/episode-csv-guide.md) | 에피소드 CSV 작성법 (섹션 구조, 열 설명, 예시) |
| [docs/narrative/planner-episode-csv-guide.md](docs/narrative/planner-episode-csv-guide.md) | 에피소드 CSV 작성 가이드 (기획자용) |
| [docs/narrative/narrative-graph-editor.md](docs/narrative/narrative-graph-editor.md) | 그래프 에디터 아키텍처, 데이터 구조, 컴파일/임포트/ID 할당 |
| [docs/narrative/narrative-graph-guide.md](docs/narrative/narrative-graph-guide.md) | 그래프 에디터 사용 가이드 (노드 생성·연결·시퀀스 편집·컴파일) |
| [docs/narrative/node-based-episode-editor-spec.md](docs/narrative/node-based-episode-editor-spec.md) | 노드 기반 에피소드 에디터 설계서 |
| **ui** |||
| [docs/ui/restscene-systems.md](docs/ui/restscene-systems.md) | RestScene UI 시스템 (에피소드 보드, 상점, 현황판, 툴팁) |
| [docs/ui/recipe-book-search.md](docs/ui/recipe-book-search.md) | 도감 검색 UI(RecipeSearchUI/RecipeSearchOptionButton), 화면 전환 흐름 |
| [docs/ui/notification-system.md](docs/ui/notification-system.md) | 알림 시스템(NotificationManager/AffinityNotificationUI/AnimatedSpriteUI), 씬 설정 |
| [docs/ui/liquor-shelf.md](docs/ui/liquor-shelf.md) | 술장 시스템 (LiquorShelfUI, 카테고리/슬롯 구조, 씬 세팅) |
| [docs/ui/mainmenu-intro.md](docs/ui/mainmenu-intro.md) | 메인메뉴 인트로 연출(팀 로고/타이틀 깜박임·발광/배경 레이어/캐릭터 스포너), 씬 세팅 |
| **gameplay** |||
| [docs/gameplay/character-presentation.md](docs/gameplay/character-presentation.md) | 캐릭터 표시(CharacterView/CharacterStage/CharacterData), 대화 렌더링 |
| [docs/gameplay/bartending-systems.md](docs/gameplay/bartending-systems.md) | 바텐딩 도구(GlassController 등), MetaballFluid 액체 입자 시스템 |
| [docs/gameplay/business-interactions.md](docs/gameplay/business-interactions.md) | 영업 씬 손님&주문, 드래그-드롭 바텐딩 |
| **tools** |||
| [docs/tools/editor-tools.md](docs/tools/editor-tools.md) | 에디터 툴 목록 및 사용법 |
