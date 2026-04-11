# 코드베이스 병합 계획

> 작성일: 2026-04-07
> 대상: 내 코드(BusinessScene/Scripts) + 상대방 코드(CoreScene/RestScene) 병합

---

## 배경

두 사람이 별도로 작업한 뒤 merge. 테스트용 임시 빌드가 섞여 있어 의도가 겹치는 부분이 다수 존재.

**게임 진행 흐름**: Episode → Rest → Episode → Rest → ...

- **Episode 씬**: 내가 만든 `EpisodeRunner` 기반 에피소드 진행 (CSV 임포터 + SO)
- **Rest 씬**: 상대방이 만든 RestScene UI에서 에피소드 선택
- **Yarn Spinner 미사용**: 상대방이 임시로 연동했으나 제거 예정

---

## 현재 구조 분석

### 내 코드 (Scripts/, BusinessScene 기반)

| 파일 | 역할 |
|---|---|
| `Scripts/GameProgress.cs` | 런타임 상태 (flags, completedEpisodeIds, vars, currentDay) |
| `Scripts/Conversation/Episode/EpisodeData.cs` | 에피소드 실행 데이터 SO (triggerCondition + nodes 포함) |
| `Scripts/Conversation/Episode/EpisodeTriggerCondition.cs` | 에피소드 시작 조건 정의 |
| `Scripts/Conversation/Episode/EpisodeTriggerManager.cs` | 조건 체크 + EpisodeRunner 실행 (씬 내 컴포넌트) |
| `Scripts/Conversation/Episode/EpisodeRunner.cs` | 에피소드 실행 엔진 (노드 트리 순회) |
| `Scripts/Core/GameModeManager.cs` | Episode 씬 내 UI 패널 모드 관리 |
| `Scripts/Conversation/DialogueController.cs` | 대사 버블 UI + 타이핑 효과 |
| `Scripts/Conversation/Sell/CustomerSpawner.cs` | 고객 표시 및 주문 대사 |

### 상대방 코드 (CoreScene/, RestScene/)

| 파일 | 역할 |
|---|---|
| `CoreScene/Scripts/MonoSingleton.cs` | 싱글톤 베이스 클래스 |
| `CoreScene/Scripts/GameManager.cs` | 게임 상태 전환 (Episode/Rest/Business) |
| `CoreScene/Scripts/SceneTransitionManager.cs` | 페이드 + Additive 씬 전환 |
| `CoreScene/Scripts/DataManager.cs` | JSON 저장/로드 |
| `CoreScene/Scripts/SaveData.cs` | 직렬화 저장 구조 |
| `CoreScene/Scripts/EpisodeManager.cs` | 에피소드 진행도 관리 (EpisodeBoardData 기반) |
| `CoreScene/Scripts/DialogueManager.cs` | Yarn Spinner 연동 → **제거 예정** |
| `RestScene/Scripts/EpisodeBoardData.cs` | RestScene UI용 에피소드 메타데이터 SO → **흡수 예정** |
| `RestScene/Scripts/EpisodeBoardManager.cs` | 에피소드 선택 보드 UI |
| `RestScene/Scripts/EpisodePhotoTrigger.cs` | 에피소드 아이콘 호버/클릭 |
| `RestScene/Scripts/EpisodeInfoUI.cs` | 에피소드 상세 정보 팝업 |
| `RestScene/Scripts/BaseUIManager.cs` | UI 애니메이션 추상 베이스 |

---

## 겹치는 부분 (충돌 목록)

### 1. 에피소드 완료 여부 추적 이중화
- 내 코드: `GameProgress.completedEpisodeIds` (HashSet)
- 상대방: `EpisodeProgressData.isCleared` + `EpisodeManager.progressDict`

### 2. 에피소드 시작 조건 체크 이중화
- 내 코드: `EpisodeTriggerManager.CanStart()` (`GameProgress` 참조)
- 상대방: `EpisodeManager.IsAvailableToStart()` (`EpisodeProgressData` 참조, 선행 ID만 체크)

### 3. 플래그/변수/날짜 저장 이중화
- 내 코드: `GameProgress` (flags, varKeys/varValues, currentDay)
- 상대방: `SaveData` (unlockedStoryFlags, variables Dictionary, dayCount)
- `variables`는 JsonUtility로 직렬화 불가 (Dictionary)

### 4. 에피소드 데이터 구조 이중화
- 내 코드: `EpisodeData` (실행 데이터 + triggerCondition)
- 상대방: `EpisodeBoardData` (RestScene UI 메타데이터)

### 5. 싱글톤 구현 불통일
- `GameProgress`, `GameModeManager`: 직접 싱글톤 구현
- CoreScene 매니저들: `MonoSingleton<T>` 상속

