# RestScene 시스템

RestScene은 에피소드 선택, 상점, 현황판을 제공하는 휴식 씬입니다.
`BusinessScene` 완료 후 자동 전환되며, 다음 에피소드를 시작하는 분기점입니다.

## 씬 진입/이탈 흐름

```
BusinessScene 종료
  └─ EpisodeManager.ClearEpisode()
       └─ GameManager.ChangeState(GameState.Rest)
            └─ SceneTransitionManager → RestScene 로드

RestScene
  └─ 플레이어가 에피소드 보드에서 에피소드 선택 → 시작 버튼
       └─ EpisodeManager.StartEpisode(id)
            └─ GameManager.ChangeState(GameState.Episode)
                 └─ SceneTransitionManager → BusinessScene 로드
```

## 스크립트 구조

```
BaseUIManager (추상)
  ├─ EpisodeBoardManager   — 에피소드 보드
  ├─ EpisodeUIManager      — 현황판 (돈, 영업 상태)
  └─ ShopUIManager         — 상점

ObjectInteraction           — 씬 오브젝트 클릭 → BaseUIManager 토글
ObjectInteractionBoard      — ObjectInteraction과 동일 (보드 전용)
EpisodePhotoTrigger         — 에피소드 보드 내 개별 사진 슬롯
EpisodeInfoUI               — 사진 호버/클릭 시 표시되는 상세 정보 팝업
BoardBackground             — 보드 배경 클릭 → 선택 초기화

TooltipManager              — 전역 싱글톤 툴팁 관리
TooltipTrigger              — 개별 UI 요소의 툴팁 트리거
TooltipPopup                — 툴팁 팝업 UI 구현체

ItemData                    — 상점 아이템 ScriptableObject
ItemSlotUI                  — 상점 아이템 슬롯 UI
CategoryButton              — 상점 카테고리 필터 버튼
ExitButton                  — 팝업 닫기 버튼 (상점에는 더 이상 사용하지 않음, 다른 팝업엔 남아있음)
ShopLoadingScreen           — 상점 최초 오픈 1회 로딩 연출
ScrollContentMinHeight      — ScrollRect Content 최소 높이(=뷰포트) 보정, Content Size Fitter 대체
RecipeBookDef                — 상점 레시피북 ScriptableObject
RecipeBookSlotUI             — 상점 레시피북 슬롯 UI
IShopCurrency / MoneyShopCurrency / StrangeCoinShopCurrency — 상점 결제 수단 추상화(ShopCurrency.cs)
```

---

## BaseUIManager (`Assets/_Project/Features/Rest/Runtime/BaseUIManager.cs`)

모든 UI 패널의 공통 기반 추상 클래스. `CanvasGroup` 필수.

| 메서드 | 설명 |
|---|---|
| `OpenUI()` | 다른 열린 팝업을 전부 `CloseUI()` → `gameObject` 활성화 → `OnOpen()` → `AnimateOpen()` |
| `CloseUI()` | `AnimateClose()` → `OnClose()` |
| `AnimateOpen()` | (추상) 자식이 직접 구현 |
| `AnimateClose()` | (추상) 자식이 직접 구현 |
| `OnOpen()` | (가상) 열릴 때 초기화 — 기본 빈 함수 |
| `OnClose()` | (가상) 닫힐 때 정리 — 기본 빈 함수 |
| `LockTransitions()` / `UnlockTransitions()` | (정적, protected) 잠긴 동안 모든 `OpenUI()` 호출이 즉시 no-op — 진행 중인 연출(예: 상점 최초 로딩) 도중 다른 팝업으로 전환되는 것을 막을 때 사용. `CloseUI()`는 막지 않으므로 잠긴 상태에서도 자기 자신은 닫을 수 있음 |

- Awake에서 `CanvasGroup.alpha = 0`, `interactable = false`, `gameObject.SetActive(false)` 초기화
- 진행 중인 코루틴은 항상 중단 후 재시작 (`_activeCoroutine`)
- **상호 배타**: 정적 `HashSet<BaseUIManager>`로 현재 열린 인스턴스를 추적. `OpenUI()`가 호출되면 자기 자신을 제외한 나머지를 자동으로 `CloseUI()` — 에피소드 보드/현황판/상점 등 이 클래스를 상속하는 모든 RestScene 팝업이 하나만 열려있도록 별도 조율 코드 없이 보장됨. `OnDestroy()`에서 자신을 목록에서 제거해 씬 언로드 후 낡은 참조가 남지 않게 함

---

## 에피소드 보드 시스템

### EpisodeBoardManager (`Assets/_Project/Features/Rest/Runtime/EpisodeBoardManager.cs`)

`BaseUIManager` 상속. 에피소드 보드 전체를 관리합니다.

**애니메이션**: 아래에서 위로 슬라이드 (`slideDistance = 150f`, `animDuration = 0.3f`)
- `Mathf.SmoothStep` 이징 적용, `Time.unscaledDeltaTime` 사용

**핵심 메서드**

| 메서드 | 설명 |
|---|---|
| `OnOpen()` | 항상 `RefreshBoard()` + `ResetBoard()` — 필수 에피소드 게이트가 있어도 사진은 계속 노출됨(아래 참고) |
| `RefreshBoard()` | `EpisodeManager.GetBoardEpisodes()`로 표시할 에피소드 목록 확보, 동적 프리팹 생성/제거 |
| `PinEpisode(photo)` | `PinnedPhoto` 설정. `IsMandatoryGateActive()`이면 그 에피소드의 `IsPlayable()` 결과와 무관하게 무조건 `Inactive`, 아니면 `IsPlayable()` 결과로 `Episode`/`Inactive` 상태 결정 |
| `ResetBoard()` | 이전 사진 `Unpin()`. `TryGetForcedDay1Episode()`가 true면 `Inactive`(빈 텍스트, 아래 "1일차 예외" 참고), 아니면 `Business`("영업") — 필수 에피소드 게이트는 이 분기에 영향을 주지 않음(아래 참고) |
| `OnStartButtonClicked()` | `PinnedPhoto != null`이면(`IsMandatoryGateActive()`가 아닐 때만) `DayFlowController.StartDefaultEpisode(id)`, null이면(미선택 상태, `TryGetForcedDay1Episode()`가 아닐 때만) `DayFlowController.StartBusinessDay()` → 이후 `CloseUI()`. 두 체크 모두 버튼이 이미 비활성화되어 있어야 정상이지만 방어적으로 재확인함 |
| `IsMandatoryGateActive()` | `HasPendingMandatoryEpisode()`(오늘 발동 조건 충족) 또는 `HasUpcomingMandatoryEpisode()`(내일, 즉 다음 영업 시작 시점에 발동 예정)가 true면 게이트 활성 |
| `TryGetForcedDay1Episode(out photo)` | 아래 "1일차 예외" 참고 |

**시각 상태 3종** (`ApplyBoardVisualState()`, `BoardVisualState` enum) — 시작 버튼(`startButtonImage`)과 하단 텍스트 박스(`bottomTextboxImage`)의 스프라이트·텍스트 색을 한 곳에서 일괄 적용

| 상태 | 발생 조건 | 텍스트 |
|---|---|---|
| `Inactive` | 필수 에피소드 게이트 활성 / 1일차 영업 금지 예외 / 클릭한 기본 에피소드가 조건 미달성 | 게이트·1일차 예외는 빈 문자열, 조건 미달성은 에피소드 제목(`textColorInactive`, 회색) |
| `Business` | 아무것도 선택 안 한 기본 상태(영업 가능) | "영업"(`textColorBusiness`, 초록) |
| `Episode` | 플레이 가능한 기본 에피소드를 선택 | 에피소드 제목(`textColorEpisode`, 주황) |

