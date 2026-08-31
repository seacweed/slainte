# CoreScene 시스템

CoreScene은 게임 전체에서 유지되는 매니저와 화면 전환 페이드를 보관합니다. 일부 `MonoSingleton`은 씬에 없을 때 첫 `Instance` 접근으로 생성됩니다.

## 게임 진행 흐름

하루는 영업(Business) 또는 에피소드(Episode) 중 하나로 진행되고, 종료 후 정산(Settlement)을 거쳐 Rest로 돌아갑니다. 필수(Mandatory) 에피소드가 남아있으면 영업 전/후에 하루 1개씩 자동으로 끼어듭니다. 시퀀싱은 `DayFlowController`가 전담하며, `EpisodeBoardManager`/`EpisodeUIManager`/`EpisodeRunner`/영업 스텁은 `GameManager`를 직접 호출하지 않고 이 컨트롤러를 거칩니다. 설계 배경은 [docs/core/game-flow-design.md](game-flow-design.md) 참고.

```
RestScene
  ├─ EpisodeUIManager ("영업 시작" 버튼)
  │    └─ DayFlowController.StartBusinessDay()
  │         ├─ 필수(Before) 에피소드 있으면 → EpisodeManager.StartEpisode(id) (GameState.Episode)
  │         └─ 없으면 → GameManager.ChangeState(GameState.Business)
  │
  └─ EpisodeBoardManager (기본 에피소드 선택, 필수 에피소드 미완료 시 비활성)
       └─ DayFlowController.StartDefaultEpisode(id)
            └─ EpisodeManager.StartEpisode(id) → GameState.Episode

GameState.Episode / GameState.Business 공통
  └─ SceneTransitionManager → BusinessScene 로드
        ├─ Episode: EpisodeRunner.Begin(EpisodeData) (씬 로드 완료 콜백)
        └─ Business: GameModeManager.RequestModeChange(GameMode.OrderMode) (씬 로드 완료 콜백)

BusinessScene (에피소드 진행 종료 시)
  └─ EpisodeManager.ClearEpisode(id) → DayFlowController.OnEpisodeCompleted()
        ├─ 필수(Before) 에피소드였다면 → GameManager.ChangeState(GameState.Business)
        └─ 아니면 → DayFlowController.GoToSettlement()

BusinessScene (영업 스텁 종료 시, BusinessStubUI 스킵 버튼)
  └─ DayFlowController.OnBusinessCompleted()
        ├─ 필수(After) 에피소드 있으면 → EpisodeManager.StartEpisode(id) (GameState.Episode)
        └─ 없으면 → DayFlowController.GoToSettlement()

GameState.Settlement
  └─ SettlementManager.BeginSettlement() (씬 전환 없음, 오버레이)
        ├─ GameProgress 당일 집계 반영(AddMoney) → SettlementUI.Show() (셔터+모니터 연출)
        └─ 확인 시 GameProgress.ResetDaySettlement() → DataManager.Save() → GameManager.ChangeState(GameState.Rest)
              └─ SceneTransitionManager → RestScene 로드
```

## GameManager (`CoreScene/Scripts/GameManager.cs`)

`MonoSingleton<GameManager>`. 게임 상태 전환과 씬 로드를 담당합니다.

```csharp
public enum GameState { None, Episode, Business, Settlement, Rest }
```

- `ChangeState(GameState)` — 이전 상태가 Episode/Business였으면 자동 저장 후 씬 전환
- `Episode` 전환 시: BusinessScene 로드 → 콜백에서 `EpisodeRunner.Begin(EpisodeData)` 호출
- `Business` 전환 시: BusinessScene 로드 → 콜백에서 `GameModeManager.RequestModeChange(GameMode.OrderMode)` 호출 (실제 영업 로직은 별도 담당자 구현 예정, 현재는 `BusinessStubUI` 스텁)
- `Settlement` 전환 시: 씬 전환 없이 `SettlementManager.BeginSettlement()` 호출
- `Rest` 전환 시: `SceneTransitionManager.TransitionToSubScene()`에 `onFadeOutComplete = SettlementManager.OnFadeOutComplete`를 넘겨, 화면이 완전히 검게 된 직후 정산 UI를 리셋(위 SettlementManager/SettlementUI 절 참고)

각 상태 전환의 다음 단계 결정(필수 에피소드 큐, 영업 전/후 순서)은 GameManager가 아니라 `DayFlowController`가 담당합니다.

## EpisodeManager (`CoreScene/Scripts/EpisodeManager.cs`)

`MonoSingleton<EpisodeManager>`. 에피소드 데이터 관리와 시작/완료 처리를 담당합니다.

