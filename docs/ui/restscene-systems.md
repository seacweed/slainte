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
ExitButton                  — 팝업 닫기 버튼
```

---

## BaseUIManager (`RestScene/Scripts/BaseUIManager.cs`)

모든 UI 패널의 공통 기반 추상 클래스. `CanvasGroup` 필수.

| 메서드 | 설명 |
|---|---|
| `OpenUI()` | `gameObject` 활성화 → `OnOpen()` → `AnimateOpen()` |
| `CloseUI()` | `AnimateClose()` → `OnClose()` |
| `AnimateOpen()` | (추상) 자식이 직접 구현 |
| `AnimateClose()` | (추상) 자식이 직접 구현 |
| `OnOpen()` | (가상) 열릴 때 초기화 — 기본 빈 함수 |
| `OnClose()` | (가상) 닫힐 때 정리 — 기본 빈 함수 |

- Awake에서 `CanvasGroup.alpha = 0`, `interactable = false`, `gameObject.SetActive(false)` 초기화
- 진행 중인 코루틴은 항상 중단 후 재시작 (`_activeCoroutine`)

---

## 에피소드 보드 시스템

### EpisodeBoardManager (`RestScene/Scripts/EpisodeBoardManager.cs`)

`BaseUIManager` 상속. 에피소드 보드 전체를 관리합니다.

**애니메이션**: 아래에서 위로 슬라이드 (`slideDistance = 150f`, `animDuration = 0.3f`)
- `Mathf.SmoothStep` 이징 적용, `Time.unscaledDeltaTime` 사용

**핵심 메서드**

| 메서드 | 설명 |
|---|---|
| `OnOpen()` | 미완료 필수 에피소드가 있으면 `ShowMandatoryGate()`로 보드를 비우고 안내만 표시, 없으면 `RefreshBoard()` + `ResetBoard()` |
| `RefreshBoard()` | `EpisodeManager.GetBoardEpisodes()`로 표시할 에피소드 목록 확보, 동적 프리팹 생성/제거 |
| `PinEpisode(photo)` | `PinnedPhoto` 설정, `EpisodeManager.IsPlayable()`(플레이 조건) 결과에 따라 하단 텍스트/버튼 활성화 |
| `ResetBoard()` | 이전 사진 `Unpin()`, 아무것도 선택하지 않은 기본 상태 — 하단 텍스트 "영업" + Play 버튼 **활성** |
| `OnStartButtonClicked()` | `PinnedPhoto != null`이면 `DayFlowController.StartDefaultEpisode(id)`, null이면(미선택 상태) `DayFlowController.StartBusinessDay()` → 이후 `CloseUI()` |

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
public Image startButtonImage;
public Color buttonActiveColor;       // 선택 시
public Color buttonInactiveColor;     // 미선택 시
```

### EpisodeManager — 보드 관련 메서드

| 메서드 | 설명 |
|---|---|
| `GetBoardEpisodes()` | 완료되지 않은 에피소드 중 `IsVisible()` 통과한 목록 반환 |
| `IsVisible(ep, gp)` | `IsUnlocked()` 가 참이거나, `{episodeId}_Discovered` 플래그가 있으면 true |
| `GetAvailableEpisodes()` | `IsUnlocked()`(해금 조건) 통과한 에피소드만 반환 |
| `IsUnlocked(ep, gp)` | `ep.triggerCondition`(해금 조건) 평가 — 만족하면 작전판에 노출 |
| `IsPlayable(ep, gp)` | `ep.playCondition`(플레이 조건) 평가 — 만족해야 Play 버튼 활성화. 조건 없으면 항상 true |

해금 조건과 플레이 조건은 독립적: 해금은 됐지만 플레이 조건 미달이면 보드엔 뜨되 Play 버튼은 비활성 상태로 남음. 자세한 내용은 [game-flow-design.md](../core/game-flow-design.md) 참고.

---

### EpisodePhotoTrigger (`RestScene/Scripts/EpisodePhotoTrigger.cs`)

에피소드 보드의 개별 사진 슬롯. `IPointerEnterHandler`, `IPointerExitHandler`, `IPointerClickHandler` 구현.

**상태**
- 기본: `normalSprite`, 오버레이 없음
- 호버: `hoverSprite`로 교체, `EpisodeInfoUI.Show()` (다른 사진이 이미 pin된 경우 호버 무시)
- 핀(클릭): `selectedSprite`로 교체, `EpisodeInfoUI.Show()` 유지, `EpisodeBoardManager.PinEpisode()` 호출
  - 다른 사진이 이미 pin된 상태에서 클릭하면 그 사진을 먼저 `Unpin()`한 뒤 새 사진을 pin (포커스 전환)
