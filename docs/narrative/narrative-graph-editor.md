# 내러티브 그래프 에디터 — 아키텍처

## 개요

CSV 기반 에피소드 편집을 대체/보완하는 비주얼 노드 에디터.  
`NarrativeGraphSO`(그래프 전용 SO) → `EpisodeDataCompiler` → `EpisodeData`(런타임) 파이프라인.  
CSV와 양방향 호환 유지.

---

## 파일 구조

```
Assets/
  Scripts/Narrative/Data/
    NarrativeGraphSO.cs       — 그래프 SO (노드 목록, 엣지 목록, 메타데이터)
    EpisodeNodeSO.cs          — Event Block 노드 (List<EpisodeEvent>, OutgoingBranches)
    TriggerNodeSO.cs          — 조건 라우터 노드 (List<GraphTriggerCondition>)
    NodeDataSO.cs             — 공통 베이스 (Guid, Position, CustomFields)
    EpisodeEvent.cs           — 시퀀스 이벤트 단위 (Dialogue/Choice/BusinessStart/…)

  Editor/Narrative/
    NarrativeGraphEditor.cs       — EditorWindow 진입점
    NarrativeGraphView.cs         — 메인 그래프 GraphView (노드 CRUD, 엣지 관리)
    NarrativeNodeView.cs          — 노드 카드 렌더링 (인라인 이벤트 요약 표시)
    NarrativeInspectorUI.cs       — 우측 인스펙터 (노드 선택 시 / 미선택 시 그래프 메타)
    NarrativeUIHelper.cs          — 공용 UI 유틸리티
    NarrativeSearchWindow.cs      — 노드 생성 검색창
    EpisodeSequenceEditor.cs      — 시퀀스 에디터 EditorWindow (이중 클릭 시 오픈)
    SequenceGraphView.cs          — 시퀀스 내부 GraphView
    SequenceNodeView.cs           — 시퀀스 노드 카드
    SequenceInspectorUI.cs        — 시퀀스 인스펙터 (각 이벤트 필드 편집)
    EpisodeDataCompiler.cs        — NarrativeGraphSO → EpisodeData 컴파일
    EpisodeDataImporter.cs        — EpisodeData → NarrativeGraphSO 역임포트
    NarrativeNodeIdAssigner.cs    — 그래프 구조 기반 노드 ID 자동 할당
    TemplateManager.cs            — 노드 템플릿 저장/로드
```

---

## 데이터 모델

### NarrativeGraphSO
```
EpisodeId, EpisodeTitle, StartNodeGuid    — 에피소드 식별 메타
ChapterId                                — 소속 챕터 ID (ChapterData.chapterId와 매칭)
EpisodeType, MandatorySlot                — Default/Mandatory 구분, Mandatory인 경우 Before/AfterBusiness
TriggerCondition: EpisodeTriggerCondition — 해금 조건 (작전판 노출 여부, requiredCustomerAppearances 포함)
PlayCondition: EpisodeTriggerCondition    — 플레이 조건 (Play 버튼 활성화 여부, 없으면 해금 시 바로 플레이 가능)
OpeningCharacters: List<CharacterSlotEntry>
Nodes: List<NodeDataSO>                  — 서브에셋으로 AddObjectToAsset
Edges: List<EdgeData>                    — { BaseNodeGuid, TargetNodeGuid, OutputPortIndex }
```

저장 경로: `Assets/Narrative/Graphs/{episodeId}.asset`

### EpisodeNodeSO (Event Block)
```
Events: List<EpisodeEvent>   — 순차 실행될 이벤트 목록
OutgoingBranches: List<string>  — 출력 포트 레이블 (포트 인덱스 = 리스트 인덱스)
```

### EpisodeEvent 타입별 사용 필드

| Type | 주요 필드 |
|---|---|
| Dialogue | SpeakerKey, OverrideSpeakerName, Text, CharacterAppearances, BgmCommand/BgmClipName, SfxCommand/SfxClipName |
| Choice | SpeakerKey, Text (선택지 전 대사), Choices[].ButtonText/SetFlags/ClearFlags/VarChanges |
| BusinessStart | CraftingTicketKey, `CraftingOutcomes: List<CraftingOutcomeData>`(결과별 Flag/VarChanges, `Get/SetCraftingFlag(result)` 등 접근자로 조회) |
| BusinessEnd | (포트만 사용) |
| BranchExit | ExitBranchName |

### TriggerNodeSO (조건 라우터)
```
Conditions: List<GraphTriggerCondition>  — { Type, Key, Operator, Value }
```
포트: 조건 0,1,… + Else(마지막). 컴파일 시 런타임 노드에 인라인되어 별도 EpisodeNode 미생성.

