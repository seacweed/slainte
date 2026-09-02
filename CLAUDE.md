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

**Slainte**는 Unity 6000.3.5f2(URP)로 제작 중인 내러티브 바텐딩 게임입니다. 이름은 아일랜드어로 "건배"를 뜻합니다. 에피소드 기반의 비주얼 노벨식 스토리텔링과 드래그-드롭 바텐딩 메커니즘을 결합한 게임입니다.

**게임 진행 흐름**: MainMenu → (Episode 또는 Business, BusinessScene) → Settlement(정산) → Rest(RestScene) → 다음 날 → ...

## 아키텍처 핵심

- **싱글톤**: 전역 상태는 `MonoSingleton<T>`(DontDestroyOnLoad), 씬 종속 UI 매니저는 `SceneSingleton<T>`(씬 수명)로 구분. `DontDestroyOnLoad`는 루트 GameObject에서만 유효하므로 모든 `MonoSingleton`은 CoreScene의 단일 루트 오브젝트 `Managers`에 컴포넌트로 함께 부착(자식으로 분리 금지)
- **런타임 상태 Source of Truth**: `GameProgress` 하나 — flags, 진행도, 재화, 재고 등 저장이 필요한 모든 상태가 여기로 모임
- **Canvas**: ScreenSpace-Overlay, Canvas Scaler Reference Resolution **2560×1440 (QHD)**. 1 canvas unit = 1px at QHD
- **네임스페이스**: `Features/Bartending`과 `Features/Business/Runtime/Flow`의 주 코드에는 `Slainte.Bartending`/`Slainte.Business`를 사용한다. Shared의 Input·Lifecycle·Content는 각각 독립 namespace/asmdef 경계를 가진다. 이전 Unity 직렬화 타입에는 전역 namespace가 남아 있으므로 새 파일은 소유 폴더의 기존 관례를 따르고, 기존 타입의 namespace 이동은 `.meta` GUID와 Scene·Prefab 직렬화를 함께 검증하는 별도 마이그레이션으로 처리한다.
- **RestScene 팝업 상호배타**: `BaseUIManager`를 상속하는 RestScene 팝업은 `OpenUI()` 호출 시 자기 자신을 제외한 나머지가 자동으로 닫힘 — 새 팝업을 추가해도 상속만 하면 자동 적용됨. 로딩 연출처럼 진행 중 다른 팝업으로 전환되면 안 되는 구간은 `LockTransitions()`/`UnlockTransitions()`로 잠글 수 있음
- **상점/술장 데이터 공유**: 상점의 재료 마스터 데이터는 임시 `ItemData`가 아니라 술장과 동일한 `LiquorBottleDef`/`LiquorCategoryDef`를 그대로 사용 — 잔량이 같은 `GameProgress` 저장소를 공유해 두 화면이 자동 동기화됨. 레시피북 구매처럼 새 해금 흐름을 추가할 땐 `GameProgress`에 새 저장소를 만들지 않고 기존 flag 시스템(`SetFlag`/`HasFlag`)을 재사용하는 패턴을 따를 것
- **상점 화폐 추상화**: 상점 슬롯(`ItemSlotUI`)은 `IShopCurrency`로 결제 수단을 주입받음(`MoneyShopCurrency`/`StrangeCoinShopCurrency`) — 새 화폐나 특수 상점을 추가할 때 슬롯/카테고리 로직을 복제하지 말고 `IShopCurrency` 구현체만 추가하는 패턴을 따를 것
- **에피소드 제조 주문의 레시피/태그 자동 판별**: 에피소드 CSV 제조 노드의 `craftingOrderTarget` 하나로 레시피 ID(`rec_XXXX`)와 맛/분위기 태그(예: `고급스러운`, `씁쓸함`)를 둘 다 표현함 — 별도 `craftingOrderType` 컬럼을 CSV에 쓰지 않음. `EpisodeCraftingBridge.ResolveOrderType()`이 그 값을 `TasteMoodTagPaletteDef`(`Assets/Resources/Bartending/Recipes/TasteMoodPalette.asset`)에 대조해 태그면 `TasteOrder`/`MoodOrder`, 아니면 레시피 ID로 보고 `EpisodeOrder`로 자동 판별함
- **화면 전용 UI는 공유 컴포넌트에 옵션을 얹지 말고 포크**: 기존 슬롯(`ItemSlotUI`)을 다른 화면(배송)에서 쓰되 그 화면만의 필드로 프리팹 크기/레이아웃 자체가 바뀌어야 하면, 공유 프리팹에 조건부 필드를 추가하지 말고 스크립트+프리팹을 통째로 복제한 전용 클래스를 만들 것(예: `ItemSlotUI` → `DeliveryItemSlotUI`). 그런 화면 전용 프리팹 참조들은 개별 UI 컴포넌트가 아니라 공용 카탈로그 SO(`LiquorShopCatalog`)에 모아두고 `Initialize()` 시점에 주입하는 패턴을 따를 것. 단, 프리팹 구조는 그대로고 크기·글씨 등 값 몇 개만 위치별로 달라야 하는 경우엔 포크 대신 `Setup()`에 선택적 오버라이드 인자를 추가하는 쪽을 우선할 것(예: `RecipeSearchOptionButton`)
- **컷씬**: `CutsceneManager`/`CutsceneUI`(CoreScene 상주)가 이미지+텍스트 슬라이드를 재생. 트리거 판단(어떤 컷씬을 언제 재생할지)은 `DayFlowController`/`SettlementManager`가 담당하며, 컷씬 종료 후 패널을 곧바로 감추지 않고 `SceneTransitionManager.onFadeOutComplete`(다음 화면이 완전히 덮인 시점)에 감춰 화면 전환 사이 빈 틈이 생기지 않게 함
- **씬 로컬 MonoBehaviour의 Awake 타이밍 함정**: 비활성 부모(예: 아직 안 열린 패널) 밑에서 `Instantiate`된 오브젝트는 활성화 전까지 Unity가 `Awake()` 호출을 미룸 — 그 전에 외부에서 `Setup()`류를 호출하면 `Awake()`에서만 캐싱한 참조가 아직 없을 수 있음. `Awake()`와 공개 메서드 양쪽에서 호출 가능한 `EnsureInitialized()` 패턴으로 방어할 것