- 언핀(pin된 사진을 재클릭): `isPinned = false` + `EpisodeBoardManager.ResetBoard()` 호출 → 보드가 "영업" 기본 상태로 복귀

**`SetEpisodeData(EpisodeData data, EpisodeBoardManager board)`**
- `data.iconNameBoard`로 `Resources.Load<Sprite>("Sprites/{name}")` 시도
- `Image` 컴포넌트 우선, 없으면 `SpriteRenderer` 대체

**`static HasBoardPhoto(EpisodeData ep)`**
- `ep.iconNameBoard`가 있으면 그 이름으로, 없으면 `ep.episodeId.Replace("_", "-")`를 기본 이름으로 사용
- `Resources/Sprites/{baseName}-idle` 스프라이트 로드 성공 여부로 표시 가능 여부 판단
- 보드에 표시하려면 `Resources/Sprites/`에 `{baseName}-idle/hover/selected` 3종 스프라이트 필요

---

### EpisodeInfoUI (`RestScene/Scripts/EpisodeInfoUI.cs`)

사진 옆에 표시되는 에피소드 상세 정보 팝업. `BaseUIManager` 미상속, 독립 활성화. 자체 `Canvas`를 `overrideSorting`으로 추가해 `sortingOrder`를 높여 항상 최상단에 렌더링. `LiquorBottleInfoCard`와 동일하게 **런타임 `Instantiate`/`Destroy` 없이** 에디터에서 미리 배치한 고정 슬롯 배열을 채우는 방식으로 구성(조건 행 개수·초상화 슬롯 개수는 에디터에 배치한 배열 길이만큼). 세로 길이는 `VerticalLayoutGroup`+`ContentSizeFitter`로 조건 개수에 따라 유동적으로 늘어남.

| 메서드 | 설명 |
|---|---|
| `Show(data, targetPhoto)` | 헤더/설명/조건 행/토글/초상화 갱신 후 위치 계산 |
| `Hide()` | `gameObject.SetActive(false)` |

**레이아웃 순서 (위 → 아래)**
1. `boardImage`(작전판 사진과 동일한 idle 스프라이트, `EpisodePhotoTrigger.GetIdleSprite()` 재사용) + `episodeNameText`(제목) — 가로 배치
2. `descriptionText` — `data.episodeDescription`
3. **해금 조건** (`triggerSection` + `triggerConditionRows`) — `data.triggerCondition` 기준, 조건이 하나도 없으면 섹션 자체를 숨김
4. **선택 조건** (`selectSection` + `selectConditionGroups`) — `data.selectConditions`(리스트) 기준. 아래 참고
5. **초상화** (`portraitSlots`) — 선택된 옵션 유무에 따라 결정. 아래 참고

**해금 조건 행 로직** (`ConditionRow { container, lockIcon, label }`, `triggerConditionRows`)
- `data.triggerCondition`(여러 항목이 AND로 결합되는 다중 필드 타입)을 `BuildConditionEntries()`로 행 여러 개로 풀어서 표시
- `minDay` → "N일차 이상" / `prerequisiteEpisodeIds` → "선행 에피소드 '제목'"(`EpisodeManager.GetEpisodeData()`로 제목 조회) / `requiredFlags` → 플래그명 그대로 / `requiredVars` → `varName 연산자 threshold`
- **`requiredCustomerAppearances`(등장 조건)는 툴팁에 표시하지 않음**
- 항목마다 충족 여부에 따라 `lockIcon.sprite`를 `unlockedSprite`/`lockedSprite`로 교체, `label.text`에 조건 설명 표시
- 마지막에 작성자가 직접 입력한 커스텀 힌트 문구(`data.triggerConditionTexts`)가 추가로 붙음, 잠금 아이콘은 `EpisodeManager.IsUnlocked()`(전체 충족 여부)로 결정
- 표시할 조건 개수보다 `triggerConditionRows` 배열이 길면 남는 행은 비활성화

