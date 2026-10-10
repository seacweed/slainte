# 캐릭터 표시 & 대화 렌더링

## 캐릭터 표시 (`Assets/_Project/Features/Business/Runtime/Presentation/`, `CharacterData`/`CharacterSlotEntry`는 `Runtime/Conversation/Episode/`)

- `CharacterView` — 프리팹 루트에 부착. `Setup(Sprite, Sprite?, blinkSprite?, blinkOverlaySprite?)` + `SwapSprite(Sprite, Sprite?, blinkSprite?, blinkOverlaySprite?)` + `PlayAppearAnimation()` / `PlayDisappearAnimation()` 제공. fade+rise+pop 애니메이션 처리. 슬롯 하단 기준으로 배치.
  - **깜빡임**: 등장 애니메이션 완료 후 자동 시작. 5~15초 랜덤 간격으로 눈 감은 스프라이트로 교체 후 1초 뒤 복원. `blinkSprite`가 null이면 비활성화. `SwapSprite` 호출 시 새 blink 스프라이트로 재시작. 퇴장 애니메이션 시작 시 중단.
  - `sprite`가 null이면 `_image` 컴포넌트를 비활성화 (overlay만 있는 캐릭터에 사용). Inspector에서 `blinkDuration`(기본 1s), `blinkIntervalMin`(5s), `blinkIntervalMax`(15s) 조정 가능.
  - `ApplySlotLayout(slot)` — 슬롯 높이(1440, 화면 전체)를 채우고 `HeightControlsWidth` ARF로 스프라이트 원본 비율 유지하며 너비 자동 결정. 슬롯의 x 위치가 캐릭터 수평 중심.
  - `AttachOverlayToFrontContainer(frontContainer)` — `VisualOverlay` 자식을 `frontContainer`로 reparent(`worldPositionStays: true`). 이후 애니메이션/alpha는 `Visual`과 동기화됨. `OnDestroy` 시 overlay GameObject 자동 정리.
  - `GetVisualWorldBoundsX(out float left, out float right)` — `_visualRT.GetWorldCorners()`로 스프라이트의 실제 화면 공간 X 경계를 반환. `HeightControlsWidth` ARF가 너비를 확정하려면 1프레임이 필요하므로, 생성 직후 호출 시 부정확할 수 있음.
  - 프리팹 자식 구조: `Visual`(바 테이블 뒤) + `VisualOverlay`(바 테이블 앞). overlay sprite가 null이면 `VisualOverlay`는 비활성화됨.
- `CharacterStage` — 슬롯 배열을 관리하고 `CharacterView`를 생성. `_activeViews`(key→view)와 `_activeSlotIndices`(key→슬롯 인덱스)로 현재 스테이지 상태를 추적합니다.
  - `ShowCharacters(IReadOnlyList<CharacterSlotEntry>, onAllShown)` — 신규 캐릭터는 입장 애니메이션, 기존 캐릭터는 스프라이트 교체만 수행. 점유된 슬롯을 추적해 신규 캐릭터는 지정 슬롯 또는 빈 슬롯에 배정합니다.
  - `SwapExpression(characterKey, expressionKey)` — 이미 스테이지에 있는 캐릭터의 표정만 교체.
  - `TryGetSlotIndex(key)` / `TryGetSlotIndexByDisplayName(name)` — 무대에 있는 화자의 슬롯 인덱스. 이름 조회는 키가 없는 기존 손님 대사 에셋용.
  - `TryGetSlotCenterX(index, space)` — 슬롯 사각형 중심 X를 지정한 공간(카메라 리그의 FrontWorld)의 로컬 좌표로 반환. 리그의 확대·이동과 무관한 값이라 캐시해 쓴다.
  - `CustomerSpawner`(영업 씬 손님)와 `EpisodeRunner`(에피소드) 모두 `CharacterStage` 하나를 공유합니다.
  - **슬롯 크기**: 화면은 QHD(2560×1440) 밖으로 나가지 않는다. 1인 슬롯 5개는 510×1440(중심 X 0, ±510, ±1020), 2인 슬롯 4개는 인접한 두 칸의 합 1020×1440(중심 X ±255, ±765).
