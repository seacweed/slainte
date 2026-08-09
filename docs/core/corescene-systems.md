# CoreScene 시스템

CoreScene은 게임 전체에서 유지되는 매니저와 화면 전환 페이드를 보관합니다. 일부 `MonoSingleton`은 씬에 없을 때 첫 `Instance` 접근으로 생성됩니다.

## 게임 진행 흐름

```
MainMenu
  └─ DayFlowManager.StartInitialEpisode(id)
       └─ EpisodeManager.StartEpisode(id)
            └─ GameManager.ChangeState(GameState.Episode)
                 └─ BusinessScene → EpisodeRunner.Begin(EpisodeData)

BusinessScene (Episode)
  ├─ 제조 노드 → 공용 주문 세션
  │    ├─ 영업과 동일한 제조·판정·피드백
  │    └─ 사용한 병 재고 차감 후 에피소드 결과 분기로 복귀
  └─ EpisodeRunner 완료
       └─ DayFlowManager.CompleteEpisode(id)
            ├─ EpisodeManager.ClearEpisode(id)
            └─ GameManager.ChangeState(GameState.Business)
                 └─ 같은 BusinessScene에서 BusinessSequenceRunner 시작

BusinessScene (Business)
  └─ BusinessSequenceRunner 완료
       └─ DayFlowManager.CompleteBusinessDay()
            ├─ currentDay + 1
            ├─ DataManager.Save()
            └─ GameManager.ChangeState(GameState.Rest)
                 └─ RestScene 로드

RestScene
  └─ EpisodeBoardManager (다음 날 에피소드 선택)
       └─ DayFlowManager.StartEpisodeFromRest(id)
            └─ GameState.Episode로 전환하며 반복
```

## GameManager (`CoreScene/Scripts/GameManager.cs`)

`MonoSingleton<GameManager>`. 게임 상태 전환과 씬 로드를 담당합니다.

```csharp
public enum GameState { None, Episode, Business, Rest }
```

- `ChangeState(GameState)` — 이전 상태가 Episode였으면 자동 저장 후 씬 전환
- Episode 전환 시: BusinessScene 로드 → 콜백에서 `EpisodeRunner.Begin(EpisodeData)` 호출
- Business 전환 시: BusinessScene을 재로드하지 않고 `BusinessFlowBootstrap.StartBusinessSequence()` 호출
- Rest 전환 시: RestScene 로드

## DayFlowManager (`CoreScene/Scripts/DayFlowManager.cs`)

`MonoSingleton<DayFlowManager>`. 에피소드, 영업, 휴식 사이의 하루 단위 전환을 담당합니다.

- `StartInitialEpisode(id)` — 메인 메뉴에서 첫 에피소드 시작
- `StartEpisodeFromRest(id)` — 휴식 화면에서 선택한 다음 에피소드 시작
- `CompleteEpisode(id)` — 에피소드 완료 처리 후 같은 BusinessScene에서 영업 시작
- `CompleteBusinessDay()` — 날짜를 1 증가시키고 저장한 뒤 RestScene으로 전환
- `currentDay`는 영업 완료 시 증가하므로 RestScene의 에피소드 조건은 다음 날을 기준으로 평가됨

## EpisodeManager (`CoreScene/Scripts/EpisodeManager.cs`)

`MonoSingleton<EpisodeManager>`. 에피소드 데이터 관리와 시작/완료 처리를 담당합니다.

- `LoadAllEpisodes()` — `Resources.LoadAll<EpisodeData>("EpisodeData")`로 Awake 시 일괄 로드
- `GetAvailableEpisodes()` — `GameProgress`로 완료 여부 및 `CanStart()` 조건 체크 후 목록 반환
- `CanStart(EpisodeData, GameProgress)` — `ProgressConditionEvaluator`에 `EpisodeTriggerCondition` 평가 위임
- `StartEpisode(id)` — `CurrentPlayingEpisodeID` 설정 → 저장 → `GameState.Episode` 전환
- `ClearEpisode(id)` — `GameProgress.MarkEpisodeCompleted()` → 현재 에피소드 ID 초기화 → 저장
- `GetEpisodeData(id)` — id로 EpisodeData 검색

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
| `GetBusinessDaySnapshot` / `SetBusinessDaySnapshot` | 진행 중인 영업 순서 저장/복원 |
| `AdvanceBusinessSequence` | 영업 주문 순서 진행 |
| `GetCustomerVisitHistory` / `RecordCustomerVisit` | 마지막 방문 날짜와 누적 방문 횟수 |
| `AddMoney` / `AddReputation` | 영업 보상 반영 |

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
    public List<string> bottleAmountKeys;
    public List<float>  bottleAmountValues;
    public int money;
    public int reputation;
    public BusinessDaySnapshot businessDay;
    public List<CustomerVisitHistorySnapshot> customerVisitHistory;
}
```

`BusinessSequenceEntrySnapshot`은 주문 키, 레시피 ID, 방문 키, 주문 유형을 저장합니다. `CustomerVisitHistorySnapshot`은 방문 키, 마지막 방문 날짜, 누적 방문 횟수를 저장합니다.

## 미결 사항

- `EpisodeData`에 조건 텍스트 필드 추가 예정 — `EpisodeInfoWindow.conditionsText` UI 연결 대기 중
- 캐릭터 조우 여부 추적 — `EpisodeInfoWindow.unknownPortrait` 로직 보존됨, 추후 `GameProgress` 구조 추가 검토
- 저장 원자성, 손상 복구, 실행 중인 상태·노드 이어하기 정책 필요