**선택 조건 (`data.selectConditions: List<SelectConditionEntry>`)**
- `SelectConditionEntry { condition, flag, conditionText, characterOverrides }` — 옵션 하나 = 조건 **하나**(`SelectSingleCondition`) + 플래그 + 커스텀 힌트 한 줄 + 초상화 override. 해금 조건과 달리 옵션 하나에 여러 조건을 AND로 걸 수 없음 — 조건을 여러 개 걸고 싶으면 옵션을 여러 개로 나눠서 표현
- `SelectSingleCondition { type, minDay, requiredFlag, prerequisiteEpisodeId, varName, varOp, varThreshold }` — `SelectConditionType`(`None`/`MinDay`/`RequiredFlag`/`PrerequisiteEpisode`/`RequiredVar`) 하나로 어떤 조건인지 결정, 나머지 필드 중 해당 타입에 대응하는 값만 사용
- 옵션당 UI도 행 하나(`SelectConditionGroup.conditionRow: ConditionRow`, 배열이 아님) — 자물쇠 아이콘 하나 + 설명 한 줄 + 토글 하나로 고정. 텍스트는 `entry.conditionText`가 있으면 그걸, 없으면 `BuildSelectConditionText()`가 조건 타입에서 자동 생성("N일차 이상", 플래그명, "선행 에피소드 '제목'", `varName 연산자 threshold` 등)
- `해금 조건`/`playCondition`과 완전히 독립 — **Play 버튼 활성화에는 전혀 영향을 주지 않음**. 어떤 옵션도 미충족/미선택이어도 에피소드는 평소대로 플레이 가능
- **옵션끼리 상호 배타적** — `selectToggleGroup`(유니티 내장 `ToggleGroup`)에 모든 옵션의 `Toggle`을 묶어서, 하나를 켜면 나머지는 자동으로 꺼짐. `Awake()`에서 `group.toggle.group = selectToggleGroup`로 한 번만 연결
- `EpisodeInfoUI.selectConditionGroups[i]`가 `data.selectConditions[i]`와 인덱스로 1:1 매칭(고정 슬롯, 배열 길이보다 옵션이 적으면 남는 슬롯은 컨테이너까지 비활성화)
- 옵션마다 `EpisodeManager.EvaluateSelectCondition(entry.condition, gp)`로 개별 충족 여부 평가. 충족 시에만 그 옵션의 토글이 인터랙션 가능(미충족이면 off 고정, 비활성화). `entry.flag`가 비어있으면 그 옵션은 토글 UI 자체를 숨김(조건 행만 정보 표시용으로 남음)
- 토글 초기값은 옵션별로 `GameProgress.HasFlag(entry.flag)`
- 실제 `GameProgress.SetFlag()`/`ClearFlag()` 반영은 툴팁에서 즉시 일어나지 않고, **`EpisodeBoardManager.OnStartButtonClicked()`에서 Play 버튼을 누르는 시점**에 `EpisodeInfoUI.IsSelectOptionOn(i)`를 옵션별로 순회하며 적용(`ApplySelectConditionFlag()`)

**초상화** (`portraitSlots`, `List<CharacterDisplay>` 기준)
- 현재 켜져 있는 옵션(`GetSelectedIndex()`)이 있고 그 옵션의 `characterOverrides`가 채워져 있으면 그 리스트를, 아니면 옵션 미선택 시 기본값인 `data.characters`를 그대로 사용(`GetActiveCharacterList()`)
- 즉 **옵션마다 서로 다른 등장인물 조합을 지정 가능** — 옵션 A는 캐릭터를 공개, 옵션 B는 비공개(???), 옵션 C는 다른 캐릭터로 교체 등 자유롭게 구성
- `CharacterDisplay.isHidden`이면 `unknownPortrait`(???) 표시, 아니면 `Resources/Sprites/{characterName}` → `Resources/Portraits/{characterName}` 순으로 로드
- 어떤 토글이든 값이 바뀔 때마다(`OnSelectToggleChanged`) 초상화만 즉시 갱신

**위치 계산** (`UpdatePosition`)
- `targetPhoto`의 월드 오른쪽 중앙 기준 오른쪽 10px에 배치
- `LayoutRebuilder.ForceRebuildLayoutImmediate()` 후 위치 갱신, 캔버스 오른쪽 경계를 넘으면 왼쪽 배치로 자동 전환