### 6. 에피소드 실행 엔진 충돌
- 내 코드: `EpisodeRunner.Begin(EpisodeData)`
- 상대방: `DialogueManager.StartEpisode(nodeName)` (Yarn Spinner)

---

## 병합 계획 (작업 순서)

### Step 1 — `EpisodeData` 확장
**파일**: `Assets/Scripts/Conversation/Episode/EpisodeData.cs`

`EpisodeBoardData`의 UI/메타 필드를 흡수:

```csharp
// 추가할 헤더 및 필드
[Header("Board Display")]
[TextArea(3, 5)] public string episodeDescription;
public string iconNameBoard;
public string iconNameArchive;
public List<EpisodeCharacter> characters;   // UI 표시용 캐릭터 이름 목록
```

`EpisodeCharacter` struct를 `EpisodeBoardData.cs`에서 `EpisodeData.cs`로 이전.
`portraitSprite` 필드는 제거하고 `characterName`만 유지.

**포함하지 않는 항목:**
- `EpisodeCategory` — 제거 (미사용)
- `episodeName` / `episodeNameEng` — `EpisodeData.episodeTitle`로 통일
- `List<EpisodeCondition> conditions` — `EpisodeCondition` 달성 여부 추적은 별도로 CoreScene에서 관리 예정

---

### Step 2 — `SaveData` 재설계
**파일**: `Assets/CoreScene/Scripts/SaveData.cs`

```csharp
// 제거
// - Dictionary<string, float> variables  (Yarn용, JsonUtility 직렬화 불가)
// - List<string> unlockedStoryFlags
// - List<EpisodeProgressData> episodeProgressList
// - string currentEpisodeID

// 추가
public List<string> flags = new();
public List<string> completedEpisodeIds = new();
public List<string> varKeys = new();
public List<int>    varValues = new();

// 유지
public string gameVersion = "1.0.0";
public int    dayCount = 1;
```

`EpisodeProgressData` 클래스 삭제 (같은 파일 `EpisodeBoardData.cs`에 있음 — Step 1 완료 후 삭제).

---

### Step 3 — `GameProgress` 수정
**파일**: `Assets/Scripts/GameProgress.cs` → `Assets/CoreScene/Scripts/GameProgress.cs`로 이동

```csharp
// 변경
public class GameProgress : MonoSingleton<GameProgress>
{
    // 기존 Awake() 직접 구현 제거 (MonoSingleton이 처리)

    // 추가 메서드
    public void LoadFrom(SaveData data);      // DataManager.Load()에서 호출
    public List<string> GetFlagList();        // DataManager.Save()에서 수거
    public List<string> GetCompletedList();
    public List<string> GetVarKeys();
    public List<int>    GetVarValues();
}
```

---

### Step 4 — `DataManager` 수정
**파일**: `Assets/CoreScene/Scripts/DataManager.cs`

```csharp
public void Save()
{
    GameProgress gp = GameProgress.Instance;
    if (gp != null)
    {
        CurrentData.dayCount          = gp.CurrentDay;
        CurrentData.flags             = gp.GetFlagList();
        CurrentData.completedEpisodeIds = gp.GetCompletedList();
        CurrentData.varKeys           = gp.GetVarKeys();
        CurrentData.varValues         = gp.GetVarValues();
    }
    // JSON 저장 (기존 유지)
}

public void Load()
{
    // JSON 로드 (기존 유지)
    GameProgress.Instance?.LoadFrom(CurrentData);
    // EpisodeManager.LoadProgress() 호출 제거
}
```

---

### Step 5 — `EpisodeManager` 재설계
**파일**: `Assets/CoreScene/Scripts/EpisodeManager.cs`

`EpisodeBoardData` → `EpisodeData` 기반으로 전면 교체.
`EpisodeTriggerManager.CanStart()` 로직 흡수.

```csharp
public class EpisodeManager : MonoSingleton<EpisodeManager>
{
    private List<EpisodeData> allEpisodes = new();
    public string CurrentPlayingEpisodeID { get; private set; }

    // LoadAllEpisodes(): Resources.LoadAll<EpisodeData>("EpisodeData")

    public List<EpisodeData> GetAvailableEpisodes()
    // → GameProgress.Instance로 CanStart() 판단

    public bool CanStart(EpisodeData ep, GameProgress gp)
    // ← EpisodeTriggerManager.CanStart() 로직 이식
    // (minDay / requiredFlags / blockedFlags / prerequisiteEpisodeIds / requiredVars)

    public EpisodeData GetEpisodeData(string id)
    // → GameManager의 콜백에서 사용

    public void StartEpisode(string episodeId)
    // CurrentPlayingEpisodeID = episodeId
    // DataManager.Instance.Save()
    // GameManager.Instance.ChangeState(GameState.Episode)

    public void ClearEpisode(string episodeId)
    // GameProgress.Instance.MarkEpisodeCompleted(episodeId)
    // DataManager.Instance.Save()

    // 삭제: progressDict, GetProgress(), LoadProgress(), SaveProgress()
}
```

