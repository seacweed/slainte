# 내러티브 그래프 에디터 — 아키텍처

## 개요

에피소드를 노드 그래프로 편집하는 에디터. **CSV와 병행 사용**이 목표다 — 같은 에피소드를 CSV(스프레드시트)로도, 그래프로도 고칠 수 있고, 어느 쪽에서 고쳐도 데이터가 바뀌지 않고 왕복된다.

```
원본 CSV (Content/Source/Episodes)
   ⇅ EpisodeCsvCodec (Read / Write — 포맷 정의는 여기 한 곳)
EpisodeData (Resources/Narrative/Episodes, 런타임)
   ⇅ EpisodeDataImporter (EpisodeData → 그래프) / EpisodeDataCompiler (그래프 → EpisodeData)
NarrativeGraphSO (Content/Graphs)
```

- **그래프 → CSV**: 컴파일하면 EpisodeData 에셋과 **원본 CSV를 직접 덮어쓴다**(`NarrativeCsvSync`).
- **CSV → 그래프**: CSV를 EpisodeData로 임포트한 뒤 그래프를 다시 만든다. 기존 그래프가 있으면 블록 위치를 유지한다.
- **충돌 감지**: 그래프는 마지막으로 읽거나 쓴 시점의 CSV 해시(`LastSyncedCsvHash`)를 기억한다. 그 뒤 CSV가 바깥에서 바뀌었는데 그래프를 컴파일하면 덮어쓰기 / 취소 / CSV에서 그래프 다시 만들기 중 고르게 한다.
- **왕복 검증**: `Narrative > Validate CSV ⇄ Graph Round Trip`이 모든 원본 CSV에 대해 CSV → EpisodeData → 그래프 → EpisodeData를 메모리에서만 수행하고 결과가 같은지 비교한다(`NarrativeRoundTripValidator`).

---

## 파일 구조

```
Assets/_Project/Features/Narrative/
  Runtime/Data/
    NarrativeGraphSO.cs       — 그래프 SO (메타데이터, 노드 목록, 엣지 목록, CSV 동기화 상태)
    EpisodeNodeSO.cs          — 블록 노드 (List<EpisodeEvent>, OutgoingBranches, RouterNodeId)
    TriggerNodeSO.cs          — 조건 라우터 노드 (Flag/Variable/Episode 조건, RuntimeNodeId)
    NodeDataSO.cs / EmptyNodeSO.cs / EdgeData.cs
    EpisodeEvent.cs           — 블록 안의 이벤트 1개 = 런타임 EpisodeNode 1개 (RuntimeNodeId 보존)

  Editor/
    NarrativeBlockModel.cs    — 블록 해석 규칙(실행 순서·포트 라벨)과 분기 라벨 문법(BranchLabel)
    EpisodeDataCompiler.cs    — 그래프 → EpisodeData (Build는 순수 변환, Compile은 저장+CSV 기록)
    EpisodeDataImporter.cs    — EpisodeData → 그래프 (직선 대사 블록 묶기, 위치 유지)
    NarrativeCsvSync.cs       — 원본 CSV 찾기·해시·충돌 질문·CSV → 그래프
    NarrativeRoundTripValidator.cs — CSV ⇄ 그래프 왕복 검증 메뉴
    NarrativeBlockEditing.cs  — 블록 안 이벤트 추가·이동·삭제, 블록 나누기·합치기 (Undo 포함)
    NarrativeGraphEditor.cs / NarrativeGraphView.cs / NarrativeInspectorUI.cs
    NarrativeNodeView.cs      — 블록 카드: 이벤트 줄 목록, 더블클릭 인라인 대사 편집, 접기/펼치기, 우클릭 메뉴
    EventInspectorUI.cs       — 선택한 이벤트 상세 편집(왼쪽 패널), CharacterCatalog(화자·표정 선택)
    NarrativeNodeIdAssigner.cs — 런타임 노드 ID 자동 부여("다음 번호" 규칙: 분기 20 → 21_k_1, 합류 22)
    NarrativeGraphLayout.cs   — 자동 배치(깊이 = 열, 포트 순서 = 행), 임포트와 툴바 "자동 정렬" 공용
    LeftClickPanDragger.cs    — 빈 공간 드래그 = 화면 이동, 클릭 = 선택 해제
    NarrativeSearchWindow.cs / NarrativeUIHelper.cs / TemplateManager.cs

Assets/_Project/Features/Business/Editor/EpisodeCsvCodec.cs — CSV ⇄ EpisodeData (그래프와 임포터 공용)
```

