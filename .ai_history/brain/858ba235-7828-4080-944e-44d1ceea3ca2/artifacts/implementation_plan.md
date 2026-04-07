# 전체 시연 사이클 및 자동 저장(Save) 통합 기획안

사용자님의 완벽한 "원 사이클 시연"을 위해 **1순위(Yarn 대본 연결)와 2순위(세이브/로드 기능)**를 통합하여 단번에 처리하는 기획안입니다!

---

## Proposed Changes

### 1. 테스트 대본(Yarn) 교체 
#### [MODIFY] [TestEpisode.yarn](file:///c:/unity_proj/slainte/slainte/Assets/Dialogue/TestEpisode.yarn)
- 현재 `Start` 노드 하나만 존재하는 스크립트를 밀어버립니다.
- CSV에 작성해 두신 ID에 대응되도록 **`1001`**, **`1002`**, **`1003`**번 대화 노드를 꼬리물기 식으로 작성합니다. 
- (이제 상황판에서 1001번 상황의 시작을 누르면 정말로 1001번용 대사가 짠 하고 나타나게 됩니다!)

### 2. 에피소드 진행도 영구 보존(SaveData) 
#### [MODIFY] [SaveData.cs](file:///c:/unity_proj/slainte/slainte/Assets/CoreScene/Scripts/SaveData.cs)
- `public List<EpisodeProgressData> episodeProgressList` 필드 추가.
- (유니티는 Dictionary를 자동 저장하지 못하므로 List 형태의 주머니를 따로 분양합니다.)

#### [MODIFY] [EpisodeManager.cs](file:///c:/unity_proj/slainte/slainte/Assets/CoreScene/Scripts/EpisodeManager.cs)
- `LoadProgress(List)`: 저장된 파일을 읽어올 때 본인의 내부 딕셔너리로 데이터를 풀어넣는 함수 추가
- `SaveProgress()`: 자신의 내부 딕셔너리에 담긴 최신 정보들(어떤 걸 깼고 수락했는지)을 List로 쭉 뽑아서 돌려주는 함수 추가

### 3. 세이브 매니저와의 릴레이 작업
#### [MODIFY] [DataManager.cs](file:///c:/unity_proj/slainte/slainte/Assets/CoreScene/Scripts/DataManager.cs)
- `Load()` 시점의 맨 끝자락에, 방금 디스크 파일에서 읽어 들인 `episodeProgressList`를 `EpisodeManager`의 입에다 먹여줍니다.
- `Save()` 시점의 맨 첫 자락에, 현재 실무를 뛰고 있는 `EpisodeManager`에게 최신 데이터 뭉치를 뽑아내(SaveProgress), `CurrentData`에 꽂아 넣은 다음 JSON 파일로 굽게 만듭니다.

---

## User Review Required

> [!NOTE] 
> 3번의 `DataManager` 작업 덕분에, **대화 씬에서 `<<EndEpisode>>`가 호출되어 `GameManager`가 휴식(Rest)모드로 돌아올 때마다 자동으로 스냅샷 세이브가 발동**됩니다. 
> 즉, 게임이 예상치 못하게 튕기더라도 방금 깬 에피소드 기록이 디스크에 안전하게 남게 됩니다!
> 
> 이 완벽한 연동 뼈대 작업들의 승인(Proceed)을 내려주시면 즉시 코드 작성에 들어가겠습니다!
