# RestScene 시스템

RestScene은 에피소드 선택, 상점, 현황판을 제공하는 휴식 씬입니다.
`BusinessScene` 완료 후 자동 전환되며, 다음 에피소드를 시작하는 분기점입니다.

## 씬 진입/이탈 흐름

```
BusinessScene 종료
  └─ EpisodeManager.ClearEpisode()
       └─ GameManager.ChangeState(GameState.Rest)
            └─ SceneTransitionManager → RestScene 로드

RestScene
  └─ 플레이어가 에피소드 보드에서 에피소드 선택 → 시작 버튼
       └─ EpisodeManager.StartEpisode(id)
            └─ GameManager.ChangeState(GameState.Episode)
                 └─ SceneTransitionManager → BusinessScene 로드
```

## 스크립트 구조

```
BaseUIManager (추상)
  ├─ EpisodeBoardManager   — 에피소드 보드
  ├─ EpisodeUIManager      — 현황판 (돈, 영업 상태)
  └─ ShopUIManager         — 상점

ObjectInteraction           — 씬 오브젝트 클릭 → BaseUIManager 토글
ObjectInteractionBoard      — ObjectInteraction과 동일 (보드 전용)
EpisodePhotoTrigger         — 에피소드 보드 내 개별 사진 슬롯
EpisodeInfoWindow           — 사진 호버/클릭 시 표시되는 상세 정보 팝업
BoardBackground             — 보드 배경 클릭 → 선택 초기화

TooltipManager              — 전역 싱글톤 툴팁 관리
TooltipTrigger              — 개별 UI 요소의 툴팁 트리거
TooltipPopup                — 툴팁 팝업 UI 구현체

ItemData                    — 상점 아이템 ScriptableObject
ItemSlotUI                  — 상점 아이템 슬롯 UI
CategoryButton              — 상점 카테고리 필터 버튼
ExitButton                  — 팝업 닫기 버튼
```

---

## BaseUIManager (`RestScene/Scripts/BaseUIManager.cs`)

모든 UI 패널의 공통 기반 추상 클래스. `CanvasGroup` 필수.

| 메서드 | 설명 |
|---|---|
| `OpenUI()` | `gameObject` 활성화 → `OnOpen()` → `AnimateOpen()` |
| `CloseUI()` | `AnimateClose()` → `OnClose()` |
| `AnimateOpen()` | (추상) 자식이 직접 구현 |
| `AnimateClose()` | (추상) 자식이 직접 구현 |
| `OnOpen()` | (가상) 열릴 때 초기화 — 기본 빈 함수 |
| `OnClose()` | (가상) 닫힐 때 정리 — 기본 빈 함수 |

- Awake에서 `CanvasGroup.alpha = 0`, `interactable = false`, `gameObject.SetActive(false)` 초기화
- 진행 중인 코루틴은 항상 중단 후 재시작 (`_activeCoroutine`)

---

## 에피소드 보드 시스템

### EpisodeBoardManager (`RestScene/Scripts/EpisodeBoardManager.cs`)

`BaseUIManager` 상속. 에피소드 보드 전체를 관리합니다.

**애니메이션**: 아래에서 위로 슬라이드 (`slideDistance = 150f`, `animDuration = 0.3f`)
- `Mathf.SmoothStep` 이징 적용, `Time.unscaledDeltaTime` 사용

**핵심 메서드**

| 메서드 | 설명 |
|---|---|
| `OnOpen()` | `RefreshBoard()` + `ResetBoard()` |
| `RefreshBoard()` | `EpisodeManager.GetBoardEpisodes()`로 표시할 에피소드 목록 확보, 동적 프리팹 생성/제거 |
| `PinEpisode(photo)` | `PinnedPhoto` 설정, `EpisodeManager.CanStart()` 결과에 따라 하단 텍스트/버튼 활성화 |
| `ResetBoard()` | 이전 사진 `Unpin()`, 하단 UI 비활성화 |
| `OnStartButtonClicked()` | `EpisodeManager.StartEpisode(PinnedPhoto.episodeData.episodeId)` → `CloseUI()` |

**RefreshBoard() 상세 흐름**