---

## 데이터 모델

### NarrativeGraphSO
```
EpisodeId, EpisodeTitle, ChapterId, StartNodeGuid
ScheduledDay, ScheduledSlot, SlotPriority   — 영업 일정 (CSV #META day/slot/priority)
TriggerCondition                            — 등장 조건 (CSV #TRIGGER)
SettlementRewards                           — CSV #SETTLEMENT_REWARDS
SourceCsvPath, LastSyncedCsvHash            — 원본 CSV 동기화 상태
Nodes, Edges                                — { BaseNodeGuid, TargetNodeGuid, OutputPortIndex }
```

### 블록(EpisodeNodeSO)과 이벤트 — `NarrativeBlockModel`

- **이벤트 하나 = 런타임 노드 하나.** 이벤트의 `RuntimeNodeId`가 곧 CSV의 `nodeId`다. `NarrativeNodeIdAssigner.RegenerateIds`가 그래프를 열 때(`PopulateView`), CSV에서 가져올 때(`EpisodeDataImporter.Import`), 구조를 바꿀 때(이벤트 추가·삭제·이동, 연결, 블록 나누기·합치기), `Compile` 직전에 흐름 전체를 다시 매긴다 — 화면의 ID가 항상 규칙과 같게 유지된다. 결과가 같으면 에셋을 더럽히지 않는다. 순수 변환인 `EpisodeDataImporter.Build`(왕복 검증용)는 CSV ID를 그대로 둔다. 블록 제목은 첫 이벤트의 ID다.
- **실행 순서**: `Events` 리스트 순서(= 카드의 줄 순서). 선택지 · 제조 이벤트는 블록을 끝내며, 그 뒤 이벤트는 실행되지 않고 오류로 표시된다. 예전 시퀀스 에디터(삭제됨)가 연결선(`StartEventGuid`/`NextEventGuids`)으로 저장한 순서는 그래프를 열 때 `MigrateLegacyOrder`가 한 번 리스트 순서로 옮기고 비운다.
- **블록 나누기**: 앞쪽 블록은 Next 하나로 새 블록에 잇고, 기존 출력 포트와 연결은 새(뒤쪽) 블록이 물려받는다. **합치기**는 그 반대이며, 다음 블록으로 들어오는 연결이 하나뿐일 때만 허용된다. 두 조작 모두 컴파일 결과를 바꾸지 않는다.
- **포트**: 블록의 마지막 이벤트가 정한다.

| 마지막 이벤트 | 포트 |
|---|---|
| Choice | 선택지 버튼마다 하나 (자동) |
| BusinessStart (제조) | 결과 6종 `CraftingJobResultPorts.Order` (자동). Good=0, Bad=1은 예전 그래프와의 호환 때문에 고정 |
| Dialogue / 이벤트 없음 | `OutgoingBranches`에 적은 조건 라벨 |

연결 없는 포트는 그 자리에서 에피소드가 끝난다. 예전의 BranchExit/BusinessEnd 이벤트는 enum 값만 직렬화 호환용으로 남아 있고, 새로 만들 수 없으며 컴파일에서 무시된다(검증 경고).

- 대사 필드(화자, 대사, 캐릭터 표정, BGM/SFX)는 선택지와 제조 이벤트에도 있다(선택지 앞 대사, 제조 전 표정 등).

### 분기 라벨 문법 (`BranchLabel`, CSV 값 형식 그대로)