**필수 에피소드 게이트**: 과거엔 `ShowMandatoryGate()`가 보드의 사진을 전부 `Destroy`해서 아예 안 보이게 했으나, 지금은 기본 에피소드 사진이 **항상 그대로 노출**되고 클릭해도 `Inactive`로만 표시되어 진행이 막힌다(선택은 막되 존재는 보여줌). "아무것도 선택 안 함" 기본 상태(`ResetBoard()`)는 게이트와 무관하게 항상 `Business`("영업")로 남는다 — 게이트의 목적은 기본 에피소드 선택을 막아 영업(그 안에서 자동으로 먼저 실행되는 필수 에피소드)으로 유도하는 것이지 영업 자체를 막는 게 아니기 때문. `DayFlowController.StartBusinessDay()`가 `AdvanceDay()` 이후 필수 에피소드를 자동으로 큐잉하므로, 영업 시작 경로는 게이트 중에도 항상 열려 있어야 한다.

**게이트 lookahead**: `HasUpcomingMandatoryEpisode()`(`EpisodeManager.GetNextMandatoryEpisode(dayOffset: 1)`)가 "내일(다음 영업 시작으로 day가 오른 직후) 발동될 필수 에피소드가 있는지"를 하루 앞당겨 체크한다. 그래서 목표일 정확히 하루 전 보드부터 게이트가 걸려 기본 에피소드로 새치기당하지 않는다. 필수 에피소드의 `MinDay`는 실제 발동을 원하는 날짜 그대로 입력하면 된다(우회책으로 하루 앞당겨 넣을 필요 없음). 자세한 것은 [game-flow-design.md](../core/game-flow-design.md) 참고.

**1일차 예외** (`day1ForcedEpisodeId`, 기본값 `"FathersNote"`): 게임 최초 진입일(`GameProgress.CurrentDay == 1`)의 보드에서만 예외적으로 "아무것도 선택 안 함(영업)" 상태를 막고, 지정된 기본 에피소드를 반드시 먼저 선택하도록 강제한다. 보드에 그 에피소드 사진이 없거나 아직 `IsPlayable()`이 false면(데이터 이상 등) 자동으로 영업 허용으로 폴백해 소프트락을 방지한다. `DayFlowController.endingEpisodeId`와 같은 패턴(인스펙터 노출 문자열로 특정 episodeId 지정).

**RefreshBoard() 상세 흐름**

1. `GetBoardEpisodes()` + `EpisodePhotoTrigger.HasBoardPhoto()` 필터로 표시 대상 목록 확정
2. 기존 자식 `EpisodePhotoTrigger` 순회 — 더 이상 표시 불필요한 항목은 `GameProgress.ClearBoardSlot(id)` 후 `Destroy`
3. `GameProgress.GetBoardSlot(id) - 1`로 이미 자리가 배정된 에피소드 슬롯 예약 (저장값은 1-indexed)
4. 자리 없는 신규 에피소드에 빈 슬롯 무작위 배정 → `GameProgress.SetBoardSlot(id, idx+1)` 저장
5. 프리팹이 이미 존재하면 부모만 올바른 슬롯으로 이동, 없으면 `photoPrefab` 인스턴스 생성

**Inspector 직렬화 필드**

```csharp
public GameObject photoPrefab;        // 에피소드 사진 프리팹
public Transform[] boardSlots;        // 고정된 6개 슬롯 위치
public TextMeshProUGUI bottomEpisodeNameText;
public Button startButton;
public Image startButtonImage;        // 버튼 스프라이트 변경용
public Image bottomTextboxImage;      // 하단 텍스트 박스 배경 스프라이트 변경용
public Sprite buttonSpriteInactive, buttonSpriteBusiness, buttonSpriteEpisode;
public Sprite textboxSpriteInactive, textboxSpriteBusiness, textboxSpriteEpisode;
public Color textColorInactive, textColorBusiness, textColorEpisode;
public string day1ForcedEpisodeId;    // 기본값 "FathersNote"
```

### EpisodeManager — 보드/필수 에피소드 관련 메서드

| 메서드 | 설명 |
|---|---|
| `GetBoardEpisodes()` | `episodeType == Default`이고 완료되지 않은 에피소드 중 `IsUnlocked()` 통과한 목록 반환. 필수(Mandatory) 에피소드는 포함되지 않음 — `DayFlowController`가 별도로 자동 큐잉 |
| `GetAvailableEpisodes()` | `GetBoardEpisodes()`와 동일 조건(현재 중복, 통합 여지 있음) |
| `IsUnlocked(ep, gp, dayOffset = 0)` | `ep.triggerCondition`(해금 조건)을 `gp.CurrentDay + dayOffset` 기준으로 평가 — `dayOffset=1`이면 "내일" 기준으로 미리 평가(게이트 lookahead용). day 비교 외 조건(플래그/변수 등)은 항상 `gp`의 실시간 상태 사용 |
| `IsPlayable(ep, gp)` | `ep.playCondition`(플레이 조건) 평가 — 만족해야 Play 버튼 활성화. 조건 없으면 항상 true |
| `GetNextMandatoryEpisode(dayOffset = 0)` | `episodeType == Mandatory`이고 완료되지 않았으며 같은 챕터인 것 중 `IsUnlocked(ep, gp, dayOffset)`을 만족하는 첫 번째 항목 반환 |
| `HasPendingMandatoryEpisode()` | `GetNextMandatoryEpisode(0) != null` — 오늘(발동 체크용) |
| `HasUpcomingMandatoryEpisode()` | `GetNextMandatoryEpisode(1) != null` — 내일(게이트 lookahead용) |

해금 조건과 플레이 조건은 독립적: 해금은 됐지만 플레이 조건 미달이면 보드엔 뜨되 Play 버튼은 비활성 상태로 남음. 자세한 내용은 [game-flow-design.md](../core/game-flow-design.md) 참고.

---

### EpisodePhotoTrigger (`Assets/_Project/Features/Rest/Runtime/EpisodePhotoTrigger.cs`)

에피소드 보드의 개별 사진 슬롯. `IPointerEnterHandler`, `IPointerExitHandler`, `IPointerClickHandler` 구현.

**상태**
- 기본: `normalSprite`, 오버레이 없음
- 호버: `hoverSprite`로 교체, `EpisodeInfoUI.Show()` (다른 사진이 이미 pin된 경우 호버 무시)
- 핀(클릭): `selectedSprite`로 교체, `EpisodeInfoUI.Show()` 유지, `EpisodeBoardManager.PinEpisode()` 호출
  - 다른 사진이 이미 pin된 상태에서 클릭하면 그 사진을 먼저 `Unpin()`한 뒤 새 사진을 pin (포커스 전환)
- 언핀(pin된 사진을 재클릭): `isPinned = false` + `EpisodeBoardManager.ResetBoard()` 호출 → 보드가 "영업" 기본 상태로 복귀

**`SetEpisodeData(EpisodeData data, EpisodeBoardManager board)`**
- `data.iconNameBoard`로 `Resources.Load<Sprite>("Rest/Sprites/EpisodeBoard/{name}")` 시도
- `Image` 컴포넌트 우선, 없으면 `SpriteRenderer` 대체