---

## 컴파일 파이프라인 (EpisodeDataCompiler)

`Narrative > Compile Graph` 또는 Graph Settings 패널의 버튼.

1. `graph.StartNodeGuid` 기준 BFS 순회
2. `EpisodeNodeSO` → `EpisodeNode` 변환 (`BuildRuntimeNode`), `ChapterId`/`EpisodeType`/`MandatorySlot`은 그래프 메타에서 그대로 복사
3. `TriggerNodeSO` 만나면 `InjectTriggerLogic` 호출 → 업스트림 노드의 flagBranches/varBranches에 인라인
4. 출력 엣지 포트별로 `nextNodeId / choices[].nextNodeId` 또는 (제조 노드의 경우) `craftingOutcomes[result].nextNodeId` 연결. 일반 분기 포트는 라벨을 파싱해 `flagBranches`(`key == true`) → `episodeBranches`(연산자 없는 순수 텍스트, 예: `StrangeCoin_0`) → `varBranches`(`var op threshold`) 순으로 판별. 제조(BusinessStart) 노드는 `CraftingJobResultPorts.Order`(`Good=0, Bad=1, MidIce=2, MidGlass=3, MidIceGlass=4, MidWrongMenu=5`)로 포트 인덱스가 고정 — Good/Bad를 0/1에 유지한 건 기존 그래프 에셋에 저장된 edge와의 호환성 때문
5. `Assets/Resources/EpisodeData/EpisodeData_{id}.asset` 저장 (기존 파일은 CopySerialized로 덮어쓰기)
6. CSV export: `ExportToCsv()` — `EpisodeCsvImporter`와 동일한 포맷 + 전체 섹션 (`NODE_EPISODE_BRANCHES` 포함)

### 런타임 nodeId 결정
컴파일 시 BFS 방문 순서대로 `{episodeId}_n{1,2,3,...}` 자동 부여.  
그래프의 "Title" 필드(노드 카드 표시명)와는 별개.

---

## 역임포트 (EpisodeDataImporter)

`Narrative > Import EpisodeData to Graph`  
선택된 `EpisodeData` SO를 `NarrativeGraphSO`로 변환.

- BFS 순회 → EpisodeNodeSO 1개/런타임 노드
- `ChapterId`/`EpisodeType`/`MandatorySlot`을 그래프 메타로 복사
- Choice 노드: SpeakerKey, Text, Choices 모두 복사
- 엣지: flagBranches/episodeBranches/varBranches → OutgoingBranches 레이블 (`{flag} == true`, `{requiredCompletedEpisodeId}`(그대로), `{var} >= {threshold}`) 자동 생성
- Grid 배치: 5열 × 320px, 220px 간격

---

## 노드 ID 자동 할당 (NarrativeNodeIdAssigner)

노드/엣지 추가·삭제·연결 시 `OnGraphViewChanged`에서 자동 트리거.  
결과는 `CustomFields["Title"]`에 저장되어 노드 카드 제목으로 표시됨.

### 명명 규칙
```
직선 흐름:    1 → 2 → 3 → 4
분기 (N포트): 3에서 분기 → 3_1_1, 3_2_1
              이어지면:   3_1_2, 3_1_3 / 3_2_2, 3_2_3
병합:         3_1_2와 3_2_4가 합쳐지면 → 4
              (각 pathId 오른쪽 _ 2개 제거 후 increment)
중첩 분기:    3_1_2에서 재분기 → 3_1_2_1_1, 3_1_2_2_1
              병합 → 3_1_3
Start 미도달: x1, x2, …
```

EpisodeNodeSO 간 직접 엣지만 추적 (TriggerNodeSO 경유 엣지 제외).

---

## 그래프 뷰 주요 동작

| 동작 | 결과 |
|---|---|
| Space / 우클릭 | 노드 생성 검색창 |
| 노드 더블클릭 | Sequence Editor 오픈 |
| Ctrl+드래그 | 범위 선택 |
| 노드/엣지 선택 후 Delete | 삭제 (엣지 데이터 자동 정리) |
| 엣지 연결/삭제 | ID 자동 재할당 |

---

## CSV 호환성

`EpisodeDataCompiler.ExportToCsv()` 출력 포맷이 `EpisodeCsvImporter`의 파싱 포맷과 동일.  
그래프 편집 → 컴파일 → 런타임 SO 생성 경로와 CSV → 임포트 → 런타임 SO 생성 경로는 동일한 `EpisodeData`를 생성.
