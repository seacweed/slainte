# 에디터 툴

Unity 에디터에서 사용할 수 있는 커스텀 툴 목록입니다.  
툴은 `Assets/_Project/Core/Editor/`, `Shared/Editor/`, `Features/*/Editor/`에 소유 영역별로 나뉘어 있습니다.

| 툴 | 파일 | 메뉴 경로 |
|---|---|---|
| 에피소드 CSV 임포터 | `EpisodeCsvImporter.cs` | Tools > Slainte > Import Episode CSV |
| 에피소드 CSV 전체 임포트 | `EpisodeCsvImporter.cs` | Tools > Slainte > Import All Episode CSVs |
| 내러티브 그래프 에디터 | `NarrativeGraphEditor.cs` | Narrative > Open Graph |
| CSV → 그래프 가져오기 | `NarrativeCsvSync.cs` | Narrative > Import CSV to Graph... |
| CSV ⇄ 그래프 왕복 검증 | `NarrativeRoundTripValidator.cs` | Narrative > Validate CSV ⇄ Graph Round Trip |
| 슬롯 기반 영업 검증 | `BusinessShiftValidator.cs` | Slainte > 품질 검증 > 슬롯 기반 영업 검증 |
| 판매·정산 규칙 검증 | `BusinessIntegrationRulesValidator.cs` | Slainte > Business > Validate Sale And Settlement Rules |
| 영업 통합 플레이테스트 씬 / 플레이 모드 검증 3종 | `BusinessIntegrationPlaytestTools.cs` | Slainte > Business > Create or Open Integration Playtest, Validate Scheduled Encounter / Slot Candidate Priority / Condition Fallback Full Day Play Mode |
| 레거시 ItemData 임포터 | `ItemDataImporter.cs` | Tools > Import Item Data (CSV) |
| 기획 CSV 에셋 임포터 | `PlanningCsvAssetImporter.cs` | Slainte > 데이터 > 기획 CSV 임포트 |
| 기준 기획 CSV 즉시 임포트 | `PlanningCsvAssetImporter.cs` | Slainte > 데이터 > 기준 CSV 바로 임포트 |
| 기획 CSV 자동 검증 | `PlanningCsvAssetImporter.cs` | Slainte > 품질 검증 > 기획 CSV 에셋 검증 |
| 코블러 셰이커 설정 | `CobblerShakerSetup.cs` | Slainte > Business > 코블러 셰이커 설정 적용 |
| 손님 풀 샘플 설정 | `CustomerPoolSetup.cs` | Slainte > Business > 손님 풀 샘플 설정 적용 |
| 손님 풀 자동 검증 | `CustomerPoolSetup.cs` | Slainte > 품질 검증 > 손님 풀 검증 |
| 캐릭터 표정 스프라이트 임포터 | `CharacterExpressionSpriteImporter.cs` | Slainte > 데이터 > 캐릭터 표정 스프라이트 폴더 임포트 |
| 캐릭터 스프라이트 접두어 제거 | `CharacterSpritePrefixStripper.cs` | Slainte > 데이터 > 캐릭터 스프라이트 접두어 제거 |
| 에피소드·일자 플레이테스트 런처 | `PlaytestLauncherWindow.cs` | Slainte > Playtest Launcher |

---

## 에피소드·일자 플레이테스트 런처

`Slainte > Playtest Launcher`에서 에피소드 단독 테스트와 특정 Day의 전체 흐름 테스트를 시작한다.

### 에피소드 테스트

1. `Episode` 탭에서 제목이나 ID를 검색하고 에피소드를 선택한다(목록은 `Day N S슬롯` 순).
2. 테스트에 적용할 `Progress Day`를 입력한다(기본값은 배정된 day).
3. 등장 조건과 무관하게 내용을 확인하려면 `Bypass trigger condition`을 켠다.
4. `Start Isolated Episode Test`를 누른다.

- 선택한 에피소드가 영업의 **1번 손님 슬롯**으로 강제 실행되고(`EpisodeManager.QueueDebugEncounter`), 나머지 슬롯은 그날 일정·랜덤 손님으로 이어진다.
- 조건 무시를 끄면 현재 저장의 플래그, 선행 에피소드, 재화, 호감도와 지정한 Day로 등장 조건을 먼저 검사한다.