| 라벨 | 컴파일 결과 |
|---|---|
| `Next` / `Default` / 빈칸 | `nextNodeId` |
| `flag == true`, `a&b == true` | `NODE_BRANCHES` `RequiredFlag` (모두 켜짐) |
| `flag == false`, `a&b == false` | `NODE_BRANCHES` `BlockedFlag` (모두 꺼짐) |
| `var >= 5` (`>=` `>` `==` `<` `<=`) | `NODE_BRANCHES` `RequiredVar` |
| 공백 없는 순수 텍스트 (`StrangeCoin_0`) | `NODE_BRANCHES` `PrerequisiteEpisode` (그 에피소드 완료 시) |

`a|b`(하나라도 켜짐)는 지원하지 않는다(해석 오류). 라벨 → `NodeBranch` 변환은 `BranchLabel.ToNodeBranch`, 역변환은 `BranchLabel.Format`. 런타임은 `EpisodeNode.branches`를 목록 순서대로 판정하므로 **포트 순서 = CSV 줄 순서 = 우선순위**다.

### Trigger 노드

조건(Flag / Variable / Episode) 포트들과 Else 포트를 가진 라우터.
- 대사 블록의 `Next` 포트에서 가리키면 그 대사 노드의 분기로 **인라인**된다(별도 노드 없음, 기존 CSV와 같은 모양).
- 선택지 · 제조 · 조건 포트에서 가리키면 **대사 없는 라우터 노드**로 컴파일된다(`RuntimeNodeId` 보존).

이벤트 없는 블록도 조건 포트가 있으면 라우터 노드가 되고(`RouterNodeId`), 없으면 연결을 그대로 통과한다. 런타임 `EpisodeRunner`는 화자 · 대사 · 선택지가 모두 없는 노드를 대화창 없이 즉시 통과한다(라우터끼리 64번 넘게 연달아 이어지면 순환으로 보고 종료).

---

## 컴파일 (`EpisodeDataCompiler`)

0. 번호 재부여 (`NarrativeNodeIdAssigner.RegenerateIds` — `Compile()`에서만, `Build()`는 하지 않음)
1. 메타데이터 복사 (일정·등장 조건·정산 보상은 직렬화 복제)
2. 이벤트 ID 부여 — 기존 ID를 먼저 예약하고, 비었거나 중복된 이벤트에만 새 ID
3. 블록마다 실행 순서대로 런타임 노드를 만들고 안에서 `nextNodeId`로 잇기
4. 블록 마지막 노드를 포트 연결대로 바깥과 잇기 (선택지 / 제조 결과 / 조건 라벨)
5. 시작 블록 → `firstNodeId`, 노드는 대화 흐름(블록) 순으로 정렬 — 분기는 갈래 1을 끝까지, 다음 갈래를 끝까지 쓰고 합류 노드는 모든 갈래 뒤에(`EpisodeGraphTraversal.BuildFlow`, 순환 연결 제외). 도달 불가 노드는 경고 후 뒤에
6. `Compile()`이 원본 CSV 충돌을 확인하고 → 원본 CSV 기록 → EpisodeData 에셋 저장. CSV는 `EpisodeCsvCodec.Write`가 쓰며, 읽을 때 기본값으로 채워지는 칸(`None`, `Money`, 배율 `1`, 제조가 아닌 줄의 제조 칸 등)은 비우고 `craftingOrderType` 컬럼은 쓰지 않는다(런타임 자동 판별). 컬럼 순서는 손으로 쓰던 CSV와 같다

오류(잘못된 라벨, 시작 노드 없음 등)가 있으면 아무것도 저장하지 않는다.

## 임포트 (`EpisodeDataImporter`)

- 분기 · 선택지 · 제조 없이 `nextNodeId` 하나로만 이어지고, 다음 노드를 가리키는 곳이 그 하나뿐인 직선 대사는 한 블록으로 묶는다(예: StrangeCoin_2 249노드 → 35블록).
- 마지막 노드의 분기를 포트 라벨 + 엣지로 만든다(`branches` 목록 순서 → Next, 포트 인덱스와 일치).
- 기존 그래프가 있으면 에셋을 지우지 않고 내용만 교체한다(GUID 유지). 블록 첫 이벤트 nodeId가 같은 블록은 위치를 유지하고, 새 블록만 `NarrativeGraphLayout.Arrange` 규칙으로 배치한다 — 시작 블록으로부터의 BFS 깊이 = 열(열 간격 = 카드 폭 340 + 240), 같은 열은 BFS 발견 순서(포트 순서)대로 카드 높이 + 60씩 아래로 쌓는다. 툴바 **자동 정렬**(`NarrativeGraphView.AutoLayout`)도 같은 규칙을 화면에 그려진 실제 카드 높이로 그래프 전체에 적용한다(Undo 지원).
- 그래프에서 만든 Trigger 노드는 CSV에 분기로 펼쳐져 저장되므로, CSV에서 그래프를 다시 만들면 블록 포트 라벨로 돌아온다(동작은 같음).