**`static HasBoardPhoto(EpisodeData ep)`**
- `ep.iconNameBoard`가 있으면 그 이름으로, 없으면 `ep.episodeId.Replace("_", "-")`를 기본 이름으로 사용
- `Resources/Rest/Sprites/EpisodeBoard/{baseName}-idle` 스프라이트 로드 성공 여부로 표시 가능 여부 판단
- 보드에 표시하려면 `Resources/Rest/Sprites/EpisodeBoard/`에 `{baseName}-idle/hover/selected` 3종 스프라이트 필요

---

### EpisodeInfoUI (`Assets/_Project/Features/Rest/Runtime/EpisodeInfoUI.cs`)

사진 옆에 표시되는 에피소드 상세 정보 팝업. `BaseUIManager` 미상속, 독립 활성화. 자체 `Canvas`를 `overrideSorting`으로 추가해 `sortingOrder`를 높여 항상 최상단에 렌더링 — 이 중첩(nested) Canvas는 **`GraphicRaycaster`도 같이 붙여야** 그 하위 UI(토글 등)가 포인터 클릭을 받는다(`Awake()`에서 없으면 자동으로 `AddComponent`). 안 붙이면 상위 루트 Canvas의 raycaster가 있어도 이 중첩 Canvas 하위 Graphic들은 클릭 대상에서 빠지는 Unity UI의 잘 알려진 함정이라, 다른 화면에 비슷하게 sortingOrder용 중첩 Canvas를 추가할 때도 주의할 것. `LiquorBottleInfoCard`와 동일하게 **런타임 `Instantiate`/`Destroy` 없이** 에디터에서 미리 배치한 고정 슬롯 배열을 채우는 방식으로 구성(조건 행 개수·초상화 슬롯 개수는 에디터에 배치한 배열 길이만큼). 세로 길이는 `VerticalLayoutGroup`+`ContentSizeFitter`로 조건 개수에 따라 유동적으로 늘어남.

| 메서드 | 설명 |
|---|---|
| `Show(data, targetPhoto)` | 헤더/설명/조건 행/토글/초상화 갱신 후 위치 계산 |
| `Hide()` | `gameObject.SetActive(false)` |

**레이아웃 순서 (위 → 아래)**
1. `boardImage`(작전판 사진과 동일한 idle 스프라이트, `EpisodePhotoTrigger.GetIdleSprite()` 재사용) + `episodeNameText`(제목) — 가로 배치
2. `descriptionText` — `data.episodeDescription`
3. **해금/플레이 조건** (`triggerSection` + `triggerConditionRows`) — `data.triggerConditionEntries` + `data.playConditionEntries` 기준, 조건이 하나도 없으면 섹션 자체를 숨김
4. **선택 조건** (`selectSection` + `selectConditionGroups`) — `data.selectConditions`(리스트) 기준. 아래 참고
5. **초상화** (`portraitSlots`) — 선택된 옵션 유무에 따라 결정. 아래 참고

**해금/플레이 조건 행 로직** (`ConditionRow { container, lockIcon, label }`, `triggerConditionRows`)
- `data.triggerConditionEntries`(TRIGGER, 해금 조건)와 `data.playConditionEntries`(PLAY_TRIGGER, 플레이 조건)를 이어붙여 `BuildTriggerConditionEntries()`로 한 목록으로 만들어 표시(TRIGGER 행들 다음 PLAY_TRIGGER 행들 순서)
- 각 `TriggerConditionEntry { condition: SelectSingleCondition, conditionText }`는 `EpisodeManager.EvaluateSelectCondition(entry.condition, gp)`로 **개별** 평가되어 각자의 충족 여부에 따라 `lockIcon.sprite`가 `unlockedSprite`/`lockedSprite`로, `label.color`가 `conditionMetTextColor`(충족, 노랑)/`conditionUnmetTextColor`(미충족, 회색)로 결정됨(전체 해금 여부가 아니라 항목별로 자물쇠·글자색이 따로 매겨짐)
- `label.text`는 `entry.conditionText`가 있으면 그걸, 없으면 `BuildSelectConditionText()`가 조건 타입에서 자동 생성("N일차 이상", 플래그명, "선행 에피소드 '제목'", `varName 연산자 threshold`, "소지금 N원 이상")
- CSV의 `TRIGGER`/`PLAY_TRIGGER`는 각각 `EpisodeCsvImporter`가 평가용 `data.triggerCondition`/`data.playCondition`(AND 결합, `blockedFlags`/`requiredCustomerAppearances` 포함 — `IsUnlocked`/`IsPlayable` 평가에 계속 사용됨)과 툴팁 표시용 `triggerConditionEntries`/`playConditionEntries`(`MinDay`/`RequiredFlag`/`PrerequisiteEpisode`/`RequiredVar`/`MinMoney` 5종 조건 + 텍스트, CSV 행 순서 보존)를 동시에 채움. `blockedFlags`/`requiredCustomerAppearances`는 CSV 행으로 표현할 수 없어 툴팁에는 표시되지 않음(인스펙터 직접 입력만 가능)
- 표시할 조건 개수보다 `triggerConditionRows` 배열이 길면 남는 행은 비활성화