1. `GetBoardEpisodes()` + `EpisodePhotoTrigger.HasBoardPhoto()` 필터로 표시 대상 목록 확정
2. 기존 자식 `EpisodePhotoTrigger` 순회 — 더 이상 표시 불필요한 항목은 `GameProgress.ClearBoardSlot(id)` 후 `Destroy`
3. `GameProgress.GetBoardSlot(id) - 1`로 이미 자리가 배정된 에피소드 슬롯 예약 (저장값은 1-indexed)
4. 자리 없는 신규 에피소드에 빈 슬롯 무작위 배정 → `GameProgress.SetBoardSlot(id, idx+1)` 저장
5. 프리팹이 이미 존재하면 부모만 올바른 슬롯으로 이동, 없으면 `photoPrefab` 인스턴스 생성

**Inspector 직렬화 필드**

```csharp
public GameObject photoPrefab;        // 에피소드 사진 프리팹
public Transform[] boardSlots;        // 고정된 6개 슬롯 위치
public TextMeshProUGUI bottomEpisodeNameText;
public Button startButton;
public Image startButtonImage;
public Color buttonActiveColor;       // 선택 시
public Color buttonInactiveColor;     // 미선택 시
```

### EpisodeManager — 보드 관련 메서드

| 메서드 | 설명 |
|---|---|
| `GetBoardEpisodes()` | 완료되지 않은 에피소드 중 `IsVisible()` 통과한 목록 반환 |
| `IsVisible(ep, gp)` | `CanStart()` 가 참이거나, `{episodeId}_Discovered` 플래그가 있으면 true |
| `GetAvailableEpisodes()` | `CanStart()` 통과한 에피소드만 반환 (시작 가능 판단용) |

---

### EpisodePhotoTrigger (`RestScene/Scripts/EpisodePhotoTrigger.cs`)

에피소드 보드의 개별 사진 슬롯. `IPointerEnterHandler`, `IPointerExitHandler`, `IPointerClickHandler` 구현.

**상태**
- 기본: 테두리 없음, 오버레이 없음
- 호버: `LineRenderer` 테두리 표시, `EpisodeInfoWindow.Show()` (다른 사진이 핀된 경우 무시)
- 핀(클릭): 테두리 + `yellowOverlay` 활성화, `EpisodeBoardManager.PinEpisode()` 호출
- 언핀(재클릭): `EpisodeBoardManager.ResetBoard()` 호출

**아웃라인 형태** (`DrawOutlineShape`)
- `PolygonCollider2D` 우선, 없으면 `BoxCollider2D` 기반으로 4점 사각형 생성

**`SetEpisodeData(EpisodeData data, EpisodeBoardManager board)`**
- `data.iconNameBoard`로 `Resources.Load<Sprite>("Sprites/{name}")` 시도
- `Image` 컴포넌트 우선, 없으면 `SpriteRenderer` 대체

**`static HasBoardPhoto(EpisodeData ep)`**
- `ep.iconNameBoard`가 있으면 그 이름으로, 없으면 `ep.episodeId.Replace("_", "-")`를 기본 이름으로 사용
- `Resources/Sprites/{baseName}-idle` 스프라이트 로드 성공 여부로 표시 가능 여부 판단
- 보드에 표시하려면 `Resources/Sprites/`에 `{baseName}-idle/hover/selected` 3종 스프라이트 필요

---

### EpisodeInfoWindow (`RestScene/Scripts/EpisodeInfoUI.cs`)

사진 옆에 표시되는 에피소드 상세 정보 팝업. `BaseUIManager` 미상속, 독립 활성화.

| 메서드 | 설명 |
|---|---|
| `Show(data, targetPhoto)` | 제목/설명 표시, 캐릭터 초상화 동적 생성, 위치 계산 |
| `Hide()` | `gameObject.SetActive(false)` |

**위치 계산** (`UpdatePosition`)
- `targetPhoto`의 월드 오른쪽 중앙 기준 오른쪽 10px에 배치
- `LayoutRebuilder.ForceRebuildLayoutImmediate()` 후 위치 갱신

**미결 사항**
- `conditionsText`는 항상 빈 문자열 — `EpisodeData`에 조건 텍스트 필드 추가 후 연결 필요
- 초상화는 현재 `unknownPortrait`(물음표 이미지)로만 표시 — 캐릭터 조우 여부 추적 로직 추후 추가 예정

---

## 현황판 (EpisodeUIManager)

`RestScene/Scripts/EpisodeUIManager.cs`, `BaseUIManager` 상속.

**애니메이션**: `EpisodeBoardManager`와 동일한 슬라이드 업 방식

**기능**
- 금액 표시 (`{money:N0} G`)
- 영업 상태 토글 (영업 중 / 준비 중)
- `OnOpen()` 시 `UpdateDashboard()` 호출

