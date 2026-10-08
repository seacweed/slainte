# 에피소드 엔진 & 오디오

## 에피소드 (`Assets/_Project/Features/Business/Runtime/Conversation/Episode/`)

`EpisodeRunner`가 `EpisodeData` 기반 에피소드를 오케스트레이션합니다. 모든 에피소드는 영업 손님 슬롯에 배정된 인카운터로만 실행됩니다:
1. `BusinessShiftController`가 슬롯 차례에 배정 후보 중 등장 조건(`triggerCondition`)을 만족하는 에피소드를 고름(`DayScheduleResolver`, 하루 흐름은 [game-flow-design.md](../core/game-flow-design.md))
2. `EpisodeManager.TryStartBusinessEncounter(episode, onCompleted)` → `EpisodeRunner.BeginBusinessEncounter()`
3. `EpisodeRunner`가 `CharacterStage`, `DialogueController`, 선택지 UI를 구동
4. 종료 시 러너는 `OrderMode`로 되돌리고 콜백 호출 → `EpisodeManager`가 `ClearEpisode(id)`(완료 기록) → `DayFlowController.NotifyEpisodeCompleted(id)` → 영업 컨트롤러가 다음 슬롯 진행

`EpisodeRunner` 내부에서는 `GameProgress`를 항상 `Progress => GameProgress.Instance` 프로퍼티로 접근합니다 (필드 캐싱 없음).

`EpisodeRunner` 입력 차단 플래그 (모두 `CanReceiveAdvanceInput`에 포함):
- `_waitingForCharacterAnim` — 캐릭터 등장·퇴장 애니메이션 중 (시작 시 무대 비우기 및 노드별)
- `_waitingForChoice` — 선택지 대기 중
- `_isTransitioning` — 선택지 슬라이드 업/다운 및 버튼 페이드 아웃 중
- `IsWaitingForChoice` (public) — `_waitingForChoice || _isTransitioning`. `InputRouter`가 이 값으로 `dialogue.Advance()` 폴스루를 차단함

캐릭터 포커스 pan: `ShowCharacters` 호출 직후 `StartPanCoroutine()`으로 1프레임 지연 코루틴을 시작해 `FrontCameraRig.PanToWorldCenterX(CharacterStage.GetActiveGroupCenterWorldX())`를 호출. 1프레임 지연은 ARF가 너비를 확정한 후 world bounds를 읽기 위함. 에피소드 종료 시 `cameraRig.ResetPan()` 호출.

선택지 표시 흐름: 말풍선 슬라이드 업 완료 후 버튼 생성. 버튼 크기 1500×80, 간격 20px. 선택 시 나머지 버튼은 즉시 투명 처리 후, 선택 버튼만 페이드 아웃(0.35s) → 슬라이드 백 → 다음 노드 진행.

