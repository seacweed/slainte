# 상황판 에피소드 선행 조건(Prerequisite) 시스템 기획안

사용자님이 말씀하신 "에피소드 간의 관계성과 선행 조건"을 처리하기 위해 다음과 같은 구조를 제안합니다.

## User Review Required

> [!IMPORTANT]
> **표시 방식 선택 (A vs B)**
> 현재 상황판에 띄울 폴라로이드 사진(에피소드)을 유니티 화면 상에 노출시키는 방법으로 어떠한 것을 선호하시나요?
>
> - **[방법 A. 수동 배치 / 동적 가리기 - 추천]**: 에디터 내에서 기획자가 미리 사진(오브젝트)들을 예쁘게 흩뿌려 배치해놓습니다. 게임이 켜질 때, **`선행 에피소드를 클리어하지 않은 사진`**은 코드가 자동으로 찾아서 투명하게 숨기거나 `SetActive(false)` 처리하여 안 보이게 만듭니다. (조건이 달성되면 짠 하고 등장)
> - **[방법 B. 좌표 기반 동적 소환]**: 엑셀(CSV) 표에 아예 X좌표, Y좌표 열을 추가합니다. 빈 상황판만 두고, 조건이 만족된 에피소드들만 코드가 해당 좌표에 Prefab을 새로 생성(Instantiate)하여 꽂아버립니다. 
> 
> *대부분의 수사 보드 연출은 삐뚤빼뚤한 디테일이 생명이므로 시각적 편집이 편한 **방법 A**를 권장합니다.*

## Proposed Changes

---

### 1. Data Schema & CSV Update
#### [MODIFY] [EpisodeData.cs](file:///c:/unity_proj/slainte/slainte/Assets/RestScene/Scripts/EpisodeData.cs)
- `public List<string> prerequisiteEpisodeIDs;` 필드를 선언하여 이 에피소드를 열기 위해 반드시 클리어해야 하는 이전 에피소드들의 ID 리스트를 보관합니다.

#### [MODIFY] [EpisodeCSVImporter.cs](file:///c:/unity_proj/slainte/slainte/Assets/Editor/EpisodeCSVImporter.cs)
- 추후 작성해주실 CSV 파일에 **`Required_Episodes`** 열이 생길 것을 고려하여, 해당 문자열을 파싱하는 로직을 임포터에 추가합니다. (작동 방식은 Characters 컬럼 파싱과 동일합니다)

---

### 2. Dependency System & Manager Logic
#### [MODIFY] [EpisodeManager.cs](file:///c:/unity_proj/slainte/slainte/Assets/CoreScene/Scripts/EpisodeManager.cs)
- `public bool IsAvailableToStart(EpisodeData data)` 메서드를 추가합니다.
- 동작 원리: 반복문으로 `data.prerequisiteEpisodeIDs`를 모두 조회한 뒤, 단 하나라도 현재 `progress.isCleared == false` 이면, `false`를 반환하여 보드판에 오를 수 없다고 차단합니다.

---

### 3. Situation Board Refresh
#### [MODIFY] [EpisodeBoardManager.cs](file:///c:/unity_proj/slainte/slainte/Assets/RestScene/Scripts/EpisodeBoardManager.cs)
- `OnOpen()` 단계에서 씬 내부에 널려있는 모든 `EpisodePhotoTrigger`들을 싹 긁어모아옵니다.
- 반복문을 돌리면서 아까 만든 `IsAvailableToStart(photo.episodeData)`를 찔러봅니다.
- **가능하다면 활성화, 닫혀있다면 비활성화 / 혹은 이미 진행 중(isStarted)이면 또다른 처리** 등을 수행하도록 동적 새로고침 기능을 추가합니다.

## Open Questions

> [!TIP]  
> 가장 핵심이 되는 질문은 **어떻게 사진을 배열하실건지 (방법 A vs B)** 입니다! 어떤 방식이 더 직관적이신가요? 
> 또한 클리어된/또는 진행 중인 에피소드는 상황판에서 어떻게 처리하는게 기획 의도와 부합하나요? (ex. 보드뷰에서 영구 삭제? 붉은 도장이 찍힌 채로 보존??)
