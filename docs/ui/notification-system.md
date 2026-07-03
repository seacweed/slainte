# 알림 시스템

## 개요

게임 중 발생하는 이벤트(호감도 변화 등)를 화면 우상단에 순차 표시합니다. 확장을 고려해 `NotificationManager`가 큐를 중앙 관리하고, 표시 로직은 각 알림 UI가 자체 담당합니다.

## 컴포넌트

### `NotificationManager` (`Assets/Scripts/UI/Notifications/`)
- `MonoSingleton<NotificationManager>`
- `GameProgress.OnAffinityChanged(varName, delta)` 이벤트 구독
- `CharacterDatabase.FindByKey(varName)`으로 `displayName` 조회 — 등록된 캐릭터 key가 아니면 알림 무시
- `Queue<(string displayName, int delta)>` 기반 순차 표시 (코루틴)
- Inspector 필드: `notificationPrefab`, `container`(Transform), `characterDB`

### `AffinityNotificationUI` (`Assets/Scripts/UI/Notifications/`)
- 캐릭터 이름(`TMP_Text`) + 방향 애니메이션(`AnimatedSpriteUI` × 2)
- `Setup(displayName, delta)` — delta 부호에 따라 `upAnim` / `downAnim` 활성화 후 `Play()`
- `PlayAndDestroy(onComplete)` — 슬라이드 인(오른쪽→원위치, smoothstep) + 페이드 인 → 유지 → 페이드 아웃 → `Destroy` + 콜백
- Inspector 필드: `nameText`, `upAnim`, `downAnim`, `slideDistance`, `fadeInDuration`, `holdDuration`, `fadeOutDuration`

### `AnimatedSpriteUI` (`Assets/Scripts/Tools/`)
범용 스프라이트 프레임 애니메이션 컴포넌트. `Image` 컴포넌트 필수.

| 필드 | 설명 |
|---|---|
| `frames` | 재생할 `Sprite[]` 배열 (파일명 번호 순 정렬 권장) |
| `fps` | 초당 프레임 수 |
| `loop` | 반복 여부 |
| `playOnAwake` | Start 시 자동 재생 |

- `Play()` / `Stop()` / `SetFrames(Sprite[], float fps)` API
- 인스턴스마다 독립 코루틴 — 동시 여러 인스턴스 충돌 없음
- `OnDisable` 시 자동 정지

**GIF 사용 방법**: GIF를 프레임별 PNG로 추출(`frame_01.png`, `frame_02.png`, ...) → Unity Import → `frames` 배열에 다중 선택 드래그

## 이벤트 흐름

```
GameProgress.AddAffinity(varName, delta)
  → OnAffinityChanged(varName, delta) 발행
      → NotificationManager.OnAffinityChanged()
          → CharacterDatabase.FindByKey(varName)
          → Queue에 (displayName, delta) 추가
          → 코루틴으로 AffinityNotificationUI 순차 인스턴스화
```

`SetAffinity(varName, value)`는 이전 값과 비교해 delta가 0이 아닐 때만 이벤트 발행.

## 씬 설정

**Canvas 계층**
```
HUD
├── DragGhostImage
└── NotificationContainer  ← 앵커 우상단, NotificationManager.container 할당
```

**AffinityNotificationUI 프리팹 구조**
```
AffinityNotificationUI (CanvasGroup + AffinityNotificationUI)
├── UpAnim   (Image + AnimatedSpriteUI)  playOnAwake=false, loop=true, 초기 비활성
├── DownAnim (Image + AnimatedSpriteUI)  playOnAwake=false, loop=true, 초기 비활성
└── NameText (TMP_Text)
```

## 확장 방법

호감도 외 다른 알림(에피소드 해금, 술 해금, 업적 등) 추가 시:
1. 해당 알림 UI 클래스 신규 작성 (자체 애니메이션 + `PlayAndDestroy` 패턴 유지)
2. `NotificationManager`에 큐 타입 또는 오버로드 메서드 추가
3. 트리거 지점에서 `NotificationManager.Instance` 호출