**선택 조건 (`data.selectConditions: List<SelectConditionEntry>`)**
- `SelectConditionEntry { condition, flag, conditionText, revealCondition, hiddenText, characterOverrides }` — 옵션 하나 = 조건 **하나**(`SelectSingleCondition`) + 플래그 + 커스텀 힌트 한 줄 + 공개 조건 + 비공개 시 텍스트 + 초상화 override. 해금 조건과 달리 옵션 하나에 여러 조건을 AND로 걸 수 없음 — 조건을 여러 개 걸고 싶으면 옵션을 여러 개로 나눠서 표현
- `SelectSingleCondition { type, minDay, requiredFlag, prerequisiteEpisodeId, varName, varOp, varThreshold, minMoney }` — `SelectConditionType`(`None`/`MinDay`/`RequiredFlag`/`PrerequisiteEpisode`/`RequiredVar`/`MinMoney`) 하나로 어떤 조건인지 결정, 나머지 필드 중 해당 타입에 대응하는 값만 사용. `MinMoney`는 `gp.CurrentMoney`(affinity가 아니라 `GameProgress`의 별도 소지금 필드)와 비교
- 옵션당 UI도 행 하나(`SelectConditionGroup.conditionRow: ConditionRow`, 배열이 아님) — 자물쇠 아이콘 하나 + 설명 한 줄 + 토글 하나로 고정. 텍스트는 `entry.conditionText`가 있으면 그걸, 없으면 `BuildSelectConditionText()`가 조건 타입에서 자동 생성("N일차 이상", 플래그명, "선행 에피소드 '제목'", `varName 연산자 threshold`, "소지금 N원 이상" 등)
- **공개 조건 (`entry.revealCondition: SelectSingleCondition`)** — "선택 조건의 내용이 플레이어에게 공개되는 조건"(선택 가능 여부 `condition`과는 별개 층). 타입과 필드 구조는 `condition`과 동일하고 `EpisodeManager.EvaluateSelectCondition()`을 그대로 재사용해 평가. 기본값(`None`)은 항상 공개(기존 데이터와 동일하게 동작). 미충족이면 `ApplySelectConditionRow()`가 조건 텍스트 대신 `entry.hiddenText`(비어있으면 `"???"`로 폴백)를 보여주고 자물쇠 아이콘도 잠김으로 고정 표시(실제 `condition` 충족 여부와 무관) — 공개 조건이 충족되는 순간 `hiddenText`에서 `conditionText`(또는 자동 생성 문구)로 전환됨
- `해금 조건`/`playCondition`과 완전히 독립 — **Play 버튼 활성화에는 전혀 영향을 주지 않음**. 어떤 옵션도 미충족/미선택이어도 에피소드는 평소대로 플레이 가능
- **옵션끼리 상호 배타적** — `selectToggleGroup`(유니티 내장 `ToggleGroup`)에 모든 옵션의 `Toggle`을 묶어서, 하나를 켜면 나머지는 자동으로 꺼짐. `Awake()`에서 `group.toggle.group = selectToggleGroup`로 한 번만 연결
- `EpisodeInfoUI.selectConditionGroups[i]`가 `data.selectConditions[i]`와 인덱스로 1:1 매칭(고정 슬롯, 배열 길이보다 옵션이 적으면 남는 슬롯은 컨테이너까지 비활성화)
- 옵션마다 `EpisodeManager.EvaluateSelectCondition(entry.condition, gp)`로 개별 충족 여부 평가. 충족 시에만 그 옵션의 토글이 인터랙션 가능(미충족이면 off 고정, 비활성화). `entry.flag`가 비어있으면 그 옵션은 토글 UI 자체를 숨김(조건 행만 정보 표시용으로 남음)
- **아이콘 3-상태** (`ApplySelectIconAndText()`): `lockIcon`(선택 사항, 미할당이면 스킵) 스프라이트를 미해금=`toggle.spriteState.disabledSprite`, 해금+선택(on)=`toggle.spriteState.selectedSprite`, 해금+미선택(off)=`Awake()`에서 캐싱해둔 프리팹 원본 스프라이트로 갱신. `Toggle` 컴포넌트 자체의 Sprite Swap Transition으로 이미 시각적 전환이 되는 경우 `lockIcon`을 비워둬도 무방함. 텍스트 색은 해금+선택(on)일 때만 `conditionMetTextColor`(노랑), 그 외엔 `conditionUnmetTextColor`(회색). 토글을 클릭해 on/off가 바뀔 때도(`OnSelectToggleChanged`) 즉시 재적용됨
- 토글 초기값은 옵션별로 `GameProgress.HasFlag(entry.flag)`
- 실제 `GameProgress.SetFlag()`/`ClearFlag()` 반영은 툴팁에서 즉시 일어나지 않고, **`EpisodeBoardManager.OnStartButtonClicked()`에서 Play 버튼을 누르는 시점**에 `EpisodeInfoUI.IsSelectOptionOn(i)`를 옵션별로 순회하며 적용(`ApplySelectConditionFlag()`)

**초상화** (`portraitSlots: PortraitSlot[]`, `List<CharacterDisplay>` 기준)
- `PortraitSlot { container, characterImage }` — `container`는 배경 이미지가 이미 붙어있는 슬롯 루트, `characterImage`는 캐릭터 스프라이트를 넣을 자식 `Image`. 캐릭터가 3개 이하면 왼쪽(0번 슬롯)부터 채우고, 남는 슬롯은 **배경까지 포함해 컨테이너 전체를 비활성화**(자식 이미지만 숨기면 배경이 계속 보여서 unknown 취급되는 버그가 있었음)
- 현재 켜져 있는 옵션(`GetSelectedIndex()`)이 있고 그 옵션의 `characterOverrides`가 채워져 있으면 그 리스트를, 아니면 옵션 미선택 시 기본값인 `data.characters`를 그대로 사용(`GetActiveCharacterList()`)
- 즉 **옵션마다 서로 다른 등장인물 조합을 지정 가능** — 옵션 A는 캐릭터를 공개, 옵션 B는 비공개(???), 옵션 C는 다른 캐릭터로 교체 등 자유롭게 구성
- `CharacterDisplay.isHidden`이면 `unknownPortrait`(???) 표시, 아니면 `characterPortraitSprites: CharacterPortraitSprite[]`(`{ characterKey, sprite }` 쌍, 인스펙터에 직접 등록)에서 `characterName`으로 조회. `Resources.Load` 경로 추측 방식은 폐기됨(초상화 스프라이트가 `Assets/_Project/Features/Rest/Art/Sprites/EpisodeBoard/character/`처럼 `Resources` 폴더 밖에 있어 애초에 못 찾았음) — 새 캐릭터를 추가하면 `characterPortraitSprites`에 키-스프라이트 쌍을 등록해야 함
- 어떤 토글이든 값이 바뀔 때마다(`OnSelectToggleChanged`) 초상화만 즉시 갱신

**위치 계산** (`UpdatePosition`)
- `targetPhoto`의 월드 오른쪽 중앙 기준 오른쪽 10px에 배치
- `LayoutRebuilder.ForceRebuildLayoutImmediate()` 후 위치 갱신, 캔버스 오른쪽 경계를 넘으면 왼쪽 배치로 자동 전환