## 노드 ID 자동 할당 (`NarrativeNodeIdAssigner`)

그래프를 먼저 컴파일(`Build`)해 실제 런타임 흐름을 얻은 뒤 그 흐름 순서로 번호를 매긴다 — Trigger 인라인, 빈 블록 통과까지 컴파일러와 같게 해석해야 화면의 ID와 CSV의 ID가 어긋나지 않기 때문이다. 이벤트·Trigger 라우터·빈 블록 라우터의 ID를 모두 다시 쓰고, 연결은 GUID 기반이라 따로 고칠 것이 없다.

```
직선: 시작 번호(현재 시작 노드가 정수면 그 값, 아니면 1)부터 +1
분기: 포트가 2개 이상인 노드 20 → 21_1_1, 21_2_1 → 21_1_2 …   ("다음 번호", 갈래 번호 = 포트 순서, 끝나는 포트도 갈래로 셈)
합류: 21_1_k 와 21_2_j 가 만나면 → 22   (가장 깊은 제안부터 오른쪽 두 마디를 떼고 +1, 모두 같아질 때까지)
공유 분기: 모든 진입이 서로 다른 분기 노드의 같은 포트 k면(갈래마다 있는 제조 노드가 결과를 공유)
          → 그 분기 노드들의 합류 번호 + _k_1   (선택지 20 → 21_i_j(제조) → 결과 22_k_1 → 합류 23)
부분 합류: 합류 후보 M이 분기 S의 일부 갈래만 모은 것이면(M을 거치지 않는 S의 갈래가 M 뒤에서 다시 만남)
          → 합류 번호 R에 _g_1 (g = 그 분기의 부분 합류 순번), 나중에 모두 만나는 곳은 R+1
          (StrangeCoin_4: 99 → 100_k_1 → 1·2 합류 101_1_1, 3·4 합류 101_2_1 → 102)
순환: 되돌아오는 연결(포트 순서 DFS의 back edge)은 번호·순서 계산에서 뺌
미도달: x1, x2 …        충돌(손으로 꼰 연결에서만): ~2 접미사
```

번호는 `EpisodeGraphTraversal.BuildFlow`의 흐름 순서(위상 정렬)대로 매기므로 노드를 볼 때 앞선 노드의 새 ID가 모두 정해져 있다. 기존 CSV 중 "같은 번호" 규칙(5 → `5_1_1`)으로 쓴 에피소드는 그래프로 가져오거나 여는 즉시 이 규칙으로 정리되고, CSV 파일은 다음 컴파일 때 바뀐다.

## 검증 (노드 ⚠ 아이콘)

컴파일러와 같은 규칙으로 판정한다: 해석할 수 없는 분기 라벨, 알 수 없는 에피소드 ID 라벨, 실행되지 않는 이벤트, 노드 ID 중복, 연결 안 된 선택지 포트, 레거시 이벤트(분기 탈출 등), Trigger 조건 형식 오류. 같은 슬롯 후보 · 하루 손님 수 초과는 Graph Settings의 일정 안내에 표시된다.

## 남은 사항

- `NarrativeDataBaker`(빌드 시 그래프 → JSON)와 `NarrativeFlow.Runtime.NarrativeManager`는 현재 런타임에서 쓰이지 않는 별도 경로이며 이번 개편 범위 밖이다.
- 화자 키 검증은 하지 않는다(주인공 등 `CharacterDatabase`에 없는 정상 화자가 있어 오탐이 많음).
