# 내러티브 그래프 에디터 사용 가이드

## 에디터 열기

Unity 메뉴 → **Narrative > Open Narrative Graph Editor**

---

## 기본 워크플로우

```
그래프 생성/열기 → 노드 추가 → 연결 → 시퀀스 편집 → 메타데이터 설정 → 컴파일
```

---

## 1. 그래프 생성 및 열기

- **Project 뷰 우클릭 → Create > Narrative > Graph** 로 `NarrativeGraphSO` 에셋 생성
- 에셋을 더블클릭하거나 에디터 창에 드래그하면 그래프가 열림
- 저장 위치 권장: `Assets/Narrative/Graphs/`

---

## 2. 노드 추가

그래프 빈 공간에서 **Space** 또는 **우클릭** → 검색창에서 노드 타입 선택

| 노드 타입 | 용도 |
|---|---|
| **Episode Node** | 대사/선택지/영업 등 실제 이벤트 블록 |
| **Trigger Node** | 플래그·변수 조건에 따른 자동 분기 라우터 |

노드 삭제: 선택 후 **Delete**

---

## 3. 노드 연결

출력 포트(오른쪽 원)를 드래그해 다른 노드의 입력 포트(왼쪽 원)에 연결.  
연결/삭제 시 **노드 ID가 자동 재할당**됩니다.

### 포트 수 조정
노드 우측 인스펙터 패널 → **Outcome Branches** 목록에서 포트 추가/삭제/이름 변경.

> Choice 이벤트가 있는 노드는 선택지 수와 포트 수를 일치시켜야 합니다.

---

## 4. 노드 ID (자동 할당)

노드 제목은 그래프 구조에 따라 자동 부여됩니다. **직접 수정 불필요.**

```
직선:     1 → 2 → 3 → 4
분기:     3에서 2포트 → 3_1_1, 3_2_1 → 3_1_2, 3_2_2 ...
병합:     3_1_k 와 3_2_j 가 같은 노드에 연결 → 4
```

- Start 노드 미설정 시 도달 불가 노드는 `x1`, `x2` ... 로 표시됨

---

## 5. 시퀀스 편집 (Sequence Editor)

**Episode Node를 더블클릭** → Sequence Editor 창 오픈.

### 이벤트 추가
Sequence Editor 빈 공간 **우클릭** → 이벤트 타입 선택 후 배치 → 포트 연결.

### 이벤트 타입별 편집 항목

#### Dialogue (대사)
| 필드 | 설명 |
|---|---|
| Speaker | 화자 키 (CharacterData의 characterKey) |
| Name | 표시 이름 오버라이드 (비워두면 캐릭터 기본 이름) |
| Text | 대사 내용 |
| Character Appearances | 화면에 등장할 캐릭터 목록: Key / 표정 / 슬롯 인덱스(-1=숨김) |
| BGM | Command(Play/Stop/Fade) + Clip 이름 |

#### Choice (선택지)
| 필드 | 설명 |
|---|---|
| Speaker / Text | 선택지 표시 전 캐릭터 대사 |
| Choice Options | 버튼 텍스트, Set/Clear Flags, Var Changes |

각 선택지의 다음 경로는 Sequence 내부 포트가 아닌 **메인 그래프의 출력 포트**로 연결합니다.  
(Outcome Branches 수 = 선택지 수)

#### Business Start (영업 시작)
| 필드 | 설명 |
|---|---|
| Ticket | CraftingTicket 키 |
| Flag (Good/Bad) | 제조 성공/실패 시 설정할 플래그 |
| Var Changes (Good/Bad) | 제조 결과에 따른 변수 변화 |

#### Branch Exit (분기 탈출)
이 이벤트가 실행되면 시퀀스를 중단하고 선택된 출력 포트로 즉시 이동.  
드롭다운에서 `Outcome Branches` 중 하나 선택.

---

## 6. Trigger Node 설정

조건 분기 라우터. **컴파일 시 런타임에 인라인**되어 별도 EpisodeNode를 생성하지 않음.

우측 인스펙터에서 조건 추가:
- **Type**: Flag / Var
- **Key**: 플래그명 또는 변수명
- **Operator / Value**: 비교 연산자와 기준값

포트 순서: 조건0, 조건1, … Else(마지막)

---

## 7. 그래프 메타데이터

아무 노드도 선택하지 않으면 우측 패널에 그래프 설정 표시.

| 항목 | 설명 |
|---|---|
| Episode ID | `EpisodeData`의 `episodeId` — 컴파일 파일명에 사용 |
| Title | 에피소드 표시 제목 |
| Start Node | 에피소드 진입 노드 지정 (ID `1`이 자동 할당됨) |
| Trigger / Opening Chars | **Ping Graph Asset** 버튼으로 Project 뷰에서 직접 Inspector 편집 |

---

## 8. 컴파일

### EpisodeData로 컴파일
- 그래프 설정 패널 하단 **Compile to EpisodeData** 버튼
- 또는 메뉴 **Narrative > Compile Graph**
- 출력: `Assets/Resources/EpisodeData/EpisodeData_{episodeId}.asset`

### CSV 내보내기
메뉴 **Narrative > Export Graph to CSV**  
기존 `EpisodeCsvImporter`로 다시 임포트 가능한 포맷으로 저장.

---

## 9. EpisodeData 역임포트

기존 `EpisodeData` SO를 그래프로 변환할 때 사용.

1. Project 뷰에서 `EpisodeData_xxx.asset` 선택
2. 메뉴 **Narrative > Import EpisodeData to Graph**
3. `Assets/Narrative/Graphs/{episodeId}.asset` 생성

> Choice 노드의 경사 대사(Text)가 빈칸인 경우 임포트 후 자동으로 채워집니다.

---

## 10. 노드 카드 인라인 정보

그래프에서 각 노드 카드에 이벤트 내용이 요약 표시됩니다.

| 색상 | 의미 |
|---|---|
| 파란색 헤더 | DIALOGUE 이벤트 |
| 노란색 헤더 | CHOICE 이벤트 |
| 주황색 헤더 | CRAFTING (BusinessStart/End) 이벤트 |
| 초록색 텍스트 | 캐릭터 등장 정보 (CharacterAppearances) |
| 노란색 소형 텍스트 | 선택지별 플래그/변수 변화 요약 |
| 분홍색 텍스트 | BGM 정보 |

Dialogue → Choice 순서로 이어지는 이벤트는 **하나의 카드**로 합쳐서 표시됩니다.

---

## 자주 쓰는 단축키

| 키 | 동작 |
|---|---|
| Space / 우클릭 | 노드 생성 |
| 더블클릭 | Sequence Editor 열기 |
| Ctrl + 드래그 | 범위 선택 |
| Delete | 선택 요소 삭제 |
| Ctrl + Z | 실행 취소 (노드 이동/생성/삭제/엣지) |