**CSV/그래프 연동**: `data.selectConditions`는 CSV `#SELECT_TRIGGER` 섹션(옵션 하나당 한 행), `NarrativeGraphSO.SelectConditions`와 컴파일러/임포터로 왕복 가능. 자세한 CSV 문법은 [episode-csv-guide.md](../narrative/episode-csv-guide.md#select_trigger) 참고. 단 `characterOverrides`(옵션별 초상화)는 `characters`(기본 초상화)와 마찬가지로 CSV/그래프를 거치지 않고 `EpisodeData` 에셋에 직접 입력 — CSV 재임포트 시에도 `flag` 이름으로 기존 값을 찾아 보존됨(`EpisodeCsvImporter.RestoreCharacterOverrides()`).

---

## 현황판 (EpisodeUIManager)

`RestScene/Scripts/EpisodeUIManager.cs`, `BaseUIManager` 상속.

**애니메이션**: `EpisodeBoardManager`와 동일한 슬라이드 업 방식

**기능**
- 금액 표시 (`{money:N0} G`)
- 영업 상태 토글 (영업 중 / 준비 중)
- `OnOpen()` 시 `UpdateDashboard()` 호출

> **주의**: `money`, `isBusinessOpen`이 현재 로컬 임시 변수로 하드코딩됨 — 추후 `GameProgress` 연동 필요

---

## 상점 시스템 (ShopUIManager)

`RestScene/Scripts/ShopUIManager.cs`, `BaseUIManager` 상속.

**애니메이션**: 2단계 펼침 (`animDuration = 0.5f`, 단계당 절반씩)
1. 가로 확장: `(0, lineWidth, 1)` → `(1, lineWidth, 1)`
2. 세로 확장: `(1, lineWidth, 1)` → `(1, 1, 1)`
- 닫을 때는 역순

**화면 구조 (4단계, `SetScreen()`으로 하나만 활성화)**

```
homePanel (재료/업그레이드/레시피북 3버튼)
 ├─ ingredientCategoryPanel — LiquorCategoryDef 대분류 버튼 그리드
 │    └─ itemScrollPanel     — LiquorBottleDef 소분류 아이템 그리드(ScrollRect)
 ├─ upgradePanel             — UpgradeDef 리스트(Vertical Layout Group + ScrollRect)
 └─ 레시피북                  — 기획 미정, OnRecipeBookClicked() 클릭 스텁만 존재
```

**중요: 재료 마스터 데이터는 `ItemData`가 아니라 술장의 `LiquorBottleDef`/`LiquorCategoryDef`를 그대로 사용**. `Resources/Items/`의 `ItemData`는 임시 데이터라 상점이 참조하지 않음(아래 ItemData 섹션 참고). 상점에서 구매한 재료의 잔량(`GameProgress.bottleAmount`, id 키)이 술장(`LiquorShelfUI`/`LiquorBottleSlotUI`)에 표시되는 잔량과 완전히 같은 저장소를 공유하므로 두 화면이 자동으로 동기화된다.

**핵심 메서드**

| 메서드 | 설명 |
|---|---|
| `ShowHome()` / `OpenIngredients()` / `OpenUpgrades()` | 화면 전환. 각 화면 타이틀(텍스트/이미지)은 코드가 아니라 해당 패널 안에 직접 배치 — 패널 활성화만으로 자동 노출됨 |
| `ShowListByCategory(LiquorCategoryDef)` | `allBottles`를 카테고리로 필터링해 `slotPrefab`(`ItemSlotUI`) 재생성, `categoryNameText`에 카테고리 이름 표시(재료 소분류 화면에서만 필요한 유일한 동적 텍스트) |
| `OnRecipeBookClicked()` | 빈 스텁 |

검색 기능은 없음(제거됨).

### LiquorBottleDef 상점용 확장 필드

술장용 필드(`id`, `displayName`, `sprite`, `unlockFlagKey`, `subCategory`, `bottleCount`, `unitVolume`) 외에 상점을 위해 추가된 필드:

| 필드 | 설명 |
|---|---|
| `category` | `LiquorCategoryDef` 참조. null이면 상점 어느 카테고리에도 노출되지 않음 |
| `price` | 1병(`unitVolume`) 구매 가격 |
| `unlockHintType` | `None`/`RecipeBook`/`Episode` — 잠금 툴팁에 표시할 힌트 종류(표시 전용, 실제 해금 여부는 기존 `unlockFlagKey`로 판정) |
| `recipeBookIcon` / `recipeBookName` | `unlockHintType == RecipeBook`일 때 툴팁에 표시 |

### ItemSlotUI (`RestScene/Scripts/ItemSlotUI.cs`)

`LiquorBottleDef` 하나를 바인딩하는 그리드 슬롯. `IPointerEnterHandler`/`IPointerExitHandler` 구현.

- **해금**: 아이콘 원색 표시, 이름/소분류 텍스트, 가격+구매버튼 활성화(재고가 가득 찼으면 버튼 비활성화)
- **잠금**: 아이콘을 검정으로 틴트(`Image.color`만 변경, 별도 실루엣 아트 불필요), 이름/소분류 대신 `lockedLabel`("입고예정") 표시, 가격 텍스트 비움 + 구매버튼 비활성화
- **호버**: 해금 시 `LiquorBottleInfoCard.Instance.Show()`(술장과 동일 컴포넌트) — 단 `LiquorBottleInfoCard`는 BusinessScene 전용으로 만들어져 있어 RestScene과 동시 로드되지 않으므로, RestScene에는 프리팹으로 추출한 별도 인스턴스를 배치(이름/소분류 텍스트는 슬롯에 이미 상시 표시되므로 이 인스턴스에서만 제거). 잠금 시 `IngredientUnlockTooltip.Instance.Show()`
- **구매(`OnBuyClick`)**: `GameProgress.TrySpendMoney(price)` 성공 시 `GameProgress.AddBottleAmount(id, unitVolume, MaxAmount)`로 **1병 단위** 충전(가득 리필이 아님), `OnPurchased` 이벤트로 `ShopUIManager`의 소지금 텍스트 갱신을 트리거

### IngredientUnlockTooltip (`RestScene/Scripts/IngredientUnlockTooltip.cs`)

잠긴 재료 호버 시 표시. `LiquorBottleInfoCard`와 동일한 싱글톤/고정 슬롯 패턴(`Instance`, `Show()`/`Hide()`, `LayoutRebuilder` 기반 위치 계산).

- `unlockHintType == RecipeBook`: 레시피북 아이콘/이름 + "레시피북 필요" 고정 텍스트
- `unlockHintType == Episode`: 실루엣 placeholder + "???" 고정 텍스트(스포일러 방지)

### 업그레이드 (`UpgradeDef.cs`, `UpgradeSlotUI.cs`)

- `UpgradeDef`(SO): `id`, `icon`, `displayName`, `description`, `pricesPerLevel(int[])` — 배열 길이가 곧 최대 레벨(현재 기획상 4단계)
- `UpgradeSlotUI`: `GameProgress.GetUpgradeLevel(id)`만큼 pip(`Image[]`, 색 토글)을 채워 표시, 다음 단계 가격을 구매버튼에 표시(만렙이면 비활성 + "MAX"), 구매 시 `TrySpendMoney` → `SetUpgradeLevel`
- 레벨은 `GameProgress`(`upgradeKeys`/`upgradeValues`)에 저장되고 `SaveData`/`DataManager`로 저장·로드됨

### ItemData (`RestScene/Scripts/ItemData.cs`) — 임시 데이터, 상점 미사용

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

`Resources/Items/`에 187개 에셋 존재하지만 임시 데이터라 위 상점 구현은 참조하지 않음(위 "재료 마스터 데이터" 설명 참고). `Assets/Editor/ItemDataImporter.cs`(CSV 임포터)도 함께 미사용 상태로 남아있음.

---

## 씬 오브젝트 인터랙션

### ObjectInteraction / ObjectInteractionBoard

`BaseUIManager`를 참조하는 씬 오브젝트 클릭 핸들러.
두 클래스는 현재 코드가 동일 — `ObjectInteraction`이 보드에도 적용 가능하므로 향후 통합 가능.

**동작**
- 호버: `highlightOverlay` 활성화
- 클릭: `targetUIManager`가 열려있으면 `CloseUI()`, 닫혀있으면 `OpenUI()`
- UI 열림 상태에서도 `highlightOverlay` 유지, `LineRenderer` 테두리 표시

**아웃라인**: `DrawOutlineShape()`로 `PolygonCollider2D` 또는 `BoxCollider2D` 기반 자동 생성

### BoardBackground (`RestScene/Scripts/BoardBackground.cs`)

에피소드 보드의 빈 배경 클릭 시 `EpisodeBoardManager.ResetBoard()` 호출.

---

## 툴팁 시스템

### TooltipManager (`RestScene/Scripts/TooltipManager.cs`)

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
| 레시피북 화면 | `ShopUIManager.OnRecipeBookClicked` | 기획 미정, 클릭 스텁만 존재 |
| 재료 소진 로직 | `GameProgress.AddBottleAmount` | 영업 중 사용에 따른 잔량 감소는 미구현(음수 delta로 재사용 가능하도록만 설계됨) |
| `LiquorBottleDef` 상점 데이터 | `Assets/Data/LiquorBottle/*.asset` | `price`/`category`/`unlockFlagKey`/`unlockHintType` 값이 비어있으면 상점에 노출되지 않음 — 애셋별로 직접 입력 필요 |