**CSV/그래프 연동**: `data.selectConditions`는 CSV `#SELECT_TRIGGER` 섹션(옵션 하나당 한 행), `NarrativeGraphSO.SelectConditions`와 컴파일러/임포터로 왕복 가능. 자세한 CSV 문법은 [episode-csv-guide.md](../narrative/episode-csv-guide.md#select_trigger) 참고. 단 `characterOverrides`(옵션별 초상화)는 `characters`(기본 초상화)와 마찬가지로 CSV/그래프를 거치지 않고 `EpisodeData` 에셋에 직접 입력 — CSV 재임포트 시에도 `flag` 이름으로 기존 값을 찾아 보존됨(`EpisodeCsvImporter.RestoreCharacterOverrides()`).

---

## 현황판 (EpisodeUIManager)

`Assets/_Project/Features/Rest/Runtime/EpisodeUIManager.cs`, `BaseUIManager` 상속.

**애니메이션**: `EpisodeBoardManager`와 동일한 슬라이드 업 방식

**기능**
- 금액 표시 (`{money:N0} G`)
- 영업 상태 토글 (영업 중 / 준비 중)
- `OnOpen()` 시 `UpdateDashboard()` 호출

> **주의**: `money`, `isBusinessOpen`이 현재 로컬 임시 변수로 하드코딩됨 — 추후 `GameProgress` 연동 필요

---

## 상점 시스템 (ShopUIManager)

`Assets/_Project/Features/Rest/Runtime/ShopUIManager.cs`, `BaseUIManager` 상속.

**열기/닫기**: 펼침·접힘 애니메이션 없음 — `AnimateOpen()`/`AnimateClose()`는 `CanvasGroup`의 `alpha`/`interactable`/`blocksRaycasts`만 즉시 전환하고 끝남(과거의 가로→세로 스케일 애니메이션은 열리는 도중 뒷화면이 그대로 비쳐 보이는 문제로 제거됨).

**화면 구조 (4단계, `SetScreen()`으로 하나만 활성화)**

```
homePanel (재료/업그레이드/레시피북 3버튼)
 ├─ ingredientCategoryPanel — LiquorCategoryDef 대분류 버튼 그리드 (ScrollRect)
 │    └─ itemScrollPanel     — LiquorBottleDef 소분류 아이템 그리드(ScrollRect)
 ├─ upgradePanel             — UpgradeDef 리스트(Vertical Layout Group + ScrollRect)
 └─ recipeBookPanel          — RecipeBookDef 리스트(Vertical Layout Group + ScrollRect)
```

**중요: 재료 마스터 데이터는 `ItemData`가 아니라 술장의 `LiquorBottleDef`/`LiquorCategoryDef`를 그대로 사용**. `Resources/Bartending/Items/`의 `ItemData`는 임시 데이터라 상점이 참조하지 않음(아래 ItemData 섹션 참고). 상점에서 구매한 재료의 잔량(`GameProgress.bottleAmount`, id 키)이 술장(`LiquorShelfUI`/`LiquorBottleSlotUI`)에 표시되는 잔량과 완전히 같은 저장소를 공유하므로 두 화면이 자동으로 동기화된다.

**핵심 메서드**

| 메서드 | 설명 |
|---|---|
| `ShowHome()` / `OpenIngredients()` / `OpenUpgrades()` / `OpenRecipeBooks()` | 화면 전환. 각 화면 타이틀(텍스트/이미지)은 코드가 아니라 해당 패널 안에 직접 배치 — 패널 활성화만으로 자동 노출됨. `OpenUpgrades()`/`OpenRecipeBooks()`는 전환과 함께 각 슬롯의 `Refresh()`도 호출해 구매 상태를 최신화 |
| `ShowListByCategory(LiquorCategoryDef)` | `allBottles`를 카테고리로 필터링해 `slotPrefab`(`ItemSlotUI`) 재생성, `categoryNameText`에 카테고리 이름 표시하고 `categoryNameColorImage`(텍스트 왼쪽의 동일 모양 액센트 이미지)를 그 카테고리의 `color`로 틴트(재료 소분류 화면에서만 필요한 유일한 동적 텍스트/이미지) |
| `GoBack()` | 뒤로가기 버튼 OnClick에 연결. 현재 화면을 `_parentScreenMap`(화면별로 고정된 상위 화면 하나, 히스토리 스택 아님)에서 찾아 `SetScreen()` — 재료 목록→재료 대분류, 재료 대분류/업그레이드/레시피북→홈 |

검색 기능은 없음(제거됨).

**뒤로가기 네비게이션**: 기존 X버튼(`ExitButton`)은 제거됨. `backButtonObject`는 `SetScreen()`이 호출될 때마다 홈 화면이 아니면 자동으로 활성화되고, 홈 화면이면 자동으로 비활성화됨 — 화면마다 별도로 표시 여부를 관리할 필요 없음.

**로딩 화면 (`ShopLoadingScreen`)**: 씬 진입 후 상점을 처음 열 때만 재생되는 로고+로딩바 연출.
- `ShopUIManager.loadingScreen` 필드로 연결, `AnimateOpen()`이 표시 직후(펼침 애니메이션이 없으므로 사실상 열리자마자) 1회 재생하고 이후 오픈부터는 건너뜀(`_hasShownLoadingOnce`)
- 재생 중에는 `ShopUIManager`의 `CanvasGroup.interactable`을 끄고 `homePanel` 자체를 `SetActive(false)`로 비활성화해 뒤에서 보이거나 눌리지 않게 함(과거엔 전체화면 불투명 `Blocker` 이미지로 덮기만 했으나, 홈 패널을 실제로 꺼서 로딩 UI 뒤로 원래 배경이 그대로 비치는 방식으로 변경— 로딩 중 화면이 아예 안 보이길 원하면 `ShopLoadingScreen` 쪽에 자체 배경이 있어야 함), `moneyDisplayRoot`(소지금 아이콘+숫자를 감싸는 부모)도 함께 꺼졌다가 재생이 끝나면 `homePanel`과 함께 다시 켜짐
- `ShopLoadingScreen.Play()`가 `fillImage.fillAmount`를 `fillDuration` 동안 0→1로 보간(`Image Type = Filled / Horizontal`)한 뒤 `root`를 다시 비활성화하는 코루틴 — `ShopUIManager.AnimateOpen()`이 이 코루틴을 직접 `yield return`으로 이어붙여 실행
- **`ShopUIManager.Start()`는 `ShowHome()`을 호출하지 않음**: `OnOpen()`이 이미 매번 `ShowHome()`을 부르는데, `ShopUIManager` GameObject는 씬 로드 시 비활성 상태로 있다가 최초 `OpenUI()` 때 활성화되므로 `Start()`가 그 순간에 지연 실행됨 — 예전엔 `Start()`도 `ShowHome()`을 불러서, 로딩 연출이 `homePanel`을 꺼둔 직후 지연된 `Start()`가 다시 켜버려 로딩 화면과 홈 화면이 겹쳐 보이는 버그가 있었음
- 로딩 재생 중에는 `LockTransitions()`로 다른 팝업(작전판 등)으로의 전환 자체를 막음. 재생 도중 `CloseUI()`로 강제 종료되는 경우(코루틴이 `StopCoroutine`으로 끊겨 `Play()`가 끝까지 못 돎)를 대비해 `AnimateClose()`에서 `UnlockTransitions()` + `loadingScreen.ResetVisual()`(root 비활성화 + fillAmount 초기화)을 방어적으로 호출 — 안 하면 다음에 열 때 로딩 화면이 켜진 채로 남아있음(`_hasShownLoadingOnce`가 이미 true라 로딩 분기를 다시 안 타서 아무도 안 꺼줌)

**소지금 표시 및 화폐 단위**: `moneyText`는 `{amount:N0}` 형식의 숫자만 표시 — "G" 같은 단위 텍스트는 코드에 없고, 화폐 아이콘 이미지를 숫자 앞에 별도 `Image`로 씬에서 배치한다. `ItemSlotUI`/`UpgradeSlotUI`/`RecipeBookSlotUI`의 개별 가격 텍스트도 동일하게 숫자만 표시하며, 각각 `currencyIcon`(`GameObject`) 필드로 아이콘을 참조해 가격이 아닌 다른 문구가 표시될 때(잠김/"MAX"/"구매완료") 아이콘도 함께 숨김 처리한다.

**기본 프레임(배경/상단/하단 오버레이)**: 5개 화면 모두가 공유하는 배경 이미지 1장 + 상단 오버레이(금액 표시, 항상 고정) 1장은 `ShopUIManager` 루트에 한 번만 배치하고 화면 패널들은 그 위에 얹힘 — 화면마다 중복 배치하지 않음. 하단 오버레이만 예외: `homePanel`(스크롤 없음)은 그 자식으로 두면 패널 활성화에 따라 자동으로 같이 열리고 닫히고, 스크롤이 있는 나머지 4화면은 각자의 `ScrollRect` Content 맨 끝에 배치해 끝까지 스크롤해야 보이는 캡 이미지로 동작(아래 `ScrollContentMinHeight` 참고).

### ScrollContentMinHeight (`Assets/_Project/Features/Rest/Runtime/ScrollContentMinHeight.cs`)

`ScrollRect`의 Content에 부착, `ContentSizeFitter`를 대체. 자식(아이템 목록 + 하단 오버레이 캡)의 실제 필요 높이가 뷰포트보다 작으면 뷰포트 높이로 고정해 `Vertical Layout Group`의 `Flexible` 자식(Spacer)이 남는 공간을 채우게 해서 하단 캡 이미지가 화면 맨 밑에 붙게 하고, 필요 높이가 뷰포트보다 크면 그 값을 그대로 써서 정상적으로 스크롤되게 한다.

- Content 구조: `ItemsContainer`(실제 동적 인스턴스 타겟, Grid 또는 Vertical) → `Spacer`(`Layout Element`: Flexible Height = 1) → `BottomOverlayImage`(`Layout Element`: Preferred Height 지정)
- `contentRoot`/`categoryButtonContent`/`upgradeContentRoot`는 바깥 Content가 아니라 이 `ItemsContainer`를 가리켜야 함 — 그렇지 않으면 런타임에 동적으로 추가되는 아이템들이 `Instantiate(prefab, contentRoot)`로 매번 맨 마지막 자식에 붙으면서 `Spacer`/`BottomOverlayImage` 뒤로 밀려 들어가 순서가 깨짐
- 재료 대분류/재료 목록/업그레이드/레시피북 4화면 모두 이 구조를 동일하게 씀(업그레이드·레시피북도 재사용 목적)

### LiquorBottleDef 상점용 확장 필드

술장용 필드(`id`, `displayName`, `shelfSprite`, `unlockFlagKey`, `subCategory`, `bottleCount`, `unitVolume`) 외에 상점을 위해 추가된 필드:

| 필드 | 설명 |
|---|---|
| `shopSprite` | `ItemSlotUI` 아이콘에 쓰는 상점 전용 스프라이트(`_blank` 변형). `shelfSprite`(술장, `_lid` 변형)와 별도 필드라 각각 채워야 함 |
| `category` | `LiquorCategoryDef` 참조. null이면 상점 어느 카테고리에도 노출되지 않음 |
| `price` | 1병(`unitVolume`) 구매 가격(원화) |
| `strangeCoinPrice` | 1병(`unitVolume`) 구매 가격(이상한 동전) — 이상한 상점 전용, 비워두면 0이라 항상 구매 가능한 것으로 처리되니 주의 |
| `unlockHintType` | `None`/`RecipeBook`/`Episode` — 잠금 툴팁에 표시할 힌트 종류(표시 전용, 실제 해금 여부는 기존 `unlockFlagKey`로 판정) |
| `recipeBookIcon` / `recipeBookName` | `unlockHintType == RecipeBook`일 때 툴팁에 표시 |

### ItemSlotUI (`Assets/_Project/Features/Rest/Runtime/ItemSlotUI.cs`)

`LiquorBottleDef` 하나를 바인딩하는 그리드 슬롯. `IPointerEnterHandler`/`IPointerExitHandler` 구현.

- **해금**: 아이콘 원색 표시(`preserveAspect = true`로 원본 비율 유지), 이름/소분류 텍스트, 가격+구매버튼 활성화(재고가 가득 찼거나 잔액이 부족하면 버튼 비활성화)
- **잠금**: 아이콘을 검정으로 틴트(`Image.color`만 변경, 별도 실루엣 아트 불필요), 이름/소분류 대신 `lockedLabel`("입고예정") 표시, 가격 텍스트 비움 + `currencyIcon` 숨김 + 구매버튼 비활성화
- **잔액 부족/재고 매진**: `priceText.color`(빨강, `priceColorInsufficient`)는 "재고는 남았는데 잔액만 모자란" 경우에만 바뀌지만, `buyButtonImage.sprite`(`buyButtonOffSprite`)는 재고 매진(`isFull`)이든 잔액 부족이든 **구매 불가능한 경우 전부** 꺼진 모양으로 바뀜(`buyButton.interactable`과 항상 같은 조건) — 원래는 잔액 부족일 때만 버튼 스프라이트를 바꿔서, 이미 가득 찬 재고(예: `defaultBottleCount == bottleCount`로 시작부터 풀스택인 아이템)가 소지금 0원에서도 "구매 가능" 모양으로 보이는 버그가 있었음(2026-08-23 수정). `buyButtonImage`/`buyButtonOnSprite`/`buyButtonOffSprite` 셋 다 인스펙터에 할당된 경우에만 동작, 비워두면 무시됨. 한 슬롯에서 구매해 잔액이 바뀌면 `ShopUIManager`가 현재 들고 있는 모든 슬롯의 `Refresh()`를 다시 호출해 다른 슬롯들의 버튼/색상도 같이 갱신됨
- **호버**: 해금 시 `LiquorBottleInfoCard.Instance.Show()`(술장과 동일 컴포넌트) — 단 `LiquorBottleInfoCard`는 BusinessScene 전용으로 만들어져 있어 RestScene과 동시 로드되지 않으므로, RestScene에는 프리팹으로 추출한 별도 인스턴스를 배치(이름/소분류 텍스트는 슬롯에 이미 상시 표시되므로 이 인스턴스에서만 제거). 잠금 시 `IngredientUnlockTooltip.Instance.Show()`
- **구매(`OnBuyClick`)**: `IShopCurrency.TrySpend(price)` 성공 시 `GameProgress.AddBottleAmount(id, unitVolume, MaxAmount)`로 **1병 단위** 충전(가득 리필이 아님), `OnPurchased` 이벤트로 `ShopUIManager`의 소지금 텍스트 갱신을 트리거
- **`Setup(def, currency)`**: `currency`를 생략하면 `MoneyShopCurrency`(원화)로 동작 — 일반 상점 호출부는 수정 없이 그대로 호환됨. 이상한 상점은 `StrangeCoinShopCurrency`를 넘겨서 같은 슬롯 로직을 재사용(아래 "이상한 상점" 참고)

### IngredientUnlockTooltip (`Assets/_Project/Features/Rest/Runtime/IngredientUnlockTooltip.cs`)

잠긴 재료 호버 시 표시. `LiquorBottleInfoCard`와 동일한 싱글톤/고정 슬롯 패턴(`Instance`, `Show()`/`Hide()`, `LayoutRebuilder` 기반 위치 계산).

- `unlockHintType == RecipeBook`: 레시피북 아이콘/이름 + "레시피북 필요" 고정 텍스트
- `unlockHintType == Episode`: 실루엣 placeholder + "???" 고정 텍스트(스포일러 방지)

### 업그레이드 (`UpgradeDef.cs`, `UpgradeSlotUI.cs`)

- `UpgradeDef`(SO): `id`, `icon`, `displayName`, `description`, `pricesPerLevel(int[])` — 배열 길이가 곧 최대 레벨(현재 기획상 4단계)
- `UpgradeSlotUI`: `GameProgress.GetUpgradeLevel(id)`만큼 pip(`Image[]`, 색 토글)을 채워 표시(아이콘 `preserveAspect = true`), 다음 단계 가격을 구매버튼에 표시(만렙이면 비활성 + "MAX"), 구매 시 `TrySpendMoney` → `SetUpgradeLevel`. `ItemSlotUI`와 동일한 잔액 부족 표시(가격 빨간색 + `buyButtonImage` off 스프라이트 교체) 적용됨
- 레벨은 `GameProgress`(`upgradeKeys`/`upgradeValues`)에 저장되고 `SaveData`/`DataManager`로 저장·로드됨

### 레시피북 (`RecipeBookDef.cs`, `RecipeBookSlotUI.cs`)

업그레이드와 동일한 스크롤 목록 구조. 슬롯 레이아웃은 왼쪽에 아이콘 + 가격/구매버튼, 오른쪽에 이름 + 설명 + 해금 정보 문구.

- `RecipeBookDef`(SO): `id`, `icon`, `price`, `displayName`, `description`, `unlockInfoText`(예: "스피릿 1종 해금" — `CategoryColorText.Highlight()`로 표시되어 안의 카테고리 단어가 자동으로 색칠됨), `unlockFlagKeys`(구매 시 설정할 `GameProgress` 플래그 목록 — 이 레시피북이 해금하는 `LiquorBottleDef.unlockFlagKey`와 같은 값을 넣어야 실제로 잠금 재료가 풀림)
- 구매 여부는 레벨이 아니라 전용 플래그(`RecipeBookDef.PurchasedFlagKey` = `"RecipeBook_{id}_Purchased"`) 하나로 판정 — `GameProgress`에 별도 저장소를 추가하지 않고 기존 flag 시스템 재사용
- `RecipeBookSlotUI.OnBuyClick()`: `TrySpendMoney(price)` 성공 시 구매 플래그 설정 + `unlockFlagKeys` 전부 `SetFlag` → 구매 완료 시 `UpgradeSlotUI`의 만렙 표시와 동일한 패턴으로 구매버튼 비활성화 + 가격 텍스트를 "구매완료"로 전환. 아이콘 `preserveAspect = true`, 잔액 부족 시 `ItemSlotUI`와 동일한 표시(가격 빨간색 + off 스프라이트) 적용됨
- `LiquorBottleDef.unlockHintType == RecipeBook`으로 표시되는 잠금 툴팁(`recipeBookIcon`/`recipeBookName`)은 여전히 별도 필드로 수동 입력 — `RecipeBookDef`를 직접 참조하지 않으므로 잠금 재료 쪽 표시 문구/아이콘과 실제 판매 중인 `RecipeBookDef`의 이름/아이콘을 일치시키는 것은 데이터 입력자의 책임

### 카테고리 고유색 (`LiquorCategoryDef.color`, `CategoryColorText.cs`)

`LiquorCategoryDef`에 `color` 필드가 추가되어 대분류마다(스피릿/리큐르 등) 고유색을 가짐. 카테고리 이름 텍스트 자체의 폰트 색은 바꾸지 않고, 두 가지 방식으로만 반영한다.

- **같은 모양의 액센트 이미지 틴트**: `LiquorCategoryButtonUI.colorImage`(선택 필드, 스프라이트는 씬에서 미리 배치)와 `ShopUIManager.categoryNameColorImage`(재료 소분류 화면 타이틀 옆)를 카테고리의 `color`로 `Image.color`만 갱신. 술장의 카테고리 버튼 프리팹은 `colorImage`를 비워두면 색이 적용되지 않음 — 상점 전용으로 켜고 싶은 곳만 프리팹에서 연결
- **자유 문장 속 단어 강조**: `CategoryColorText.Register(categories)`(`ShopUIManager.Start()`에서 1회 등록) + `CategoryColorText.Highlight(text)`가 문장 속에 등장하는 카테고리 `displayName`을 TMP `<color=#RRGGBB>` 태그로 감싸 반환. 레시피북의 `description`/`unlockInfoText`에 적용됨. 전역 텍스트 후킹이 아니라 명시적으로 `Highlight()`를 거친 텍스트에만 적용되므로, 새 텍스트에 카테고리 단어를 강조하려면 해당 텍스트 대입 지점에서 직접 호출해야 함

### 이상한 상점 (`ShopUIManager` 확장)

이상한 동전(`strange_coin`, "이상한 동전 - 0부" 클리어로 해금)으로 재료를 구매하는 특수 상점. 일반 상점과 같은 `allBottles`(`LiquorBottleDef`) 목록을 재사용하되 결제 수단과 비주얼만 다르다 — 별도 데이터/씬 상태를 만들지 않고 기존 화면 전환 체계에 편입하는 방식으로 구현됨. 코드·씬 배치 모두 완료된 상태.

**해금 및 진입**
- `ShopUIManager.strangeShopUnlockEpisodeId`(기본 `"StrangeCoin_0"`)를 `GameProgress.IsEpisodeCompleted()`로 체크 — 완료 전엔 코인 아이콘(`strangeShopIconButton`) 자체가 숨겨짐(`RefreshStrangeShopIcon()`, `OnOpen()`마다 재평가)
- 코인 아이콘 클릭(`OnStrangeShopIconClicked()`)이 일반 상점 ↔ 이상한 상점을 토글: 스프라이트를 `strangeShopIconOffSprite`/`OnSprite`로 교체, `moneyDisplayRoot`/`strangeCoinDisplayRoot`를 상호 배타적으로 표시, `ApplyShopSkin()` 호출
- 진입 시 홈이 아니라 **재료 카테고리 화면(`strangeIngredientCategoryPanel`)부터 시작** — 이상한 상점은 재료 섹션만 존재하므로 홈 화면 자체가 없음
- `SetScreen()`/`_parentScreenMap`이 이상한 상점의 두 패널(`strangeIngredientCategoryPanel`, `strangeItemScrollPanel`)도 같이 관리 — `GoBack()`/`backButtonObject`가 별도 코드 없이 그대로 동작. 단 `strangeIngredientCategoryPanel`은 이상한 상점의 최상위 화면이라 `_parentScreenMap`에 없음(뒤로가기로 못 나감) — 나가려면 코인 아이콘만 눌러야 함(`backButtonObject`는 `target != homePanel && target != strangeIngredientCategoryPanel`일 때만 표시)

**화폐 (`ShopCurrency.cs`)**
- `IShopCurrency { GetPrice(LiquorBottleDef), CurrentAmount, TrySpend(amount) }` — `ItemSlotUI`가 `GameProgress`를 직접 호출하지 않고 이 인터페이스로만 결제
- `MoneyShopCurrency`: `def.price` / `GameProgress.CurrentMoney` / `TrySpendMoney` (일반 상점 기본값, `ItemSlotUI.Setup()`에서 currency 생략 시 자동 사용)
- `StrangeCoinShopCurrency`: `def.strangeCoinPrice` / `GameProgress.GetAffinity("strange_coin")` / `GameProgress.TrySpendAffinity("strange_coin", amount)`
- "이상한 동전" 자체는 `GameProgress`에 새 저장소를 만들지 않고 기존 범용 수치 변수 저장소(`GetAffinity`/`AddAffinity`, 원래 캐릭터 호감도용이지만 임의의 문자열 key로 범용 사용 가능)를 `varName = "strange_coin"`으로 재사용 — 에피소드 CSV의 `varChanges`(`strange_coin+1` 형식)로 지급 가능, 코드 추가 불필요
- `GameProgress.TrySpendAffinity(varName, amount)`: `TrySpendMoney`와 동일하게 잔액 부족 시 차감 없이 `false` 반환. 화폐 전용이 아니라 범용이라 다른 특수 화폐가 생겨도 재사용 가능

**아이템 리스트**: `BuildStrangeCategoryButtons()`/`ShowStrangeListByCategory()`/`UpdateStrangeList()`가 일반 상점의 `BuildCategoryButtons()`/`ShowListByCategory()`/`UpdateList()`와 동일한 구조로 병렬 존재(코드 중복을 감수하고 패널·프리팹만 분리) — `UpdateStrangeList()`만 `slot.Setup(bottle, _strangeCurrency)`로 화폐를 주입한다는 점이 다름. `RefreshStrangeCoinText()`가 잔액 텍스트 갱신 + 이상한 상점 슬롯들의 `Refresh()`를 담당(일반 상점의 `RefreshMoneyText()`와 대응, 서로 다른 화폐라 상대 슬롯은 갱신하지 않음)

**스킨 교체 (`ThemedSprite`)**: 패널과 무관하게 `ShopWindow`에 상시 깔려있는 배경류(윈도우 배경/상단 오버레이/뒤로가기 버튼)는 각 패널 안에 있지 않아 패널 전환만으로는 안 바뀜 — `ShopUIManager` 내부 직렬화 클래스 `ThemedSprite { image, normalSprite, strangeSprite }`가 이 3곳(`windowBackgroundSkin`/`upperOverlaySkin`/`backButtonSkin`)을 `ApplyShopSkin(bool)`로 한 번에 교체. `OnOpen()`(항상 일반 모드로 리셋)과 `OnStrangeShopIconClicked()`(토글)에서 호출됨

**카테고리 버튼 프리팹 재사용 시 주의**: `strangeCategoryButtonPrefab`를 `ShopCategoryButton.prefab` 복제로 만들 경우, 원본에 있던 클릭 범위 버그(배경 `Image`가 `Button` 컴포넌트의 부모 오브젝트에 있어 아이콘+텍스트 바깥 영역은 클릭이 안 되는 문제 — Unity 이벤트 버블링은 자식→부모 방향으로만 핸들러를 찾기 때문)를 원본에서 먼저 고친 뒤 복제할 것. 자세한 원인은 이 문서가 아니라 프리팹 자체를 열어 `Button`/배경 `Image`의 부모-자식 관계를 확인.

### ItemData (`Assets/_Project/Features/Rest/Runtime/ItemData.cs`) — 임시 데이터, 상점 미사용

```csharp
[CreateAssetMenu(menuName = "Shop/Item Data")]
public class ItemData : ScriptableObject
{
    public string itemName;
    public Sprite icon;
    public int price;
    public string desc;
    public ItemType category;
}

public enum ItemType { Alcohol, Liqueur, NonAlcohol, Powder, Tool, Glass }
```

`Resources/Bartending/Items/`의 `ItemData` 120개는 임시 데이터라 위 상점 구현은 참조하지 않음(같은 폴더의 제작용 `ItemDef` 18개와 별개). `Assets/_Project/Features/Rest/Editor/ItemDataImporter.cs`(CSV 임포터)도 함께 미사용 상태로 남아있음.

---

## 씬 오브젝트 인터랙션

### ObjectInteraction / ObjectInteractionBoard

`BaseUIManager`를 참조하는 씬 오브젝트 클릭 핸들러.
두 클래스는 현재 코드가 동일 — `ObjectInteraction`이 보드에도 적용 가능하므로 향후 통합 가능.

**배경 오버레이 3종** (`idleOverlay`/`hoverOverlay`/`grayOverlay`): 매 프레임 `UpdateBackgrounds()`가 상태를 재계산해 셋 중 하나만 켠다.
- `idle`: 평상시(컬러 이미지)
- `hover`: 이 오브젝트에 마우스가 올라가 있거나(다른 UI가 안 열려 있을 때) 자신의 UI가 열려 있을 때 — 노란 테두리
- `gray`: 다른 오브젝트의 UI가 열려 있어 이 오브젝트가 비활성 상태이거나, `IsInteractionAvailable()`이 거짓일 때

구버전 단일 `highlightOverlay` 필드도 하위 호환으로 남아있다 — `hoverOverlay`를 연결하지 않은 오브젝트에서만 `useLegacyHighlight` 경로로 동작.

**클릭 가용성** (`disableWhenRestShopRestricted` + `tvDatabase`): 켜져 있으면 `TVBroadcastRuntime.IsRestShopDisabled(GameProgress.Instance, database, out reason)`로 활성 TV 방송의 `DisableRestShop` 효과를 확인해, 제한 중이면 클릭을 막고(`reason`을 로그로만 출력) 배경도 `gray`로 표시한다. 다른 오브젝트(TV, 작전판 등)는 이 플래그를 꺼둔 채로 사용해 제한 대상이 아니게 한다.

**상호배타**: 정적 `ActiveInteractions`/`AnyUIOpen`으로 씬에 있는 모든 `ObjectInteraction` 인스턴스 중 하나라도 UI가 열려 있는지 판단 — 열려 있으면 다른 오브젝트는 클릭이 막히고 회색으로 표시된다.

**아웃라인**: `DrawOutlineShape()`로 `PolygonCollider2D` 또는 `BoxCollider2D` 기반 자동 생성

### RestSceneVisualStateCoordinator

`ObjectInteraction.AnyUIOpen`을 매 프레임 폴링해 씬 전역 배경(`colorBackground`/`grayBackground`)을 켜고 끄는 조율자. 개별 오브젝트(TV/작전판/상점)의 하이라이트·회색 처리는 각자의 `ObjectInteraction`이 담당하고, 이 컴포넌트는 "UI가 하나라도 열려 있으면 화면 전체를 흑백으로" 하는 전역 톤 전환만 맡는다.

### BoardBackground (`Assets/_Project/Features/Rest/Runtime/BoardBackground.cs`)

에피소드 보드의 빈 배경 클릭 시 `EpisodeBoardManager.ResetBoard()` 호출.

---

## 툴팁 시스템

### TooltipManager (`Assets/_Project/Features/Rest/Runtime/TooltipManager.cs`)

`Instance` 싱글톤 (MonoSingleton 미사용, 직접 Awake에서 `Instance = this`).

**핀 시스템**: `PinnedOwner` — 한 번에 하나의 `TooltipTrigger`만 고정 가능
- 고정된 상태에서 다른 트리거의 `ShowTooltip()` / `HideTooltip()` 요청은 무시

| 메서드 | 설명 |
|---|---|
| `ShowTooltip(text, targetRect, owner)` | 툴팁 표시 및 위치 계산 |
| `HideTooltip(owner)` | 호출자가 현재 owner일 때만 숨김 |
| `SetPin(owner, isPinned)` | 고정 권한 설정/해제 |

**위치 계산**: 오른쪽 배치 기본, 화면 밖 넘칠 경우 왼쪽으로 자동 전환.

---

## 미결 사항 요약

| 항목 | 위치 | 설명 |
|---|---|---|
| 현황판 데이터 | `EpisodeUIManager` | `money`, `isBusinessOpen` → `GameProgress` 연동 필요 |
| `ObjectInteraction` 중복 | `ObjectInteractionBoard` | 두 클래스 코드 동일, 하나로 통합 가능 |
| 재료 소진 로직 | `GameProgress.AddBottleAmount` | 영업 중 사용에 따른 잔량 감소는 미구현(음수 delta로 재사용 가능하도록만 설계됨) |
| `LiquorBottleDef` 상점 데이터 | `Assets/_Project/Features/Bartending/Content/Generated/LiquorBottles/*.asset` | `price`/`category`/`unlockFlagKey`/`unlockHintType` 값이 비어있으면 상점에 노출되지 않음 — 애셋별로 직접 입력 필요 |
| `RecipeBookDef` 에셋 | `Assets/_Project/Features/Rest/Runtime/RecipeBookDef.cs` | 코드만 존재, 실제 SO 에셋과 `ShopUIManager.allRecipeBooks`/`recipeBookPanel` 연결은 아직 수동 작업 필요 |
| `LiquorCategoryDef.color` 값 | `LiquorCategoryDef` 에셋 | 필드만 추가됨, 카테고리별 실제 색상 값은 아직 미입력(기본값 흰색) |