- `CharacterSlotEntry` — `{ characterKey, expressionKey, slotIndex }` 세 필드. `slotIndex`가 0 이상이면 해당 인덱스 슬롯에 직접 배치, `-1`(기본값)이면 빈 슬롯에 자동 배정. 슬롯 인덱스: 0=Center, 1=Left, 2=Right, 3=Left2, 4=Right2, 5~8=Interaction0~3(통합 스프라이트 전용).
- `CharacterData` — 캐릭터 1명 = 파일 1개. `defaultSprite` + `defaultOverlaySprite` + `List<ExpressionEntry>` (`{ key, sprite, overlaySprite, blinkSprite, blinkOverlaySprite }`)로 모든 표정을 하나의 에셋에 보관.
  - `nameColor` — 대화창 이름 텍스트 색상. Inspector에서 캐릭터별로 지정. `overrideSpeakerName`이 있어도 항상 `speakerKey` 기준 색상이 적용됨.
  - `GetSprite(expressionKey)` — 표정 키로 스프라이트 조회(없으면 defaultSprite 반환).
  - `GetOverlaySprite(expressionKey)` — overlay 스프라이트 조회(없으면 defaultOverlaySprite 반환). overlay가 불필요한 캐릭터는 모든 overlay 필드를 비워두면 됨.
  - `GetBlinkSprite(expressionKey)` / `GetBlinkOverlaySprite(expressionKey)` — 눈 감은 스프라이트 조회. 표정에 지정된 값이 없으면 `defaultBlinkSprite` / `defaultBlinkOverlaySprite` 반환. 둘 다 null이면 해당 표정에서 깜빡임 없음.
  - **주인공은 1인칭 시점이므로 스프라이트 없음.** `CharacterData`는 화자 이름 표시용으로만 사용하고, `EpisodeNode.characters`에는 포함하지 않습니다.

## 대화 렌더링 (`Assets/_Project/Features/Business/Runtime/Conversation/DialogueController.cs`)

TMPro 타이핑 애니메이션. 모든 모드에서 공유하는 단일 컴포넌트입니다:
- `StartDialogue(List<DialogueLine>)` — 손님 대화용 배치 큐 방식
- `ShowSingleLine(speakerName, text, nameColor)` — 에피소드 노드별 단일 출력. `nameColor`는 `CharacterData.nameColor`에서 전달됨
- `ShowSingleLine(DialogueLine)` — 화자 키(`speakerKey`)까지 넘기는 형태. 에피소드와 손님 폴백 주문 대사가 사용
- `SkipTypingIfNeeded()` — 타이핑 스킵 (InputRouter → EncounterRunner → DialogueController 경로). 이전 말풍선이 사라지는 중이면 새 내용이 들어가는 순간 전체 문장을 바로 보여 준다
- `SlideUpForChoices(float amount, Action onComplete)` — 선택지 표시 시 말풍선을 위로 이동 (smoothstep)
- `SlideBackToOrigin(Action onComplete)` — 선택지 해제 후 말풍선 원위치 복귀
- `DialogueClosed` 이벤트 — 대화 종료 시 발행
- `FadeOutAndHide(onComplete)` — 말풍선을 페이드 아웃한 뒤 닫는다(제조 진입 연출용, `DialogueClosed`는 보내지 않음)
- `IsOpen`은 내부 플래그다(페이드 중 alpha가 0이 되는 순간에도 열린 상태 유지)
- Inspector: `bubbleRect` — 배치·슬라이드 대상 RectTransform. `ChoiceContainer`는 `bubbleRect`의 자식으로 배치해야 함 (bubble 이동 시 함께 이동)

