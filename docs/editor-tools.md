# 에디터 툴

Unity 에디터에서 사용할 수 있는 커스텀 툴 목록입니다.  
모든 툴은 `Assets/Editor/` 에 위치합니다.

| 툴 | 파일 | 메뉴 경로 |
|---|---|---|
| 에피소드 CSV 임포터 | `EpisodeCsvImporter.cs` | Tools > Slainte > Import Episode CSV |
| 아이템 데이터 임포터 | `ItemDataImporter.cs` | Tools > Import Item Data (CSV) |

---

## 에피소드 CSV 임포터

CSV 파일을 읽어 `EpisodeData` ScriptableObject를 생성하는 툴입니다.  
CSV 작성 방법은 [episode-csv-guide.md](episode-csv-guide.md) 를 참고하세요.

### 사용 방법

1. Unity 메뉴 → **Tools > Slainte > Import Episode CSV**
2. **Browse** 버튼으로 CSV 파일 선택
3. Output Folder 확인 (기본값: `Assets/Data/EpisodeData`)
4. **Import** 클릭

### 동작 규칙

- 출력 파일명은 `EpisodeData_{episodeId}.asset` 으로 자동 결정
- 같은 `episodeId`의 에셋이 이미 존재하면 **덮어쓰기**
- CSV 섹션 헤더(`#META` 등)는 열 수 패딩이 있어도 정상 인식
- UTF-8 BOM 파일 지원

### CSV 섹션 구조 요약

| 섹션 | 역할 |
|---|---|
| `#META` | 에피소드 ID, 제목, 첫 노드 |
| `#TRIGGER` | 발동 조건 (일수, 플래그, 선행 에피소드) |
| `#OPENING_CHARS` | 오프닝 캐릭터 슬롯 |
| `#NODES` | 대화 노드 목록 |
| `#NODE_CHARS` | 노드별 캐릭터 표정 |
| `#CHOICES` | 플레이어 선택지 |
| `#NODE_BRANCHES` | 플래그 조건 분기 (`requiredAllFlags` AND / `requiredAnyFlags` OR) |
| `#NODE_VAR_BRANCHES` | 수치 변수(호감도 등) 조건 분기 |
