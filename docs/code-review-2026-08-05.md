# Slainte 프로젝트 전체 코드 리뷰

- 리뷰 일자: 2026-08-05 (Asia/Seoul)
- 대상 브랜치/리비전: `mergeDummy` / `8e030e8`
- Unity: `6000.3.5f2`, URP `17.3.0`
- 범위: 런타임·에디터 C# 154개(20,604줄), 빌드 씬 4개, 주요 Prefab/ScriptableObject/CSV, 현재 미커밋 변경
- 결론: **컴파일은 통과하지만 현재 상태는 릴리스 차단(Release blocked)**

## 1. 요약

런타임과 에디터 어셈블리는 경고·오류 없이 컴파일된다. 빌드 씬에도 명시적인 Missing Script는 발견되지 않았다. 그러나 핵심 플레이 경로에는 컴파일로 잡히지 않는 데이터·상태·물리 오류가 있다.

가장 먼저 처리해야 할 문제는 다음과 같다.

1. `StrangeCoin_2~4`의 제조 노드 8개에 `craftingRecipeId`가 없어 제작이 시작되지 않고 항상 실패 분기로 진행한다. 주문표 데이터도 8개 모두 누락됐다.
2. 액체 입자 혼합이 서로 다른 입자 용량을 고려하지 않아 재료량을 생성하거나 소멸시킨다. 이 값이 주문 판정에 그대로 사용된다.
3. 빌드에 포함된 휴식 씬 상점은 구매 버튼을 눌러도 로그만 출력하며 재화/소유권을 변경하지 않는다.
4. 저장 파일을 직접 덮어쓰고 예외·백업·마이그레이션 처리가 없어, 한 번의 중단이나 파일 손상으로 시작/저장이 깨질 수 있다.
5. 메인 메뉴는 저장 진행도와 무관하게 항상 첫 에피소드를 시작하며, 활성 에피소드/게임 상태는 저장하지 않는다.

심각도 기준은 다음과 같다.

| 등급 | 의미 | 건수 |
|---|---|---:|
| P1 | 핵심 기능/진행/데이터 정확성 장애. 다음 배포 전에 수정 | 7 |
| P2 | 특정 조건의 교착·성능 급락·상태 오염. 우선 계획 필요 | 8 |
| P3 | 유지보수성·개발 효율·표현 오류 | 4 |

## 2. P1 — 배포 전 필수 수정

### P1-01. 에피소드 2~4의 제조 콘텐츠가 데이터 마이그레이션에서 누락됨

**증거**

