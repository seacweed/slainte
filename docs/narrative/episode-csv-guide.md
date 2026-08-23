# 에피소드 CSV 작성 가이드

에피소드 대화 데이터를 CSV 파일로 작성하면 Unity 에디터 툴을 통해 자동으로 게임 데이터로 변환됩니다.

## 목차

- [파일 규칙](#파일-규칙)
- [전체 구조](#전체-구조)
- [섹션별 작성법](#섹션별-작성법)
  - [META](#meta)
  - [TRIGGER](#trigger)
  - [PLAY_TRIGGER](#play_trigger)
  - [OPENING_CHARS](#opening_chars)
  - [BOARD](#board)
  - [BOARD_CHARS](#board_chars)
  - [SETTLEMENT_REWARDS](#settlement_rewards)
  - [SELECT_CHARS](#select_chars)
  - [NODES](#nodes)
  - [NODE_CRAFTING_BRANCHES](#node_crafting_branches)
  - [NODE_CHARS](#node_chars)
  - [CHOICES](#choices)
  - [NODE_BRANCHES](#node_branches)
  - [NODE_VAR_BRANCHES](#node_var_branches)
  - [NODE_EPISODE_BRANCHES](#node_episode_branches)
- [특수 표기법](#특수-표기법)
- [작성 예시](#작성-예시)
- [영업 인카운터 풀에 연결](#영업-인카운터-풀에-연결)
- [임포트 방법](#임포트-방법)
- [자주 하는 실수](#자주-하는-실수)

---

## 파일 규칙

- **파일명**: 자유롭게 지정 가능 (임포트 후 에셋명은 `episodeId` 기준으로 자동 결정)
- **인코딩**: UTF-8
- **권장 편집 도구**: Google Sheets, Excel, 메모장 등 CSV를 저장할 수 있는 모든 도구

---

## 전체 구조

파일은 `#섹션명` 으로 구분된 최대 16개 섹션으로 이루어집니다.  
각 섹션은 **헤더 행(열 이름)** → **데이터 행** 순서로 작성합니다.

```
#META
(헤더 행)
(데이터 행)

#TRIGGER
...

#PLAY_TRIGGER
...

#SELECT_TRIGGER
...

#SELECT_CHARS
...

#OPENING_CHARS
...

#BOARD
...

#BOARD_CHARS
...

#SETTLEMENT_REWARDS
...

#NODES
...

#NODE_CRAFTING_BRANCHES
...

#NODE_CHARS
...

#CHOICES
...

#NODE_BRANCHES
...

#NODE_VAR_BRANCHES
...

#NODE_EPISODE_BRANCHES
...
```

> 빈 행은 무시됩니다. 가독성을 위해 섹션 사이에 빈 행을 추가해도 됩니다.

---

## 섹션별 작성법

### META

에피소드의 기본 정보입니다. **데이터 행은 반드시 1개** 작성합니다.

| 열 | 설명 | 예시 |
|---|---|---|
| `episodeId` | 에피소드 고유 ID (에셋 파일명에 사용됨) | `StrangeCoin_0` |
| `episodeTitle` | 게임에 표시될 에피소드 제목 | `이상한 동전 - 0` |
| `firstNodeId` | 대화가 시작될 첫 번째 노드 ID | `0` |
| `episodeType` | `Default`(Rest 보드에서 직접 선택) / `Mandatory`(영업 전후 자동 삽입) / `Encounter`(영업 중 인카운터). 비우면 `Default` | `Encounter` |
| `mandatorySlot` | `episodeType=Mandatory`일 때만 사용. `BeforeBusiness` / `AfterBusiness`. 비우면 `None` | `BeforeBusiness` |
| `chapterId` | 소속 챕터 ID (`ChapterData.chapterId`와 매칭, 챕터 스코프 필수 에피소드 큐 조회에 사용) | `chapter_1` |

```csv
#META
episodeId,episodeTitle,firstNodeId,episodeType,mandatorySlot,chapterId
StrangeCoin_0,이상한 동전 - 0,0,Encounter,None,chapter_1
```

> `episodeType`/`mandatorySlot`/`chapterId` 열은 생략해도 됩니다(빈 값은 각각 `Default`/`None`/빈 문자열로 처리됨). 기존 CSV를 그대로 재임포트해도 문제없습니다.
>
> `Encounter`는 Rest 보드와 필수 에피소드 큐에 나타나지 않습니다. 실제 영업에서 실행하려면 `BusinessOrderFlowSettings.requiredActions` 또는 개발용 통합 테스트 설정에 해당 에피소드를 `EncounterEpisode` 액션으로 등록해야 합니다.

---

### TRIGGER

이 에피소드가 작전판(Rest 화면)에 **해금(노출)**되는 조건입니다. **행 하나 = 조건 하나**이며, 여러 행을 작성하면 **AND로 결합**됩니다(`SELECT_TRIGGER`와 같은 행 방식이지만, 옵션이 아니라 항목이 전부 AND로 묶인다는 점이 다릅니다). 작전판 툴팁에는 이 행들이 **작성한 순서 그대로, 행별 텍스트와 함께** 표시됩니다.

- 컬럼: `conditionType`(`MinDay`/`RequiredFlag`/`PrerequisiteEpisode`/`RequiredVar`/`MinMoney` 중 하나), `conditionValue`(타입에 따라 의미가 다름 — 아래 표), `text`(툴팁에 표시할 커스텀 힌트. 비우면 조건에서 문구를 자동 생성)
- **생략 가능** — 섹션 자체를 안 쓰면 "해금 조건 없음"(항상 노출)으로 처리됩니다.
- `blockedFlags`/`requiredCustomerAppearances` 조건은 CSV로 작성할 수 없습니다 — 필요하면 에셋 인스펙터에서 `triggerCondition` 필드에 직접 입력하세요(그 경우 툴팁에는 표시되지 않습니다).

| `conditionType` | `conditionValue` 형식 | 예시 |
|---|---|---|
| `MinDay` | 숫자 | `3` |
| `RequiredFlag` | 플래그 이름 | `flag_met_customer` |
| `PrerequisiteEpisode` | 에피소드 ID | `Intro_0` |
| `RequiredVar` | `varName연산자값` (연산자: `>=` `>` `==` `<` `<=`) | `sally_affinity>=5` |
| `MinMoney` | 숫자(소지금 이 값 이상이어야 함) | `500000` |

```csv
#TRIGGER
conditionType,conditionValue,text
MinDay,3,3일차 이후
RequiredFlag,flag_met_customer,손님과 첫 만남
```

---

### PLAY_TRIGGER

`TRIGGER`(해금 조건)와 컬럼 구성·문법이 완전히 동일하지만 의미가 다릅니다 — 이 조건을 만족해야 작전판에서 **Play 버튼이 활성화**됩니다. 해금은 됐지만 아직 플레이는 못 하는 상태(예: 사진은 작전판에 떴지만 눌러보면 버튼이 비활성)를 표현할 때 씁니다.

- **생략 가능** — 섹션 자체를 안 쓰면 "플레이 조건 없음"(해금되면 바로 플레이 가능)으로 처리됩니다.
- 작전판 툴팁에는 `TRIGGER`와 `PLAY_TRIGGER`의 모든 행이 **한 목록에 합쳐져서** 표시됩니다(순서는 TRIGGER 행들 다음 PLAY_TRIGGER 행들).

```csv
#PLAY_TRIGGER
conditionType,conditionValue,text
RequiredVar,sally_affinity>=5,사라 호감도 5 이상
PrerequisiteEpisode,Intro_0,'인트로' 에피소드 완료
```

> **주의**: `TRIGGER`/`PLAY_TRIGGER`는 `SELECT_TRIGGER`와 마찬가지로 재임포트 시 항상 CSV 내용으로 전체 교체됩니다(섹션이 있으면 없는 행은 사라짐). 섹션 자체를 안 쓰면 "조건 없음"으로 처리될 뿐, 기존 값이 유지되지는 않습니다.

---

### SELECT_TRIGGER

작전판 툴팁에서 플레이어가 직접 on/off로 토글할 수 있는 조건입니다. `TRIGGER`/`PLAY_TRIGGER`와 달리 **Play 버튼 활성화 여부에 영향을 주지 않습니다** — 어떤 옵션도 미충족/미선택이어도 에피소드는 평소대로 플레이 가능합니다.

- **행 하나 = 옵션 하나**입니다. 여러 옵션을 만들고 싶으면 행을 여러 개 작성하세요(`OPENING_CHARS`처럼 다중 행 섹션)
- **옵션들은 서로 배타적**입니다 — 툴팁에서 하나를 켜면 나머지는 자동으로 꺼집니다(라디오 버튼처럼 동작)
- **옵션 하나당 조건은 딱 하나**입니다(`TRIGGER`/`PLAY_TRIGGER`처럼 여러 조건을 AND로 걸 수 없음). 조건을 여러 개 걸고 싶으면 옵션(행)을 여러 개로 나눠서 작성하세요
- 그 조건이 충족된 옵션만 토글 인터랙션이 가능(미충족이면 off로 고정, 비활성 표시)
- 켜진 옵션의 on/off 값은 Play 버튼 클릭(에피소드 시작) 시점에 그 행의 `selectFlag` 열 플래그로 반영됨(켜진 옵션 → `SetFlag`, 나머지 옵션 → `ClearFlag`) — 에피소드 노드의 `flagBranches` 등에서 분기 조건으로 사용
- 컬럼: `conditionType`(`None`/`MinDay`/`RequiredFlag`/`PrerequisiteEpisode`/`RequiredVar`/`MinMoney` 중 하나), `conditionValue`(타입에 따라 의미가 다름 — 아래 표), `selectFlag`(반영할 플래그 이름), `selectText`(이 옵션 조건 뒤에 표시할 커스텀 힌트 한 줄. 비우면 조건에서 문구를 자동 생성), `revealConditionType`/`revealConditionValue`(이 옵션의 내용을 플레이어에게 공개하는 조건 — 형식은 `conditionType`/`conditionValue`와 동일. 비우면 항상 공개), `hiddenText`(`revealConditionType`/`revealConditionValue` 미충족일 때 `selectText` 대신 표시할 텍스트. **비우면 `"???"`로 표시** — 미충족 시 항상 `"???"`가 아니라, 여기 채워둔 다른 문구를 보여주다가 reveal 조건이 충족되면 `selectText`로 바뀌는 것도 가능)

| `conditionType` / `revealConditionType` | `conditionValue` / `revealConditionValue` 형식 | 예시 |
|---|---|---|
| `None` | (비움) | 조건 없음 — 항상 토글 가능 / 항상 공개 |
| `MinDay` | 숫자 | `3` |
| `RequiredFlag` | 플래그 이름 | `flag_got_hint` |
| `PrerequisiteEpisode` | 에피소드 ID | `Intro_0` |
| `RequiredVar` | `varName연산자값`(`TRIGGER`의 `requiredVars` 문법과 동일) | `sally_affinity>=5` |
| `MinMoney` | 숫자(소지금 이 값 이상이어야 함) | `500000` |

```csv
#SELECT_TRIGGER
conditionType,conditionValue,selectFlag,selectText,revealConditionType,revealConditionValue,hiddenText
RequiredFlag,flag_got_hint,select_confront_f72,단도직입적으로 물어본다,,,
RequiredFlag,flag_got_hint,select_evade_f72,모르는 척 넘어간다,MinDay,5,수상한 낌새가 느껴진다
```

두 번째 옵션은 `MinDay=5` 미만이면 `hiddenText`인 "수상한 낌새가 느껴진다"를 보여주다가, 5일차부터는 `selectText`인 "모르는 척 넘어간다"로 바뀝니다. 첫 번째 옵션처럼 `hiddenText`를 비워두면(reveal 조건도 없으므로 항상 공개) 아무 영향이 없습니다.

> **주의**: `SELECT_TRIGGER`는 재임포트 시 항상 CSV 내용으로 전체 교체됩니다(섹션이 있으면 없는 옵션은 사라짐).

---

### SELECT_CHARS

`SELECT_TRIGGER`의 각 옵션이 선택됐을 때 보여줄 초상화(`characterOverrides`)입니다. **선택 사항** — 비워두면(섹션 자체를 안 쓰면) 해당 옵션은 기본 `characters`(아래 `BOARD_CHARS`)를 그대로 사용합니다.

- **행 하나 = 초상화 슬롯 하나**이며, "몇 번째 캐릭터"인지는 별도 열이 아니라 **같은 `selectFlag`끼리 CSV에 작성된 순서**로 정해집니다. 즉 `selectFlag`가 같은 행을 여러 개 작성하면 1번째 행이 `BOARD_CHARS`의 1번째 슬롯, 2번째 행이 2번째 슬롯... 순으로 채워집니다.
- 슬롯 개수·순서는 `characters`(기본 초상화 목록)와 맞춰야 합니다.
- `selectFlag`는 `SELECT_TRIGGER`의 `selectFlag` 열과 일치해야 매칭됩니다.
- **부분 교체 불가**: 한 `selectFlag`에 대해 작성한 행들은 해당 옵션의 초상화 목록 **전체**를 대체합니다(리스트 길이만큼만 표시되고 나머지 슬롯은 사라짐). 예를 들어 캐릭터가 3명 등장하는데 그중 3번째 캐릭터만 바뀌는 경우에도, 1·2번째 캐릭터를 그대로 유지하려면 1·2번째 행에 기존과 동일한 값을 반복해서 **3줄을 모두** 작성해야 합니다. "이 슬롯은 건드리지 않음"을 표현하는 방법은 없습니다.

| 열 | 설명 | 예시 |
|---|---|---|
| `selectFlag` | 대상 옵션의 `SELECT_TRIGGER.selectFlag` | `select_confront_f72` |
| `isHidden` | `true`면 이 슬롯을 "???"로 비공개 표시 | `false` |
| `characterName` | `isHidden=false`일 때 표시할 캐릭터 이름 | `f72` |

```csv
#SELECT_CHARS
selectFlag,isHidden,characterName
select_confront_f72,false,f72
select_evade_f72,true,
```

> **주의**: `#SELECT_CHARS` 섹션 자체가 CSV에 없으면 기존 에셋의 `characterOverrides`가 유지됩니다. 섹션을 쓰면(빈 섹션 포함) 그 옵션들의 초상화는 CSV가 기준이 되며, 행이 없는 `selectFlag`는 초상화가 빈 목록(기본 `characters` 미사용, 초상화 없음)으로 대체됩니다.

---

### OPENING_CHARS

에피소드 시작 시 무대에 배치되는 캐릭터 목록입니다.  
캐릭터가 여러 명이면 **행을 여러 개** 작성합니다.

| 열 | 설명 | 예시 |
|---|---|---|
| `characterKey` | 캐릭터 ID | `f72` |
| `expressionKey` | 시작 표정 | `neutral` |
| `slotIndex` | 배치 슬롯 (`-1` = 자동, `0` = Center, `1` = Left, `2` = Right, `3` = Left2, `4` = Right2, `5~8` = Interaction0~3 통합 스프라이트 전용) | `-1` |

```csv
#OPENING_CHARS
characterKey,expressionKey,slotIndex
f72,frust,-1
```

---

### BOARD

작전판(Rest 화면)에 표시되는 정보입니다. **선택 사항** — 비워두면(섹션 자체를 안 쓰면) 기존 에셋 값을 유지합니다. **데이터 행은 1개**만 작성합니다.

| 열 | 설명 | 예시 |
|---|---|---|
| `episodeDescription` | 작전판에 표시될 에피소드 설명(여러 줄 가능) | `이상한 동전을 주운 손님이 찾아온다.` |
| `iconNameBoard` | 작전판 카드에 쓸 아이콘 이름 | `icon_coin` |
| `iconNameArchive` | 아카이브(다시보기)에 쓸 아이콘 이름 | `icon_coin_archive` |

```csv
#BOARD
episodeDescription,iconNameBoard,iconNameArchive
"이상한 동전을 주운 손님이 찾아온다.",icon_coin,icon_coin_archive
```

> 해금 조건 커스텀 힌트는 `BOARD`가 아니라 `TRIGGER`/`PLAY_TRIGGER` 각 행의 `text` 열에 작성합니다.

> **주의**: 설명에 쉼표가 있으면 `NODES`의 `text`와 마찬가지로 큰따옴표로 감싸야 합니다.

---

### BOARD_CHARS

작전판에서 선택 조건 미충족/미선택 시(기본으로) 보여줄 초상화 목록입니다. **선택 사항** — 비워두면(섹션 자체를 안 쓰면) 기존 에셋 값을 유지합니다.

- **행 하나 = 초상화 슬롯 하나**입니다. 여러 명이면 행을 여러 개 작성하고, 순서가 곧 슬롯 순서입니다.

| 열 | 설명 | 예시 |
|---|---|---|
| `isHidden` | `true`면 이 슬롯을 "???"로 비공개 표시 | `false` |
| `characterName` | `isHidden=false`일 때 표시할 캐릭터 이름 | `f72` |

```csv
#BOARD_CHARS
isHidden,characterName
false,f72
```

---

### SETTLEMENT_REWARDS

에피소드가 끝날 때 특정 플래그가 서 있으면 정산 화면에 커스텀 보상 줄을 추가합니다. **선택 사항** — 비워두면(섹션 자체를 안 쓰면) 기존 에셋 값을 유지합니다. 0개, 1개, 여러 개 모두 가능합니다.

- **행 하나 = 보상 조건 하나**입니다.
- 에피소드 종료 시점에 `requiredFlag`가 켜져 있는 행만 정산 화면에 반영됩니다(예: 특정 선택지의 `setFlags`나 제조 결과의 `flag`로 미리 세워둔 플래그).
- 지급은 **정산 시점**에 이루어집니다(음료 판매처럼 그 자리에서 바로 지급되지 않습니다).

| 열 | 설명 | 예시 |
|---|---|---|
| `requiredFlag` | 이 플래그가 서 있어야 보상이 지급됨 | `celi_apology_paid` |
| `label` | 정산 화면에 표시할 문구 | `소란 피워서 미안해 - 셀리` |
| `amount` | 지급 금액(음수면 차감) | `150` |

```csv
#SETTLEMENT_REWARDS
requiredFlag,label,amount
celi_apology_paid,소란 피워서 미안해 - 셀리,150
```

---

### NODES

대화 노드 목록입니다. 노드 하나 = 화면에 표시되는 대사 한 줄입니다.

| 열 | 설명 | 예시 |
|---|---|---|
| `nodeId` | 노드 고유 ID | `0`, `5-1-1` |
| `speakerKey` | 말하는 캐릭터 ID (`shaun` = 주인공) | `f72` |
| `overrideSpeakerName` | 이름창에 표시할 임시 이름 (비우면 캐릭터 기본 이름 사용) | `???` |
| `text` | 대사 내용 | `안녕하세요.` |
| `nextNodeId` | 다음에 이동할 노드 ID (비우면 에피소드 종료) | `1` |
| `requiresCrafting` | 제조 판정 여부 (`true` / `false`) | `false` |
| `craftingTicketKey` | 사용할 제조 티켓 ID (`requiresCrafting=true` 일 때만 작성) | `sc0_f72` |
| `bgmCommand` | BGM 명령 (`none` / `play` / `stop`, 비우면 `none`) | `play` |
| `bgmClipName` | 재생할 BGM 파일명 (`bgmCommand=play` 일 때만 작성, 확장자 제외) | `bgm_tension` |

**제조 판정 노드** 작성 시: `text`와 `nextNodeId`는 비우고, `requiresCrafting=true` + `craftingTicketKey`만 작성합니다. 제조 결과별(goodjob/badjob/midjob 4종) 이동 노드·플래그·변수 변경은 `#NODE_CRAFTING_BRANCHES` 섹션에 작성합니다.

**선택지 노드** 작성 시: `nextNodeId`는 비우고 `#CHOICES` 섹션에 선택지를 작성합니다.

**분기 노드** 작성 시: `nextNodeId`는 조건이 모두 맞지 않을 때의 기본 이동 노드입니다. 조건 분기는 `#NODE_BRANCHES` / `#NODE_EPISODE_BRANCHES` / `#NODE_VAR_BRANCHES`에 작성합니다.

```csv
#NODES
nodeId,speakerKey,overrideSpeakerName,text,nextNodeId,requiresCrafting,craftingTicketKey,bgmCommand,bgmClipName
0,f72,???,흘..크흘…,1,false,,play,bgm_tension
5,f72,???,,,true,sc0_f72,,
```

> **주의**: 대사에 쉼표(`,`)가 포함된 경우 반드시 큰따옴표로 감싸야 합니다.  
> 예: `"여기, 이거 드세요."`

---

### NODE_CRAFTING_BRANCHES

제조 결과(`CraftingJobResult`: `Good`/`MidIce`/`MidGlass`/`MidIceGlass`/`MidWrongMenu`/`Bad`)별로 이동할 노드·설정할 플래그·수치 변수 변경을 지정합니다. `#NODE_BRANCHES`와 마찬가지로 **노드 하나가 여러 행을 가질 수 있는** 섹션이며, 실제로 사용하는 결과만큼만 행을 씁니다(6개를 다 채울 필요 없음).

| 열 | 설명 | 예시 |
|---|---|---|
| `nodeId` | 제조 판정 노드 ID (`requiresCrafting=true`인 노드) | `5` |
| `result` | 제조 결과 (`Good`/`MidIce`/`MidGlass`/`MidIceGlass`/`MidWrongMenu`/`Bad`) | `Good` |
| `nextNodeId` | 해당 결과일 때 이동할 노드 ID | `5-1-1` |
| `flag` | 해당 결과일 때 설정할 플래그 | `sc0_crafted_good` |
| `varChanges` | 해당 결과일 때 수치 변수 변경 (`\|` 구분, `+`/`-` 증감) | `sally_affinity+5` |

- 특정 결과에 대한 행이 없으면, 그 결과에서는 다음 노드로 넘어가지 않고 에피소드가 종료됩니다(`#NODES`의 `nextNodeId`도 비어있는 경우와 동일).
- `midjob-ice`/`midjob-glass`/`midjob-ice_glass`/`midjob-wrongmenu`가 무엇을 뜻하는지는 기획 규칙에 따르며(예: 얼음 유무만 다르면 `MidIce`, 잔 종류만 다르면 `MidGlass`, 주문과 다른 레시피를 올바르게 만들면 `MidWrongMenu`), 실제 자동 판정 로직은 아직 없고 `CraftingJudgeUI`의 버튼으로 수동 판정합니다.

```csv
#NODE_CRAFTING_BRANCHES
nodeId,result,nextNodeId,flag,varChanges
5,Good,5-1-1,sc0_crafted_good,sally_affinity+5
5,Bad,5-2-1,sc0_crafted_bad,sally_affinity-2
5,MidIce,5-3-1,,
```

---

### NODE_CHARS

각 노드에서 표시되는 캐릭터의 표정을 지정합니다.  
한 노드에 캐릭터가 여러 명이면 **같은 `nodeId`로 행을 여러 개** 작성합니다.

| 열 | 설명 | 예시 |
|---|---|---|
| `nodeId` | 대상 노드 ID | `0` |
| `characterKey` | 캐릭터 ID | `f72` |
| `expressionKey` | 해당 노드에서의 표정 | `smile` |
| `slotIndex` | 슬롯 위치 (`-1` = 자동, `0~4` = 일반 슬롯, `5~8` = Interaction0~3 통합 스프라이트 전용) | `-1` |

```csv
#NODE_CHARS
nodeId,characterKey,expressionKey,slotIndex
0,f72,frust,-1
30,f54,mid,-1
30,f72,laugh,-1
30,sally,mid,-1
```

---

### CHOICES

플레이어 선택지가 있는 노드의 선택지 목록입니다.  
선택지가 없으면 이 섹션은 **헤더만 남기고 비워도** 됩니다.

| 열 | 설명 | 예시 |
|---|---|---|
| `nodeId` | 선택지가 속한 노드 ID | `10` |
| `choiceIndex` | 선택지 순서 (0부터 시작) | `0` |
| `buttonText` | 버튼에 표시될 텍스트 | `그래 말해봐` |
| `nextNodeId` | 선택 시 이동할 노드 ID | `11a` |
| `setFlags` | 선택 시 **켤** 플래그 (`\|` 구분) | `flag_agreed` |
| `clearFlags` | 선택 시 **끌** 플래그 (`\|` 구분) | `flag_open` |
| `varChanges` | 선택 시 **수치 변수 변경** (`\|` 구분, `+`/`-`로 증감) | `sally_affinity+5` |

```csv
#CHOICES
nodeId,choiceIndex,buttonText,nextNodeId,setFlags,clearFlags,varChanges
10,0,잘 지내?,11a,flag_talked,,sally_affinity+5
10,1,볼 일 없어,11b,,,sally_affinity-2
```

---

### NODE_BRANCHES

플래그 상태에 따라 다음 노드를 분기합니다.  
분기가 없으면 이 섹션은 **헤더만 남기고 비워도** 됩니다.

| 열 | 설명 | 예시 |
|---|---|---|
| `nodeId` | 분기가 적용될 노드 ID | `5` |
| `requiredAllFlags` | 이 플래그가 **모두** 켜져 있어야 분기 (쉼표 구분, AND 조건) | `flag_a,flag_b` |
| `requiredAnyFlags` | 이 플래그 중 **하나라도** 켜져 있으면 분기 (쉼표 구분, OR 조건) | `flag_c,flag_d` |
| `nextNodeId` | 조건 충족 시 이동할 노드 ID | `5_alt` |

- `requiredAllFlags`와 `requiredAnyFlags` 중 하나만 사용합니다. 둘 다 값이 있으면 `requiredAllFlags`(AND)가 우선합니다.
- 한 노드에 여러 조건을 쓰려면 **행을 여러 개** 작성합니다. 위에서 아래 순서로 확인하고, 처음으로 맞는 조건으로 이동합니다.
- 어떤 조건도 맞지 않으면 `#NODES`의 `nextNodeId`로 이동합니다.
- 확인 순서: `NODE_BRANCHES` → `NODE_EPISODE_BRANCHES` → `NODE_VAR_BRANCHES` → `#NODES`의 기본 `nextNodeId`.

```csv
#NODE_BRANCHES
nodeId,requiredAllFlags,requiredAnyFlags,nextNodeId
5,flag_a,flag_b,,5_and_alt
6,,flag_c,flag_d,6_or_alt
```

---

### NODE_VAR_BRANCHES

수치 변수 값에 따라 다음 노드를 분기합니다. 호감도·평판 등 점수 기반 분기에 사용합니다.  
분기가 없으면 이 섹션은 **헤더만 남기고 비워도** 됩니다.

| 열 | 설명 | 예시 |
|---|---|---|
| `nodeId` | 분기가 적용될 노드 ID | `5` |
| `varName` | 확인할 수치 변수 이름 | `sally_affinity` |
| `op` | 비교 연산자 (`>=` `>` `==` `<` `<=`) | `>=` |
| `threshold` | 비교 기준값 | `10` |
| `nextNodeId` | 조건 충족 시 이동할 노드 ID | `5_high` |

- 한 노드에 여러 조건을 쓰려면 **행을 여러 개** 작성합니다. 위에서 아래 순서로 확인하고, 처음으로 맞는 조건으로 이동합니다.
- 어떤 조건도 맞지 않으면 `#NODES`의 `nextNodeId`로 이동합니다.

```csv
#NODE_VAR_BRANCHES
nodeId,varName,op,threshold,nextNodeId
5,sally_affinity,>=,10,5_high
5,sally_affinity,>=,5,5_mid
```

> 위 예시는 `sally_affinity`가 10 이상이면 `5_high`, 5 이상이면 `5_mid`, 그 미만이면 `#NODES`의 기본 `nextNodeId`로 이동합니다.

---

### NODE_EPISODE_BRANCHES

특정 에피소드의 완료 여부에 따라 다음 노드를 분기합니다. "A 에피소드를 클리어한 뒤 B 에피소드를 진행하면 내용이 달라진다" 같은 챕터 간 연동에 사용합니다.  
분기가 없으면 이 섹션은 **헤더만 남기고 비워도** 됩니다.

| 열 | 설명 | 예시 |
|---|---|---|
| `nodeId` | 분기가 적용될 노드 ID | `5` |
| `requiredCompletedEpisodeId` | 이 에피소드가 완료되어 있어야 분기 | `StrangeCoin_0` |
| `nextNodeId` | 조건 충족 시 이동할 노드 ID | `5_after_coin` |

- 한 노드에 여러 조건을 쓰려면 **행을 여러 개** 작성합니다. 위에서 아래 순서로 확인하고, 처음으로 맞는 조건으로 이동합니다.
- 어떤 조건도 맞지 않으면 `NODE_VAR_BRANCHES` → `#NODES`의 기본 `nextNodeId` 순으로 확인합니다.

```csv
#NODE_EPISODE_BRANCHES
nodeId,requiredCompletedEpisodeId,nextNodeId
5,StrangeCoin_0,5_after_coin
```

> 그래프 에디터에서는 이 조건을 별도 컬럼이 아니라 **엣지 라벨에 에피소드 ID를 그대로 적는 것**(예: `StrangeCoin_0`)으로 표현합니다. 자세한 내용은 [narrative-graph-guide.md](narrative-graph-guide.md) 참고.

---

## 특수 표기법

### 텍스트 서식

대사(`text`)와 버튼 텍스트에 Rich Text 태그를 사용할 수 있습니다.

| 태그 | 결과 | 예시 |
|---|---|---|
| `<b>텍스트</b>` | **굵게** | `<b>5번가</b>에서` |
| `<u>텍스트</u>` | 밑줄 | `<u>당장 나가!</u>` |
| `<i>텍스트</i>` | 기울임 | `<i>(혼잣말)</i>` |

### 리스트 구분자

플래그나 ID 목록은 파이프(`|`)로 구분합니다.

```
flag_a|flag_b|flag_c
```

### 쉼표가 포함된 텍스트

대사에 쉼표가 있으면 **큰따옴표로 감쌉니다**. Google Sheets나 Excel에서 저장하면 자동 처리됩니다.

```csv
0,f72,???,흘..크흘…,1,false,,,
1,f72,???,"여기, 이거 드세요.",2,false,,,
```

---

## 작성 예시

아래는 분기가 있는 짧은 에피소드의 전체 예시입니다.

```csv
#META
episodeId,episodeTitle,firstNodeId,episodeType,mandatorySlot,chapterId
Example_0,예시 에피소드,0,Default,None,chapter_1

#OPENING_CHARS
characterKey,expressionKey,slotIndex
f72,neutral,-1

#BOARD
episodeDescription,iconNameBoard,iconNameArchive
"작전판에 표시될 짧은 설명입니다.",icon_example,icon_example_archive

#BOARD_CHARS
isHidden,characterName
false,f72

#NODES
nodeId,speakerKey,overrideSpeakerName,text,nextNodeId,requiresCrafting,craftingTicketKey,bgmCommand,bgmClipName
0,f72,???,뭘 마시겠어?,1,false,,play,bgm_bar
1,shaun,,추천해줘.,2,false,,none,
2,f72,,그럼 선택해.,,false,,none,
3a,f72,,좋은 선택이야.,4,false,,none,
3b,f72,,그것도 나쁘지 않아.,4,false,,none,
4,f72,,또 오게.,,false,,stop,

#NODE_CHARS
nodeId,characterKey,expressionKey,slotIndex
0,f72,neutral,-1
1,f72,neutral,-1
2,f72,smile,-1
3a,f72,smile,-1
3b,f72,neutral,-1
4,f72,neutral,-1

#CHOICES
nodeId,choiceIndex,buttonText,nextNodeId,setFlags,clearFlags,varChanges
2,0,맥주,3a,flag_chose_beer,,sally_affinity+5
2,1,위스키,3b,flag_chose_whiskey,,

#NODE_BRANCHES
nodeId,requiredAllFlags,requiredAnyFlags,nextNodeId

#NODE_VAR_BRANCHES
nodeId,varName,op,threshold,nextNodeId

#NODE_EPISODE_BRANCHES
nodeId,requiredCompletedEpisodeId,nextNodeId
```

---

## 영업 인카운터 풀에 연결

CSV의 `#META` 행에서 `episodeType`을 `Encounter`로 작성한 뒤 임포트합니다. 임포트된 `EpisodeData` 에셋을 `BusinessOrderFlowSettings` 에셋의 `Random Encounters` 목록에 등록하고 상대 가중치를 설정합니다.

- `triggerCondition`을 만족하고 아직 완료하지 않은 인카운터만 영업 시작 풀에 들어옵니다.
- 일반 손님과 랜덤 인카운터는 하나의 가중치 후보군에서 추첨됩니다.
- 인카운터에는 시간 쿨다운이 없으며, 각 `episodeId`는 하나의 영업일에 최대 1회만 시작됩니다.
- 완료한 인카운터는 이후 영업일에도 다시 풀에 들어오지 않습니다.
- 같은 날의 `Required Actions`에 필수 인카운터로 등록된 에피소드는 지정된 타이밍을 보장하기 위해 그날의 랜덤 풀에서 제외됩니다.

CSV 임포트는 에피소드 에셋만 만듭니다. `Random Encounters`에 등록하는 단계는 자동으로 실행되지 않습니다.

---

## 임포트 방법

1. Unity 메뉴 → **Tools > Slainte > Import Episode CSV**
2. **Browse** 버튼으로 작성한 CSV 파일 선택
3. **Import** 클릭
4. `Assets/Resources/EpisodeData/EpisodeData_{episodeId}.asset` 으로 저장됨

같은 `episodeId`의 에셋이 이미 존재하면 **덮어씁니다**.

`BOARD`/`BOARD_CHARS`/`SELECT_CHARS` 섹션은 CSV에 아예 없으면(헤더조차 없으면) 기존 에셋 값을 유지합니다. 즉 이 섹션들만 CSV에 없는 예전 CSV를 재임포트해도 인스펙터에서 채워둔 값이 지워지지 않습니다. 반대로 섹션을 (빈 섹션이라도) 작성하면 그때부터 CSV가 해당 필드의 기준이 됩니다.

---

## 자주 하는 실수

| 증상 | 원인 | 해결 |
|---|---|---|
| 임포트 후 대사가 안 보임 | `firstNodeId`가 실제 노드 ID와 다름 | `#META`의 `firstNodeId`와 `#NODES`의 첫 `nodeId`를 맞춤 |
| 선택지 후 대화가 안 이어짐 | 선택지 노드의 `nextNodeId`를 채워둠 | 선택지 노드는 `nextNodeId` 비워두기 |
| 에피소드가 갑자기 종료됨 | 노드의 `nextNodeId`가 비어있거나 존재하지 않는 ID | `nextNodeId` 확인 |
| 표정이 바뀌지 않음 | `#NODE_CHARS`에 해당 노드 행이 없음 | 표정이 바뀌는 노드마다 `#NODE_CHARS` 행 추가 |
| 쉼표 이후 텍스트가 잘림 | 대사에 쉼표가 있는데 따옴표로 안 감쌈 | 해당 셀을 `"큰따옴표"` 로 감싸기 |
| 분기가 동작하지 않음 | `varChanges` 형식 오류 | `varName+숫자` 또는 `varName-숫자` 형식 확인 |
| 필수 에피소드인데 Rest 보드에서 선택 가능 | `episodeType`을 `Mandatory`로 안 바꿈 | `#META`의 `episodeType`, `mandatorySlot` 확인 |
| 챕터별 필수 에피소드 큐 조회가 안 됨 | `chapterId`가 비어있거나 다른 챕터와 다름 | `#META`의 `chapterId`를 `ChapterData.chapterId`와 일치시키기 |
| 재임포트했더니 작전판 설명/아이콘/초상화가 사라짐 | `#BOARD`/`#BOARD_CHARS`/`#SELECT_CHARS` 섹션을 (빈 섹션으로) 작성해서 CSV가 기준이 됐는데 실제 값은 안 채움 | 값을 인스펙터로 계속 관리하고 싶으면 해당 섹션을 CSV에서 아예 빼기 |
| 선택 옵션의 초상화가 기본 초상화로만 나옴 | 해당 `selectFlag`에 대한 `#SELECT_CHARS` 행이 없음(섹션은 있지만 그 flag 행이 없으면 빈 목록으로 대체됨) | `#SELECT_CHARS`에 해당 `selectFlag` 행 추가 |
