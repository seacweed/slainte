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
| 캐릭터 표정 스프라이트 임포터 | `CharacterExpressionSpriteImporter.cs` | Slainte > 데이터 > 캐릭터 표정 스프라이트 폴더 임포트 |
| 캐릭터 스프라이트 접두어 제거 | `CharacterSpritePrefixStripper.cs` | Slainte > 데이터 > 캐릭터 스프라이트 접두어 제거 |
| 에피소드·일자 플레이테스트 런처 | `PlaytestLauncherWindow.cs` | Slainte > Playtest Launcher |

---

## 에피소드·일자 플레이테스트 런처

`Slainte > Playtest Launcher`에서 에피소드 단독 테스트와 특정 Day의 전체 흐름 테스트를 시작한다.

### 에피소드 테스트

1. `Episode` 탭에서 제목이나 ID를 검색하고 에피소드를 선택한다.
2. 테스트에 적용할 `Progress Day`를 입력한다.
3. 해금·플레이 조건과 무관하게 내용을 확인하려면 `Bypass trigger and play conditions`를 켠다.
4. `Start Isolated Episode Test`를 누른다.

- Default와 Mandatory는 에피소드 내용을 바로 실행한다. Mandatory의 Before/After Business 배치까지 검증하려면 Day 테스트를 사용한다.
- Encounter는 BusinessScene 진입 후 실제 `TryStartBusinessEncounter()` 경로로 실행하며, 완료 후 영업으로 복귀한다.
- 조건 무시를 끄면 현재 저장의 플래그, 선행 에피소드, 재화, 호감도와 지정한 Day를 기준으로 조건을 검사한다.

### Day 테스트

1. `Day` 탭에서 테스트할 `Target Day`를 입력한다.
2. `Prepare previous Mandatory episodes as completed` 사용 여부를 정한다.
3. `Start Isolated Day Test`를 누른다.

- Day 1은 `StartFirstDay()`로 시작한다.
- Day 2 이상은 내부 진행도를 Day N-1로 맞춘 뒤 `StartBusinessDay()`를 호출하므로 실제 실행 Day는 정확히 N이 된다.
- 필수 에피소드 기준 상태를 사용하면 이전 Day의 Mandatory는 완료, 해당 Day 이후의 Mandatory는 미완료로 임시 구성한다. 선택형 에피소드, 플래그, 재화, 호감도는 현재 저장 상태를 유지한다.
- 이후 흐름은 필수 에피소드 → 영업 → 영업 후 필수 에피소드 → 정산 → 휴식의 실제 런타임 경로를 사용한다.

### 저장 격리

런처는 Play Mode가 시작되면 현재 `GameProgress`를 메모리에 스냅샷으로 보관하고 디스크 저장과 저장 파일 삭제를 차단한다. Play Mode 종료 시 메모리 상태를 복원하며 `autosave.json`은 변경하지 않는다. 강제 종료가 발생해도 디스크 쓰기 자체가 차단되어 원래 저장 파일은 유지된다.

---

## 기획 CSV 에셋 임포터

아이템 CSV, 레시피 CSV, PDF에서 옮긴 배합 CSV를 읽어 실제 바텐딩 런타임이 사용하는 에셋을 생성한다.

- 아이템 한 행에서 `ItemDef`와 `LiquorBottleDef`를 함께 갱신한다.
- 레시피 한 행에서 `CocktailRecipeDef`를 갱신하고 배합 CSV의 재료·용량을 연결한다.
- 15개 기본 레시피에서 잔·얼음만 다른 숨은 Mid 판정 레시피 73개를 자동 생성한다. 재료·제조법 변형은 생성하지 않는다.
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

---

## 캐릭터 표정 스프라이트 임포터

`Assets/Sprites/characters/{key}/` 폴더 안의 png 파일들을 `CharacterData.expressions`에 자동으로 채우는 툴입니다.

### 사용 방법

1. Project 창에서 대상 `CharacterData` 에셋 선택 (예: `CharacterData_f54.asset`)
2. Unity 메뉴 → **Slainte > 데이터 > 캐릭터 표정 스프라이트 폴더 임포트**
3. 폴더 선택창에서 스프라이트 폴더 지정 (예: `Assets/Sprites/characters/f54`)

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

1. Project 창에서 대상 폴더(들) 선택 (예: `Assets/Sprites/characters/eliot`)
2. Unity 메뉴 → **Slainte > 데이터 > 캐릭터 스프라이트 접두어 제거**

### 동작 규칙

- 폴더 바로 안의 Sprite만 대상 (하위 폴더 미포함)
- 파일명이 "폴더명_"으로 시작하면(대소문자 무관) 그 접두어만 제거
- 접두어가 없는 파일은 건너뜀
