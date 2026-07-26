# 에피소드 엔진 & 오디오

## 에피소드 (`Assets/Scripts/Conversation/Episode/`)

`EpisodeRunner`가 `EpisodeData` 기반 에피소드를 오케스트레이션합니다:
1. `EpisodeBoardManager`에서 에피소드 선택 → `EpisodeManager.StartEpisode(id)` 호출
2. `GameManager.ChangeState(GameState.Episode)` → `SceneTransitionManager`가 BusinessScene 로드
3. 씬 로드 완료 콜백에서 `EpisodeRunner.Begin(EpisodeData)` 호출
4. `EpisodeRunner`가 `CustomerStage`, `DialogueController`, 선택지 UI를 구동
5. 에피소드 종료 시 `EpisodeManager.ClearEpisode(id)` → `GameManager.ChangeState(GameState.Rest)`

에피소드 조건 체크는 `EpisodeManager.CanStart(EpisodeData, GameProgress)` 에서 처리합니다 (`EpisodeTriggerCondition` 기반).

`EpisodeRunner` 내부에서는 `GameProgress`를 항상 `Progress => GameProgress.Instance` 프로퍼티로 접근합니다 (필드 캐싱 없음).

`EpisodeRunner` 입력 차단 플래그 (모두 `CanReceiveAdvanceInput`에 포함):
- `_waitingForCharacterAnim` — 캐릭터 등장 애니메이션 중 (오프닝 및 노드별)
- `_waitingForChoice` — 선택지 대기 중
- `_isTransitioning` — 선택지 슬라이드 업/다운 및 버튼 페이드 아웃 중
- `IsWaitingForChoice` (public) — `_waitingForChoice || _isTransitioning`. `InputRouter`가 이 값으로 `dialogue.Advance()` 폴스루를 차단함

캐릭터 포커스 pan: `ShowCharacters` 호출 직후 `StartPanCoroutine()`으로 1프레임 지연 코루틴을 시작해 `FrontCameraRig.PanToWorldCenterX(CharacterStage.GetActiveGroupCenterWorldX())`를 호출. 1프레임 지연은 ARF가 너비를 확정한 후 world bounds를 읽기 위함. 에피소드 종료 시 `cameraRig.ResetPan()` 호출.

선택지 표시 흐름: 말풍선 슬라이드 업 완료 후 버튼 생성. 버튼 크기 1500×80, 간격 20px. 선택 시 나머지 버튼은 즉시 투명 처리 후, 선택 버튼만 페이드 아웃(0.35s) → 슬라이드 백 → 다음 노드 진행.

`EpisodeData.openingCharacters: List<CharacterSlotEntry>` — 에피소드 시작 시 표시할 캐릭터+표정.
`EpisodeNode.characters: List<CharacterSlotEntry>` — 해당 노드에서 표시할 캐릭터+표정. 비어 있으면 스테이지 변경 없음.
`EpisodeNode` 제조 관련 필드:
- `requiresCrafting: bool` + `craftingTicketKey: string` + `craftingRecipeId: string` — 공용 주문 세션을 통해 티켓과 레시피를 준비하고 `CraftingMode`로 전환
- `nextNodeIdGood: string` / `nextNodeIdBad: string` — 제조 결과에 따른 분기 노드 (비어 있으면 `nextNodeId` 사용)

`EpisodeNode` 분기 필드:
- `flagBranches: List<NodeFlagBranch>` — 플래그 조건 분기. `{ requiredFlag, nextNodeId }` 목록을 순서대로 확인해 처음 맞는 노드로 이동
- `varBranches: List<NodeVarBranch>` — 수치 변수 조건 분기. `{ VarCondition(varName, op, threshold), nextNodeId }` 목록을 순서대로 확인. `flagBranches` 이후에 평가됨
- 두 조건 모두 맞지 않으면 `nextNodeId`(기본 흐름) 사용

`EpisodeChoice` 필드:
- `setFlags` / `clearFlags` — 선택 시 플래그 변경
- `varChanges: List<VarChange>` — 선택 시 수치 변수 증감. `{ varName, delta }`

`EpisodeTriggerCondition` 필드:
- `requiredVars: List<VarCondition>` — 수치 변수 조건이 모두 충족되어야 에피소드 발동. `VarCondition`은 `varName`, `op`(`CompareOp` 열거형), `threshold` 보유

에피소드 제조는 영업과 동일한 `BusinessOrderSessionController`와 `BusinessBartendingBootstrap`을 사용합니다. 주문 세션이 제조·제출·Good/Mid/Bad 판정·피드백을 처리하고, `EpisodeRunner`는 결과를 받아 `Good`만 성공 분기로, `Mid`와 `Bad`는 실패 분기로 연결합니다. 병 용량 변화는 즉시 `GameProgress` 재고에 반영되며 제조 완료 시 저장됩니다. 폐기한 재료도 복구되지 않습니다.

테이블 슬롯 UI와 실제 충돌 슬롯은 제조 세션이 `CraftingMode`에 들어갈 때 전체 `BarCounter`와 루트 Canvas(실제 Game View)가 화면에서 겹치는 영역, 즉 현재 보이는 테이블 부분의 중앙에 생성됩니다. 이때 레이아웃 오브젝트 자체가 아니라 생성된 슬롯 자식 전체의 경계 중앙을 맞춥니다. 제조를 벗어나면 함께 제거되며, 씬의 `TableSlots`는 크기와 간격 계산용 비활성 템플릿으로만 사용합니다.

에피소드 주문은 영업 보상과 영업 진행도를 적용하지 않습니다. `CraftingJudgeUI`는 레거시 디버그 UI로 런타임에서 비활성화됩니다.

## 오디오 (`Assets/Scripts/Audio/`)

`AudioManager` (`MonoSingleton<AudioManager>`). BGM 크로스페이드를 담당합니다:
- `PlayBgm(string clipName, float fadeDuration)` — `Resources/BGM/{clipName}` 클립을 로드해 재생. 이미 같은 클립이 재생 중이면 무시. 두 `AudioSource`(bgmSourceA/B)를 교대로 사용해 크로스페이드 처리
- `StopBgm(float fadeDuration)` — 현재 재생 중인 BGM을 페이드아웃 후 정지
- Inspector에서 `bgmSourceA` / `bgmSourceB`를 직접 연결하거나, 비워두면 자동 생성

`EpisodeNode` BGM 필드:
- `bgmCommand: BgmCommand` — `None`(변경 없음, 기본값) / `Play`(재생) / `Stop`(정지)
- `bgmClipName: string` — `Play`일 때만 사용. 확장자 없는 파일명 (`Resources/BGM/` 기준)

`EpisodeRunner`가 노드 진입 시 `ApplyBgmCommand()`를 호출해 `AudioManager`에 위임합니다.

BGM 클립은 `Assets/Resources/BGM/` 폴더에 배치해야 합니다.