## 문서

필요한 섹션만 읽어 컨텍스트 부하를 줄이세요.

| 문서 | 내용 |
|---|---|
| [docs/implementation-plan.md](docs/implementation-plan.md) | 회의록 반영 전체 구현 순서, 단계별 완료 조건, 선행 자료 |
| **core** |||
| [docs/core/architecture.md](docs/core/architecture.md) | GameMode/패널 구조, 입력 처리, GameProgress, 데이터 패턴, 공용 UI 유틸리티 |
| [docs/core/scene-structure.md](docs/core/scene-structure.md) | 씬 계층 구조 (Canvas, Panel, GameObject) |
| [docs/core/corescene-systems.md](docs/core/corescene-systems.md) | CoreScene 매니저 구조, 게임 흐름, 정산(SettlementManager/SettlementUI) 로직, 저장/로드 |
| [docs/core/game-flow-design.md](docs/core/game-flow-design.md) | Day 흐름 설계(영업/에피소드/정산), 필수 에피소드 큐, 챕터·해금 데이터 모델 |
| [docs/core/cutscene-system.md](docs/core/cutscene-system.md) | 컷씬 시스템(CutsceneManager/CutsceneUI, 슬라이드 재생 흐름, 화면 전환 동기화, 3개 트리거 지점) |
| [docs/core/unity-build.md](docs/core/unity-build.md) | Unity 버전, 빌드 방법, 개발 환경 |
| **narrative** |||
| [docs/narrative/episode-engine.md](docs/narrative/episode-engine.md) | 에피소드 오케스트레이션(EpisodeRunner, 분기, 제조 판정 연동, 정산 커스텀 보상), 오디오/BGM/SFX |
| [docs/narrative/episode-csv-guide.md](docs/narrative/episode-csv-guide.md) | 에피소드 CSV 작성법 (섹션 구조, 열 설명, 예시) |
| [docs/narrative/narrative-graph-editor.md](docs/narrative/narrative-graph-editor.md) | 그래프 에디터 아키텍처, 데이터 구조, 컴파일/임포트/ID 할당 |
| [docs/narrative/narrative-graph-guide.md](docs/narrative/narrative-graph-guide.md) | 그래프 에디터 사용 가이드 (노드 생성·연결·시퀀스 편집·컴파일) |
| [docs/narrative/node-based-episode-editor-spec.md](docs/narrative/node-based-episode-editor-spec.md) | 노드 기반 에피소드 에디터 설계서 |
| **ui** |||
| [docs/ui/restscene-systems.md](docs/ui/restscene-systems.md) | RestScene UI 시스템 (에피소드 보드, 상점, 현황판, 툴팁) |
| [docs/ui/recipe-book-search.md](docs/ui/recipe-book-search.md) | 도감 검색·상세 UI(RecipeSearchUI, 맛/분위기 태그 팔레트, 레시피 상세, 재료 카테고리 색상), 화면 전환 흐름 |
| [docs/ui/notification-system.md](docs/ui/notification-system.md) | 알림 시스템(NotificationManager/AffinityNotificationUI/AnimatedSpriteUI), 씬 설정 |
| [docs/ui/liquor-shelf.md](docs/ui/liquor-shelf.md) | 술장 시스템 (LiquorShelfUI, 카테고리/슬롯 구조, 씬 세팅) |
| [docs/ui/tv-broadcast-system.md](docs/ui/tv-broadcast-system.md) | TV 방송 시스템 (예보→활성 하루 지연 구조, 5종 효과, 휴식 화면 표시) |
| [docs/ui/mainmenu-intro.md](docs/ui/mainmenu-intro.md) | 메인메뉴 인트로 연출(팀 로고/타이틀 깜박임·발광/배경 레이어/캐릭터 스포너), 씬 세팅 |
| **gameplay** |||
| [docs/gameplay/character-presentation.md](docs/gameplay/character-presentation.md) | 캐릭터 표시(CharacterView/CharacterStage/CharacterData), 대화 렌더링 |
| [docs/gameplay/bartending-systems.md](docs/gameplay/bartending-systems.md) | 바텐딩 도구(GlassController 등), 액체 의미 데이터 계층(LiquidPayload/VesselLiquidTracker)과 레시피 판정(CocktailEvaluator), MetaballFluid 시각 시스템 |
| [docs/gameplay/business-interactions.md](docs/gameplay/business-interactions.md) | 영업 씬 손님&주문, 손님 재등장 규칙, 주문표 대사 흐름, 판매 보상 공식(팁/실수 페널티), 드래그-드롭 바텐딩 |
| [docs/gameplay/item-data-table-guide.md](docs/gameplay/item-data-table-guide.md) | 바텐딩 아이템·레시피·배합 기준 CSV와 임포트 규칙 |
| [docs/gameplay/known-issues.md](docs/gameplay/known-issues.md) | 재조사·재검증이 필요한 미해결 버그와 보류 결정 목록 |
| [docs/bartending-art-data-mismatches.md](docs/bartending-art-data-mismatches.md) | 병 아트 세트와 LiquorBottleDef/ItemDef 연결 현황, 데이터·아트 누락 목록 |
| [docs/customer-availability-missing-data.md](docs/customer-availability-missing-data.md) | 손님 등장조건 CSV 반영 현황, 누락된 에피소드·플래그·이미지 목록 |
| **tools** |||
| [docs/tools/editor-tools.md](docs/tools/editor-tools.md) | 에디터 툴 목록 및 사용법 |