에피소드 시작 시 무대는 비워지고(`ShowCharacters(null)`), 등장 캐릭터는 첫 노드의 `characters`가 정한다(오프닝 캐릭터 섹션은 폐지).
`EpisodeNode.characters: List<CharacterSlotEntry>` — 해당 노드에서 표시할 캐릭터+표정. 비어 있으면 스테이지 변경 없음.
`EpisodeNode` 제조 관련 필드:
- `requiresCrafting: bool` + `craftingTicketKey: string` — 노드 진입 시 `CraftingMode`로 전환
- `craftingOrderTarget: string` — 레시피 ID 또는 맛/분위기 태그(둘 중 뭔지는 `EpisodeCraftingBridge.ResolveOrderType()`이 `TasteMoodTagPaletteDef`에 대조해 자동 판별, 별도 타입 컬럼 없음)
- `craftingPaymentEnabled` / `craftingPaymentCurrency` / `craftingPaymentMultiplier` — 제조 완료 시 가격 지급 여부/통화/배율. CSV 작성법은 [episode-csv-guide.md](episode-csv-guide.md#nodes) 참고
- `craftingOutcomes: List<CraftingOutcome>` — 제조 결과(`CraftingJobResult`: `Good` / `MidIce` / `MidGlass` / `MidIceGlass` / `MidWrongMenu` / `Bad`)별 분기 노드/플래그/변수 변경(결과가 있는 것만 항목으로 존재, sparse). `EpisodeNode.GetNextNodeId(result)` / `GetCraftingFlag(result)` / `GetCraftingVarChanges(result)` 접근자로 조회(비어 있으면 `nextNodeId` 사용)

`EpisodeNode` 분기 필드:
- `branches: List<NodeBranch>` — 조건 분기. `{ conditionType(RequiredFlag / BlockedFlag / RequiredVar / PrerequisiteEpisode), flags, varCondition, episodeId, nextNodeId }`. `EpisodeRunner.ResolveNextNodeId`가 목록을 **순서대로** `NodeBranch.IsSatisfied(GameProgress)`로 확인해 처음 맞는 노드로 이동(목록 순서 = CSV 줄 순서 = 그래프 포트 순서)
- 하나도 맞지 않으면 `nextNodeId`(기본 흐름) 사용

`EpisodeChoice` 필드:
- `setFlags` / `clearFlags` — 선택 시 플래그 변경
- `varChanges: List<VarChange>` — 선택 시 수치 변수 증감. `{ varName, delta }`

`EpisodeTriggerCondition`(등장 조건) 필드: `minDay`, `minMoney`, `requiredFlags`, `blockedFlags`, `prerequisiteEpisodeIds`, `requiredVars`(`VarCondition`: `varName`, `op`, `threshold`), `requiredCustomerAppearances`. 모두 AND이며 `ProgressConditionEvaluator.IsMet()`이 판정합니다.

제조 완료는 `EpisodeRunner.NotifyCraftingCompleted(CraftingJobResult result)` 호출로 처리합니다. `node.craftingOrderTarget`이 있으면 `EpisodeCraftingBridge`가 영업과 동일한 자동 판정 세션(`BusinessOrderSessionController`)을 실행해 결과를 `CraftingJobResult`로 변환하고, 기술적 초기화 실패 시에만 `CraftingJudgeUI`의 6개 버튼(Good/Mid-Ice/Mid-Glass/Mid-Ice+Glass/Mid-WrongMenu/Bad) 수동 판정으로 폴백합니다(`CraftingJudgeUI`는 `EpisodeRunner.IsUsingManualCrafting`이 true이고 `CraftingMode`일 때만 노출되는 레거시 디버그 패널 — `craftingOrderTarget`이 팔레트에 없는 오타 태그라 레시피 조회에 실패하는 경우도 이 폴백을 타므로, 의도치 않게 이 패널이 뜬다면 태그 오타부터 의심할 것). `craftingOrderTarget`이 없는 노드는 처음부터 수동 판정 경로만 사용합니다. 자세한 매핑 규칙은 [business-interactions.md](../gameplay/business-interactions.md#에피소드-제조-노드-판정-episodecraftingbridge) 참고.

`EpisodeData.settlementRewards: List<EpisodeSettlementReward>` — `{ requiredFlag, label, amount }` 목록. `EpisodeRunner`가 에피소드 정상 종료 시 각 항목의 `requiredFlag`가 서 있는지 확인해, 켜져 있으면 `GameProgress.AddSettlementReward(label, amount)`로 그날 정산 화면에 커스텀 보상 줄을 추가합니다(정산 시점 지급). CSV 작성법은 [episode-csv-guide.md](episode-csv-guide.md#settlement_rewards) 참고.

## 오디오 (`Assets/_Project/Core/Runtime/Audio/`)

`AudioManager` (`MonoSingleton<AudioManager>`). BGM 크로스페이드를 담당합니다:
- `PlayBgm(string clipName, float fadeDuration)` — `Resources/Core/Audio/BGM/{clipName}` 클립을 로드해 재생. 이미 같은 클립이 재생 중이면 무시. 두 `AudioSource`(bgmSourceA/B)를 교대로 사용해 크로스페이드 처리
- `StopBgm(float fadeDuration)` — 현재 재생 중인 BGM을 페이드아웃 후 정지
- Inspector에서 `bgmSourceA` / `bgmSourceB`를 직접 연결하거나, 비워두면 자동 생성

`EpisodeNode` BGM 필드:
- `bgmCommand: BgmCommand` — `None`(변경 없음, 기본값) / `Play`(재생) / `Stop`(정지)
- `bgmClipName: string` — `Play`일 때만 사용. 확장자 없는 파일명 (`Resources/Core/Audio/BGM/` 기준)

`EpisodeRunner`가 노드 진입 시 `ApplyBgmCommand()`를 호출해 `AudioManager`에 위임합니다.

BGM 클립은 `Assets/Resources/Core/Audio/BGM/` 폴더에 배치해야 합니다.

### SFX

BGM과 완전히 분리된 채널이다 — BGM은 무한 반복 + 크로스페이드(교체 시 이전 곡이 페이드아웃되며 끊김), SFX는 원샷 재생(반복 없음, 여러 개 겹쳐도 서로 안 끊김, BGM을 전혀 건드리지 않음). SFX를 재생하려고 `bgmCommand`를 같이 쓰면 안 됨 — BGM 크로스페이드가 발동해 기존 BGM이 멈추고 그 클립이 무한 반복되어 버림.

`AudioManager`:
- `PlaySfx(string clipName, float volume = 1f)` — `Resources/Core/Audio/SFX/{clipName}` 클립을 로드해 BGM과 별도인 전용 `AudioSource`(`sfxSource`)에서 `PlayOneShot`으로 재생
- `sfxSource`는 `bgmSourceA`/`B`와 마찬가지로 Inspector에서 직접 연결하거나 비워두면 자동 생성(단, `loop = false`로 생성됨)

`EpisodeNode` SFX 필드:
- `sfxCommand: SfxCommand` — `None`(기본값) / `Play`. `Stop`은 없음(원샷은 알아서 끝남)
- `sfxClipName: string` — `Play`일 때만 사용. 확장자 없는 파일명(`Resources/Core/Audio/SFX/` 기준)

`EpisodeRunner`가 노드 진입 시 `ApplyBgmCommand()`와 같은 자리에서 `ApplySfxCommand()`도 호출.

SFX 클립은 `Assets/Resources/Core/Audio/SFX/` 폴더에 배치해야 합니다(BGM 폴더와 별도).

CSV/노드 그래프 에디터에서의 작성법은 [episode-csv-guide.md](episode-csv-guide.md#nodes), [narrative-graph-editor.md](narrative-graph-editor.md) 참고.