- `LoadAllEpisodes()` — `Resources.LoadAll<EpisodeData>("EpisodeData")`로 Awake 시 일괄 로드
- `GetAvailableEpisodes()` / `GetBoardEpisodes()` — `episodeType == Default`인 에피소드만 반환 (Mandatory는 플레이어가 직접 선택하지 않음), `GameProgress` 완료 여부 및 `CanStart()` 조건 체크
- `CanStart(EpisodeData, GameProgress)` — `EpisodeTriggerCondition` 기반 조건 검사
  - `minDay`, `requiredFlags`, `blockedFlags`, `prerequisiteEpisodeIds`, `requiredVars`, `requiredCustomerAppearances` 순서로 검사
- `GetNextMandatoryEpisode()` — `episodeType == Mandatory`이고 미완료인 에피소드 중 챕터 스코프(`GameProgress.CurrentChapterId` 설정 시)로 필터링해 `CanStart()`를 만족하는 첫 번째를 반환 (순서는 `prerequisiteEpisodeIds` 체이닝으로 보장)
- `HasPendingMandatoryEpisode()` — `GetNextMandatoryEpisode() != null`
- `StartEpisode(id)` — `CurrentPlayingEpisodeID` 설정 → 저장 → `GameState.Episode` 전환
- `ClearEpisode(id)` — `GameProgress.MarkEpisodeCompleted()` → 현재 에피소드 ID 초기화 → 저장
- `GetEpisodeData(id)` — id로 EpisodeData 검색

## DayFlowController (`CoreScene/Scripts/DayFlowController.cs`)

`MonoSingleton<DayFlowController>`. 하루 진행 순서(필수 에피소드 큐, 영업, 정산)를 전담합니다. `EpisodeRunner`/`EpisodeBoardManager`/`EpisodeUIManager`/영업 스텁은 `GameManager`를 직접 호출하지 않고 이 클래스를 거칩니다.

- `StartBusinessDay()` — Rest에서 "영업 시작" 클릭 시 호출. 먼저 `GameProgress.AdvanceDay()`로 Day 증가 후, 필수(Before) 에피소드가 있으면 그것부터 시작, 없으면 바로 `GameState.Business`
- `StartDefaultEpisode(id)` — Rest 보드에서 기본 에피소드 선택 시 호출. 먼저 `GameProgress.AdvanceDay()`로 Day 증가 후 에피소드 시작. 완료 후 곧장 정산으로 이어짐
- `OnEpisodeCompleted()` — `EpisodeRunner.EndEncounter()`에서 호출. 필수(Before) 에피소드였다면 영업으로, 아니면 정산으로 이동
- `OnBusinessCompleted()` — 영업(스텁/실제) 종료 시 호출. 필수(After) 에피소드가 있으면 그것을 시작, 없으면 정산으로 이동
- `GoToSettlement()` — `GameManager.ChangeState(GameState.Settlement)`

> MainMenu의 첫 에피소드 진입(`MainMenuManager`)은 Rest를 거치지 않아 위 두 메서드를 호출하지 않으므로 Day 1은 그대로 유지되고, 이후 Rest에서 처음 누르는 시작 버튼부터 Day가 증가한다.

## SettlementManager / SettlementUI (`CoreScene/Scripts/SettlementManager.cs`, `SettlementUI.cs`)

`SettlementManager`(`MonoSingleton`, CoreScene의 `Managers` 루트 오브젝트에 배치)는 `GameManager.ChangeState(GameState.Settlement)`에서 호출되는 `BeginSettlement()`을 통해 당일 정산을 처리합니다.

