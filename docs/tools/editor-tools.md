# 에디터 툴

Unity 에디터에서 사용할 수 있는 커스텀 툴 목록입니다.  
모든 툴은 `Assets/Editor/` 에 위치합니다.

| 툴 | 파일 | 메뉴 경로 |
|---|---|---|
| 에피소드 CSV 임포터 | `EpisodeCsvImporter.cs` | Tools > Slainte > Import Episode CSV |
| 아이템 데이터 임포터 | `ItemDataImporter.cs` | Tools > Import Item Data (CSV) |
| 기획 CSV 에셋 임포터 | `PlanningCsvAssetImporter.cs` | Slainte > 데이터 > 기획 CSV 임포트 |
| 기획 CSV 자동 검증 | `PlanningCsvAssetImporter.cs` | Slainte > 품질 검증 > 기획 CSV 에셋 검증 |
| 코블러 셰이커 설정 | `CobblerShakerSetup.cs` | Slainte > Business > 코블러 셰이커 설정 적용 |
| 손님 풀 샘플 설정 | `CustomerPoolSetup.cs` | Slainte > Business > 손님 풀 샘플 설정 적용 |
| 손님 풀 자동 검증 | `CustomerPoolSetup.cs` | Slainte > 품질 검증 > 손님 풀 검증 |

---

## 기획 CSV 에셋 임포터

아이템 CSV, 레시피 CSV, PDF에서 옮긴 배합 CSV를 읽어 실제 바텐딩 런타임이 사용하는 에셋을 생성한다.

- 아이템 한 행에서 `ItemDef`와 `LiquorBottleDef`를 함께 갱신한다.
- 레시피 한 행에서 `CocktailRecipeDef`를 갱신하고 배합 CSV의 재료·용량을 연결한다.
- 15개 기본 레시피에서 숨은 Mid 판정 레시피 87개를 자동 생성한다.
- 도수 칸이 비어 있으면 재료별 도수와 용량으로 완성 음료 도수를 계산한다.
- 아이리시 커피, 블랙 커피, 핫 테디처럼 배합이 없는 레시피는 주문 대상에서 자동 제외한다.
- 재임포트는 ID 기준 갱신 방식이므로 중복 에셋을 만들지 않는다.

자동 검증은 에셋 수, 재료 참조, 번햄 사워 계산 도수, 정확 제조 Good, 잔 변형 Mid를 확인한다.

---

## 에피소드 CSV 임포터

CSV 파일을 읽어 `EpisodeData` ScriptableObject를 생성하는 툴입니다.  
CSV 작성 방법은 [../narrative/episode-csv-guide.md](../narrative/episode-csv-guide.md) 를 참고하세요.

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
