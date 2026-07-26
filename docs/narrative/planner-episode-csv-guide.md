# 에피소드 CSV 작성 가이드 (기획자용)

이 문서는 게임 에피소드 대사와 흐름을 CSV 파일로 작성하는 방법을 기획자 관점에서 설명합니다.  
작성한 CSV 파일은 Unity 에디터 툴을 통해 게임 데이터로 자동 변환됩니다.

---

## 목차

1. [시작하기 전에](#1-시작하기-전에)
2. [파일 기본 규칙](#2-파일-기본-규칙)
3. [CSV 구조 한눈에 보기](#3-csv-구조-한눈에-보기)
4. [섹션별 작성법](#4-섹션별-작성법)
   - [META — 에피소드 기본 정보](#meta--에피소드-기본-정보)
   - [TRIGGER — 에피소드 발동 조건](#trigger--에피소드-발동-조건)
   - [OPENING_CHARS — 첫 등장 캐릭터](#opening_chars--첫-등장-캐릭터)
   - [NODES — 대사 노드](#nodes--대사-노드)
   - [NODE_CHARS — 노드별 표정](#node_chars--노드별-표정)
   - [CHOICES — 선택지](#choices--선택지)
   - [NODE_BRANCHES — 플래그 분기](#node_branches--플래그-분기)
   - [NODE_VAR_BRANCHES — 수치 분기](#node_var_branches--수치-분기)
5. [흐름 유형별 작성 패턴](#5-흐름-유형별-작성-패턴)
   - [일반 대화 흐름](#일반-대화-흐름)
   - [플레이어 선택지](#플레이어-선택지)
   - [플래그 분기](#플래그-분기)
   - [호감도 분기](#호감도-분기)
   - [제조 판정](#제조-판정)
6. [전체 예시](#6-전체-예시)
7. [작업 후 게임 반영 방법](#7-작업-후-게임-반영-방법)
8. [자주 하는 실수](#8-자주-하는-실수)

---

## 1. 시작하기 전에

### 준비물

- Google Sheets, Excel, 또는 메모장 등 CSV를 저장할 수 있는 도구
  - **추천**: Google Sheets (협업 편리, 저장 시 자동 따옴표 처리)
- 사용할 캐릭터 ID와 표정 ID 목록 (담당 개발자에게 확인)

### 핵심 개념

| 용어 | 설명 |
|---|---|
| **노드** | 대사 한 줄 = 화면에 표시되는 단위 |
| **nodeId** | 각 노드를 구분하는 고유 번호/이름. 숫자(`0`, `1`) 또는 문자 포함(`3a`, `5-1`)도 가능 |
| **플래그** | ON/OFF로 관리되는 게임 상태값. 예: `flag_met_customer` |
| **수치 변수** | 숫자로 관리되는 게임 상태값. 예: 호감도 `sally_affinity` |
| **섹션** | CSV 안에서 `#섹션명` 으로 시작하는 구분 단위 |

---

## 2. 파일 기본 규칙

- **파일명**: 자유롭게 지정 가능 (임포트 후 에셋명은 `episodeId` 기준으로 자동 결정)
- **인코딩**: **UTF-8** 로 저장 (한글이 깨지는 경우 인코딩 확인)
- **대사에 쉼표(`,`)가 포함될 경우**: 해당 셀을 `"큰따옴표"` 로 감쌉니다.
  - Google Sheets / Excel에서 저장하면 자동으로 처리됩니다.

---

## 3. CSV 구조 한눈에 보기

파일은 아래 8개 섹션으로 구성됩니다. **섹션 순서는 반드시 지켜야 합니다.**

```
#META           ← 에피소드 ID·제목·시작 노드
#TRIGGER        ← 이 에피소드가 언제 발동되는지
#OPENING_CHARS  ← 에피소드 시작 시 무대에 서 있는 캐릭터
#NODES          ← 대사 목록 (핵심 섹션)
#NODE_CHARS     ← 각 대사에서 캐릭터 표정
#CHOICES        ← 플레이어 선택지
#NODE_BRANCHES  ← 플래그 기반 분기
#NODE_VAR_BRANCHES ← 수치(호감도 등) 기반 분기
```

> 분기나 선택지가 없는 섹션은 **헤더 행만 남기고 데이터 없이 두면** 됩니다.

---

## 4. 섹션별 작성법

---

### META — 에피소드 기본 정보

에피소드의 이름과 시작점을 지정합니다. **딱 1행**만 작성합니다.

| 열 | 설명 | 예시 |
|---|---|---|
| `episodeId` | 에피소드 고유 ID. 한 번 정하면 바꾸지 마세요 (에셋 파일명 기준) | `StrangeCoin_0` |
| `episodeTitle` | 게임에 표시되는 에피소드 제목 | `이상한 동전 - 0` |
| `firstNodeId` | 대화가 시작되는 첫 번째 노드 ID | `0` |

```csv
#META
episodeId,episodeTitle,firstNodeId
StrangeCoin_0,이상한 동전 - 0,0
```

---

### TRIGGER — 에피소드 발동 조건

이 에피소드가 게임 내에서 등장하는 조건입니다. **딱 1행**만 작성합니다.  
조건이 없는 열은 **비워두면** 됩니다.

| 열 | 설명 | 예시 |
|---|---|---|
| `minDay` | 발동 가능한 최소 날짜 | `3` |
| `requiredFlags` | **모두 켜져 있어야** 발동. 여러 개는 `\|` 구분 | `flag_met_f72\|flag_talked` |
| `blockedFlags` | 하나라도 켜져 있으면 **발동 안 함**. 여러 개는 `\|` 구분 | `flag_episode_ended` |
| `prerequisiteEpisodeIds` | **모두 완료되어야** 발동. 여러 개는 `\|` 구분 | `Intro_0\|Intro_1` |
| `requiredVars` | 수치 변수 조건. 여러 개는 `\|` 구분 | `sally_affinity>=10` |

**`requiredVars` 연산자**: `>=` `>` `==` `<` `<=`

```csv
#TRIGGER
minDay,requiredFlags,blockedFlags,prerequisiteEpisodeIds,requiredVars
3,flag_met_customer,,Intro_0,sally_affinity>=5
```

---

### OPENING_CHARS — 첫 등장 캐릭터

에피소드가 시작될 때 무대에 미리 배치되는 캐릭터 목록입니다.  
캐릭터가 여러 명이면 **행을 여러 개** 작성합니다.

| 열 | 설명 | 예시 |
|---|---|---|
| `characterKey` | 캐릭터 ID | `f72` |
| `expressionKey` | 시작 표정 ID | `neutral` |
| `slotIndex` | 배치 위치. 보통 `-1`(자동) 사용 | `-1` |

**슬롯 위치 참고**

| 값 | 위치 |
|---|---|
| `-1` | 자동 배치 (권장) |
| `0` | 가운데 |
| `1` | 왼쪽 |
| `2` | 오른쪽 |

```csv
#OPENING_CHARS
characterKey,expressionKey,slotIndex
f72,frust,-1
```

---

### NODES — 대사 노드

**가장 중요한 섹션**입니다. 대사 한 줄 = 행 하나입니다.

| 열 | 설명 | 예시 |
|---|---|---|
| `nodeId` | 노드 고유 ID | `0`, `3a`, `5-1` |
| `speakerKey` | 말하는 캐릭터 ID. 주인공은 `shaun` | `f72` |
| `overrideSpeakerName` | 이름창 임시 표기. 정체를 숨길 때 사용 | `???` |
| `text` | 대사 내용 | `오늘은 어떤 걸 드릴까요?` |
| `nextNodeId` | 다음 노드 ID. 비우면 에피소드 종료 | `1` |
| `requiresCrafting` | 제조 판정 여부 | `false` |
| `craftingTicketKey` | 제조 티켓 ID (판정 있을 때만) | `sc0_f72` |
| `craftingRecipeId` | 실제 제조·판정에 사용할 레시피 ID (`recipes.csv`) | `vodka_lemon` |
| `nextNodeIdGood` | 제조 성공 시 이동 노드 | `5-1-1` |
| `nextNodeIdBad` | 제조 실패 시 이동 노드 | `5-2-1` |
| `bgmCommand` | BGM 명령 (`play` / `stop` / `none` 또는 빈 칸) | `play` |
| `bgmClipName` | BGM 파일명 (확장자 제외, `play` 일 때만) | `bgm_tension` |

**노드 유형별 주의사항**

| 유형 | `nextNodeId` | 비고 |
|---|---|---|
| 일반 대사 | 다음 노드 ID 입력 | — |
| 마지막 대사 (에피소드 종료) | **비워두기** | — |
| 선택지 노드 | **비워두기** | `#CHOICES`에 선택지 작성 |
| 분기 노드 | 기본값 (조건 불일치 시 이동) | `#NODE_BRANCHES`에 분기 작성 |
| 제조 판정 노드 | **비워두기** | `text`도 비움, 성공/실패 노드 입력 |

```csv
#NODES
nodeId,speakerKey,overrideSpeakerName,text,nextNodeId,requiresCrafting,craftingTicketKey,nextNodeIdGood,nextNodeIdBad,bgmCommand,bgmClipName,craftingFlagGood,craftingFlagBad,craftingVarChangesGood,craftingVarChangesBad,craftingRecipeId
0,f72,???,흘..크흘…,1,false,,,,play,bgm_tension,,,,,
1,shaun,,뭔가 원하는 게 있나요?,2,false,,,,,,,,,
2,f72,,,,true,sc0_f72,3a,3b,,,sc0_good,sc0_bad,sally_affinity+5,sally_affinity-2,vodka_lemon
```

---

### NODE_CHARS — 노드별 표정

각 노드에서 캐릭터의 표정을 지정합니다.  
**표정이 바뀌는 노드마다** 행을 추가해야 합니다. 한 노드에 캐릭터가 여러 명이면 같은 `nodeId`로 행을 여러 개 씁니다.

| 열 | 설명 | 예시 |
|---|---|---|
| `nodeId` | 대상 노드 ID | `0` |
| `characterKey` | 캐릭터 ID | `f72` |
| `expressionKey` | 표정 ID | `smile` |
| `slotIndex` | 슬롯 위치 (보통 `-1` 자동) | `-1` |

```csv
#NODE_CHARS
nodeId,characterKey,expressionKey,slotIndex
0,f72,frust,-1
5,f72,laugh,-1
5,f54,mid,-1
```

---

### CHOICES — 선택지

플레이어가 고를 수 있는 선택지를 작성합니다.  
선택지가 없으면 **헤더 행만 남기고 비워두세요**.

| 열 | 설명 | 예시 |
|---|---|---|
| `nodeId` | 선택지가 속한 노드 ID | `10` |
| `choiceIndex` | 선택지 순서 (0부터 시작) | `0` |
| `buttonText` | 버튼에 표시될 텍스트 | `그래 말해봐` |
| `nextNodeId` | 선택 시 이동할 노드 ID | `11a` |
| `setFlags` | 선택 시 **켤** 플래그. 여러 개는 `\|` 구분 | `flag_agreed` |
| `clearFlags` | 선택 시 **끌** 플래그. 여러 개는 `\|` 구분 | `flag_open` |
| `varChanges` | 선택 시 수치 변수 변경. `+`/`-` 로 증감 | `sally_affinity+5` |

```csv
#CHOICES
nodeId,choiceIndex,buttonText,nextNodeId,setFlags,clearFlags,varChanges
10,0,잘 지내?,11a,flag_talked,,sally_affinity+5
10,1,볼 일 없어,11b,,,sally_affinity-2
```

---

### NODE_BRANCHES — 플래그 분기

특정 플래그 상태에 따라 다른 노드로 이동합니다.  
분기가 없으면 **헤더 행만 남기고 비워두세요**.

| 열 | 설명 | 예시 |
|---|---|---|
| `nodeId` | 분기 확인을 할 노드 ID | `5` |
| `requiredAllFlags` | 이 플래그가 **모두** 켜져 있어야 분기 (AND 조건, `\|` 구분) | `flag_a\|flag_b` |
| `requiredAnyFlags` | 이 플래그 중 **하나라도** 켜져 있으면 분기 (OR 조건, `\|` 구분) | `flag_c\|flag_d` |
| `nextNodeId` | 분기 시 이동할 노드 ID | `5_alt` |

- `requiredAllFlags`와 `requiredAnyFlags` 중 **하나만** 사용합니다. 둘 다 값이 있으면 `requiredAllFlags`(AND)가 우선합니다.
- 한 노드에 여러 조건이 있으면 **행을 여러 개** 씁니다. 위에서 아래 순서로 확인하며 처음 맞는 조건으로 이동합니다.
- 어떤 조건도 해당 없으면 `#NODES`의 `nextNodeId`로 이동합니다.

```csv
#NODE_BRANCHES
nodeId,requiredAllFlags,requiredAnyFlags,nextNodeId
5,flag_took_coin,,5_alt
6,,flag_saw_fight|flag_heard_rumor,6_alt
```

---

### NODE_VAR_BRANCHES — 수치 분기

호감도·평판 등 수치에 따라 다른 노드로 이동합니다.  
분기가 없으면 **헤더 행만 남기고 비워두세요**.

| 열 | 설명 | 예시 |
|---|---|---|
| `nodeId` | 분기 확인을 할 노드 ID | `5` |
| `varName` | 확인할 수치 변수 이름 | `sally_affinity` |
| `op` | 비교 연산자 (`>=` `>` `==` `<` `<=`) | `>=` |
| `threshold` | 비교 기준값 | `10` |
| `nextNodeId` | 조건 충족 시 이동할 노드 ID | `5_high` |

```csv
#NODE_VAR_BRANCHES
nodeId,varName,op,threshold,nextNodeId
5,sally_affinity,>=,10,5_high
5,sally_affinity,>=,5,5_mid
```

> 위 예시: 호감도 10 이상 → `5_high` / 5 이상 → `5_mid` / 미만 → 기본 `nextNodeId`

---

## 5. 흐름 유형별 작성 패턴

### 일반 대화 흐름

가장 기본 형태입니다. 노드가 순서대로 이어집니다.

```
노드 0 → 노드 1 → 노드 2 → (종료)
```

```csv
#NODES
nodeId,speakerKey,...,text,nextNodeId,...
0,f72,,,안녕하세요.,1,false,,,,
1,shaun,,,어서오세요.,2,false,,,,
2,f72,,,뭘 드릴까요?,,false,,,,
```

노드 2의 `nextNodeId`가 비어 있으므로 에피소드가 종료됩니다.

---

### 플레이어 선택지

1. `#NODES`에서 선택지 노드의 `nextNodeId`를 **비워둡니다**.
2. `#CHOICES`에 선택지와 각 분기 노드를 작성합니다.

```
노드 2 (선택지) ─── 선택 A → 노드 3a
                └── 선택 B → 노드 3b
```

```csv
#NODES
2,f72,,,뭘 마시겠어요?,,false,,,,

#CHOICES
2,0,맥주로 주세요,3a,flag_chose_beer,,
2,1,위스키 주세요,3b,flag_chose_whiskey,,
```

---

### 플래그 분기

1. `#NODES`의 해당 노드에 `nextNodeId`로 **기본 이동 노드**를 적습니다.
2. `#NODE_BRANCHES`에 플래그 조건과 대체 이동 노드를 작성합니다.

```
노드 5 ─── flag_took_coin=ON  → 노드 5_alt
        └── 조건 없음         → 노드 6 (기본)
```

```csv
#NODES
5,f72,,,오늘 어땠어요?,6,false,,,,,,,,

#NODE_BRANCHES
nodeId,requiredAllFlags,requiredAnyFlags,nextNodeId
5,flag_took_coin,,5_alt
```

---

### 호감도 분기

`#NODE_VAR_BRANCHES`를 사용합니다. 높은 조건부터 순서대로 작성합니다.

```
노드 10 ─── sally_affinity >= 10 → 노드 10_high
         ├── sally_affinity >= 5  → 노드 10_mid
         └── 조건 없음            → 노드 10_low (기본)
```

```csv
#NODES
10,f72,,,그래서 어떻게 생각해?,10_low,false,,,,

#NODE_VAR_BRANCHES
10,sally_affinity,>=,10,10_high
10,sally_affinity,>=,5,10_mid
```

---

### 제조 판정

칵테일 제조 성공/실패에 따라 대화가 달라지는 노드입니다.

1. 해당 노드의 `text`와 `nextNodeId`는 **비워둡니다**.
2. `requiresCrafting`을 `true`로 설정합니다.
3. `craftingTicketKey`, `craftingRecipeId`, `nextNodeIdGood`, `nextNodeIdBad`를 채웁니다.
4. 제조에서는 영업과 같은 병 재고를 사용합니다. 사용하거나 폐기한 재료는 복구되지 않습니다.
5. 판정이 `Good`이면 성공 분기, `Mid` 또는 `Bad`이면 실패 분기로 이동합니다.

```
노드 5 (제조 판정) ─── 성공 → 노드 5-1-1
                   └── 실패 → 노드 5-2-1
```

```csv
#NODES
5,,,,,true,sc0_f72,5-1-1,5-2-1,,,,,,,vodka_lemon
```

---

## 6. 전체 예시

분기와 선택지를 모두 포함한 짧은 에피소드 예시입니다.

```csv
#META
episodeId,episodeTitle,firstNodeId
Example_0,예시 에피소드,0

#TRIGGER
minDay,requiredFlags,blockedFlags,prerequisiteEpisodeIds,requiredVars
0,,,,

#OPENING_CHARS
characterKey,expressionKey,slotIndex
f72,neutral,-1

#NODES
nodeId,speakerKey,overrideSpeakerName,text,nextNodeId,requiresCrafting,craftingTicketKey,nextNodeIdGood,nextNodeIdBad,bgmCommand,bgmClipName,craftingFlagGood,craftingFlagBad,craftingVarChangesGood,craftingVarChangesBad,craftingRecipeId
0,f72,???,뭘 마시겠어?,1,false,,,,play,bgm_bar,,,,
1,shaun,,추천해줘.,2,false,,,,,,,,
2,f72,,그럼 선택해.,,false,,,,,,,,
3a,f72,,좋은 선택이야.,4,false,,,,,,,,
3b,f72,,그것도 나쁘지 않아.,4,false,,,,,,,,
4,f72,,또 오게.,,false,,,,stop,,,,,

#NODE_CHARS
nodeId,characterKey,expressionKey,slotIndex
0,f72,neutral,-1
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
```

---

## 7. 작업 후 게임 반영 방법

1. CSV 파일을 **UTF-8**로 저장합니다.
2. Unity 에디터 상단 메뉴 → **Tools > Slainte > Import Episode CSV** 클릭
3. **Browse** 버튼으로 CSV 파일 선택
4. **Import** 클릭
5. `Assets/Data/EpisodeData/EpisodeData_{episodeId}.asset` 으로 저장됩니다.

> 같은 `episodeId`의 에셋이 이미 있으면 **덮어씁니다**.

---

## 8. 자주 하는 실수

| 증상 | 원인 | 해결 |
|---|---|---|
| 임포트 후 대사가 안 보임 | `firstNodeId`가 실제 첫 노드 ID와 다름 | `#META`의 `firstNodeId`와 `#NODES`의 첫 `nodeId`를 일치시키기 |
| 선택지 후 대화가 안 이어짐 | 선택지 노드의 `nextNodeId`를 채워둠 | 선택지 노드는 `nextNodeId` **비워두기** |
| 에피소드가 갑자기 끝남 | `nextNodeId`가 비어있거나 존재하지 않는 ID | 모든 `nextNodeId` 확인 |
| 표정이 바뀌지 않음 | `#NODE_CHARS`에 해당 노드 행이 없음 | 표정이 바뀌어야 하는 노드마다 `#NODE_CHARS` 행 추가 |
| 대사 텍스트가 잘림 | 대사에 쉼표가 있는데 따옴표로 안 감쌈 | 해당 셀을 `"큰따옴표"` 로 감싸기 |
| 분기가 동작하지 않음 | `varChanges` 형식 오류 | `varName+숫자` 또는 `varName-숫자` 형식 확인 |
| 에피소드가 발동 안 됨 | `TRIGGER` 조건 미충족 | `requiredFlags`, `minDay`, `prerequisiteEpisodeIds` 재확인 |
| 한글이 깨짐 | 인코딩이 UTF-8이 아님 | 저장 시 인코딩을 **UTF-8** 로 지정 |