- 제조 노드는 `craftingRecipeId`가 비어 있으면 즉시 실패 처리한다: [EpisodeRunner.cs](../Assets/Scripts/Conversation/Episode/EpisodeRunner.cs#L172) 172~182행.
- 누락 노드 수:
  - `StrangeCoin_2`: 2개 — [첫 노드](../Assets/Resources/EpisodeData/EpisodeData_StrangeCoin_2.asset#L353), [두 번째 노드](../Assets/Resources/EpisodeData/EpisodeData_StrangeCoin_2.asset#L1937)
  - `StrangeCoin_3`: 3개 — [30번 노드](../Assets/Resources/EpisodeData/EpisodeData_StrangeCoin_3.asset#L1036), [85번 노드](../Assets/Resources/EpisodeData/EpisodeData_StrangeCoin_3.asset#L2874), [93번 노드](../Assets/Resources/EpisodeData/EpisodeData_StrangeCoin_3.asset#L3172)
  - `StrangeCoin_4`: 3개 — [21번 노드](../Assets/Resources/EpisodeData/EpisodeData_StrangeCoin_4.asset#L594), [27번 노드](../Assets/Resources/EpisodeData/EpisodeData_StrangeCoin_4.asset#L1151), [72번 노드](../Assets/Resources/EpisodeData/EpisodeData_StrangeCoin_4.asset#L2539)
- 위 노드가 참조하는 `sc2_sally`, `sc2_f72`, `sc3_sally`, `sc3_sally_1`, `sc3_e12`, `sc4_f72`, `sc4_sally`, `sc4_e12` 주문표 키도 `OrderTicketDatabase`에 없다. 조회 실패는 별도 오류 없이 티켓을 표시하지 않는다: [OrderTicketManager.cs](../Assets/Scripts/OrderTicket/OrderTicketManager.cs#L63) 63~72행.
- `StrangeCoin_4` 끝에는 ID·텍스트·선택지가 모두 빈 노드가 5개 직렬화돼 있다: [EpisodeData_StrangeCoin_4.asset](../Assets/Resources/EpisodeData/EpisodeData_StrangeCoin_4.asset#L5482).

**영향**

플레이어가 해당 제조 구간에서 실제로 칵테일을 만들 기회를 얻지 못하고 강제로 Bad 경로로 이동한다. 에피소드 결과와 플래그가 의도와 달라진다.

**권장 수정**

- 원본 CSV/그래프에서 8개 노드의 레시피 ID와 주문표를 복원하고 재임포트한다.
- 에디터 메뉴 검증이 아니라 빌드 전 자동 검증을 추가한다. `requiresCrafting == true`인 모든 노드에 대해 레시피 ID 존재, CSV 레시피 존재, 티켓 키 존재, Good/Bad 분기 존재를 검사하고 실패 시 빌드를 중단한다.
- 빈 노드와 중복/빈 ID도 같은 검증에서 거부한다.

### P1-02. 액체 혼합이 재료량 보존 법칙을 위반함

**증거**

[LiquidParticleData.cs](../Assets/Scripts/Bartending/LiquidParticleData.cs#L91) 91~129행의 `MixPair()`는 각 입자의 조성 비율을 상대 입자 비율로 단순 보간한 뒤 기존 입자 총량을 곱한다. 두 입자의 용량이 다르면 전체 재료량이 보존되지 않는다.

예를 들어 100 ml의 A 입자와 10 ml의 B 입자를 `strength = 1`로 섞으면 결과가 100 ml B + 10 ml A가 된다. 혼합 전 100 ml였던 A가 10 ml로 줄고, 10 ml였던 B가 100 ml로 늘어난다. 충돌/교반 경로는 이 메서드를 직접 호출한다: [LiquidReaction.cs](../Assets/MetaballFluid/Scripts/LiquidReaction.cs#L150) 150~160행.

**영향**

잔에 집계되는 보드카/주스 양과 색, 레시피 점수가 물리적 투입량과 달라진다. 플레이 결과의 핵심 판정이 비결정적으로 왜곡된다.

**권장 수정**

- 각 재료의 평형 비율을 `(left 재료량 + right 재료량) / (left 총량 + right 총량)`으로 계산하고 양쪽이 그 평형값으로 수렴하게 한다.
- 또는 실제 교환 용량을 계산해 같은 양을 양방향으로 이동시킨다.
- 10:10, 100:10, 10:100 용량 조합에서 모든 재료량·총량·온도가 보존되는 EditMode 단위 테스트를 추가한다.

### P1-03. 휴식 씬 상점의 구매 기능이 구현되지 않음

**증거**

[ItemSlotUI.cs](../Assets/RestScene/Scripts/ItemSlotUI.cs#L29) 29~33행의 구매 처리는 로그만 출력한다. `RestScene`의 실제 `ShopUIManager`는 많은 상품 에셋과 이 슬롯 Prefab을 참조하므로, 개발 전용 사본이 아니라 빌드에 노출되는 경로다.

현재 상품 데이터 [ItemData.cs](../Assets/RestScene/Scripts/ItemData.cs#L3)는 안정적인 ID나 소유 상태가 없고, 제작 시스템의 `ItemDef`, 술장 시스템의 `LiquorBottleDef`와 별도 모델이다. 이 구조에서는 구매 결과를 재고에 안전하게 연결하기 어렵다.

**영향**

사용자는 가격과 구매 버튼을 보지만 어떤 변화도 얻지 못한다. 상점을 진행 시스템으로 인식하면 명백한 기능 장애다.

**권장 수정**

- 출시 범위가 아니면 상점 진입점과 구매 버튼을 비활성화하고 “준비 중” 상태를 명확히 표시한다.
- 출시 범위라면 공통 안정 ID를 기준으로 상품/제작/술장 데이터를 연결하고, 잔액 확인 → 차감 → 소유권/재고 반영 → 저장을 하나의 트랜잭션으로 구현한다.

### P1-04. 저장 파일이 비원자적이며 손상 복구가 없음

**증거**

- 저장은 대상 파일을 바로 덮어쓴다: [DataManager.cs](../Assets/CoreScene/Scripts/DataManager.cs#L36) 36~38행.
- 로드는 `ReadAllText`와 `FromJson`을 예외 처리 없이 호출하고 결과의 null/스키마도 검증하지 않는다: 같은 파일 61~75행.
- [SaveData.cs](../Assets/CoreScene/Scripts/SaveData.cs#L5)에 `gameVersion`은 있지만 마이그레이션에서 사용되지 않는다.

**영향**

쓰기 도중 종료, 디스크 오류, 수동 편집, 버전 변경으로 JSON이 손상되면 시작 시 예외가 나거나 이후 저장이 null 참조로 실패할 수 있다. 자동 저장 하나뿐이라 복구 경로도 없다.

**권장 수정**

- 임시 파일에 쓴 뒤 flush하고 원본을 원자적으로 교체한다.
- 직전 정상 저장 백업을 유지한다.
- 로드 전체를 예외 처리하고 구조/범위/병렬 리스트 길이를 검증한 뒤, 실패하면 백업 또는 새 데이터로 복구한다.
- `gameVersion`별 명시적 마이그레이션과 실패 로그를 둔다.

### P1-05. 저장 게임을 이어할 수 없고 첫 에피소드가 반복됨

**증거**

- 메인 메뉴 시작 버튼은 조건 없이 `StrangeCoin_0`을 요청한다: [MainMenuManager.cs](../Assets/Scripts/MainMenu/MainMenuManager.cs#L14) 14~17행.
- `CanStart()`는 완료 여부를 검사하지 않으므로 이미 완료한 에피소드도 직접 요청하면 다시 시작된다: [EpisodeManager.cs](../Assets/CoreScene/Scripts/EpisodeManager.cs#L61) 61~85행.
- `StartEpisode()`가 즉시 저장하지만 [SaveData.cs](../Assets/CoreScene/Scripts/SaveData.cs#L5)에는 활성 에피소드 ID나 `GameState`가 없다. 해당 저장으로는 현재 위치를 복원할 수 없다.

**영향**

재실행 후 Start를 누르면 저장된 날짜/완료 상태와 무관하게 도입부가 다시 재생된다. 진행 중 에피소드나 Rest/Business 상태도 복원되지 않는다.

**권장 수정**

- 메인 메뉴에서 새 게임과 이어하기를 구분한다.
- 최소한 `GameState`, 활성 에피소드 ID, 필요한 체크포인트를 저장하고 유효성을 검증한 뒤 복원한다.
- 완료 에피소드 재실행은 명시적인 회상/디버그 경로에서만 허용한다.

### P1-06. 씬 전환이 중복 호출과 실패를 안전하게 복구하지 못함

**증거**

[SceneTransitionManager.cs](../Assets/CoreScene/Scripts/SceneTransitionManager.cs#L54) 54~65행은 전환 중 여부를 확인하지 않고 코루틴을 계속 시작한다. 다음 실패 경로도 있다.

- `fadeCanvasGroup` null 여부를 확인하기 전에 71행에서 접근한다.
- `UnloadSceneAsync()`가 null을 반환할 수 있지만 78행에서 즉시 `isDone`을 읽는다.
- 새 씬 로드가 실패하면 기존 씬을 이미 언로드한 상태에서 88행 `yield break`로 종료한다. 화면은 검게 남고 raycast 차단도 해제되지 않는다.
- Fade는 `Time.deltaTime`을 사용하므로 `timeScale == 0`이면 영구 정지한다.
- [GameManager.cs](../Assets/CoreScene/Scripts/GameManager.cs#L17)는 전환 성공 전에 `CurrentState`를 변경해 실패 시 논리 상태와 실제 씬이 달라진다.

**권장 수정**

- 단일 전환 상태머신/세마포어를 두고 중복 요청의 큐잉·취소 정책을 정한다.
- 대상 씬을 먼저 검증하거나 로드한 뒤 기존 씬을 언로드한다.
- 모든 실패/취소 경로에서 페이드와 raycast를 복원하는 공통 `finally` 성격의 정리를 둔다.
- `Time.unscaledDeltaTime`을 사용하고 성공 콜백에서만 `GameState`를 확정한다.

### P1-07. 제작 아이템 입력 소유권이 없어 다중 선택 및 커서 고착이 가능함

**증거**

`BottleController`, `BeakerController`, `GlassController`, `StirringRodController`가 각각 `Update()`에서 전역 좌클릭을 독립 처리한다.

- [BottleController.cs](../Assets/Scripts/Bartending/BottleController.cs#L266)
- [BeakerController.cs](../Assets/Scripts/Bartending/BeakerController.cs#L133)
- [GlassController.cs](../Assets/Scripts/Bartending/GlassController.cs#L151)
- [StirringRodController.cs](../Assets/Scripts/Bartending/StirringRodController.cs#L97)

겹친 Collider에서는 한 클릭에 여러 오브젝트가 집히거나, 현재 오브젝트를 놓는 같은 프레임에 다른 오브젝트가 집힐 수 있다. 또한 병/비커/잔은 기울이기 시작할 때 커서를 잠그지만 비활성화/파괴 시 해제하지 않는다. 교반봉만 [OnDisable 정리](../Assets/Scripts/Bartending/StirringRodController.cs#L75)를 갖는다. 제작 세션은 모드 변경 시 루트 전체를 파괴하므로 기울이는 중 전환하면 커서가 숨겨진 채 남을 수 있다.

**권장 수정**

- `BartendingInteractionController` 하나가 포인터 hit-test, 현재 캡처 대상, 드래그/회전 상태를 소유하게 한다.
- 커서 잠금은 lease/token 방식의 공용 서비스로 관리하고 모든 `OnDisable`/`OnDestroy`에서 반드시 반환한다.
- UI 위 클릭은 `EventSystem.current.IsPointerOverGameObject()` 등으로 월드 입력에서 제외한다.

## 3. P2 — 높은 우선순위

### P2-01. 액체 입자 연산량이 입자 수에 비례해 급증함

[LiquidReaction.cs](../Assets/MetaballFluid/Scripts/LiquidReaction.cs#L42)는 깨어 있는 각 입자가 `FixedUpdate`에서 0.05초마다 `Physics2D.OverlapCircle`을 수행한다(163~217행). 현재 영업 설정의 300개 입자가 모두 깨어 있으면 초당 약 6,000회의 영역 검색이 발생한다. 각 입자의 [LiquidParticleData.Update](../Assets/Scripts/Bartending/LiquidParticleData.cs#L242)도 냉각을 갱신하고, [LiquidPool.Update](../Assets/MetaballFluid/Scripts/ObjPooling.cs#L87)는 활성 입자를 다시 전부 순회하며 컴포넌트를 조회한다.

교반 검색과 열 갱신을 중앙 시뮬레이터에서 배치하고, 공간 해시/용기 단위 혼합으로 바꾸는 것이 좋다. 프로파일러 기준으로 입자 예산과 프레임 예산을 정해야 한다.

### P2-02. 현재 미커밋 풀 변경은 상한 없이 오브젝트를 생성함

현재 작업 트리의 [ObjPooling.cs](../Assets/MetaballFluid/Scripts/ObjPooling.cs#L32)는 큐가 비면 즉시 새 입자를 생성한다. 최대치/백프레셔/경고가 없어 계속 붓거나 입자가 용기에 남으면 메모리와 물리 부하가 무제한 증가한다. 기존 문서의 “`poolSize` 최대, 소진 시 스킵” 정책과도 반대다.

초기 크기와 최대 크기를 분리하고, 확장은 제한된 chunk로 하며, 최대치 도달 시 방출 억제 또는 가장 오래된 비보존 입자 회수 정책을 명시해야 한다. 생성 수/활성 수/거부 수 메트릭도 필요하다.

### P2-03. 빈 주문 대사는 영업 상태를 영구 정지시킴

[BusinessOrderSessionController.cs](../Assets/Scripts/Business/BusinessOrderSessionController.cs#L121)는 상태를 `PresentingOrder`로 바꾸고, `DialogueClosed` 이벤트에서만 `AwaitingDecision`으로 이동한다. 그러나 [CustomerSpawner.cs](../Assets/Scripts/Conversation/Sell/CustomerSpawner.cs#L86)는 대사 목록이 비면 `HideImmediate()`만 호출하며 닫힘 이벤트를 발생시키지 않는다. 잘못된 `CustomerOrderData` 하나로 주문이 영구 정지한다.

빈 대사를 데이터 검증에서 거부하거나, 표시할 대사가 없으면 컨트롤러가 바로 결정 상태로 이동해야 한다.

### P2-04. 영업 시퀀스가 주문 시작 실패를 무시함

[BusinessSequenceRunner.cs](../Assets/Scripts/Business/BusinessSequenceRunner.cs#L66) 82행은 `BeginOrder()`의 bool 결과와 null 세션을 무시하고 항상 반환한다. 반면 [BusinessOrderSessionController.cs](../Assets/Scripts/Business/BusinessOrderSessionController.cs#L80)는 초기화 누락, 빈 레시피 ID, 잘못된 상태에서 false를 반환한다. [BusinessSequencePlanner.cs](../Assets/Scripts/Business/BusinessSequencePlanner.cs#L24)는 고객 키만 검사하고 빈 레시피 ID도 스냅샷에 넣는다.

현재 샘플 설정은 유효하지만 설정 실수 하나면 시퀀스가 아무 이벤트 없이 멈춘다. 시작 실패 시 오류 UI를 표시하고 항목을 실패 처리/건너뛰거나 재시도 정책을 적용해야 한다.

### P2-05. 자동 생성 싱글턴이 구성 오류를 숨기고 불완전한 매니저를 만듦

[MonoSingleton.cs](../Assets/CoreScene/Scripts/MonoSingleton.cs#L7)는 인스턴스가 없으면 빈 GameObject와 컴포넌트를 자동 생성한다. 따라서 `Instance != null`이나 `Instance?.` 검사는 누락된 CoreScene 구성을 탐지하지 못한다. 직렬화 참조가 필요한 `SceneTransitionManager`가 자동 생성되면 나중에 null 참조로 실패한다.

또한 base `Awake()`가 중복 오브젝트를 파괴해도 파생 `Awake()`는 계속 실행된다. `DataManager`, `EpisodeManager`, `GameProgress`, `AudioManager`의 중복 인스턴스가 파괴 대기 중 로드/자식 생성 같은 부작용을 수행할 수 있다.

`TryGetExistingInstance()`와 명시적 bootstrap 등록을 제공하고, 필수 매니저는 누락 시 즉시 명확한 오류를 내야 한다. base `Awake()`는 성공 여부를 반환하거나 파생 클래스가 중복 여부를 검사해 즉시 return하게 한다.

### P2-06. BGM 교차 페이드 중 Stop하면 두 번째 소스가 계속 재생됨

[AudioManager.cs](../Assets/Scripts/Audio/AudioManager.cs#L45)는 진행 중인 교차 페이드를 중단한 뒤 `_activeSource`만 페이드아웃한다. 이미 재생을 시작한 `_inactiveSource`는 부분 볼륨으로 계속 재생되며 source swap도 일어나지 않는다(53~77행).

페이드 취소 시 양쪽 소스의 현재 상태를 정규화하고, Stop은 두 소스를 모두 중단/정리해야 한다. 페이드는 unscaled time 또는 오디오 DSP 시간 기준이 안전하다.

### P2-07. 추가 재료 허용 오차가 합계가 아니라 항목별로 누락 계산됨

[CocktailEvaluator.cs](../Assets/Scripts/Bartending/CocktailEvaluator.cs#L253) 263~275행은 각 추가 재료가 `recipe.toleranceMl` 이하이면 합계에서 완전히 제외한다. 예를 들어 허용 오차 8 ml인 레시피에 서로 다른 추가 재료를 8 ml씩 두 개 넣으면 16 ml가 들어가도 extra penalty는 0이다. 총량 범위 안에만 들면 성공할 수 있다.

모든 허용되지 않은 추가 재료량을 먼저 합산한 후 전역 허용 오차와 비교하거나, “항목별 허용”이 의도라면 CSV에 별도 정책으로 명시해야 한다.

### P2-08. 빌드 입력에 테스트 단축키와 UI 클릭 전파가 남아 있음

[InputRouter.cs](../Assets/Scripts/Input/InputRouter.cs#L20)에는 숫자 1로 테스트 손님을 생성하고 숫자 2로 조건 없이 테스트 에피소드를 실행하는 코드가 빌드 경로에 남아 있다(37~43행). 좌클릭은 UI 위 여부를 검사하지 않고 대사를 진행하므로 주문/도감 버튼 클릭이 대사 넘김으로 함께 처리될 수 있다.

테스트 입력은 `UNITY_EDITOR || DEVELOPMENT_BUILD`로 제한하고, 입력 라우터에서 UI 이벤트 소비 여부를 먼저 확인해야 한다.

## 4. P3 — 유지보수 개선

### P3-01. 실제 자동 테스트와 어셈블리 경계가 없음

Unity Test Framework 패키지는 있지만 `*Test.cs`/`*Tests.cs` 파일과 프로젝트 `.asmdef`는 없다. 154개 스크립트가 사실상 큰 런타임/에디터 어셈블리에 묶여 있어 회귀 검출과 컴파일 격리가 어렵다. 현재 `BartendingSystemValidator`는 수동 메뉴 검증이며 CI 테스트를 대체하지 못한다.

우선순위가 높은 테스트는 저장 손상 복구, 에피소드 데이터 무결성, 액체 질량 보존, 레시피 판정 경계값, 영업 상태 전이, 씬 전환 중복 호출이다.

### P3-02. 레시피 검색은 화면 전환만 있고 결과 데이터가 없음

[RecipeSearchUI.cs](../Assets/Scripts/RecipeBook/RecipeSearchUI.cs#L113)는 옵션 클릭 시 제목/색과 View만 바꾸며 칵테일 결과를 만들거나 필터링하지 않는다. 이는 [기존 문서](ui/recipe-book-search.md)에도 “틀만 구현”으로 기록된 알려진 미완성 기능이다. 출시 범위가 아니면 진입점을 숨기고, 범위라면 CSV 카탈로그와 단일 데이터 소스로 연결해야 한다.

### P3-03. 문서와 코드의 구조가 일부 불일치함

[architecture.md](core/architecture.md)는 `GameModeManager`를 `MonoSingleton`으로, 수치 API를 `GetVar/SetVar/AddVar`로, 아이템 공급을 `ShelfUI/DrawerUI`로 설명한다. 실제 코드는 `SceneSingleton`, `GetAffinity/SetAffinity/AddAffinity`, 동적 `BusinessBartendingBootstrap`/`LiquorShelfUI` 경로다. 새 개발자가 폐기된 경로를 다시 사용할 위험이 있다.

### P3-04. 소규모 프레임/표현 비용과 상태 드리프트

- [FullSizeQuad.cs](../Assets/MetaballFluid/Scripts/FullSizeQuad.cs#L23)는 카메라/해상도 변경 여부와 상관없이 매 프레임 scale을 다시 계산한다.
- [CharacterView.cs](../Assets/Scripts/Presentation/CharacterView.cs#L132)는 표정 교체 시 기본 이미지 aspect ratio만 갱신하고 overlay aspect ratio는 갱신하지 않는다.
- [DialogueController.cs](../Assets/Scripts/Conversation/DialogueController.cs#L178)는 문자열을 글자마다 이어 붙여 긴 대사에서 할당량이 커지고, `soundEveryNChars == 0`이면 221행에서 0으로 나누며, null 대사는 187행에서 예외가 난다. TMP의 `maxVisibleCharacters`와 입력값 clamp를 권장한다.

## 5. 현재 미커밋 변경에 대한 별도 메모

리뷰 시점 작업 트리는 이미 수정된 상태였으며, 본 리뷰는 이를 되돌리거나 수정하지 않았다.

변경 파일:

- `Assets/Editor/BartendingSystemValidator.cs`
- `Assets/MetaballFluid/Scripts/ObjPooling.cs`
- `Assets/Prefabs/Glass.prefab`
- `Assets/Scenes/Sample_Scene.unity`
- `Assets/Scripts/Bartending/BottleController.cs`
- 새 파일 `BottlePivotTestPanel.cs` 및 `.meta`

주의점:

1. `ObjPooling.cs`의 무제한 확장은 P2-02에 설명한 성능/메모리 회귀다.
2. `BottleController`의 가상 pivot 경로는 `Update()`에서 `Rigidbody2D.position/rotation`을 직접 대입한다. 연속 충돌 sweep을 보장하려면 물리 tick의 `MovePosition/MoveRotation` 또는 kinematic 목표를 사용해 실제 터널링을 검증해야 한다.
3. `BottlePivotTestPanel`은 일반 런타임 어셈블리에 들어가는 `OnGUI` 디버그 UI다. 현재는 빌드 씬이 아닌 `Sample_Scene`에만 연결됐지만, Editor/Development 조건부 컴파일 또는 전용 테스트 어셈블리로 격리하는 편이 안전하다.
4. 병/비커/잔의 커서 해제 문제는 pivot 변경과 무관하게 공통 베이스/서비스에서 함께 해결하는 것이 좋다.

## 6. 검증 결과

| 검증 | 결과 |
|---|---|
| `dotnet build Assembly-CSharp.csproj --no-restore` | 성공, 경고 0 / 오류 0 |
| `dotnet build Assembly-CSharp-Editor.csproj --no-restore` | 성공, 경고 0 / 오류 0 |
| Build Settings | MainMenu, Core, Business, Rest 4개 씬 활성 |
| 빌드 씬 Script GUID | 프로젝트/PackageCache에서 모두 해석됨 |
| 명시적 `m_Script: {fileID: 0}` | 발견되지 않음 |
| 에피소드 노드 참조 | 비어 있지 않은 node reference는 모두 대상 노드가 존재 |
| 캐릭터/레시피 ID | 비어 있지 않은 기존 참조는 해석됨. 단, 제조 레시피 필드 자체가 8곳 누락 |
| 주문표 ID | 에피소드 2~4의 8개 키 누락 |
| 자동 테스트 | 없음 |

## 7. Unity 오류 창/크래시 기록과의 관계

리뷰 시점에 `Crash_2026-08-05_112137224/crash.dmp`가 존재했다. 앞서 표시된 Windows 오류의 `0x80000003`은 managed C# 예외 메시지가 아니라 네이티브 breakpoint 계열 예외다. 재시작 후 현재 `Editor.log`에는 managed exception이나 Missing Script는 없었고, `Assets/MetaballFluid/Sprites/metaball_4 1.png.new`에 대한 SourceAssetDB 수정 시간 불일치 Import Error 1건이 기록돼 있었다.

따라서 이 리뷰만으로 Windows 오류 창의 직접 원인을 특정할 수는 없다. 액체 입자 부하는 크래시 위험을 높이는 코드 경로이지만 원인이라고 단정할 스택 증거는 없다. 다음 발생 시 Unity 버그 리포터 로그/심볼이 포함된 dump 분석과 재현 직전 Editor.log를 보존해야 한다.

## 8. 권장 처리 순서

1. 에피소드 2~4 제조 데이터/티켓 복원 후 자동 데이터 검증 추가.
2. `LiquidPayload.MixPair` 보존식 수정 및 단위 테스트 추가.
3. 미출시 기능(상점·레시피 결과)을 숨기거나 최소 완성 조건 충족.
4. 원자적 저장/백업/마이그레이션과 이어하기 흐름 구현.
5. 씬 전환 상태머신과 제작 입력/커서 소유권 통합.
6. 액체 시뮬레이션을 배치화하고 풀 최대치·성능 예산 도입.
7. 주문/오디오/판정의 P2 경계 조건 테스트 추가.
8. Runtime/Core/Business/Bartending/Editor 단위로 `.asmdef`를 나누고 문서 갱신.

## 9. 잘된 점

- 공용 `BusinessOrderSessionController`로 영업과 에피소드 제작 후처리를 분리한 방향은 확장성이 좋다.
- 데이터 카탈로그가 대소문자 비구분 ID 조회와 중복 경고를 제공한다.
- 영업 진행 스냅샷을 주문 경계에서 저장하고 이벤트 구독을 `OnDestroy`에서 해제하는 흐름은 명확하다.
- `GameModeManager`를 씬 범위 싱글턴으로 바꾼 것은 씬 로컬 직렬화 참조 수명과 맞는다.
- 컴파일과 빌드 씬 스크립트 연결 상태는 깨끗하다.
