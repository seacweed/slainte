# CLAUDE.md

이 파일은 Claude Code(claude.ai/code)가 이 저장소에서 작업할 때 참고하는 안내 문서입니다.

## 반드시 지켜야 할 점

- 코드 내에 한글 사용 금지(주석 제외)
- 후에 다양한 기능이 추가될 수 있으므로 OOP 기반 설계, 확장성 고려한 코드 작성
- 계획부터 말하고 승인 받은 후에 작업 진행
- 요구사항이 명확하지 않거나 부족한 점이 있는 경우 반드시 질문 후 답변
- 최적화를 고려한 코드 작성
- claude.md 업데이트 시 프로젝트 전체를 아우르는 중심 내용만 이 파일에 작성(docs에 추가될 내용은 작성하지 말 것), 세부 사항들은 docs의 개별 문서에 작성. 필요시 새로운 문서 생성하고 claude.md에 링크 추가.

## 프로젝트 개요

**Slainte**는 Unity 2023.3.5f2(URP)로 제작 중인 내러티브 바텐딩 게임입니다. 이름은 아일랜드어로 "건배"를 뜻합니다. 에피소드 기반의 비주얼 노벨식 스토리텔링과 드래그-드롭 바텐딩 메커니즘을 결합한 게임입니다.

**게임 진행 흐름**: MainMenu → Episode(BusinessScene) → Rest(RestScene) → Episode → Rest → ...

## 아키텍처 핵심

- **싱글톤**: 전역 상태는 `MonoSingleton<T>`(DontDestroyOnLoad 유지, `GameProgress`, `EpisodeManager`, `DataManager`, `GameManager`, `AudioManager`, `SceneTransitionManager`), 씬 종속 UI 매니저는 `SceneSingleton<T>`(DontDestroyOnLoad 없음, `GameModeManager`, `NotificationManager`) — BusinessScene처럼 Additive로 매 에피소드 언로드/재로드되는 씬에서 씬 로컬 UI를 직접 참조하는 매니저가 전역 싱글톤이면 재로드 시 낡은 인스턴스를 참조하는 버그가 생기므로 분리
- **런타임 상태 Source of Truth**: `GameProgress` (flags, completedEpisodeIds, affinityVars, boardSlots, currentDay)
- **에피소드 데이터**: `EpisodeData` 단일 SO — `Resources/EpisodeData/`에 배치, `EpisodeManager`가 일괄 로드
- **Canvas**: ScreenSpace-Overlay, Canvas Scaler Reference Resolution **2560×1440 (QHD)**. 1 canvas unit = 1px at QHD
- **씬 전환**: `GameManager` + `SceneTransitionManager` (Additive, 페이드)
- **게임 상태**: `GameState.None`(MainMenu 초기) / `GameState.Episode` / `GameState.Rest`
- **에피소드 종료**: `EpisodeRunner.EndEncounter()` → `EpisodeManager.ClearEpisode()` → `GameManager.ChangeState(Rest)`
- **UI 패널 open/close**: `GameModeManager`가 단순 표시용 패널은 `CanvasGroup` 즉시 on-off로, 사용자 토글이 필요한 패널(주문서, 도감, 술장)은 `SetInteractable()`/`Open()` 호출만 하고 실제 슬라이드 애니메이션은 각 UI가 자체 관리
- **카메라 이동**: `FrontCameraRig`가 `frontWorld` anchoredPosition으로 배경을 가로(에피소드 캐릭터 포커스)·세로(서랍 열기, S/W키)로 이동. 주문서/도감/술장 패널은 세로 이동만 따라가고 가로 팬에는 화면 고정 (`verticalFollowPanels`)
- **술장**: `LiquorShelfUI` — 우측 슬라이드 토글(R키), 카테고리별 컨테이너 show/hide, `GameProgress` 해금 플래그로 슬롯 표시 제어. 버튼 이미지는 버튼이 아닌 패널 자체의 Image 컴포넌트(`panelImage`)를 교체하는 방식 (`RecipeBookUI`, `OrderTicketUI` 공통 적용). 병 잔여량은 `GameProgress`가 소스오브트루스, 호버 시 `LiquorBottleInfoCard`가 병 개수 기반 상태 표시
- **알림 시스템**: `GameProgress.OnAffinityChanged` 이벤트 → `NotificationManager` 수신 → 화면 우상단 순차 표시. `AnimatedSpriteUI`(PNG 프레임 배열 코루틴 재생)로 방향 애니메이션 처리
- **에피소드 그래프 편집**: `NarrativeGraphSO`(그래프 SO) → `EpisodeDataCompiler` → `EpisodeData`(런타임). 역방향: `EpisodeDataImporter`. 노드 ID 자동 할당: `NarrativeNodeIdAssigner`. CSV와 양방향 호환 유지

## 문서

필요한 섹션만 읽어 컨텍스트 부하를 줄이세요.

| 문서 | 내용 |
|---|---|
| **core** |||
| [docs/core/architecture.md](docs/core/architecture.md) | GameMode/패널 구조, 입력 처리, GameProgress, 데이터 패턴, 공용 UI 유틸리티 |
| [docs/core/scene-structure.md](docs/core/scene-structure.md) | 씬 계층 구조 (Canvas, Panel, GameObject) |
| [docs/core/corescene-systems.md](docs/core/corescene-systems.md) | CoreScene 매니저 구조, 게임 흐름, 저장/로드 |
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
| **gameplay** |||
| [docs/gameplay/character-presentation.md](docs/gameplay/character-presentation.md) | 캐릭터 표시(CharacterView/CharacterStage/CharacterData), 대화 렌더링 |
| [docs/gameplay/bartending-systems.md](docs/gameplay/bartending-systems.md) | 바텐딩 도구(GlassController 등), MetaballFluid 액체 입자 시스템 |
| [docs/gameplay/business-interactions.md](docs/gameplay/business-interactions.md) | 영업 씬 손님&주문, 드래그-드롭 바텐딩 |
| **tools** |||
| [docs/tools/editor-tools.md](docs/tools/editor-tools.md) | 에디터 툴 목록 및 사용법 |
