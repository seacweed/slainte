# 술장 시스템 (LiquorShelf)

## 개요

화면 우측에서 슬라이드로 열리는 카테고리형 술 진열장 UI. D키로 토글, 카테고리 버튼 클릭으로 해당 카테고리 열기.

## 스크립트 구성

| 스크립트 | 위치 | 역할 |
|---|---|---|
| `LiquorShelfUI` | ShelfPanel | 메인 컨트롤러 — 슬라이드, 카테고리 전환, EpisodeMode 대응 |
| `LiquorCategoryButtonUI` | 카테고리 버튼 프리팹 (술장/상점 공용) | 클릭 시 `LiquorShelfUI.OpenCategory()`(술장) 또는 `ShopUIManager.ShowListByCategory()`(상점) 호출. `colorImage`(선택 필드)가 연결된 프리팹 인스턴스에서만 카테고리 고유색으로 틴트 — 술장 프리팹은 비워두면 색이 적용되지 않음 |
| `LiquorBottleSlotUI` | `BottleSlot.prefab` (`Assets/Prefabs/`) | 해금 플래그 확인, 호버 정보 표시, 잔여량 갱신, 제작 중 좌클릭 선택. `Setup(def)`로 런타임 주입되어 인스턴스화됨 |
| `LiquorBottleInfoCard` | ShelfPanel 직계 자식 (씬에 단 하나) | 호버한 병의 이름/소분류/병 단위 상태/잔여량 표시 |
| `LiquorBottleDef` | ScriptableObject | 술 데이터 (id, shelfSprite, shopSprite, unlockFlagKey, subCategory, bottleCount, unitVolume) |
| `LiquorBottleCatalog` | ScriptableObject (술장/상점 공용) | `List<LiquorBottleDef> bottles` — 전체 술 목록. `LiquorShelfUI`와 `ShopUIManager`(일반/이상한 상점)가 동일 에셋을 참조해 술 추가 시 한 곳만 등록하면 됨 |
| `LiquorCategoryDef` | ScriptableObject | 카테고리 데이터 (id, displayName, icon, color) |
| `CategoryColorText` | 정적 유틸리티 클래스 | 등록된 카테고리들의 `displayName`을 문장 속에서 찾아 `color`로 TMP `<color>` 태그를 씌우는 헬퍼. `ShopUIManager.Start()`에서 `Register()`, 자유 문장 텍스트를 대입하는 지점에서 `Highlight()`를 명시적으로 호출해야 적용됨(전역 자동 적용 아님) — 상점 레시피북 설명/해금정보에 사용, 자세한 내용은 [restscene-systems.md](restscene-systems.md#카테고리-고유색-liquorcategorydefcolor-categorycolortextcs) 참고 |
| `LiquorStockLevelPalette` | ScriptableObject (공유 에셋 1개) | 병 잔여량 아이콘용 12단계 스프라이트(empty/intermediate×10/full) 팔레트, `GetSprite(ratio01)`로 조회 |

## 데이터 구조

**`LiquorBottleDef`** (`Assets > Create > Bartending > Liquor Bottle`)
- `shelfSprite`: `LiquorBottleSlotUI`(술장 슬롯)에 표시되는 스프라이트, `_lid` 변형 이미지 사용
- `shopSprite`: `ItemSlotUI`(상점 슬롯)에 표시되는 스프라이트, `_blank` 변형 이미지 사용. `shelfSprite`와 별개 필드이므로 둘 다 채워야 함
- 바테이블(제조 중 실제로 놓이는 병)에 뜨는 "기본" 이미지는 이 SO가 아니라 별도 `ItemDef.icon`(`Assets/_Project/Features/Bartending/Runtime/Interaction/ItemDef.cs`)에서 관리 — 세 화면(상점/술장/바테이블)이 각각 다른 스프라이트 소스를 참조하는 구조
- `unlockFlagKey`: 비어있으면 항상 해금
- `subCategory`: 정보카드에 표시할 소분류 텍스트
- `bottleCount`: 정보카드에 표시할 병 아이콘 개수 (예: 6)
- `unitVolume`: 병 1개당 용량, ml (예: 700)
- `MaxAmount` (계산 프로퍼티): `bottleCount * unitVolume`

**`LiquorBottleCatalog`** (`Assets > Create > Bartending > Liquor Bottle Catalog`)
- `bottles`: `LiquorBottleDef` 전체 목록. `LiquorShelfUI`는 이 목록을 `LiquorBottleDef.category`(상점 그룹핑에 쓰이는 필드 재사용)로 필터링해 카테고리별 슬롯을 생성 — 카테고리 내 슬롯 순서는 이 리스트의 순서를 그대로 따름

**`LiquorCategoryDef`** (`Assets > Create > Bartending > Liquor Category`)
- `color`: 카테고리 고유색. 라벨 텍스트 색 자체는 바꾸지 않고, `LiquorCategoryButtonUI.colorImage`/`ShopUIManager.categoryNameColorImage` 같은 액센트 이미지 틴트와 `CategoryColorText` 문장 강조에만 쓰임

**`LiquorStockLevelPalette`** (`Assets > Create > Bartending > Liquor Stock Level Palette`)
- `emptySprite` / `intermediateSprites[10]` / `fullSprite` — 총 12개
- `GetSprite(ratio01)`: `ratio01 <= 0` → empty, `>= 1` → full, 그 외엔 올림 기준 10% 단위 구간(`Mathf.CeilToInt(ratio01 * 10) - 1`)으로 `intermediateSprites` 선택
- 씬에 에셋 1개만 만들어 `LiquorBottleInfoCard`가 공유 참조 (다른 UI가 같은 12단계를 재사용할 경우도 이 에셋 하나만 교체하면 됨)

**`LiquorShelfUI.CategoryEntry`** (인스펙터 배열)
- `def`: LiquorCategoryDef SO
- `container`: 해당 카테고리 슬롯 컨테이너 GameObject (`GridLayoutGroup` 부착, `Awake()`에서 이 아래에 슬롯이 동적 생성됨)
- `backgroundSprite`: 카테고리별 배경 스프라이트

**`LiquorShelfUI` 슬롯 생성 필드**
- `catalog`: `LiquorBottleCatalog` 참조
- `slotPrefab`: `BottleSlot.prefab` 참조
- `Awake()` → `BuildCategorySlots()`에서 `catalog.bottles`를 `entry.def`(카테고리)로 필터링해 `entry.container` 아래에 `Instantiate` 후 `Setup(bottle)` 호출. 씬에 슬롯을 수동 배치할 필요 없음

## 씬 계층 구조

실제 씬(`BusinessScene`) 기준. `LiquorShelf`는 스크롤뷰 이름이며 패널 자체가 아님 — 혼동 주의.

```
LiquorShelfPanel  [LiquorShelfUI]  ← shelfPanelRect (우측 슬라이드 대상)
├── CategoryButtons  (카테고리 탭 스크롤뷰) ← categoryButtonContent
├── LiquorShelf  (ScrollRect, vertical)
│   └── Viewport  [Image + Mask]  ← 이 안쪽은 전부 클리핑됨
│       └── Content
│           ├── ShelfBG  [Image + AspectRatioFitter]
│           ├── Category1, Category1 (1), ... × N  [GridLayoutGroup]  ← categoryEntries[i].container
│           │   └── (BottleSlot.prefab 인스턴스 — `BuildCategorySlots()`가 런타임 생성, 씬에 수동 배치 없음)
│           └── ...
├── LiquorBottleInfoCard  ← 반드시 LiquorShelfPanel의 직계 자식, 맨 마지막 순서 (Viewport Mask 밖 + 렌더링 최상단)
└── ShelfCloseButton
```

`Category1...N` 컨테이너의 `GridLayoutGroup` 설정 (BusinessScene 기준): `CellSize = (120, 190)`, `Spacing = (40, 65)`, `Constraint = Fixed Column Count`. 병 이미지 크기가 고정이라는 전제로 셀 크기를 맞춰둔 값 — 병 스프라이트 규격이 바뀌면 5개 컨테이너 전부 같이 조정해야 함.

`LiquorShelf`(스크롤뷰) 바로 아래에는 `Viewport` / `CategoryButtons` / `ShelfCloseButton` 외에 장식용 `ShelfFrame`(캐비닛 프레임 이미지, 패널 전체를 덮는 크기)이 **마지막 자식**으로 존재 — Hierarchy 순서상 맨 위에 렌더링되므로 **Raycast Target을 반드시 꺼둬야** 함(아래 "알려진 함정" 참고).

## 슬라이드 위치 (QHD 2560×1440 기준)

- `closedX`: ShelfScrollRect 너비 — 카테고리 버튼만 화면 우측에 노출
- `openX`: `0` — 전체 패널이 우측 엣지에 붙어 표시

## 입력

- **D키**: `LiquorShelfUI.Toggle()` — 배송 화면이 열려 있는 상태에서 닫으면 `IsDeliveryOpen`(배송 패널이 아직 켜져 있는지)만 보고 `EndDeliverySession()` 없이 서랍만 `Slide(closedX, ...)`로 슬라이드시켜 감춤(레시피북 차단/배송 캐릭터 등 내부 상태·배송 패널 활성 상태는 그대로 유지 — `_deliveryPanel`이 `shelfPanelRect`의 자식이라 슬라이드에 같이 딸려 나감). 다시 열면 `IsDeliveryOpen`이 여전히 true이므로 셔터/재초기화 없이 `Slide(openX, ...)`만으로 즉시 복원. 배송이 열려 있지 않았을 때는 기존처럼 `Close()`(카테고리 상태 종료)/마지막 카테고리(없으면 첫 번째) 열기로 동작. 배송 패널 자체의 닫기(X) 버튼은 별도로 `Close()`를 호출해 레시피북 차단 해제·배송 캐릭터 퇴장·셔터 리셋까지 포함한 완전 종료를 수행함(D키 경로와 분리됨)
- **술병 좌클릭**: CraftingMode에서 해당 병을 테이블 오른쪽 빈 슬롯부터 즉시 배치
- OrderMode / CraftingMode에서만 동작, EpisodeMode에서는 무시

## GameModeManager 연동

- EpisodeMode 진입 시 `SetInteractable(false)` → 닫힘 + 버튼 비활성화
- EpisodeMode 해제 시 `SetInteractable(true)` → 버튼 재활성화 (자동 열기 없음)

## LiquorBottleSlotUI

- `def`는 `Setup(def)`로 런타임 주입 (`LiquorShelfUI.BuildCategorySlots()` 참고). `bottleImage`는 같은 GameObject의 `Image`를 `GetComponent`로 자동 참조 (슬롯 GameObject에 `Image` 컴포넌트 필수 — `BottleSlot.prefab` 루트에 부착돼 있음)
- `Awake`에서 `Refresh()` 자동 호출 (`Setup()` 호출 전이라 `def == null`이면 조기 반환)
- 미해금이어도 스프라이트는 항상 표시하되 `bottleImage.color = Color.black`로 검정 실루엣 처리 (해금 시 `Color.white`) — 상점 `ItemSlotUI`와 동일한 방식
- `bottleImage.preserveAspect = true`를 `Refresh()`에서 매번 설정 (프리팹 인스펙터 값이 아니라 코드에서 강제)
- `IPointerEnterHandler`/`IPointerExitHandler` 구현 → 호버 시 `LiquorBottleInfoCard.Instance.Show(def, amount, rect)` / `Hide()`
- `IPointerClickHandler` 구현 → 좌클릭 시 `BusinessBartendingBootstrap.TryPlaceBottleFromShelf()` 호출
- 같은 종류의 병이 이미 테이블에 있거나 재고가 0이거나 빈 슬롯이 없으면 배치하지 않음
- `LiquorBottleDef.id`와 `Resources/Items` 아래 제작용 `ItemDef.id`가 같아야 실제 병을 생성할 수 있음
- `amountFillImage`(선택, `Image` Type=Filled/Horizontal, 프리팹에 포함)가 연결돼 있으면 `Refresh()`마다 `fillAmount = amount / def.MaxAmount`로 상시 갱신 (호버 무관, 잠금 시 자동 숨김)

## 잔여량 저장 (GameProgress)

- `GameProgress`에 `bottleAmountKeys`/`bottleAmountValues`(인스펙터 노출) + 런타임 `Dictionary<string,float>` 캐시
- API: `GetBottleAmount(id, defaultValue)` / `SetBottleAmount(id, value)` — `id`는 `LiquorBottleDef.id`와 일치해야 함
- `SaveData`/`DataManager`에 세이브·로드 반영됨
- **주의**: `DataManager.Awake()`가 항상 `Load()`를 호출하고, 세이브 파일이 없으면 빈 `SaveData()`로 `GameProgress.LoadFrom()`을 덮어씀 → 인스펙터에 미리 채워둔 `bottleAmountKeys/Values`도 Play 진입 즉시 초기화됨
- **테스트 팁**: Play 진입 후 인스펙터에서 값 채우고, `GameProgress` 컴포넌트 우클릭 → **Rebuild Runtime Sets (Debug)** 실행하면 즉시 반영

## LiquorBottleInfoCard (호버 정보카드)

- 씬에 단 하나만 존재, `Instance` 싱글턴 프로퍼티로 참조. 평소 `SetActive(false)`
- 표시 내용: 이름 / 소분류 / 병 개수만큼의 상태 아이콘(`Image[] stateImages`, `LiquorStockLevelPalette.GetSprite()`로 12단계 중 선택) / (재고가 조금이라도 있으면) `"310/700ml"` 형식 잔여량 텍스트
- 계산 예: `bottleCount=6, unitVolume=700`인 술이 2410ml 남으면 `fullCount=3`(2100ml, `GetSprite(1f)`), 나머지 310ml인 in-use 1칸(`GetSprite(310/700)`), empty 2칸(`GetSprite(0f)`), 텍스트 `"310/700ml"`
- **마지막 병이 정확히 꽉 찬 경우도 표시됨**: 텍스트 표시 여부(`showAmount`)는 아이콘 렌더링용 in-use 판정(`remainder > 0f && fullCount < bottleCount`)과 별개 조건(`clampedAmount > 0f`)을 쓴다 — 아이콘 판정만 재사용하면 `remainder == 0`(마지막 병까지 정확히 꽉 참)일 때 "사용 중인 병 없음"으로 오판해 텍스트 전체가 숨겨지는 버그가 있었음. 텍스트에 표시할 값도 `remainder > 0 ? remainder : unitVolume`으로 계산해, 꽉 찬 경우 `"700/700ml"`처럼 뜬다(재고가 0일 때만 텍스트 자체가 숨겨짐)
- **아이콘 레이아웃**: `stateImages` 10칸 = 1번째 줄(고정 5개) + `secondRowContainer`(2번째 줄, `bottleCount > 5`일 때만 `SetActive(true)`). 개별 아이콘 활성화는 기존처럼 `i < bottleCount` 기준, 2번째 줄 컨테이너는 `VerticalLayoutGroup` + `ContentSizeFitter`(Vertical Fit = Preferred Size)로 감싸서 꺼졌을 때 카드 높이가 자동으로 줄어들게 함
- 위치 계산은 `RestScene/Scripts/TooltipManager.UpdatePosition`(화면 밖 벗어나면 좌우 자동 전환)의 구조를 참고해 새로 작성 — world position 기반이라 부모가 어디든 계산엔 무관, 단 `Viewport`의 `Mask` 밖(= `LiquorShelfPanel` 직계 자식)에 둬야 렌더링이 잘리지 않음

## 상시 표시 잔여량 바

- `LiquorBottleSlotUI.amountFillImage` 하나로는 채워진 부분만 그려지고 빈 공간은 안 보임
- 빈 공간을 명확히 하려면 배경용 `Image`(Type=Simple, 어두운 단색)를 `amountFillImage`와 동일한 위치/크기로 깔고 **Hierarchy 순서상 `amountFillImage`보다 먼저(더 위)** 배치 — `BottleSlot.prefab`에 `AmountBarBG`(배경) / `AmountBar`(fill) 자식으로 이미 구성돼 있어 슬롯마다 개별 배치할 필요 없음

## 알려진 함정

| 항목 | 설명 |
|---|---|
| `unlockFlagKey` 미해금 | 테스트 데이터에 `unlockFlagKey`가 채워져 있고 `GameProgress.flags`가 비어있으면 검정 실루엣으로만 표시됨(정상 동작, 에러 아님). 해금 상태로 테스트하려면 비워두거나 `SetFlag()`로 미리 심어둘 것 |
| `catalog`/`slotPrefab` 미연결 | `LiquorShelfUI.catalog` 또는 `slotPrefab`이 비어있으면 `BuildCategorySlots()`가 조용히 아무 슬롯도 생성하지 않음(에러 없음) — 카테고리를 열었는데 슬롯이 하나도 안 보이면 이 두 필드부터 확인 |
| 잔여량-바텐딩 연동 | 실제로 따를 때 잔여량이 줄어드는 로직은 DragandDrop/바텐딩 브랜치 정리 이후 별도 작업 (아직 미구현) |
| `ShelfFrame`이 호버 이벤트 차단 | `LiquorShelf` 하위 마지막 자식인 `ShelfFrame`(장식용 캐비닛 프레임, 패널 전체 크기)의 `Image.Raycast Target`이 켜져 있으면, PNG 중앙이 투명해도 Unity 레이캐스트는 알파를 무시하고 사각형 전체를 히트박스로 잡아 그 아래 `Viewport`의 모든 `BottleSlot`이 호버를 못 받음(정보카드가 아예 안 뜸). `ShelfFrame`은 순수 장식용이므로 **Raycast Target을 반드시 꺼둘 것** |
| 새 이미지 에셋 교체 시 씬/SO 참조 재연결 필요 | `order_ticket`/`recipe_book`처럼 기존 파일명 그대로 내용만 덮어쓰면 GUID가 유지돼 자동 반영되지만, `shelf`처럼 새 파일명으로 추가하면 GUID가 달라져 `LiquorBottleDef.shelfSprite`/`shopSprite`/`LiquorCategoryDef.icon`/`LiquorShelfUI.categoryEntries[].backgroundSprite` 등 기존 참조가 예전 스프라이트를 계속 가리킴. `Awake()`/`ShowCategory()`가 이 값들로 런타임에 강제 재할당하므로, 에디터에서 Image를 직접 드래그해 바꿔도 Play 시 예전 이미지로 되돌아감 — 데이터 소스(SO 에셋/직렬화 필드) 쪽을 새 스프라이트로 재연결해야 함 |

## 배송(Delivery) 탭

`LiquorShelfUI`는 술장 위에 별도로 배치된 `deliveryButton`(순정 `Button`, 카테고리 버튼 목록과 분리된 독립 버튼 — `LiquorCategoryButtonUI`는 카테고리별로 동적 복제되는 버튼용이라 씬에 하나만 정적으로 배치되는 이 버튼엔 맞지 않아 안 씀)을 클릭하면 `LiquorShopCatalog.deliveryPanelPrefab`(`DeliveryShopPanelUI`)을 술장 안에서 여는 별도 구매 패널로 제공한다. 배송 불가 시 회색 표시는 별도 오버레이 없이 `deliveryButton`의 `interactable`을 끄는 것만으로 처리한다 — `Button`의 Colors 설정에서 `Transition = Color Tint` + `Disabled Color`를 회색으로 지정해두면 `interactable = false`일 때 유니티가 자동으로 틴트해줌. `DeliveryShopPanelUI.ConfigureFromShopTemplate(ShopUIManager)`(에디터 전용 `DeliveryShopPrefabGenerator`에서만 호출)가 휴식 화면 `ShopUIManager`의 UI 계층 일부를 복사해 구성하므로 구매·카테고리 전환 로직을 새로 만들지 않는다 — 단 아이템 슬롯/카테고리 버튼 프리팹(아래 참고)은 이 복사 대상이 아니라 `Initialize()` 시점에 `LiquorShopCatalog.deliveryItemSlotPrefab`/`deliveryCategoryButtonPrefab`에서 채워진다(배송 전용 스킨을 쓰기 위함). 가격은 `deliveryPriceMultiplier`(기본 2배)로 일반 상점과 분리.

- **화면 구성**: `homePanel`/`upgradePanel`은 배송에 필요 없어 `DeliveryShopPanelUI`가 아예 추적하지 않음 — `ingredientCategoryPanel`(카테고리 목록, 최상위 화면)과 `itemScrollPanel`(아이템 목록) 딱 두 화면만 있음. 이상한 상점과 동일하게 `backButton`을 카테고리 화면에서는 숨기고(`SetScreen()`에서 `target != ingredientCategoryPanel`일 때만 표시) 아이템 목록에서만 눌러서 카테고리로 돌아갈 수 있게 함. `RebindCopiedButtons()`가 복사돼 온 버튼의 기존 persistent listener 메서드 이름(`ShopUIManager.ShowHome`/`OpenIngredients`/`GoBack`)을 감지해 전부 `ShowCategories()`로 재연결하므로, 이상한 상점 프리팹에서 뒤로가기 버튼을 그대로 복사해 붙여넣어도 별도 배선 없이 동작함
- **카테고리 아이콘 색상**: `itemScrollPanel`에서 `categoryNameColorImage`(`ShopUIManager.categoryNameColorImage`와 동일한 패턴 — 카테고리 이름 옆 액센트 이미지)를 `ShowListByCategory()`가 `category.color`로 틴트
- `TryOpenDelivery()`: `_deliveryAvailable`이 꺼져 있으면 패널을 열지 않고 사유(`_deliveryUnavailableReason`)를 로그로만 남긴다. `SetDeliveryAvailable(false, reason)`으로 외부(TV 방송 등)가 배송을 막을 수 있다.
- **셔터 연출(진입 전용)**: `shutter`(`ShelfShutterUI`)가 있으면 `TryOpenDelivery()`가 `PlayEnterDelivery(onScreenSwitch)`를 호출해 위/아래 셔터가 **완전히 닫힘 → 화면 내용 전환(카테고리 컨테이너 숨김 + 배송 패널 표시) → 살짝 열린 위치에서 정지** 순으로 재생된다. **배송 → 술장 복귀는 애니메이션이 없다** — `EndDeliverySession()`이 `shutter.ResetImmediate()`로 셔터를 즉시 완전히 열린(숨김) 위치로 되돌릴 뿐이다(닫기 버튼을 누르면 술장 서랍 자체가 옆으로 슬라이드해 닫히는 기존 연출만 재생되고, 그 후 카테고리 버튼으로 다시 열어도 셔터는 안 나옴).
  - `ShelfShutterUI`는 `topShutter`/`bottomShutter`의 `anchoredPosition`만 Lerp할 뿐 클리핑엔 관여하지 않는다 — "열린(숨김)" 위치가 패널 밖으로 삐져나오므로, `topShutter`/`bottomShutter`를 감싸는 부모 컨테이너에 `RectMask2D`(스크롤 아닌 단순 사각 클리핑이라 `LiquorShelf` 스크롤뷰의 `Viewport`가 쓰는 `Mask`보다 이쪽이 적합)를 붙이고 그 컨테이너의 `RectTransform`을 보여도 되는 범위(예: 술장 패널 전체)로 맞춰야 화면 밖까지 삐져나오지 않는다
- **배송 패널 배치/크기**: `BuildDelivery()`가 `_deliveryPanel`을 인스턴스화한 뒤, `shutter`가 있으면 `_deliveryPanel.transform.SetSiblingIndex(shutter.transform.GetSiblingIndex())`로 형제 인덱스를 셔터 바로 앞자리에 끼워 넣는다(셔터가 항상 배송 패널보다 위에 그려지도록). 크기/앵커는 더 이상 코드에서 부모를 꽉 채우도록 강제하지 않고 `deliveryPanelPrefab`에 미리 잡아둔 `RectTransform` 값을 그대로 씀 — 원하는 위치·크기로 보이게 하려면 프리팹 쪽 `RectTransform`을 조정하면 됨
- 구매 성공 시 `HandleDeliveryPurchased()` → `DeliveryCharacterPresenter`가 바테이블 뒤에서 배송 캐릭터를 슬라이드로 등장시키고, `deliveryCharacterSlideDuration + deliveryCharacterHoldDuration` 뒤 자동으로 숨긴다. 연속 구매 시 진행 중이던 코루틴을 멈추고 다시 시작해 등장 시간이 계속 리셋되지 않게 한다.
- `EndDeliverySession()`: 배송 패널을 닫으면 `SetInteractable(false)`+`HideImmediate()`로 즉시 비활성화하고, 셔터도 즉시 리셋하며, 배송 캐릭터도 슬라이드로 퇴장시킨다. `BlocksRecipeBook`이 `true`인 동안(배송 세션 진행 중)에는 레시피북 잠금 해제 판단에 이 플래그가 쓰인다.

### DeliveryItemSlotUI (`RestScene/Scripts/DeliveryItemSlotUI.cs`)

배송 전용 아이템 슬롯. `ItemSlotUI`와 필드/구매 로직은 동일하되(별도 클래스로 분리 — `ItemSlotUI` 쪽은 배송이 갈라져 나가면서 `priceMultiplier` 관련 코드를 제거해 다시 배율 1 고정인 일반/이상한 상점 전용으로 정리됨), 구매 버튼 아래에 원가(배율 적용 전 가격)를 회색 텍스트 + 대각선 취소선(`originalPriceText`/`originalPriceStrike`)으로 추가 표시하는 부분만 다르다. 이 클래스는 배송 전용(배율이 항상 1이 아님)이라 별도 조건 없이 해금 상태에서 항상 표시된다.

- 프리팹: `ItemSlotUI.prefab`을 복제한 별도 프리팹(예: `DeliveryItemSlotUI.prefab`)에 `ItemSlotUI` 대신 `DeliveryItemSlotUI` 컴포넌트를 부착하고, 원가 텍스트/취소선/원가용 통화 아이콘(`originalPriceText`/`originalPriceStrike`/`originalPriceCurrencyIcon`, 셋 다 `unlocked && 유효한 가격`일 때만 같이 켜짐) 오브젝트를 추가해 필드에 연결. 기존 `ItemSlotUI.prefab`(일반/이상한 상점 공용)은 건드리지 않음 — 슬롯 높이가 커지면 그쪽 그리드 레이아웃까지 영향을 주기 때문
- 이 새 프리팹은 `DeliveryShopPanelUI`에 직접 연결하는 게 아니라 `LiquorShopCatalog.deliveryItemSlotPrefab`(Delivery UI 헤더)에 연결 — `deliveryPanelPrefab`/`deliveryPortrait`와 같은 자리에서 관리되며, `DeliveryShopPanelUI.Initialize()`가 이 값을 읽어 자신의 `itemSlotPrefab`을 채운다(패널 프리팹 자체엔 이 필드가 인스펙터 노출 안 됨)

## ContentHeightToBackground 세팅

- 컴포넌트 위치: ShelfScrollRect Content
- `Content` → Content RectTransform 자기 자신
- `Background` → ShelfBGImage RectTransform
- 카테고리 전환 시 `LiquorShelfUI`가 sprite와 AspectRatioFitter.aspectRatio를 동시에 교체
