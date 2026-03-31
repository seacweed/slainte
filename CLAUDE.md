# CLAUDE.md

이 파일은 Claude Code(claude.ai/code)가 이 저장소에서 작업할 때 참고하는 안내 문서입니다.

## 반드시 지켜야 할 점

- 코드 내에 한글 사용 금지
- 후에 다양한 기능이 추가될 수 있으므로 OOP 기반 설계, 확장성 고려한 코드 작성
- 계획부터 말하고 승인 받은 후에 작업 진행
- 최적화를 고려한 코드 작성

## 프로젝트 개요

**Slainte**는 Unity 2023.3.5f2(URP)로 제작 중인 내러티브 바텐딩 게임입니다. 이름은 아일랜드어로 "건배"를 뜻합니다. 에피소드 기반의 비주얼 노벨식 스토리텔링과 드래그-드롭 바텐딩 메커니즘을 결합한 게임입니다.

## Unity 빌드 & 개발

이 프로젝트는 Unity 프로젝트입니다 — CLI 빌드/테스트 명령은 없습니다. 모든 빌드, 실행, 테스트는 **Unity 에디터**에서 진행합니다. IDE 지원을 위해 `slainte.slnx`를 열어 사용하세요.

- Unity 버전: **2023.3.5f2**
- 렌더 파이프라인: **Universal Render Pipeline (URP)**
- 목표 해상도: **2560×1440**

## 아키텍처 개요

게임은 2개의 씬과 4개의 상호 연결된 시스템으로 구성됩니다.

### 씬 구조

| 씬 | 역할 |
|---|---|
| `SellScene.unity` | 바텐딩 메인 플레이 (드래그-드롭, 손님 주문 대응) |
| `EpisodeScene.unity` | 분기 선택지가 있는 스토리 대화 진행 |

### 핵심 시스템

**1. 게임 진행 관리 (`Assets/Scripts/GameProgress.cs`)**
`DontDestroyOnLoad`로 씬 전환 시에도 유지되는 중앙 싱글톤. 다음을 추적합니다:
- 스토리 플래그 (`HasFlag` / `SetFlag` / `ClearFlag`) — 내러티브 분기에 사용
- 에피소드 완료 여부 (`IsEpisodeCompleted` / `MarkEpisodeCompleted`) — 재실행 방지
- 현재 게임 내 일/시간 진행

**2. 에피소드 / 대화 시스템 (`Assets/Scripts/Conversation/`)**
- `EpisodeData` (ScriptableObject): 에피소드 구조를 노드 그래프로 정의. 각 `EpisodeNode`는 화자, 대사, 선택지, 표시 캐릭터, 설정/해제할 플래그를 가짐
- `EpisodeTriggerManager`: `EpisodeTriggerCondition`(minDay, requiredFlags, blockedFlags, prerequisiteEpisodes)을 확인해 에피소드 실행 여부를 결정
- 에피소드가 트리거되면 `EpisodeRuntimeContext`를 통해 에피소드 데이터가 전달되고, `EpisodeScene`이 로드되며, `EpisodeDialogueRunner`가 노드 그래프를 순회
- `CharacterPresenter`: `CharacterDatabase`를 사용해 5개 슬롯(Center, Left, Right, Left2, Right2)에 캐릭터 스프라이트를 배치
- `DialogueController`: TMPro로 타이핑 효과 텍스트를 렌더링. 대사 시퀀스가 끝나면 `DialogueClosed` 이벤트를 발생시킴
- `DialogueSkip`: 전역 입력 핸들러(스페이스/클릭)로, 활성 대화 시스템으로 입력을 라우팅

**3. 드래그-드롭 바텐딩 (`Assets/Scripts/DragandDrop/`)**
- `ItemDef` (ScriptableObject): `ItemType`(Bottle, Glass, Tool)과 `dragMovesObject` 플래그로 아이템을 정의
- `DragManager` 싱글톤: 현재 드래그 상태와 고스트 이미지를 관리
- `UIItemDraggable`: 두 가지 드래그 모드 구현 — **Move**(술병 — 원본이 이동), **Copy**(잔/도구 — 원본은 유지, 복사본이 테이블에 배치)
- `UIDropSlot`: 유효한 드롭 대상. 아이템 타입 규칙을 적용
- `ShelfUI`(술병, Move 모드)와 `DrawerUI`(잔/도구, Copy 모드): `ItemDef` 배열로 아이템을 배치
- `StateManager`: 전면/선반 뷰 전환(A/D 키). `FrontCameraRig`이 카메라 이동을 애니메이션 처리

**4. 손님 주문 & 티켓 시스템 (`Assets/Scripts/Conversation/Sell/`, `Assets/Scripts/OrderTicket/`)**
- `CustomerOrderData` (ScriptableObject): 손님 정의 — 스프라이트, 고유 키, 주문 대사 리스트
- `CustomerSpawner`: 손님 스프라이트를 등장 애니메이션과 함께 표시한 후 대화를 시작
- `DialogueController.DialogueClosed` 이벤트가 발생하면 `OrderTicketManager`가 `OrderTicketUI`를 표시 (하단에서 슬라이드 업. E 키로 토글)
- `OrderTicketData` (ScriptableObject): 손님 이름, 메모, 수량/가격이 포함된 주문 아이템 목록

### 데이터 패턴

모든 컨텐츠는 `Assets/Data/`에 ScriptableObject 데이터베이스로 저장됩니다. 각 데이터베이스는 리스트를 가지며, 런타임에 문자열 키로 항목을 검색합니다. 새 컨텐츠(손님, 에피소드, 캐릭터)를 추가하려면 새 ScriptableObject 에셋을 만들고 해당 데이터베이스 에셋에 등록하세요.

### 주요 설계 패턴

- **DontDestroyOnLoad 싱글톤:** `GameProgress`, `StateManager`, `DragManager`
- **이벤트 기반 대화 흐름:** `DialogueController.DialogueClosed`를 통해 시스템이 연쇄적으로 동작
- **ScriptableObject 데이터베이스:** 모든 게임 컨텐츠를 에디터에서 구성, 하드코딩 없음
- **노드 그래프 에피소드:** `EpisodeData`가 ID로 노드를 저장. `EpisodeChoice.nextNodeId`로 분기 처리
