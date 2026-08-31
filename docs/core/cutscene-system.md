# 컷씬 시스템

게임 흐름 중 특정 시점(오프닝, Day1 종료, 엔딩)에 이미지+텍스트 슬라이드를 순서대로 보여주는 시스템입니다.

## 클래스 (`Assets/_Project/Core/Runtime/Cutscene/`)

- `CutsceneData` — 슬라이드(`Sprite image` + `string text`) 리스트를 담는 ScriptableObject. `cutsceneId`로 식별
- `CutsceneIds` — 컷씬 id 상수(`Today`/`FathersNote`/`Ending`, 값은 각각 `"Cutscene_Today"`/`"Cutscene_FathersNote"`/`"Cutscene_Ending"`)
- `CutsceneUI` — 실제 재생기. CoreScene에 상주하는 풀스크린 오버레이(`SettlementUI`와 동일한 성격)
- `CutsceneManager` (`MonoSingleton<CutsceneManager>`) — `cutsceneId` → `CutsceneData` 조회 후 `CutsceneUI`에 재생 위임

## 슬라이드 재생 흐름 (`CutsceneUI`)

슬라이드 하나: 이미지 페이드인(`imageFadeInDuration`, 기본 1초) → (텍스트 있으면) 한 글자씩 타이핑(`charDelay`, `DialogueController.TypeLine`과 동일하게 `<...>` 리치텍스트 태그는 통째로 붙임) → 대기(`postTypingHoldDuration`, 기본 3초) → 즉시 사라짐(페이드아웃 아님) → 다음 슬라이드.

전체 재생 앞뒤로 `entryHoldDuration`/`exitHoldDuration`(기본 각 0.3초) 동안 완전히 빈 검은 화면 상태로 대기하는 여백이 있음 — 이전/다음 화면과 바로 붙지 않도록 하는 완충 구간. 이 구간에서도 에디터에 남아있던 이미지/텍스트가 보이지 않도록 `ResetSlideVisuals()`로 매번 확실히 비움.

**클릭/스페이스로 스킵**: `CutsceneUI.Update()`가 자체적으로 `Input.GetMouseButtonDown(0) || Input.GetKeyDown(KeyCode.Space)`를 폴링(`InputRouter`/`GameMode`와 무관, `SettlementUI`와 동일 패턴). 스킵 시 진행 중이던 단계를 즉시 중단하고 바로 다음 슬라이드의 페이드인부터 시작(대사 시스템의 "타이핑 스킵→진행" 2단계가 아니라 단일 동작).

## 화면 구조 (씬 배치)

```
TransitionCanvas (Sorting Order 999)
├─ SettlementPanel
├─ FadePanel        (SceneTransitionManager)
└─ CutscenePanel     (CutsceneUI, 맨 마지막 순서 — 항상 최상단 렌더링)
    ├─ Background    (Image, 항상 불투명 — CanvasGroup 영향 밖에 둠)
    └─ SlideGroup     (CanvasGroup — CutsceneUI.slideGroup)
        ├─ SlideImage
        └─ SlideText
```

까만 배경(`Background`)은 `slideGroup`(CanvasGroup) **밖**에 둬야 한다 — 슬라이드가 사라질 때 배경까지 같이 사라지면 안 되기 때문. `slideGroup`은 이미지+텍스트만 감싸며, 배경은 패널이 켜져있는 동안 항상 불투명 유지.

## 화면 전환과의 동기화: 감춤 타이밍

컷씬이 끝나도 `CutsceneUI`는 자기 자신을 바로 `SetActive(false)`로 감추지 않는다. `onComplete` 콜백(보통 `SceneTransitionManager`의 새 페이드아웃을 시작시킴)이 화면을 완전히 덮기까지는 시간이 걸리므로, 컷씬 패널을 먼저 감추면 그 틈에 뒷 화면이 잠깐 노출된다.

대신 `CutsceneUI.HideImmediate()`(재생 중이 아닐 때만 동작)를 새 화면이 **완전히 화면을 덮은 시점**에 호출한다. 이 시점은 `SceneTransitionManager.TransitionToSubScene(scene, onComplete, onFadeOutComplete)`의 `onFadeOutComplete` — `SettlementManager`가 정산 UI(셔터/모니터)를 감출 때 이미 쓰던 것과 동일한 콜백이다.

`GameManager.HandleSceneTransition()`이 **모든** 씬 전환의 `onFadeOutComplete`에서 공통으로 `CutsceneManager.Instance?.HideImmediate()`를 호출하도록 되어 있음(컷씬이 재생 중이 아니었으면 안전하게 무시됨). `SettlementManager`가 엔딩 컷씬 후 `MainMenuScene`으로 직접 전환하는 지점(아래 참고)에도 동일하게 `onFadeOutComplete`를 넘김.

## 트리거 지점 3곳

1. **오프닝**(`CutsceneIds.Today`): `MainMenuManager.OnStartClicked()`가 `CutsceneManager.Play(CutsceneIds.Today, () => DayFlowController.Instance?.StartFirstDay())` 호출. `StartFirstDay()`는 Day를 증가시키지 않고(이미 1) `StartBusinessDay()`와 동일한 필수 에피소드 큐 로직(`BeginMandatoryOrBusiness()`)을 태움.
2. **Day1 종료**(`CutsceneIds.FathersNote`): `DayFlowController.StartFirstDay()`가 `_pendingSettlementCutsceneId = CutsceneIds.FathersNote`를 세팅. 이후 정산이 끝나면(`SettlementManager.OnSettlementClosed()`) 이 값을 `ConsumePendingSettlementCutscene()`로 가져가 재생 후 `GameState.Rest`로 전환.
3. **엔딩**(`CutsceneIds.Ending`): `DayFlowController.StartDefaultEpisode(episodeId)`에서 `episodeId`가 Inspector에 설정된 `endingEpisodeId`(엔딩 에피소드의 `episodeId`, 데이터 필드가 아니라 문자열 직접 비교 — `EpisodeData` 스키마는 건드리지 않음)와 일치하면 `_pendingSettlementCutsceneId = CutsceneIds.Ending`을 세팅. 정산 종료 후 `SettlementManager.OnSettlementClosed()`가 이 값을 확인해 컷씬 재생 후 `GameState.Rest`가 아니라 `SceneTransitionManager.Instance?.TransitionToSubScene("MainMenuScene")`로 직접 전환(엔딩이라 로고 인트로가 다시 재생됨).

`SettlementManager.OnSettlementClosed()`는 2번/3번 트리거가 공통으로 거치는 유일한 지점이며, `DayFlowController.ConsumePendingSettlementCutscene()`가 반환한 id로 어떤 컷씬을 재생할지(또는 아예 재생 안 할지) 결정한다.

## 데이터 작성

`Create → Slainte → Cutscene Data`로 에셋 생성, `Cutscene Id`를 `CutsceneIds` 상수값과 정확히 일치시켜야 함. `CutsceneManager.cutscenes` 리스트에 등록.
