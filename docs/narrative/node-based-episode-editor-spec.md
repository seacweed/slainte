# 노드 기반 에피소드 에디터 설계서 (v2.0)

## 1. 개요
본 문서는 기존 프로젝트의 런타임 데이터 구조(`EpisodeData` ScriptableObject)와 완벽하게 호환되면서, 시각적인 흐름 제어와 복잡한 이벤트 연출을 직관적으로 관리하기 위한 **하이브리드 노드 에디터**의 사양을 정의한다.

기존 CSV 방식이 데이터의 '대량 입력'에 유리했다면, 본 에디터는 **'논리적 분기 설계'**와 **'상세 연출(영업 시퀀스 포함)'**에 특화되어 있으며, 데이터의 범용성을 위해 **CSV 및 JSON 포맷**으로의 내보내기를 모두 지원한다.

---

## 2. 핵심 설계 개념: 계층형 에디팅 (Hierarchical Editing)

에디터는 두 가지 수준의 작업 영역을 제공하여 복잡도를 관리한다.

### 2.1 메인 흐름 그래프 (Main Flow Graph)
*   **역할**: 에피소드의 전체적인 시나리오 전개와 논리적 분기를 설계한다.
*   **노드 단위**: 하나의 노드는 의미 있는 '장면(Scene)' 또는 '논리적 판정점'을 나타낸다.
*   **노드 종류**:
    *   **Event Block 노드**: 실제 대사나 이벤트가 순차적으로 발생하는 단위.
    *   **Trigger 노드**: 플래그 또는 변수 조건에 따라 경로를 자동으로 분기하는 라우터.
    *   **Start/End 노드**: 에피소드의 진입점과 종료점 정의.

### 2.2 내부 이벤트 시퀀서 (Inner Event Inspector)
*   **역할**: `Event Block` 노드 내부에서 일어나는 개별 액션들을 리스트 형태로 관리한다.
*   **기능**: 노드를 클릭하면 나타나는 전용 창에서 이벤트를 추가/삭제/순서변경 할 수 있다.
*   **관리 대상 이벤트**:
    *   **Dialogue (대사)**: 화자 선택, 이름 오버라이드, 텍스트 입력, 캐릭터 표정 및 위치(Slot) 설정.
    *   **Choice (선택지)**: 플레이어가 선택할 버튼 생성 및 각 선택지별 다음 경로 연결.
    *   **Business Start (영업 시작)**: 제조 모드로 전환. 제조 티켓(Ticket Key)을 지정한다.
    *   **Business End (영업 결과)**: 제조 성공/실패에 따른 후속 흐름 연결.

---

## 3. 노드 상세 사양

### 3.1 Trigger 노드 (Conditional Branch)
대사 없이 논리적 판정만 수행하는 노드이다.
*   **필드**:
    *   **Condition List**: `Type(Flag/Var)`, `Key`, `Operator`, `Value`.
    *   **Output Ports**: 각 조건에 대응하는 출력 포트와 `Else(Default)` 포트.
*   **컴파일**: 이전 노드(또는 블록의 마지막 노드)의 `flagBranches` 및 `varBranches`로 변환된다.

### 3.2 Event Block 노드
내부에 여러 개의 이벤트를 포함할 수 있는 컨테이너이다.
*   **내부 이벤트 관리**:
    *   **Timeline View**: 이벤트들이 실행되는 순서대로 수직 리스트 표시.
    *   **Event Editor**: 선택한 이벤트의 세부 속성(화자, 텍스트, 애니메이션 등) 편집.
*   **영업 시퀀스 통합**:
    *   블록 내에 `Business Start` 이벤트가 포함되면, 해당 지점에서 게임이 중단되고 제조 화면으로 전환된다.
    *   `Business Start` 이벤트는 제조 결과 6종(`CraftingJobResult`: Good/Mid-Ice/Mid-Glass/Mid-Ice+Glass/Mid-WrongMenu/Bad)에 대응하는 포트를 시각적으로 제공한다.

---

## 4. 데이터 컴파일 및 저장 (Mapping & Serialization)

에디터의 시각적 요소는 최종적으로 기존 `EpisodeData` 구조로 압축(Flattening)되어 저장되며, 두 가지 방식의 직렬화를 지원한다.

1.  **노드 자동 분할**: 내부 시퀀서의 이벤트들은 각각 고유한 `nodeId`를 가진 `EpisodeNode`들로 자동 변환된다. (예: `BlockA_1`, `BlockA_2`)
2.  **연결 자동화**: 
    *   시퀀스 내부의 '대사' 사이에는 자동으로 `nextNodeId`가 연결된다.
    *   블록의 마지막 이벤트는 그래프상에서 연결된 다음 노드(또는 트리거)를 가리킨다.
3.  **영업 로직 매핑**:
    *   `Business Start` 지점의 노드는 `requiresCrafting = true` 속성을 가진다.
    *   6개 포트(Good/Bad/Mid-Ice/Mid-Glass/Mid-Ice+Glass/Mid-WrongMenu)에 연결된 경로는 `EpisodeNode.craftingOutcomes`(`List<CraftingOutcome>`, 결과별 `nextNodeId`/`flag`/`varChanges`를 담는 항목) 에 매핑된다. 포트 인덱스는 `CraftingJobResultPorts.Order`(`Good=0, Bad=1, MidIce=2, MidGlass=3, MidIceGlass=4, MidWrongMenu=5`)로 고정.
4.  **멀티 포맷 내보내기**:
    *   **ScriptableObject**: 유니티 엔진 내에서 즉시 사용 가능한 바이너리 에셋.
    *   **CSV**: 기존 워크플로우와의 호환성 및 대량 편집용 포맷.
    *   **JSON**: 외부 툴과의 연동, 버전 관리(Git) 최적화 및 웹 기반 데이터 검토용 포맷.

---

## 5. UI/UX 상세 기획

1.  **작업 영역 분리**:
    *   **왼쪽/중앙**: 노드 그래프 캔버스 (전체 흐름).
    *   **오른쪽**: 시퀀서/인스펙터 패널 (노드 내부 이벤트 상세 편집).
2.  **직관적 연결**:
    *   선택지(`Choice`) 이벤트나 영업 종료(`Business End`) 이벤트는 노드 우측에 동적으로 포트를 생성하여 그래프 상에서 직접 경로를 연결할 수 있게 한다.
3.  **유효성 검사 (Validation)**:
    *   연결되지 않은 포트, 존재하지 않는 화자 키, 루프 오류 등을 실시간으로 감지하여 노드에 경고 아이콘 표시.
4.  **데이터 호환성 (Import/Export)**:
    *   **Import**: 기존 CSV 또는 JSON 파일을 노드 그래프로 시각화.
    *   **Export**: 설계된 내용을 CSV, JSON 또는 유니티 `.asset` 파일로 저장.
    *   **Sync**: 외부 파일 변경 시 에디터에 알림 및 동기화 옵션 제공.