### 말풍선 배치 (`DialogueController` + `DialogueFocusPresenter`)

- **대사마다 페이드**: 이전 말풍선 페이드 아웃(`fadeDuration`, 기본 0.12초) → 새 내용·크기·위치 적용 → 페이드 인과 동시에 타이핑. `fadeDuration` 0이면 바로 바뀐다. 첫 줄은 페이드 인만 한다.
- **크기**: 폭 `bubbleWidth`(840) 고정. 높이 = `topPadding`(20) + 이름 줄(80) + 본문 높이 + 다음 표시 줄(80) + `bottomPadding`(20).
  - 본문 높이는 **전체 문장 기준으로 미리** `GetPreferredValues`로 계산해 타이핑 중에 커지지 않는다. 본문 폭 = 840 − 좌우 여백(`textSideMargin` 20)×2.
  - 이름은 왼쪽 정렬(`nameLeftMargin` 30), 다음 표시는 오른쪽 정렬(`indicatorRightMargin` 20)·줄 가운데. 이름·본문·다음 표시의 RectTransform은 런타임에 덮어쓰므로 에디터에서 맞출 필요가 없다.
  - 배경 `Image`는 런타임에 `Sliced`로 바뀐다. 9-slice border는 `textBox` 스프라이트에 설정한다.
- **배치 결정**: `DialogueController`는 화자·카메라를 모르고, 대사 시작 시 `IDialogueBubblePlacementResolver`(`DialogueFocusPresenter`)에게 배치(`DialogueBubblePlacement`)를 묻는다. 배치를 묻는 순간 카메라 이동이 시작되므로, 페이드 아웃 동안 카메라가 먼저 움직이고 새 말풍선은 최종 위치에 나타난다.

| 화자 | 카메라 | 말풍선 |
|---|---|---|
| 무대 위 캐릭터 | 그 슬롯 중심으로 확대·이동 | 이동이 끝난 뒤 화면에서 본 슬롯 중심 X. **윗변 고정**(`characterBubbleTopY`), 아래로 늘어남. 위쪽 가운데 `arrowFront` |
| 주인공(`protagonistKey`, 기본 `shaun`) | 직전 포커스 유지(확대 전이면 그 자리에서 확대) | 화면 가운데. **아랫변 고정**(`protagonistBubbleBottomY`), 위로 늘어남. 아래쪽 가운데 `arrowBack` |
| 무대에 없는 화자·내레이션 | 직전 포커스 유지 | 주인공 위치에 화살표 없이 |

- 말풍선 전체가 패널(QHD) 안에 남도록 중심 X를 제한한다. 맨 끝 슬롯에서는 슬롯 중심과 살짝 어긋날 수 있다.
- **화자 판별 순서**: `DialogueLine.speakerKey`(에피소드는 `node.speakerKey`) → 키가 없으면 표시 이름으로 무대 캐릭터를 찾는다. 주인공도 키가 없으면 주인공 `CharacterData`의 표시 이름으로 판별한다. 그래서 기존 손님 대사 에셋은 다시 임포트하지 않아도 된다.
- 선택지 슬라이드는 이번 줄의 배치 위치(`_basePosition`) 기준으로 오르내린다.

### 카메라 확대 포커스 (`FrontCameraRig`)