### Day 테스트

1. `Day` 탭에서 테스트할 `Target Day`를 입력한다.
2. `Prepare episodes scheduled before this day as completed` 사용 여부를 정한다.
3. `Start Isolated Day Test`를 누른다.

- Day 1은 `StartFirstDay()`로 시작한다.
- Day 2 이상은 내부 진행도를 Day N-1로 맞춘 뒤 `StartBusinessDay()`를 호출하므로 실제 실행 Day는 정확히 N이 된다.
- 기준 상태를 사용하면 이전 Day에 배정된 에피소드는 완료, 해당 Day 이후 배정분은 미완료로 임시 구성한다. 플래그·재화·호감도는 현재 저장 상태를 유지하므로 분기 에피소드는 실제 플레이와 다를 수 있다.
- 이후 흐름은 영업(배정 에피소드 + 랜덤 손님) → 정산 → 휴식의 실제 런타임 경로를 사용한다.

### 저장 격리

런처는 Play Mode가 시작되면 현재 `GameProgress`를 메모리에 스냅샷으로 보관하고 디스크 저장과 저장 파일 삭제를 차단한다. Play Mode 종료 시 메모리 상태를 복원하며 `autosave.json`은 변경하지 않는다. 강제 종료가 발생해도 디스크 쓰기 자체가 차단되어 원래 저장 파일은 유지된다.

---

## 기획 CSV 에셋 임포터

`Features/Bartending/Content/Source/Planning`의 `items.csv`, `recipes.csv`, `recipe_ingredients.csv`를 읽어 실제 바텐딩 런타임 에셋을 생성·갱신한다.

- 아이템 한 행에서 `ItemDef`와 `LiquorBottleDef`를 함께 갱신한다.
- 레시피 한 행에서 `CocktailRecipeDef`를 갱신하고 배합 CSV의 재료·용량을 연결한다.
- 잔·얼음 Mid 판정용 중복 레시피 에셋은 생성하지 않는다. 해당 결과는 주문 평가기가 기본 레시피와 제출 조성을 비교해 직접 분류한다.
- 도수 칸이 비어 있으면 재료별 도수와 용량으로 완성 음료 도수를 계산한다.
- 배합이 없는 레시피는 주문 대상에서 자동 제외한다. 현재 기본 레시피 21종은 모두 배합이 연결되어 주문 가능하다.
- 재임포트는 ID 기준 갱신 방식이므로 중복 에셋을 만들지 않는다.

출력 위치는 `Resources/Bartending/Items`, `Resources/Bartending/Recipes`, `Features/Bartending/Content/Generated/LiquorBottles/Planning`이다. 제품 레시피는 `Resources/Bartending/Recipes`만 읽는다.

자동 검증은 기본 레시피 에셋 수, 재료 참조, 번햄 사워 계산 도수, 정확 제조 `Good`, 잔 불일치 `MidGlass`, 다른 기본 레시피 제출 `MidWrongMenu`를 확인한다.

---

## 에피소드 CSV 임포터

CSV 파일을 읽어 `EpisodeData` ScriptableObject를 생성하는 툴입니다.  
CSV 작성 방법은 [../narrative/episode-csv-guide.md](../narrative/episode-csv-guide.md) 를 참고하세요.

### 사용 방법

1. Unity 메뉴 → **Tools > Slainte > Import Episode CSV**
2. **Browse** 버튼으로 CSV 파일 선택
3. Output Folder 확인 (기본값: `Assets/Resources/Narrative/Episodes`)
4. **Import** 클릭

### 동작 규칙

