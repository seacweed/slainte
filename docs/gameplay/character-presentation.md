# 캐릭터 표시 & 대화 렌더링

## 캐릭터 표시 (`Assets/_Project/Features/Business/Runtime/Presentation/`)

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
  - `GetActiveGroupCenterWorldX()` — 활성 캐릭터 전체의 스프라이트 좌우 끝 X값(world space) 평균을 반환. 캐릭터가 없으면 `Screen.width * 0.5f` 반환.
  - `CustomerSpawner`(영업 씬 손님)와 `EpisodeRunner`(에피소드) 모두 `CustomerStage` 하나를 공유합니다. 슬롯 5개.
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
- `SkipTypingIfNeeded()` — 타이핑 스킵 (InputRouter → EncounterRunner → DialogueController 경로)
- `SlideUpForChoices(float amount, Action onComplete)` — 선택지 표시 시 말풍선을 위로 이동 (smoothstep)
- `SlideBackToOrigin(Action onComplete)` — 선택지 해제 후 말풍선 원위치 복귀
- `DialogueClosed` 이벤트 — 대화 종료 시 발행
- Inspector: `bubbleRect` — 슬라이드 애니메이션 대상 RectTransform. `ChoiceContainer`는 `bubbleRect`의 자식으로 배치해야 함 (bubble 이동 시 함께 이동)