- 화면 흔들림 같은 효과를 빼면 카메라는 QHD 밖을 비추지 않는다. 상태는 **확대 여부 + 카메라 중심 X** 두 값이다.
- **확대**: QHD 윗변을 고정한 채 아래쪽 `croppedBottom`(140px)이 잘리는 배율(1440 / 1300 ≈ 1.108). 보이는 폭은 약 2311px이다.
- **중심 X 제한**: 보이는 영역이 FrontWorld(QHD) 밖으로 나가지 않도록 `ClampFocusX`가 제한한다. 현재 배율에서는 가운데 기준 ±124.4px이라, 가운데 슬롯을 뺀 나머지는 대부분 끝까지 붙는다.
- **이동**: 목표가 바뀔 때마다 ease를 처음부터 다시 틀면 이동 중 속도가 0으로 끊기므로, 배율·X 모두 속도를 이어받는 `SmoothDamp`(`smoothTime` 0.25초)로 따라간다. 슬롯 중심 X는 `DialogueFocusPresenter`가 처음 한 번 계산해 캐시한다.
- **API**: `FocusOn(localX)`, `KeepFocus()`, `ResetView(onComplete)`, `ProjectToScreenAtTarget(localX)`(이동이 끝난 뒤 화면 위치 미리 계산), `IsAtFullView`. 바텐딩 세션 일시정지에 쓰는 `MoveStarted/MoveUpdated/MoveCompleted` 이벤트는 유지.
- **위치 공식**: 배율 s, 중심 f일 때 `anchoredPosition = 기준 + (−s·f, top·(1−s))`. 로컬 (f, top)이 화면 (가운데, top)에 온다.
- **축소 시점은 제조 진입뿐이다.** 대사창이 닫히거나, 주문과 주문 사이, 인카운터 종료, 하루 끝 정산 화면으로 넘어갈 때는 확대를 유지한다(다음 화자가 나오면 그쪽으로 이동).
  - 제조 진입 순서: 말풍선 페이드 아웃(`DialogueController.FadeOutAndHide`) → 카메라 축소(`ResetView`) → 제조 모드 전환.
  - `GameModeManager.RequestModeChange(CraftingMode)`는 등록된 `ICraftingEntryTransition`(`DialogueFocusPresenter`)의 연출이 끝난 뒤에 모드를 바꾼다. 등록된 연출이 없으면 카메라 축소만 기다린다. 바텐딩 세션이 생성 시점 화면 배치로 슬롯 줄을 한 번만 정렬하기 때문에 축소 전에 세션을 만들면 안 된다.
  - 연출이 즉시 끝나면(말풍선 없음 + 이미 전체 화면) 같은 프레임에 전환된다. 기다리는 동안 다른 모드 요청이 오면 대기 중인 제조 진입은 취소된다(`IsCraftingRequestPending`).
- 제조 중이거나 제조 진입을 기다리는 동안에는 대사가 나와도 확대하지 않는다.
- 에피소드와 일반 손님 주문·결과 대사 모두 같은 경로를 쓴다.
- 이전의 가로 팬(`PanToWorldCenterX`)·제조 중 가로 보정(`SetHorizontalFixed`)·`GetActiveGroupCenterWorldX`는 제거됐다.

### 씬 세팅

1. `FrontCameraRig`는 캔버스와 같은 2560×1440. 피벗 가운데.
2. 슬롯 크기·위치는 위 "슬롯 크기" 참고.
3. `DialogueFocusPresenter`를 씬의 아무 오브젝트에나 하나 붙인다(참조는 비워 두면 씬에서 찾는다).
4. 말풍선(`DialogueBox`)
   - 배경 `Image` 스프라이트 = `textBox`(9-slice border 설정).
   - 자식 `Image` 두 개를 만들어 `arrowFront`(`textBox_arrow_front`), `arrowBack`(`textBox_arrow_back`)에 연결. 크기는 원본(36×27, Set Native Size). 위치·표시는 코드가 정한다.
   - `characterBubbleTopY`, `protagonistBubbleBottomY`로 세로 위치를 조정한다.
   - `ChoiceContainer`는 말풍선 아랫변 기준 **Pos Y −35**. 주인공 말풍선의 `arrowBack`(높이 27)이 아랫변 밖으로 붙으므로, 그보다 더 내려야 선택지와 겹치지 않는다. 선택지 슬라이드 거리(`EpisodeRunner`)는 이 값과 무관하게 버튼 높이·간격 합만큼이다.