> **주의**: `money`, `isBusinessOpen`이 현재 로컬 임시 변수로 하드코딩됨 — 추후 `GameProgress` 연동 필요

---

## 상점 시스템 (ShopUIManager)

`RestScene/Scripts/ShopUIManager.cs`, `BaseUIManager` 상속.

**애니메이션**: 2단계 펼침 (`animDuration = 0.5f`, 단계당 절반씩)
1. 가로 확장: `(0, lineWidth, 1)` → `(1, lineWidth, 1)`
2. 세로 확장: `(1, lineWidth, 1)` → `(1, 1, 1)`
- 닫을 때는 역순

**화면 구성**
- `categoryPanel`: 카테고리 선택 홈 화면
- `itemScrollPanel`: 필터링된 아이템 목록 (ScrollRect)

**핵심 메서드**

| 메서드 | 설명 |
|---|---|
| `GoHome()` | `categoryPanel` 표시, `itemScrollPanel` 숨김, 검색 초기화 |
| `ShowListByCategory(ItemType)` | 카테고리 필터 적용, `itemScrollPanel` 표시 |
| `OnSearchValueChange(text)` | 비어있으면 `GoHome()`, 아니면 이름 기준 검색 |
| `UpdateList(category?, search)` | 기존 슬롯 Destroy 후 필터 결과로 재생성 |

### ItemData (`RestScene/Scripts/ItemData.cs`)

```csharp
[CreateAssetMenu(menuName = "Shop/Item Data")]
public class ItemData : ScriptableObject
{
    public string itemName;
    public Sprite icon;
    public int price;
    public string desc;
    public ItemType category;
}

public enum ItemType { Alcohol, Liqueur, NonAlcohol, Powder, Tool, Glass }
```

`Resources/ShopItem/` 경로에 배치. 현재 10개 에셋 존재.

---

## 씬 오브젝트 인터랙션

### ObjectInteraction / ObjectInteractionBoard

`BaseUIManager`를 참조하는 씬 오브젝트 클릭 핸들러.
두 클래스는 현재 코드가 동일 — `ObjectInteraction`이 보드에도 적용 가능하므로 향후 통합 가능.

**동작**
- 호버: `highlightOverlay` 활성화
- 클릭: `targetUIManager`가 열려있으면 `CloseUI()`, 닫혀있으면 `OpenUI()`
- UI 열림 상태에서도 `highlightOverlay` 유지, `LineRenderer` 테두리 표시

**아웃라인**: `DrawOutlineShape()`로 `PolygonCollider2D` 또는 `BoxCollider2D` 기반 자동 생성

### BoardBackground (`RestScene/Scripts/BoardBackground.cs`)

에피소드 보드의 빈 배경 클릭 시 `EpisodeBoardManager.ResetBoard()` 호출.

---

## 툴팁 시스템

### TooltipManager (`RestScene/Scripts/TooltipManager.cs`)

`Instance` 싱글톤 (MonoSingleton 미사용, 직접 Awake에서 `Instance = this`).

**핀 시스템**: `PinnedOwner` — 한 번에 하나의 `TooltipTrigger`만 고정 가능
- 고정된 상태에서 다른 트리거의 `ShowTooltip()` / `HideTooltip()` 요청은 무시

| 메서드 | 설명 |
|---|---|
| `ShowTooltip(text, targetRect, owner)` | 툴팁 표시 및 위치 계산 |
| `HideTooltip(owner)` | 호출자가 현재 owner일 때만 숨김 |
| `SetPin(owner, isPinned)` | 고정 권한 설정/해제 |

**위치 계산**: 오른쪽 배치 기본, 화면 밖 넘칠 경우 왼쪽으로 자동 전환.

---

## 미결 사항 요약

| 항목 | 위치 | 설명 |
|---|---|---|
| `conditionsText` 연결 | `EpisodeInfoWindow` | `EpisodeData`에 조건 텍스트 필드 추가 대기 중 |
| 캐릭터 초상화 | `EpisodeInfoWindow` | 조우 여부 추적 로직 추가 후 실제 이미지 표시 예정 |
| 현황판 데이터 | `EpisodeUIManager` | `money`, `isBusinessOpen` → `GameProgress` 연동 필요 |
| `ObjectInteraction` 중복 | `ObjectInteractionBoard` | 두 클래스 코드 동일, 하나로 통합 가능 |