- `BuildSettlementSummary()`가 `GameProgress.GetDayDrinkSales()`(주문 1건당 등급·정가·페널티가 기록된 `BusinessSaleRecord` 리스트)를 순회해 `SettlementData`를 구성: 총 판매량(전체 건수+`listedPrice` 합), 팁(Good 건수+팁 합), 실수(Bad 건수+`listedPrice+penaltyAmount` 합), 배송 이용(`GameProgress.DayDeliveryCount/DayDeliverySpend`), 에피소드 커스텀 보상(`GameProgress.GetDaySettlementRewards()`)
- `ApplyRecordedIncome()`이 `DayTotalIncome - DayPaidMoneyIncome`(아직 지갑에 반영되지 않은 차액 — 음료 판매는 즉시 지급이라 보통 0, 에피소드 커스텀 보상만 여기서 실제 지급됨)만큼 `AddMoney()`한 뒤 `SettlementUI.Show(data, onClosed)` 호출. 보상/판정 공식 자체는 [business-interactions.md](../gameplay/business-interactions.md)와 `Assets/_Project/Features/Business/Runtime/Flow/BusinessOrderSessionModels.cs`(`BusinessOrderRewardCalculator`) 참고
- `SettlementUI` 연출 순서: 셔터 슬라이드 → 모니터 슬라이드(`SlideTo` 공용 코루틴, 셔터와 동일 로직 재사용) → 헤더(챕터명, `Day {n} 결과 보고`) → 스크롤 줄("총 판매량 x N +revenue" → "팁 x N +tip" → "실수 x N -missed" → "배송 이용 x N -spend" → 0개 이상의 커스텀 보상 줄, 값이 0인 항목은 줄 자체를 생략) → 푸터(총 소득/구분선/보유 자산)를 한 스텝씩 순차 표시
- 순차 표시 도중 클릭/스페이스 입력 시 남은 스텝 전부 즉시 표시(스킵), 다 표시된 후 "아무 키나 눌러 진행" 표시 상태에서 다시 입력하면 `SettlementUI.Close()` 호출 — 확인 버튼 없음, 입력 관례는 `InputRouter`의 대화 진행(`Input.GetMouseButtonDown(0) || Input.GetKeyDown(KeyCode.Space)`)과 동일
- `Close()`는 UI를 바로 감추지 않고 `onClosed` 콜백만 호출 → `SettlementManager.OnSettlementClosed()`가 `GameProgress.ResetDaySettlement()` → `DataManager.Save()` → `GameManager.ChangeState(GameState.Rest)` 순으로 진행. `GameState.Rest` 전환은 항상 정산 화면을 닫으면서 진입하므로, `GameManager`가 `SceneTransitionManager.TransitionToSubScene()`에 `onFadeOutComplete` 콜백으로 `SettlementManager.OnFadeOutComplete()`를 넘김 — 화면이 완전히 검게 된 직후(씬 언로드 전) 호출되어 `SettlementUI.HideAndReset()`으로 셔터/모니터를 원위치로 되돌리고 UI를 비활성화. 즉 셔터/모니터/보고서는 페이드아웃이 끝날 때까지 화면에 그대로 유지되고, 리셋은 화면이 안 보이는 시점에만 일어나 티가 나지 않음
- 씬 배치: `SettlementUI`(비주얼)는 CoreScene의 영속 오버레이 Canvas(페이드 캔버스와 같은 위치)에 두어야 어느 씬에서 전환되든 위에 표시됨. `SettlementManager`(로직)는 다른 `MonoSingleton`과 함께 `Managers` 오브젝트에 배치

## GameProgress (`Scripts/GameProgress.cs`)

`MonoSingleton<GameProgress>`. 런타임 게임 상태의 단일 Source of Truth입니다.

| 메서드 | 설명 |
|---|---|
| `LoadFrom(SaveData)` | DataManager.Load() 직후 호출, 디스크 데이터를 런타임 상태로 반영 |
| `GetFlagList()` / `GetCompletedList()` | DataManager.Save() 직전 데이터 수거용 |
| `GetAffinityKeys()` / `GetAffinityValues()` | 호감도 변수 직렬화용 수거 |
| `GetBoardSlotKeys()` / `GetBoardSlotValues()` | 보드 슬롯 위치 직렬화용 수거 |
| `GetBottleAmountKeys()` / `GetBottleAmountValues()` | 병 재고 직렬화용 수거 |
| `HasFlag` / `SetFlag` / `ClearFlag` | 스토리 플래그 관리 |
| `IsEpisodeCompleted` / `MarkEpisodeCompleted` | 에피소드 완료 기록 |
| `GetAffinity` / `SetAffinity` / `AddAffinity` | 호감도 정수 변수 — CSV `varName` 필드와 연결 |
| `GetBoardSlot` / `SetBoardSlot` / `ClearBoardSlot` | 에피소드 보드 슬롯 위치 (affinity와 저장소 분리) |
| `GetBottleAmount` / `SetBottleAmount` | 병 ID별 남은 전체 재고 |
| `SetCurrentDay` / `CurrentDay` | 게임 내 일수 |
| `AdvanceDay` | 일수 +1. `DayFlowController`의 Rest 시작 진입점(`StartBusinessDay`/`StartDefaultEpisode`)에서 호출 |
| `GetCustomerAppearance` / `IncrementCustomerAppearance` | 손님 등장 횟수 (affinity와 동일한 key/value 리스트 패턴). 실제 증가 호출은 영업 시스템(별도 담당자) 책임 |
| `SetCurrentChapter` / `CurrentChapterId` | 현재 챕터 ID. 들어온 chapterId가 기존과 다르면 `currentDay`를 1로 리셋(챕터가 바뀌면 Day 1부터 재시작). 최초 게임 시작 시 `MainMenuManager`가 비어있으면 `ChapterData.LoadFirst()`로 채움 — 게임 중 챕터 전환 트리거 자체는 미정, 훅만 존재 |
| `AddMoney` / `CurrentMoney` | 누적 보유 금액 (세이브 영속) |
| `RecordDrinkSale` / `AddDayIncome` / `ResetDaySettlement` | 주문 1건 결과(`BusinessSaleRecord`, 등급·정가·팁·페널티 포함)를 당일 리스트(`dayDrinkSales`)와 집계 필드에 누적, 정산 후 전체 당일 집계 리셋 |
| `RecordDeliveryPurchase` | 배송 탭 구매 1건마다 `DayDeliveryCount`/`DayDeliverySpend` 누적 |
| `AddSettlementReward` / `GetDaySettlementRewards` | 에피소드 종료 시 조건을 만족한 커스텀 보상(라벨+금액)을 `dayTotalIncome`에는 즉시 더하되(정산 시점 실지급) 목록에 기록 — `EpisodeRunner.EndEncounter()`가 호출 |