- 출력 파일명은 `EpisodeData_{episodeId}.asset` 으로 자동 결정
- 같은 `episodeId`의 에셋이 이미 존재하면 **덮어쓰기**
- CSV 섹션 헤더(`#META` 등)는 열 수 패딩이 있어도 정상 인식
- UTF-8 BOM 파일 지원
- 이 임포터는 `EpisodeData`만 만든다. 그래프로 편집하려면 **Narrative > Import CSV to Graph...**(또는 그래프 에디터 툴바 **Import CSV...**)를 쓴다 — 그래프 쪽 사용법은 [../narrative/narrative-graph-guide.md](../narrative/narrative-graph-guide.md)
- 폐지된 섹션(`#PLAY_TRIGGER` 등)과 예전 분기 형식은 읽되 Console에 경고를 남긴다

### CSV 섹션 구조 요약

| 섹션 | 역할 |
|---|---|
| `#META` | 에피소드 ID, 제목, 첫 노드, 챕터, 영업 일정(`day` / `slot` / `priority`) |
| `#TRIGGER` | 등장 조건 (MinDay, MinMoney, Required/BlockedFlag, PrerequisiteEpisode, RequiredVar, CustomerAppearance) |
| `#SETTLEMENT_REWARDS` | 정산 커스텀 보상 |
| `#NODES` | 대화 노드 목록 (기본값과 같은 칸은 비워 둠) |
| `#NODE_CRAFTING_BRANCHES` | 제조 결과별 이동 노드·플래그·변수 |
| `#NODE_CHARS` | 노드별 캐릭터 표정 |
| `#CHOICES` | 플레이어 선택지 |
| `#NODE_BRANCHES` | 조건 분기 (`conditionType`: RequiredFlag / BlockedFlag / RequiredVar / PrerequisiteEpisode, 줄 순서 = 우선순위) |

---

## 캐릭터 표정 스프라이트 임포터

`Assets/_Project/Features/Business/Art/Sprites/Characters/{key}/` 폴더 안의 png 파일들을 `CharacterData.expressions`에 자동으로 채우는 툴입니다.

### 사용 방법

1. Project 창에서 대상 `CharacterData` 에셋 선택 (예: `CharacterData_f54.asset`)
2. Unity 메뉴 → **Slainte > 데이터 > 캐릭터 표정 스프라이트 폴더 임포트**
3. 폴더 선택창에서 스프라이트 폴더 지정 (예: `Assets/_Project/Features/Business/Art/Sprites/Characters/f54`)

### 동작 규칙

- 폴더 바로 안의 png 파일만 대상 (하위 폴더 미포함)
- `key`는 파일명(확장자 제외)을 그대로 사용
- 같은 key의 entry가 이미 있으면 `sprite` 필드만 새 파일로 **항상 덮어씀**
- 없으면 새 entry를 추가 (다른 필드는 비워둠)
- 파일명이 `_closed`로 끝나면(예: `mid_closed.png`) **별도 expression을 만들지 않고**, 앞부분(`mid`)과 `{앞부분}_left`, `{앞부분}_right` expression들의 `blinkSprite`에 그 스프라이트를 연결(항상 덮어씀). 대응하는 expression이 하나도 없으면 경고만 남기고 건너뜀
- 단, 파일명이 정확히 `closed`(접두어 없이 단독)이면 이 규칙에서 제외되어 일반 파일처럼 `key: closed`인 expression을 만들고 `sprite`를 채움
- `defaultSprite`, `overlaySprite`, `blinkOverlaySprite`는 전혀 건드리지 않음 — 오버레이 연결은 직접 채워야 함

---

## 캐릭터 스프라이트 접두어 제거

선택한 폴더 안 스프라이트 파일명에서 "폴더명_" 접두어(대소문자 무관)를 일괄 제거하는 툴입니다. `AssetDatabase.RenameAsset`을 사용하므로 GUID가 보존되어 기존 참조(CharacterData 등)가 깨지지 않습니다.

### 사용 방법

1. Project 창에서 대상 폴더(들) 선택 (예: `Assets/_Project/Features/Business/Art/Sprites/Characters/f54`)
2. Unity 메뉴 → **Slainte > 데이터 > 캐릭터 스프라이트 접두어 제거**

### 동작 규칙

- 폴더 바로 안의 Sprite만 대상 (하위 폴더 미포함)
- 파일명이 "폴더명_"으로 시작하면(대소문자 무관) 그 접두어만 제거
- 접두어가 없는 파일은 건너뜀
