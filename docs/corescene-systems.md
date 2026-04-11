# CoreScene 시스템

CoreScene은 게임 전체에서 DontDestroyOnLoad로 유지되는 매니저들이 배치된 씬입니다.

## 게임 진행 흐름

```
RestScene
  └─ EpisodeBoardManager (에피소드 선택)
       └─ EpisodeManager.StartEpisode(id)
            ├─ DataManager.Save()
            └─ GameManager.ChangeState(GameState.Episode)
                  └─ SceneTransitionManager → BusinessScene 로드
                        └─ EpisodeRunner.Begin(EpisodeData) (씬 로드 완료 콜백)

BusinessScene (에피소드 진행)
  └─ EpisodeManager.ClearEpisode(id)
        ├─ GameProgress.MarkEpisodeCompleted(id)
        ├─ DataManager.Save()
        └─ GameManager.ChangeState(GameState.Rest)
              └─ SceneTransitionManager → RestScene 로드
```

## GameManager (`CoreScene/Scripts/GameManager.cs`)

`MonoSingleton<GameManager>`. 게임 상태 전환과 씬 로드를 담당합니다.

```csharp
public enum GameState { None, Episode, Rest }
```

- `ChangeState(GameState)` — 이전 상태가 Episode였으면 자동 저장 후 씬 전환
- Episode 전환 시: BusinessScene 로드 → 콜백에서 `EpisodeRunner.Begin(EpisodeData)` 호출

## EpisodeManager (`CoreScene/Scripts/EpisodeManager.cs`)

`MonoSingleton<EpisodeManager>`. 에피소드 데이터 관리와 시작/완료 처리를 담당합니다.

- `LoadAllEpisodes()` — `Resources.LoadAll<EpisodeData>("EpisodeData")`로 Awake 시 일괄 로드
- `GetAvailableEpisodes()` — `GameProgress`로 완료 여부 및 `CanStart()` 조건 체크 후 목록 반환
- `CanStart(EpisodeData, GameProgress)` — `EpisodeTriggerCondition` 기반 조건 검사
  - `minDay`, `requiredFlags`, `blockedFlags`, `prerequisiteEpisodeIds`, `requiredVars` 순서로 검사
- `StartEpisode(id)` — `CurrentPlayingEpisodeID` 설정 → 저장 → `GameState.Episode` 전환
- `ClearEpisode(id)` — `GameProgress.MarkEpisodeCompleted()` → 저장
- `GetEpisodeData(id)` — id로 EpisodeData 검색

## GameProgress (`Scripts/GameProgress.cs`)

`MonoSingleton<GameProgress>`. 런타임 게임 상태의 단일 Source of Truth입니다.

| 메서드 | 설명 |
|---|---|
| `LoadFrom(SaveData)` | DataManager.Load() 직후 호출, 디스크 데이터를 런타임 상태로 반영 |
| `GetFlagList()` / `GetCompletedList()` / `GetVarKeys()` / `GetVarValues()` | DataManager.Save() 직전 데이터 수거용 |
| `HasFlag` / `SetFlag` / `ClearFlag` | 스토리 플래그 관리 |
| `IsEpisodeCompleted` / `MarkEpisodeCompleted` | 에피소드 완료 기록 |
| `GetVar` / `SetVar` / `AddVar` | 정수형 전역 변수 (호감도 등) |
| `SetCurrentDay` / `CurrentDay` | 게임 내 일수 |

## DataManager (`CoreScene/Scripts/DataManager.cs`)

`MonoSingleton<DataManager>`. JSON 저장/로드를 담당합니다.

- `Save()` — `GameProgress.Instance`에서 데이터 수거 → `JsonUtility.ToJson(SaveData)` → 파일 기록
- `Load()` — 파일 읽기 → `GameProgress.Instance.LoadFrom(CurrentData)` 호출

## SaveData (`CoreScene/Scripts/SaveData.cs`)

JsonUtility로 직렬화되는 저장 구조체입니다.

```csharp
public class SaveData
{
    public string gameVersion;
    public int dayCount;
    public List<string> flags;
    public List<string> completedEpisodeIds;
    public List<string> varKeys;
    public List<int>    varValues;
}
```

## 미결 사항

- `EpisodeData`에 조건 텍스트 필드 추가 예정 — `EpisodeInfoWindow.conditionsText` UI 연결 대기 중
- 캐릭터 조우 여부 추적 — `EpisodeInfoWindow.unknownPortrait` 로직 보존됨, 추후 `GameProgress` 구조 추가 검토
- Yarn Spinner 패키지 — `DialogueManager.cs` 삭제됨, 패키지 자체 제거 여부는 추후 결정
