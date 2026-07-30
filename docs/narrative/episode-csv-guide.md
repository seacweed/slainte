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
  - [NODES](#nodes)
  - [NODE_CHARS](#node_chars)
  - [CHOICES](#choices)
  - [NODE_BRANCHES](#node_branches)
  - [NODE_VAR_BRANCHES](#node_var_branches)
  - [NODE_EPISODE_BRANCHES](#node_episode_branches)
- [특수 표기법](#특수-표기법)
- [작성 예시](#작성-예시)
- [임포트 방법](#임포트-방법)
- [자주 하는 실수](#자주-하는-실수)

---

## 파일 규칙

- **파일명**: 자유롭게 지정 가능 (임포트 후 에셋명은 `episodeId` 기준으로 자동 결정)
- **인코딩**: UTF-8
- **권장 편집 도구**: Google Sheets, Excel, 메모장 등 CSV를 저장할 수 있는 모든 도구

---

## 전체 구조

파일은 `#섹션명` 으로 구분된 최대 10개 섹션으로 이루어집니다.  
각 섹션은 **헤더 행(열 이름)** → **데이터 행** 순서로 작성합니다.

```
#META
(헤더 행)
(데이터 행)

#TRIGGER
...

#PLAY_TRIGGER
...

#OPENING_CHARS
...

#NODES
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
| `episodeType` | `Default`(Rest 보드에서 직접 선택) / `Mandatory`(필수, 영업 전후 자동 삽입). 비우면 `Default` | `Default` |
| `mandatorySlot` | `episodeType=Mandatory`일 때만 사용. `BeforeBusiness` / `AfterBusiness`. 비우면 `None` | `BeforeBusiness` |
| `chapterId` | 소속 챕터 ID (`ChapterData.chapterId`와 매칭, 챕터 스코프 필수 에피소드 큐 조회에 사용) | `chapter_1` |

```csv
#META
episodeId,episodeTitle,firstNodeId,episodeType,mandatorySlot,chapterId
StrangeCoin_0,이상한 동전 - 0,0,Default,None,chapter_1
```

> `episodeType`/`mandatorySlot`/`chapterId` 열은 생략해도 됩니다(빈 값은 각각 `Default`/`None`/빈 문자열로 처리됨). 기존 CSV를 그대로 재임포트해도 문제없습니다.

---

### TRIGGER

이 에피소드가 작전판(Rest 화면)에 **해금(노출)**되는 조건입니다. **데이터 행은 반드시 1개** 작성합니다.

| 열 | 설명 | 예시 |
|---|---|---|
| `minDay` | 발동 가능한 최소 일수 | `3` |
| `requiredFlags` | 이 플래그가 **모두 켜져있어야** 발동 | `flag_a\|flag_b` |
| `blockedFlags` | 이 플래그 중 **하나라도 켜져있으면** 발동 안 함 | `flag_ended` |
| `prerequisiteEpisodeIds` | 이 에피소드들이 **모두 완료되어야** 발동 | `Intro_0\|Intro_1` |
| `requiredVars` | 수치 변수 조건이 **모두 충족되어야** 발동 | `sally_affinity>=10` |
| `requiredCustomerAppearances` | 손님이 **이 횟수 이상 등장해야** 발동 (`캐릭터ID:횟수`, `\|` 구분) | `himiko:3` |

- 조건이 없는 열은 **비워두면** 됩니다.
- 여러 값은 `|` 로 구분합니다.
- `requiredVars` 지원 연산자: `>=` `>` `==` `<` `<=`

```csv
#TRIGGER
minDay,requiredFlags,blockedFlags,prerequisiteEpisodeIds,requiredVars,requiredCustomerAppearances
3,flag_met_customer,,Intro_0,sally_affinity>=5,himiko:3
```

---

### PLAY_TRIGGER

`TRIGGER`(해금 조건)와 컬럼 구성이 완전히 동일하지만 의미가 다릅니다 — 이 조건을 만족해야 작전판에서 **Play 버튼이 활성화**됩니다. 해금은 됐지만 아직 플레이는 못 하는 상태(예: 사진은 작전판에 떴지만 눌러보면 버튼이 비활성)를 표현할 때 씁니다.

- **생략 가능** — 섹션 자체를 안 쓰면 "플레이 조건 없음"(해금되면 바로 플레이 가능)으로 처리됩니다.
- 열 구성과 문법은 `TRIGGER`와 동일합니다.

```csv
#PLAY_TRIGGER
minDay,requiredFlags,blockedFlags,prerequisiteEpisodeIds,requiredVars,requiredCustomerAppearances
0,,,,,
```

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
| `nextNodeIdGood` | 제조 성공 시 이동할 노드 ID | `5-1-1` |
| `nextNodeIdBad` | 제조 실패 시 이동할 노드 ID | `5-2-1` |
| `bgmCommand` | BGM 명령 (`none` / `play` / `stop`, 비우면 `none`) | `play` |
| `bgmClipName` | 재생할 BGM 파일명 (`bgmCommand=play` 일 때만 작성, 확장자 제외) | `bgm_tension` |
| `craftingFlagGood` | 제조 **성공** 시 설정할 플래그 | `sc0_crafted_good` |
| `craftingFlagBad` | 제조 **실패** 시 설정할 플래그 | `sc0_crafted_bad` |
| `craftingVarChangesGood` | 제조 **성공** 시 수치 변수 변경 (`\|` 구분, `+`/`-` 증감) | `sally_affinity+5` |
| `craftingVarChangesBad` | 제조 **실패** 시 수치 변수 변경 (`\|` 구분, `+`/`-` 증감) | `sally_affinity-2` |

**제조 판정 노드** 작성 시: `text`와 `nextNodeId`는 비우고, `requiresCrafting=true` + 성공/실패 노드 ID를 작성합니다.

**선택지 노드** 작성 시: `nextNodeId`는 비우고 `#CHOICES` 섹션에 선택지를 작성합니다.

**분기 노드** 작성 시: `nextNodeId`는 조건이 모두 맞지 않을 때의 기본 이동 노드입니다. 조건 분기는 `#NODE_BRANCHES` / `#NODE_EPISODE_BRANCHES` / `#NODE_VAR_BRANCHES`에 작성합니다.

```csv
#NODES
nodeId,speakerKey,overrideSpeakerName,text,nextNodeId,requiresCrafting,craftingTicketKey,nextNodeIdGood,nextNodeIdBad,bgmCommand,bgmClipName,craftingFlagGood,craftingFlagBad,craftingVarChangesGood,craftingVarChangesBad
0,f72,???,흘..크흘…,1,false,,,,play,bgm_tension,,,,
5,f72,???,,,true,sc0_f72,5-1-1,5-2-1,,,sc0_crafted_good,sc0_crafted_bad,sally_affinity+5,sally_affinity-2
```

> **주의**: 대사에 쉼표(`,`)가 포함된 경우 반드시 큰따옴표로 감싸야 합니다.  
> 예: `"여기, 이거 드세요."`

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

#TRIGGER
minDay,requiredFlags,blockedFlags,prerequisiteEpisodeIds,requiredVars,requiredCustomerAppearances
0,,,,,

#OPENING_CHARS
characterKey,expressionKey,slotIndex
f72,neutral,-1

#NODES
nodeId,speakerKey,overrideSpeakerName,text,nextNodeId,requiresCrafting,craftingTicketKey,nextNodeIdGood,nextNodeIdBad,bgmCommand,bgmClipName,craftingFlagGood,craftingFlagBad,craftingVarChangesGood,craftingVarChangesBad
0,f72,???,뭘 마시겠어?,1,false,,,,play,bgm_bar,,,,
1,shaun,,추천해줘.,2,false,,,,none,,,,,
2,f72,,그럼 선택해.,,false,,,,none,,,,,
3a,f72,,좋은 선택이야.,4,false,,,,none,,,,,
3b,f72,,그것도 나쁘지 않아.,4,false,,,,none,,,,,
4,f72,,또 오게.,,false,,,,stop,,,,,

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

## 임포트 방법

1. Unity 메뉴 → **Tools > Slainte > Import Episode CSV**
2. **Browse** 버튼으로 작성한 CSV 파일 선택
3. **Import** 클릭
4. `Assets/Resources/EpisodeData/EpisodeData_{episodeId}.asset` 으로 저장됨

같은 `episodeId`의 에셋이 이미 존재하면 **덮어씁니다**.

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