## DataManager (`CoreScene/Scripts/DataManager.cs`)

`MonoSingleton<DataManager>`. JSON 저장/로드를 담당합니다.

- `Save()` — `GameProgress.Instance`에서 데이터 수거 → `JsonUtility.ToJson(SaveData)` → `autosave.json` 직접 기록
- `Load()` — 파일 읽기 → `GameProgress.Instance.LoadFrom(CurrentData)` 호출

현재 저장은 임시 파일 교체나 백업 없이 본 파일에 직접 쓰며, 진행 중인 에피소드 노드와 `GameState`는 저장하지 않습니다.

## SaveData (`CoreScene/Scripts/SaveData.cs`)

JsonUtility로 직렬화되는 저장 구조체입니다.

```csharp
public class SaveData
{
    public string gameVersion;
    public int dayCount;
    public List<string> flags;
    public List<string> completedEpisodeIds;
    public List<string> affinityKeys;    // 호감도 변수 키
    public List<int>    affinityValues;
    public List<string> boardSlotKeys;   // 에피소드 보드 슬롯 위치
    public List<int>    boardSlotValues;
    public List<string> bottleAmountKeys;    // 술장 병 잔여량
    public List<float>  bottleAmountValues;
    public List<string> customerAppearanceKeys;  // 손님 등장 횟수
    public List<int>    customerAppearanceValues;
    public List<string> upgradeKeys;     // 상점 업그레이드 레벨
    public List<int>    upgradeValues;
    public string currentChapterId;
    public int    currentMoney;          // 누적 보유 금액
    public int    reputation;

    // 당일 집계 (정산 후 ResetDaySettlement()로 리셋)
    public int    dayDrinkSalesCount;
    public int    dayDrinkBaseRevenue;
    public int    dayDrinkTipRevenue;
    public int    dayDrinkRevenue;
    public int    dayTotalIncome;
    public int    dayPaidMoneyIncome;
    public int    dayStrangeCoinBaseRevenue;
    public int    dayStrangeCoinTipRevenue;
    public int    dayStrangeCoinRevenue;
    public int    dayPaidStrangeCoinIncome;
    public int    dayReputationDelta;
    public List<BusinessSaleRecord> dayDrinkSales;       // 주문 1건당 등급·정가·팁·페널티
    public int    dayDeliveryCount;
    public int    dayDeliverySpend;
    public List<SettlementRewardEntry> daySettlementRewards; // 에피소드 커스텀 정산 보상

    public string tvForecastBroadcastId;
    public bool   tvForecastRevealed;
    public string tvActiveBroadcastId;
    public int    tvActiveBusinessDay;
}
```

`BusinessSequenceEntrySnapshot`은 주문 키, 레시피 ID, 방문 키, 주문 유형을 저장합니다. `CustomerVisitHistorySnapshot`은 방문 키, 마지막 방문 날짜, 누적 방문 횟수를 저장합니다.

## 미결 사항

- 캐릭터 조우(등장) 횟수 저장소는 `GameProgress.GetCustomerAppearance`/`IncrementCustomerAppearance`로 구현됨 — 실제 증가 호출(영업 중 손님 등장 시점)은 영업 시스템(별도 담당자) 통합 대기 중
- `currentChapterId` 전환 트리거 로직 미정 — 최초 게임 시작 시 `MainMenuManager`가 `ChapterData.LoadFirst()`로 첫 챕터를 설정하는 것까지만 구현됨. 게임 진행 중 챕터가 실제로 바뀌는 지점(특정 에피소드 완료 등)은 아직 없고, `SetCurrentChapter` 호출 시 Day 1 리셋 훅만 존재
- `Business` 상태는 `BusinessStubUI` 스텁 — 실제 영업 시스템은 별도 담당자가 통합 예정
- 자세한 하루 흐름 설계는 [game-flow-design.md](game-flow-design.md) 참고