---

### Step 6 — `GameManager` 수정
**파일**: `Assets/CoreScene/Scripts/GameManager.cs`

Episode 씬 로드 후 콜백에서 `DialogueManager` → `EpisodeRunner` 연결:

```csharp
case GameState.Episode:
    sceneName = "BusinessScene";
    onTransitionComplete = () =>
    {
        string epId  = EpisodeManager.Instance?.CurrentPlayingEpisodeID;
        EpisodeData data = EpisodeManager.Instance?.GetEpisodeData(epId);
        var runner = Object.FindFirstObjectByType<EpisodeRunner>();
        runner?.Begin(data);
    };
    break;
```

임시 테스트용 Update() 키 입력 코드 제거.
`GameState.Business` enum 값 제거 (영업 씬이 별도로 존재하지 않으므로).

---

### Step 7 — RestScene UI 수정

`EpisodeBoardData` → `EpisodeData` 참조로 교체.

| 파일 | 변경 내용 |
|---|---|
| `RestScene/Scripts/EpisodeBoardManager.cs` | `List<EpisodeBoardData>` → `List<EpisodeData>`, `EpisodeManager.GetProgress()` 참조 제거 |
| `RestScene/Scripts/EpisodePhotoTrigger.cs` | `EpisodeBoardData episodeData` → `EpisodeData episodeData` |
| `RestScene/Scripts/EpisodeInfoUI.cs` | `EpisodeBoardData` → `EpisodeData`, 조건 달성 여부는 `GameProgress.Instance.IsEpisodeCompleted()` 기반으로 변경 |

---

### Step 8 — `GameModeManager` 싱글톤 통일
**파일**: `Assets/Scripts/Core/GameModeManager.cs`

```csharp
// 변경
public class GameModeManager : MonoSingleton<GameModeManager>
// 직접 Awake() 싱글톤 구현 제거
```

---

### Step 9 — 파일 삭제

| 파일 | 이유 |
|---|---|
| `CoreScene/Scripts/DialogueManager.cs` | Yarn 미사용, EpisodeRunner로 대체 |
| `Scripts/Conversation/Episode/EpisodeTriggerManager.cs` | EpisodeManager로 로직 흡수 |
| `RestScene/Scripts/EpisodeBoardData.cs` | EpisodeData로 통합 완료 후 삭제 |

---

## 작업 순서 (의존성)

```
Step 1 (EpisodeData 확장)
  → Step 2 (SaveData 재설계)
  → Step 3 (GameProgress 수정)
    → Step 4 (DataManager 수정)
    → Step 5 (EpisodeManager 재설계)
      → Step 6 (GameManager 수정)
      → Step 7 (RestScene UI 수정)
  → Step 8 (GameModeManager 싱글톤)
  → Step 9 (파일 삭제)
```

Step 1이 완료되어야 나머지 모든 파일이 `EpisodeData`를 올바르게 참조할 수 있음.
Step 9는 모든 참조가 교체된 것을 확인한 뒤 마지막에 수행.

---

## 미결 사항

- `EpisodeCondition` 달성 여부 추적:
  `EpisodeBoardData.conditions`는 EpisodeData에 흡수하지 않음.
  대신 조건(완료된 에피소드 ID)의 달성 여부는 `GameProgress.completedEpisodeIds`로 판단하는 방식으로 추후 설계.
  `EpisodeProgressData.conditionUnlocks` / `characterMeets` 배열은 함께 제거.

- `EpisodeData`에 조건 텍스트 필드 추가 예정:
  `EpisodeInfoWindow.conditionsText` UI는 보존되어 있음.
  추후 `EpisodeData`에 조건 관련 필드 추가 시 `EpisodeInfoWindow.Show()`에서 연결.

- `EpisodeCategory` enum: 미사용으로 제거. `EpisodeBoardData.cs` 삭제 시 함께 사라짐.

- `EpisodeUIManager`의 `isBusinessOpen`, `money` 임시 코드:
  영업 씬 미구현이므로 당분간 유지, 영업 씬 구현 시 분리.

- Yarn Spinner 패키지:
  `DialogueManager.cs` 삭제 후에도 패키지 자체는 남아 있음. 완전히 제거할지 여부는 추후 결정.
