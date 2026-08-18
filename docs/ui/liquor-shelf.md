# 술장 시스템 (LiquorShelf)

## 개요

화면 우측에서 슬라이드로 열리는 카테고리형 술 진열장 UI. R키로 토글, 카테고리 버튼 클릭으로 해당 카테고리 열기.

## 스크립트 구성

| 스크립트 | 위치 | 역할 |
|---|---|---|
| `LiquorShelfUI` | ShelfPanel | 메인 컨트롤러 — 슬라이드, 카테고리 전환, EpisodeMode 대응 |
| `LiquorCategoryButtonUI` | 카테고리 버튼 프리팹 (술장/상점 공용) | 클릭 시 `LiquorShelfUI.OpenCategory()`(술장) 또는 `ShopUIManager.ShowListByCategory()`(상점) 호출. `colorImage`(선택 필드)가 연결된 프리팹 인스턴스에서만 카테고리 고유색으로 틴트 — 술장 프리팹은 비워두면 색이 적용되지 않음 |
| `LiquorBottleSlotUI` | 개별 슬롯 오브젝트 | 해금 플래그 확인, 호버 정보 표시, 잔여량 갱신, 제작 중 좌클릭 선택 |
| `LiquorBottleInfoCard` | ShelfPanel 직계 자식 (씬에 단 하나) | 호버한 병의 이름/소분류/병 단위 상태/잔여량 표시 |
| `LiquorBottleDef` | ScriptableObject | 술 데이터 (id, sprite, unlockFlagKey, subCategory, bottleCount, unitVolume) |
| `LiquorCategoryDef` | ScriptableObject | 카테고리 데이터 (id, displayName, icon, color) |
| `CategoryColorText` | 정적 유틸리티 클래스 | 등록된 카테고리들의 `displayName`을 문장 속에서 찾아 `color`로 TMP `<color>` 태그를 씌우는 헬퍼. `ShopUIManager.Start()`에서 `Register()`, 자유 문장 텍스트를 대입하는 지점에서 `Highlight()`를 명시적으로 호출해야 적용됨(전역 자동 적용 아님) — 상점 레시피북 설명/해금정보에 사용, 자세한 내용은 [restscene-systems.md](restscene-systems.md#카테고리-고유색-liquorcategorydefcolor-categorycolortextcs) 참고 |
| `LiquorStockLevelPalette` | ScriptableObject (공유 에셋 1개) | 병 잔여량 아이콘용 12단계 스프라이트(empty/intermediate×10/full) 팔레트, `GetSprite(ratio01)`로 조회 |

## 데이터 구조

**`LiquorBottleDef`** (`Assets > Create > Bartending > Liquor Bottle`)
- `unlockFlagKey`: 비어있으면 항상 해금
- `subCategory`: 정보카드에 표시할 소분류 텍스트
- `bottleCount`: 정보카드에 표시할 병 아이콘 개수 (예: 6)
- `unitVolume`: 병 1개당 용량, ml (예: 700)
- `MaxAmount` (계산 프로퍼티): `bottleCount * unitVolume`

**`LiquorCategoryDef`** (`Assets > Create > Bartending > Liquor Category`)
- `color`: 카테고리 고유색. 라벨 텍스트 색 자체는 바꾸지 않고, `LiquorCategoryButtonUI.colorImage`/`ShopUIManager.categoryNameColorImage` 같은 액센트 이미지 틴트와 `CategoryColorText` 문장 강조에만 쓰임

**`LiquorStockLevelPalette`** (`Assets > Create > Bartending > Liquor Stock Level Palette`)
- `emptySprite` / `intermediateSprites[10]` / `fullSprite` — 총 12개
- `GetSprite(ratio01)`: `ratio01 <= 0` → empty, `>= 1` → full, 그 외엔 올림 기준 10% 단위 구간(`Mathf.CeilToInt(ratio01 * 10) - 1`)으로 `intermediateSprites` 선택
- 씬에 에셋 1개만 만들어 `LiquorBottleInfoCard`가 공유 참조 (다른 UI가 같은 12단계를 재사용할 경우도 이 에셋 하나만 교체하면 됨)

**`LiquorShelfUI.CategoryEntry`** (인스펙터 배열)
- `def`: LiquorCategoryDef SO
- `container`: 해당 카테고리 슬롯 컨테이너 GameObject
- `backgroundSprite`: 카테고리별 배경 스프라이트

## 씬 계층 구조

실제 씬(`BusinessScene`) 기준. `LiquorShelf`는 스크롤뷰 이름이며 패널 자체가 아님 — 혼동 주의.

```
LiquorShelfPanel  [LiquorShelfUI]  ← shelfPanelRect (우측 슬라이드 대상)
├── CategoryButtons  (카테고리 탭 스크롤뷰) ← categoryButtonContent
├── LiquorShelf  (ScrollRect, vertical)
│   └── Viewport  [Image + Mask]  ← 이 안쪽은 전부 클리핑됨
│       └── Content
│           ├── ShelfBG  [Image + AspectRatioFitter]
│           ├── Category1, Category1 (1), ... × N  ← categoryEntries[i].container
│           │   └── BottleRow1 × N  (한 줄)
│           │       └── BottleSlot1 × N  [LiquorBottleSlotUI + Image (+ amountFillImage)]
│           └── ...
├── LiquorBottleInfoCard  ← 반드시 LiquorShelfPanel의 직계 자식, 맨 마지막 순서 (Viewport Mask 밖 + 렌더링 최상단)
└── ShelfCloseButton
```

`LiquorShelf`(스크롤뷰) 바로 아래에는 `Viewport` / `CategoryButtons` / `ShelfCloseButton` 외에 장식용 `ShelfFrame`(캐비닛 프레임 이미지, 패널 전체를 덮는 크기)이 **마지막 자식**으로 존재 — Hierarchy 순서상 맨 위에 렌더링되므로 **Raycast Target을 반드시 꺼둬야** 함(아래 "알려진 함정" 참고).

## 슬라이드 위치 (QHD 2560×1440 기준)

- `closedX`: ShelfScrollRect 너비 — 카테고리 버튼만 화면 우측에 노출
- `openX`: `0` — 전체 패널이 우측 엣지에 붙어 표시

## 입력

- **R키**: `LiquorShelfUI.Toggle()` — 열려있으면 닫기, 닫혀있으면 마지막 카테고리(없으면 첫 번째)로 열기
- **술병 좌클릭**: CraftingMode에서 해당 병을 테이블 오른쪽 빈 슬롯부터 즉시 배치
- OrderMode / CraftingMode에서만 동작, EpisodeMode에서는 무시

## GameModeManager 연동

- EpisodeMode 진입 시 `SetInteractable(false)` → 닫힘 + 버튼 비활성화
- EpisodeMode 해제 시 `SetInteractable(true)` → 버튼 재활성화 (자동 열기 없음)

## LiquorBottleSlotUI

- `def`는 인스펙터에서 직접 지정, `bottleImage`는 같은 GameObject의 `Image`를 `GetComponent`로 자동 참조 (슬롯 GameObject에 `Image` 컴포넌트 필수)
- `Awake`에서 `Refresh()` 자동 호출
- `unlockFlagKey`가 비어있거나 `GameProgress.HasFlag(unlockFlagKey)`이면 이미지 표시
- `IPointerEnterHandler`/`IPointerExitHandler` 구현 → 호버 시 `LiquorBottleInfoCard.Instance.Show(def, amount, rect)` / `Hide()`
- `IPointerClickHandler` 구현 → 좌클릭 시 `BusinessBartendingBootstrap.TryPlaceBottleFromShelf()` 호출
- 같은 종류의 병이 이미 테이블에 있거나 재고가 0이거나 빈 슬롯이 없으면 배치하지 않음
- `LiquorBottleDef.id`와 `Resources/Items` 아래 제작용 `ItemDef.id`가 같아야 실제 병을 생성할 수 있음
- `amountFillImage`(선택, `Image` Type=Filled/Horizontal)가 연결돼 있으면 `Refresh()`마다 `fillAmount = amount / def.MaxAmount`로 상시 갱신 (호버 무관, 잠금 시 자동 숨김)

## 잔여량 저장 (GameProgress)

- `GameProgress`에 `bottleAmountKeys`/`bottleAmountValues`(인스펙터 노출) + 런타임 `Dictionary<string,float>` 캐시
- API: `GetBottleAmount(id, defaultValue)` / `SetBottleAmount(id, value)` — `id`는 `LiquorBottleDef.id`와 일치해야 함
- `SaveData`/`DataManager`에 세이브·로드 반영됨
- **주의**: `DataManager.Awake()`가 항상 `Load()`를 호출하고, 세이브 파일이 없으면 빈 `SaveData()`로 `GameProgress.LoadFrom()`을 덮어씀 → 인스펙터에 미리 채워둔 `bottleAmountKeys/Values`도 Play 진입 즉시 초기화됨
- **테스트 팁**: Play 진입 후 인스펙터에서 값 채우고, `GameProgress` 컴포넌트 우클릭 → **Rebuild Runtime Sets (Debug)** 실행하면 즉시 반영

## LiquorBottleInfoCard (호버 정보카드)

- 씬에 단 하나만 존재, `Instance` 싱글턴 프로퍼티로 참조. 평소 `SetActive(false)`
- 표시 내용: 이름 / 소분류 / 병 개수만큼의 상태 아이콘(`Image[] stateImages`, `LiquorStockLevelPalette.GetSprite()`로 12단계 중 선택) / (사용 중인 병이 있을 때만) `"310/700ml"` 형식 잔여량 텍스트
- 계산 예: `bottleCount=6, unitVolume=700`인 술이 2410ml 남으면 `fullCount=3`(2100ml, `GetSprite(1f)`), 나머지 310ml인 in-use 1칸(`GetSprite(310/700)`), empty 2칸(`GetSprite(0f)`), 텍스트 `"310/700ml"`
- **아이콘 레이아웃**: `stateImages` 10칸 = 1번째 줄(고정 5개) + `secondRowContainer`(2번째 줄, `bottleCount > 5`일 때만 `SetActive(true)`). 개별 아이콘 활성화는 기존처럼 `i < bottleCount` 기준, 2번째 줄 컨테이너는 `VerticalLayoutGroup` + `ContentSizeFitter`(Vertical Fit = Preferred Size)로 감싸서 꺼졌을 때 카드 높이가 자동으로 줄어들게 함
- 위치 계산은 `RestScene/Scripts/TooltipManager.UpdatePosition`(화면 밖 벗어나면 좌우 자동 전환)의 구조를 참고해 새로 작성 — world position 기반이라 부모가 어디든 계산엔 무관, 단 `Viewport`의 `Mask` 밖(= `LiquorShelfPanel` 직계 자식)에 둬야 렌더링이 잘리지 않음

## 상시 표시 잔여량 바

- `LiquorBottleSlotUI.amountFillImage` 하나로는 채워진 부분만 그려지고 빈 공간은 안 보임
- 빈 공간을 명확히 하려면 슬롯마다 배경용 `Image`(Type=Simple, 어두운 단색)를 `amountFillImage`와 동일한 위치/크기로 깔고 **Hierarchy 순서상 `amountFillImage`보다 먼저(더 위)** 배치 — 모든 병 공통 색상 하나면 충분

## 알려진 함정

| 항목 | 설명 |
|---|---|
| `BottleSlot1`에 `Image` 컴포넌트 누락 | 씬에 수동 배치된 슬롯 중 다수가 `Image` 컴포넌트 없이 `LiquorBottleSlotUI`만 붙어있어 스프라이트가 안 뜸. 여러 개 동시 선택 후 Add Component로 일괄 추가 가능 |
| `unlockFlagKey` 미해금 | 테스트 데이터에 `unlockFlagKey`가 채워져 있고 `GameProgress.flags`가 비어있으면 스프라이트가 조용히 안 뜸(에러 없음). 테스트 시에는 비워두거나 `SetFlag()`로 미리 심어둘 것 |
| 잔여량-바텐딩 연동 | 실제로 따를 때 잔여량이 줄어드는 로직은 DragandDrop/바텐딩 브랜치 정리 이후 별도 작업 (아직 미구현) |
| `ShelfFrame`이 호버 이벤트 차단 | `LiquorShelf` 하위 마지막 자식인 `ShelfFrame`(장식용 캐비닛 프레임, 패널 전체 크기)의 `Image.Raycast Target`이 켜져 있으면, PNG 중앙이 투명해도 Unity 레이캐스트는 알파를 무시하고 사각형 전체를 히트박스로 잡아 그 아래 `Viewport`의 모든 `BottleSlot`이 호버를 못 받음(정보카드가 아예 안 뜸). `ShelfFrame`은 순수 장식용이므로 **Raycast Target을 반드시 꺼둘 것** |
| 새 이미지 에셋 교체 시 씬/SO 참조 재연결 필요 | `order_ticket`/`recipe_book`처럼 기존 파일명 그대로 내용만 덮어쓰면 GUID가 유지돼 자동 반영되지만, `shelf`처럼 새 파일명으로 추가하면 GUID가 달라져 `LiquorBottleDef.sprite`/`LiquorCategoryDef.icon`/`LiquorShelfUI.categoryEntries[].backgroundSprite` 등 기존 참조가 예전 스프라이트를 계속 가리킴. `Awake()`/`ShowCategory()`가 이 값들로 런타임에 강제 재할당하므로, 에디터에서 Image를 직접 드래그해 바꿔도 Play 시 예전 이미지로 되돌아감 — 데이터 소스(SO 에셋/직렬화 필드) 쪽을 새 스프라이트로 재연결해야 함 |

## ContentHeightToBackground 세팅

- 컴포넌트 위치: ShelfScrollRect Content
- `Content` → Content RectTransform 자기 자신
- `Background` → ShelfBGImage RectTransform
- 카테고리 전환 시 `LiquorShelfUI`가 sprite와 AspectRatioFitter.aspectRatio를 동시에 교체
