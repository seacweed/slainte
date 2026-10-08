# 에피소드 CSV 작성 가이드

에피소드 대화 데이터를 CSV 파일로 작성하면 Unity 에디터 툴을 통해 자동으로 게임 데이터로 변환됩니다.

## 목차

- [파일 규칙](#파일-규칙)
- [전체 구조](#전체-구조)
- [섹션별 작성법](#섹션별-작성법)
  - [META](#meta)
  - [TRIGGER](#trigger)
  - [SETTLEMENT_REWARDS](#settlement_rewards)
  - [NODES](#nodes)
  - [NODE_CRAFTING_BRANCHES](#node_crafting_branches)
  - [NODE_CHARS](#node_chars)
  - [CHOICES](#choices)
  - [NODE_BRANCHES](#node_branches)
- [특수 표기법](#특수-표기법)
- [작성 예시](#작성-예시)
- [영업 일정에 배정](#영업-일정에-배정)
- [임포트 방법](#임포트-방법)
- [자주 하는 실수](#자주-하는-실수)

---

## 파일 규칙

- **파일명**: 자유롭게 지정 가능 (임포트 후 에셋명은 `episodeId` 기준으로 자동 결정)
- **인코딩**: UTF-8
- **권장 편집 도구**: Google Sheets, Excel, 메모장 등 CSV를 저장할 수 있는 모든 도구

---

## 전체 구조

파일은 `#섹션명` 으로 구분된 최대 11개 섹션으로 이루어집니다.  
각 섹션은 **헤더 행(열 이름)** → **데이터 행** 순서로 작성합니다. 열은 **헤더의 열 이름으로 찾으므로** 열 순서를 바꾸거나 쓰지 않는 열을 빼도 됩니다.

```
#META
(헤더 행)
(데이터 행)

#TRIGGER
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
```

> 기획 개편으로 `#PLAY_TRIGGER`, `#SELECT_TRIGGER`, `#SELECT_CHARS`, `#BOARD`, `#BOARD_CHARS`, `#OPENING_CHARS` 섹션과 `#META`의 `episodeType`, `mandatorySlot` 열은 **없어졌습니다**. 남아 있어도 임포트는 되지만 무시되며 Console에 경고가 뜹니다 — CSV에서 지워 주세요.
>
> 조건 분기도 `#NODE_BRANCHES` 한 섹션으로 합쳐졌습니다. 예전 형식(`#NODE_BRANCHES`의 `requiredAllFlags` 열, `#NODE_VAR_BRANCHES`, `#NODE_EPISODE_BRANCHES`)은 아직 읽히지만 경고가 뜹니다 — [NODE_BRANCHES](#node_branches)의 새 형식으로 옮겨 주세요(그래프에서 Compile하면 자동으로 새 형식으로 다시 써집니다).

> 빈 행은 무시됩니다. 가독성을 위해 섹션 사이에 빈 행을 추가해도 됩니다. 완전히 빈 줄뿐 아니라 **모든 셀이 비어있는 콤마만 있는 줄**(`,,,,,,,,,,,`, 스프레드시트에서 열 개수를 맞추려고 자동으로 채워지는 흔적)도 데이터 행으로 취급되지 않고 건너뜁니다.

---

## 섹션별 작성법

### META

에피소드의 기본 정보와 **영업 일정(등장 시점)**입니다. **데이터 행은 반드시 1개** 작성합니다.

| 열 | 설명 | 예시 |
|---|---|---|
| `episodeId` | 에피소드 고유 ID (에셋 파일명에 사용됨) | `StrangeCoin_0` |
| `episodeTitle` | 에피소드 제목 | `이상한 동전 - 0` |
| `firstNodeId` | 대화가 시작될 첫 번째 노드 ID | `0` |
| `chapterId` | 소속 챕터 ID (`ChapterData.chapterId`와 매칭). 비우면 모든 챕터에서 후보가 됨 | `Sector0` |
| `day` | 등장하는 날짜(1부터). **비우면 영업에 자동으로 등장하지 않습니다** | `5` |
| `slot` | 그 날의 손님 슬롯 번호(1~하루 손님 수, 기본 1~5) | `3` |
| `priority` | 같은 `day`/`slot`에 후보가 여럿일 때 우선순위. **큰 값부터** 등장 조건을 확인. 비우면 0 | `10` |

```csv
#META
episodeId,episodeTitle,firstNodeId,chapterId,day,slot,priority
StrangeCoin_0,이상한 동전 - 0,0,Sector0,5,3,
```

- 같은 `day`/`slot`에 여러 에피소드를 배정할 수 있습니다. 슬롯 차례가 오면 `priority`가 큰 것부터 `#TRIGGER`(등장 조건)를 확인해 **처음 만족하는 하나만** 등장합니다. 하나도 만족하지 않으면 그 슬롯에는 랜덤 손님이 옵니다.
- 같은 `priority`끼리는 `episodeId` 순서로 확인합니다. 의도한 순서가 있다면 `priority`를 다르게 주세요.
- 이미 완료한 에피소드는 다시 등장하지 않습니다. 조건 미충족으로 등장하지 못한 에피소드는 다른 날로 밀리지 않습니다.

---

### TRIGGER

이 에피소드의 **등장 조건**입니다. 배정된 슬롯 차례가 왔을 때 판정합니다(같은 날 앞 슬롯에서 세운 flag도 반영됨). **행 하나 = 조건 하나**이며, 여러 행은 **AND로 결합**됩니다.

- 컬럼: `conditionType`, `conditionValue`
- **생략 가능** — 섹션을 안 쓰거나 비우면 "조건 없음"(배정된 슬롯에서 항상 등장)입니다.

| `conditionType` | `conditionValue` 형식 | 의미 | 예시 |
|---|---|---|---|
| `MinDay` | 숫자 | 이 날짜 이상 | `3` |
| `MinMoney` | 숫자 | 소지금 이 값 이상 | `150000` |
| `RequiredFlag` | 플래그 이름 | 이 플래그가 켜져 있어야 함 | `sc_0_good` |
| `BlockedFlag` | 플래그 이름 | 이 플래그가 켜져 있으면 등장 안 함 | `sc_0_bad` |
| `PrerequisiteEpisode` | 에피소드 ID | 이 에피소드를 완료했어야 함 | `StrangeCoin_0` |
| `RequiredVar` | `varName연산자값` (연산자: `>=` `>` `==` `<` `<=`) | 수치 변수 조건 | `sally_affinity>=5` |
| `CustomerAppearance` | `characterKey>=횟수` | 그 손님이 랜덤 손님으로 이 횟수 이상 등장했어야 함 | `f72>=3` |

```csv
#TRIGGER
conditionType,conditionValue
PrerequisiteEpisode,StrangeCoin_0
RequiredFlag,sc_0_good
```

> 예전 `#TRIGGER`의 `text` 열(작전판 툴팁 문구)은 더 이상 쓰지 않습니다. 남아 있어도 무시됩니다.
>
> **분기 에피소드 예시**: 앞 에피소드의 결과에 따라 Day 7의 2번 슬롯이 `A_good` 또는 `A_bad`가 되게 하려면, 두 CSV 모두 `day=7, slot=2`로 두고 각각 `RequiredFlag`/`BlockedFlag`로 조건을 나눕니다. 둘 다 아니면(예: 앞 에피소드 자체를 못 봤으면) 랜덤 손님이 옵니다.

---

### SETTLEMENT_REWARDS

에피소드가 끝날 때 특정 플래그가 서 있으면 정산 화면에 커스텀 보상 줄을 추가합니다. **선택 사항** — 섹션 자체를 안 쓰면 기존 에셋 값을 유지합니다(헤더만 있는 빈 섹션은 "보상 없음"). 0개, 1개, 여러 개 모두 가능합니다.

- **행 하나 = 보상 조건 하나**입니다.
- 에피소드 종료 시점에 `requiredFlag`가 켜져 있는 행만 정산 화면에 반영됩니다(예: 특정 선택지의 `setFlags`나 제조 결과의 `flag`로 미리 세워둔 플래그).
- 지급은 **정산 시점**에 이루어집니다(음료 판매처럼 그 자리에서 바로 지급되지 않습니다).

| 열 | 설명 | 예시 |
|---|---|---|
| `requiredFlag` | 이 플래그가 서 있어야 보상이 지급됨 | `celi_apology_paid` |
| `amount` | 지급 금액(음수면 차감) | `150` |
| `label` | 정산 화면에 표시할 문구 | `소란 피워서 미안해 - 셀리` |

```csv
#SETTLEMENT_REWARDS
requiredFlag,amount,label
celi_apology_paid,150,소란 피워서 미안해 - 셀리
```

---

### NODES

대화 노드 목록입니다. 노드 하나 = 화면에 표시되는 대사 한 줄입니다.

| 열 | 설명 | 예시 |
|---|---|---|
| `nodeId` | 노드 고유 ID (번호 규칙은 아래 "노드 ID 규칙") | `0`, `6_1_1` |
| `speakerKey` | 말하는 캐릭터 ID (`shaun` = 주인공) | `f72` |
| `overrideSpeakerName` | 이름창에 표시할 임시 이름 (비우면 캐릭터 기본 이름 사용) | `???` |
| `text` | 대사 내용 | `안녕하세요.` |
| `nextNodeId` | 다음에 이동할 노드 ID (비우면 에피소드 종료) | `1` |
| `requiresCrafting` | 제조 판정 여부 (`TRUE` / 빈칸 = 아님) | `TRUE` |
| `craftingTicketKey` | 사용할 제조 티켓 ID (`requiresCrafting=true` 일 때만 작성) | `sc0_f72` |
| `craftingOrderTarget` | 판정 기준이 될 레시피 ID 또는 조건 태그 (`requiresCrafting=true` 일 때 필수, 비우면 제조 세션이 시작되지 않음) | `rec_1019` |
| `bgmCommand` | BGM 명령 (`none` / `play` / `stop`, 비우면 `none`) | `play` |
| `bgmClipName` | 재생할 BGM 파일명 (`bgmCommand=play` 일 때만 작성, 확장자 제외) | `bgm_tension` |
| `sfxCommand` | 효과음 명령 (`none` / `play`, 비우면 `none`) | `play` |
| `sfxClipName` | 재생할 효과음 파일명 (`sfxCommand=play` 일 때만 작성, 확장자 제외, `Resources/Core/Audio/SFX/` 폴더 기준) | `sfx_bell` |
| `craftingPaymentEnabled` | 제조 완료 시 가격 지급 여부 (비우면 실제 제조 노드는 `true`) | `true` |
| `craftingPaymentCurrency` | 지급 통화 (`Money` / `StrangeCoin`, 비우면 `Money`) | `StrangeCoin` |
| `craftingPaymentMultiplier` | 통화별 레시피 가격에 적용할 양수 배율 (비우거나 잘못된 값이면 `1`) | `2` |

BGM은 무한 반복 재생되며 새로 재생하면 이전 BGM과 크로스페이드로 교체됩니다. SFX는 BGM과 별도 채널에서 한 번만 재생되고(반복 없음), BGM을 멈추지 않으며 여러 개가 겹쳐 재생될 수 있습니다.

**빈칸 = 기본값**: 아래 칸은 비워 두면 기본값이 들어가며, 그래프 에디터가 CSV를 쓸 때도 기본값과 같으면 비워 둡니다. `requiresCrafting` 빈칸 = `FALSE`, `bgmCommand`/`sfxCommand` 빈칸 = 없음, `craftingPaymentEnabled` 빈칸 = 제조 노드이고 `craftingOrderTarget`이 있으면 `TRUE`, `craftingPaymentCurrency` 빈칸 = `Money`, `craftingPaymentMultiplier` 빈칸 = `1`. 주문 유형(`craftingOrderType`)은 `craftingOrderTarget`으로 자동 판별되므로 컬럼을 쓰지 않습니다.

**제조 판정 노드** 작성 시: `text`와 `nextNodeId`는 비우고, `requiresCrafting=true` + `craftingTicketKey` + `craftingOrderTarget`을 작성합니다. `craftingOrderTarget`이 비어 있으면 제조 세션이 시작되지 않아 주문서도 뜨지 않고 판정도 진행되지 않습니다. 제조 결과별 이동 노드·플래그·변수 변경은 `#NODE_CRAFTING_BRANCHES` 섹션에 작성합니다. 선택지별 통화나 배율이 다르면 제조 노드를 나누고 각 노드에 결제 열을 별도로 입력합니다.
>
> **레시피 ID vs 맛/분위기 태그**: `craftingOrderTarget`에 뭘 쓰든 별도로 지정할 컬럼은 없습니다 — `EpisodeCraftingBridge.ResolveOrderType()`이 값 자체를 `Assets/Resources/Bartending/Recipes/TasteMoodPalette.asset`(맛/분위기 태그 팔레트)에 대조해서 자동으로 판별합니다. 팔레트에 등록된 태그 문자열(예: `고급스러운`, `씁쓸함`)이면 태그 기반 주문(맛/분위기 조건만 맞으면 통과)으로, 등록 안 된 값이면 레시피 ID(`rec_1019` 등, 정확히 그 레시피여야 통과)로 처리됩니다. 팔레트에 없는 오타 태그를 쓰면 존재하지 않는 레시피 ID로 취급되어 제조가 항상 실패하니, 태그를 쓸 땐 팔레트에 등록된 문자열과 정확히 일치하는지 확인하세요.

**선택지 노드** 작성 시: `nextNodeId`는 비우고 `#CHOICES` 섹션에 선택지를 작성합니다.

**분기 노드** 작성 시: `nextNodeId`는 조건이 모두 맞지 않을 때의 기본 이동 노드입니다. 조건 분기는 `#NODE_BRANCHES`에 작성합니다.

```csv
#NODES
nodeId,speakerKey,overrideSpeakerName,text,nextNodeId,requiresCrafting,craftingTicketKey,craftingOrderTarget,bgmCommand,bgmClipName,sfxCommand,sfxClipName,craftingPaymentEnabled,craftingPaymentCurrency,craftingPaymentMultiplier
0,f72,???,흘..크흘…,1,,,,play,bgm_tension,,,,,
5,,,,,TRUE,sc0_f72,rec_1019,,,play,sfx_bell,,StrangeCoin,2
```

> **주의**: 대사에 쉼표(`,`)가 포함된 경우 반드시 큰따옴표로 감싸야 합니다.  
> 예: `"여기, 이거 드세요."`

#### 노드 ID 규칙과 줄 순서

그래프 에디터가 CSV를 쓸 때는 아래 규칙으로 번호를 매기고, 노드를 **대화 흐름(블록) 순서**로 나열합니다 — 분기가 나오면 갈래 1을 끝까지, 다음 갈래를 끝까지 쓰고, 합류 노드는 모든 갈래 뒤에 옵니다. 손으로 쓸 때도 같은 규칙을 따르면 그래프를 거쳐도 번호가 바뀌지 않습니다.

| 흐름 | ID |
|---|---|
| 직선 | 1씩 증가 (`0 → 1 → 2`) |
| 분기 (선택지·제조·조건 노드 `20`) | 다음 번호로 갈래마다 `21_1_1`, `21_2_1` … (갈래 번호 = 선택지·결과·분기 순서) |
| 합류 | `22` |
| 갈래 안의 분기 (`21_1_3`) | `21_1_4_1_1` … → 합류 `21_1_5` |
| 갈래마다 있는 제조 노드가 결과를 공유 | 결과 `22_k_1` → 합류 `23` |
| 갈래 일부가 먼저 만난 뒤 모두 만남 | 먼저 만난 곳 `22_1_1`, `22_2_1` … → 모두 만나는 곳 `22` |

자세한 규칙은 [narrative-graph-guide.md](narrative-graph-guide.md#노드-id-규칙-자동).

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

플래그·변수·에피소드 완료 여부에 따라 다음 노드를 분기합니다. `#TRIGGER`와 같은 조건 타입 이름과 값 형식을 씁니다.
분기가 없으면 이 섹션은 **헤더만 남기고 비워도** 됩니다.

| 열 | 설명 | 예시 |
|---|---|---|
| `nodeId` | 분기가 적용될 노드 ID | `5` |
| `conditionType` | 조건 종류 (아래 표) | `RequiredFlag` |
| `conditionValue` | 조건 값 | `flag_a` |
| `nextNodeId` | 조건 충족 시 이동할 노드 ID | `6_1_1` |

| conditionType | conditionValue | 분기 조건 |
|---|---|---|
| `RequiredFlag` | 플래그 하나 또는 여러 개(`,` 또는 `\|`로 구분 — 쉼표를 쓰면 셀을 따옴표로 감쌈) | 모두 켜져 있으면 |
| `BlockedFlag` | 위와 같음 | 모두 꺼져 있으면 |
| `RequiredVar` | `변수이름` + 연산자(`>=` `>` `==` `<` `<=`) + 정수, 예: `sally_affinity>=10` | 비교가 참이면 |
| `PrerequisiteEpisode` | 에피소드 ID | 그 에피소드를 완료했으면 |

- **한 줄 = 분기 하나**. 한 노드의 줄들을 **위에서 아래 순서로** 확인하고, 처음 맞는 줄의 `nextNodeId`로 이동합니다(조건 종류와 관계없이 줄 순서가 우선순위).
- 어떤 조건도 맞지 않으면 `#NODES`의 `nextNodeId`로 이동합니다.
- "하나라도 켜져 있으면(OR)" 조건은 없습니다. 플래그마다 줄을 따로 쓰면 같은 효과입니다.

```csv
#NODE_BRANCHES
nodeId,conditionType,conditionValue,nextNodeId
5,RequiredFlag,"sc0_true, sc4_1_1_true",6_1_1
5,BlockedFlag,sc0_true,6_2_1
5,RequiredVar,sally_affinity>=10,6_3_1
5,PrerequisiteEpisode,StrangeCoin_0,6_4_1
```

> 위 예시는 두 플래그가 모두 켜져 있으면 `6_1_1`, 아니고 `sc0_true`가 꺼져 있으면 `6_2_1`, 아니고 `sally_affinity`가 10 이상이면 `6_3_1`, 아니고 StrangeCoin_0을 완료했으면 `6_4_1`, 모두 아니면 `#NODES`의 기본 `nextNodeId`로 이동합니다.

> 그래프 에디터에서는 이 조건을 블록의 포트 라벨(`a&b == true`, `a == false`, `var >= 10`, `StrangeCoin_0`)로 표현하며, 포트 순서가 곧 줄 순서입니다. 자세한 내용은 [narrative-graph-guide.md](narrative-graph-guide.md) 참고.

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
episodeId,episodeTitle,firstNodeId,chapterId,day,slot,priority
Example_0,예시 에피소드,0,Sector0,2,4,

#NODES
nodeId,speakerKey,overrideSpeakerName,text,nextNodeId,requiresCrafting,craftingTicketKey,craftingOrderTarget,bgmCommand,bgmClipName,sfxCommand,sfxClipName,craftingPaymentEnabled,craftingPaymentCurrency,craftingPaymentMultiplier
0,f72,???,뭘 마시겠어?,1,,,,play,bgm_bar,,,,,
1,shaun,,추천해줘.,2,,,,,,,,,,
2,f72,,그럼 선택해.,,,,,,,,,,,
3_1_1,f72,,좋은 선택이야.,4,,,,,,,,,,
3_2_1,f72,,그것도 나쁘지 않아.,4,,,,,,,,,,
4,f72,,또 오게.,,,,,stop,bgm_bar,,,,,

#NODE_CHARS
nodeId,characterKey,expressionKey,slotIndex
0,f72,neutral,-1
1,f72,neutral,-1
2,f72,smile,-1
3_1_1,f72,smile,-1
3_2_1,f72,neutral,-1
4,f72,neutral,-1

#CHOICES
nodeId,choiceIndex,buttonText,nextNodeId,setFlags,clearFlags,varChanges
2,0,맥주,3_1_1,flag_chose_beer,,sally_affinity+5
2,1,위스키,3_2_1,flag_chose_whiskey,,

#NODE_BRANCHES
nodeId,conditionType,conditionValue,nextNodeId
```

---

## 영업 일정에 배정

별도 등록 단계는 없습니다. `#META`의 `day`/`slot`을 채워 임포트하면 그 날의 그 슬롯에 자동으로 배정됩니다(`EpisodeManager`가 시작 시 전체 에피소드를 일정표로 색인).

- 하루 손님 수는 `Assets/Resources/Business/BusinessOrderFlowSettings.asset`의 **하루 손님 수**(`customersPerDay`, 기본 5)입니다. `slot`은 이 범위 안이어야 합니다.
- 특정 손님을 특정 날 꼭 등장시키고 싶을 때도 짧은 에피소드로 만들어 같은 방식으로 배정합니다.
- 챕터의 마지막 날은 `ChapterData`의 `lastDay`로 정합니다. 그 날 정산이 끝나면 엔딩 컷씬이 재생됩니다.

---

## 임포트 방법

> 같은 에피소드를 [그래프 에디터](narrative-graph-guide.md)로도 편집할 수 있다. 그래프에서 컴파일하면 **이 원본 CSV 파일을 직접 덮어쓰므로**, CSV를 고친 뒤에는 그래프 설정의 **CSV에서 그래프 다시 만들기**로 그래프를 먼저 갱신하자(잊어도 컴파일 시 변경 감지 경고가 뜬다).

1. Unity 메뉴 → **Tools > Slainte > Import Episode CSV**
2. **Browse** 버튼으로 작성한 CSV 파일 선택
3. **Import** 클릭
4. `Assets/Resources/Narrative/Episodes/EpisodeData_{episodeId}.asset` 으로 저장됨

`Assets/_Project/Features/Narrative/Content/Source/Episodes/`의 CSV 전체를 한 번에 다시 임포트하려면 **Tools > Slainte > Import All Episode CSVs**를 사용합니다. 경고(지워야 할 옛 섹션, 중복 nodeId 등)는 Console에 파일별로 출력됩니다.

같은 `episodeId`의 에셋이 이미 존재하면 **덮어씁니다**.

`SETTLEMENT_REWARDS` 섹션은 CSV에 아예 없으면(헤더조차 없으면) 기존 에셋 값을 유지합니다. 섹션을 (빈 섹션이라도) 작성하면 그때부터 CSV가 기준이 됩니다. 나머지 섹션은 항상 CSV 내용으로 교체됩니다.

대사에 줄바꿈을 넣으려면 셀 전체를 큰따옴표로 감싸면 됩니다(스프레드시트에서 셀 안 줄바꿈으로 저장하면 자동 처리).

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
| 에피소드가 영업에 안 나옴 | `#META`의 `day`/`slot`이 비어 있음, `chapterId`가 현재 챕터와 다름, 또는 `#TRIGGER` 미충족 | `day`/`slot`/`chapterId`와 등장 조건 확인 |
| 같은 슬롯의 다른 에피소드가 먼저 나옴 | `priority`가 같거나 반대 | 먼저 확인할 에피소드의 `priority`를 더 크게 |
| "nodeId가 중복됩니다" 경고 | `#NODES`에 같은 `nodeId`가 두 번 있음 — 뒤쪽 노드는 절대 실행되지 않음 | 하나의 ID를 바꾸고 그 ID를 가리키는 `nextNodeId`도 수정 |
