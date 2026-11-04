# 술 선택 공간 (IngredientSelection)과 도구 공간 (ToolSelection)

## 개요

영업 씬 바텐딩 화면은 다음 순서로 그려진다.

**바 배경 → 손님 → 바테이블 배경 → 손님 바 테이블(`BarCounter`) → 술 선택 공간 → 도구 공간 → 제작 공간**

술 선택 공간은 기존 우측 술장(`LiquorShelfUI`)을 대체한다.

- 대분류별 재료가 가운데 정렬로 한 줄에 나열된다(기획상 한 대분류 최대 5종).
- 재료는 윗부분만 보이고, 아래쪽은 제작 공간 배경에 가려진다.
- A/D 키로 대분류를 순환 전환한다. 줄이 좌우로 빠질 때 도구 공간 양옆 블록 뒤로 지나가 "술이 도구 공간 뒤로 들어가는" 것처럼 보인다.
- **항상 보이며** 제조 모드에서만 조작을 받는다. 제조를 빠져나가도 현재 대분류를 유지하므로, 다음 제조는 마지막으로 보던 대분류로 시작한다.
- 재료를 클릭하면 병(가니쉬는 가니쉬)이 하나 생성되어 **바로 손에 쥐어진다.** 빈 슬롯에 놓거나 술 선택 공간에 다시 반환한다.

도구 공간은 기존 W/S 도구장 서랍을 대체한다([아래](#도구-공간)). 서랍과 W/S 카메라 세로 이동은 제거됐다.

해금되지 않은 재료는 임시로 제외했다. 잔량 표시와 배송은 슬롯 호버·클릭으로 다시 넣었다([아래](#잔량-표시와-배송)). 기존 `LiquorShelfUI`와 배송 패널 코드는 삭제하지 않았다([liquor-shelf.md](liquor-shelf.md)).

## 스크립트 구성

`Assets/_Project/Features/Bartending/Runtime/IngredientSelection/`

| 스크립트 | 역할 |
|---|---|
| `SelectionSlotUI` | 재료·도구 슬롯 공통 기반. 호버 시 위로 슬라이드, 윤곽선, 이름·분류 라벨, 놓기·반납에 소비된 누름 가드. 소유자 계약은 `ISelectionSlotOwner` |
| `IngredientSelectionUI` | 대분류 줄 생성, A/D 슬라이드 전환, 제조 중 조작 가드, 병·가니쉬 반환 영역(`IBottleReturnZone`) |
| `IngredientSlotUI` | 재료 하나의 아이콘·이름·소분류 표시, 재고 없음은 `_dis` 이미지(`shelfDisabledSprite`)로 교체(전용 이미지가 없으면 반투명), 호버 라벨에 잔량 아이콘 줄과 배송 가격 줄 |
| `BottleDeliveryService` | 재고 0 재료의 배송 규칙: 가격 계산, 배송 가능 여부(해금·TV 제한), 결제(`IShopCurrency`), 정산 기록, 연출 중 중복 방지, 지급 |
| `DeliveryRisePresenter` | 배송원(n8 `delivery`)이 바테이블 뒤에서 올라왔다 내려가는 연출. 끝나거나 중단되면 완료 콜백(지급)을 호출 |
| `IngredientOutlineEffect` | 스프라이트 모양을 따르는 단색 윤곽선(기본 노란색 5px). 메시를 8방향으로 복사하고 전용 셰이더 `Slainte/UI/SpriteSolidOutline`(`Art/Shaders/UI/SpriteSolidOutline.shader`)이 복사본을 단색으로 칠함 |
| `BartendingBottleStock` | 꺼낸 병·가니쉬와 `GameProgress` 재고를 잇는 재고 규칙(`BusinessBartendingBootstrap` 소유) |
| `BottleReturnZones` | 병·가니쉬 반환 영역 등록소. 아이템 컨트롤러가 특정 UI 클래스를 모르도록 반환 판정을 분리하고, 같은 프레임 중복 반환을 막음 |

`Assets/_Project/Features/Bartending/Runtime/ToolSelection/`

| 스크립트 | 역할 |
|---|---|
| `ToolSelectionUI` | 도구 공간. 잔·도구 진열 줄 생성, 클릭 집기, 반납 영역(`IToolReturnZone`). 실제 보관·꺼내기·반납은 부트스트랩의 캐비닛 슬롯 API에 위임 |
| `ToolSlotUI` | 잔·도구 하나. 진열 전용 단일 이미지(`shelfInSprite`)가 있으면 한 장으로 그리고 꺼낸 동안 `_dis`로 교체. 없으면 여러 장의 레이어 이미지 표시, 꺼낸 동안 반투명, 보관 중에는 실제 상태(얼음 잔량 등) 반영 |
| `ToolReturnZones` | 도구 반납 영역 등록소. 도구 컨트롤러(잔·비커·셰이커·바스푼·얼음 버킷)가 놓기 전에 먼저 확인 |

가니쉬: `GarnishDef`(`Runtime/LiquorShelf/`)와 `GarnishController`(`Runtime/`). [가니쉬](#가니쉬) 참고.

## 재고 규칙

`GameProgress`의 병 잔량(`GetBottleAmount`)은 **술장과 제작대에 나와 있는 병을 합친 총량**이다. 저장 형식은 그대로다.

- **술장 재고** = 총량 − 제작대에 나와 있는 같은 재료의 양(`GetShelfAmount`). 병은 현재 잔량을, 가니쉬는 1개당 1을 더한다.
- **꺼내는 양** = 술장 재고 % 병 용량(`ItemDef.capacityMl`). 나머지가 0이면 가득 찬 한 병이다.
  - 따 둔 병이 먼저 나온다.
  - 예: 800ml(700ml 병) → 100ml 병 → 700ml 병
- **같은 재료를 여러 병 꺼낼 수 있다.** 이미 무언가를 들고 있거나 술장 재고가 0이면 거부한다(슬롯이 비어 있지 않아도 꺼낼 수는 있다. 놓을 빈 슬롯이 없으면 반환하면 된다).
- **따를 때**는 `BottleController.CapacityChanged`로 줄어든 양만 총량에서 뺀다. 병이 비어도 자동으로 채우지 않는다.
- **반환·제조 종료**: 오브젝트만 없앤다. 총량은 이미 소비량만큼만 줄어 있으므로 남은 양이 자동으로 술장에 합쳐진다.
  - 예: 300ml 병과 500ml 병을 반환하면 → 다음에 100ml 병, 그다음 700ml 병이 나온다.
- 재고 변화는 `BusinessBartendingBootstrap.StockChanged(inventoryId)`로 알린다. UI는 해당 재료 슬롯만 갱신한다.

## 잔량 표시와 배송

- **잔량 표시**: 호버 라벨의 소분류 밑에 `stock_` 아이콘을 `bottleCount`칸 한 줄로 보여 준다. 기준은 술장 재고(제작대에 꺼낸 병 제외)다. 가득 찬 병 → 따 둔 병 1칸(남은 비율) → 빈 병 순이며, 아이콘은 `LiquorStockLevelPalette`(`Content/Generated/Inventory/LiquorStockLevel.asset`)가 고른다. 프리팹의 `StockIcon` 템플릿을 필요한 수만큼 한 번 복제해 재사용한다.
- **배송 가격 줄**: 술장 재고가 0이고 배송이 가능하면 잔량 아이콘 밑에 `[화폐 아이콘] 가격` 줄이 나온다. 소지금이 모자라면 가격이 빨간색이다. 소지금은 변경 이벤트가 없어 호버 순간 슬롯이 최신 상태를 다시 받는다(`IIngredientSlotRefresher`).
  - 배송 가격 = 상점 가격(`LiquorBottleDef.price`) × `LiquorShopCatalog.shelfDeliveryPriceMultiplier`(기본 10). 상점 가격이 0인 재료(가니쉬 등)는 배송 대상이 아니다.
  - 배송 가능 = 해금(`BusinessOrderFlowSettings.IsDeliveryUnlocked`) && TV `DisableDelivery` 방송이 없는 날. `BusinessFlowBootstrap`이 영업 시작 때 `IngredientSelectionUI.SetDeliveryAvailable()`로 알려 주며, 그 전에는 막혀 있다.
- **배송 주문**: 재고 0 슬롯을 클릭하면 즉시 결제하고 정산의 "배송 이용"에 기록한다(`RecordDeliveryPurchase`). 이어 `DeliveryRisePresenter`가 배송원을 바테이블 뒤에서 올렸다 내리고, 끝나는 순간 1병(`unitVolume`, `MaxAmount` 한도)을 총량에 더해(`AddDeliveredBottle`) 슬롯이 다시 활성화된다.
  - 연출 중에는 다른 배송 클릭을 결제 전에 거절한다.
  - 연출 도중 오브젝트가 꺼지면 코루틴 대신 `OnDisable`에서 바로 지급한다(결제만 되고 병이 안 들어오는 상황 방지).
  - 돈이 모자라거나 배송이 막혀 있으면 아무 일도 일어나지 않는다.
- **클리핑**: 호버 라벨(이름·소분류·잔량·가격, 약 223px)이 위로 올라오면 `RowViewport` 위쪽 경계를 넘으므로, BusinessScene `RowViewport`의 `RectMask2D.padding.w`를 −150으로 두어 클리핑 범위만 위로 넓혔다(슬롯 배치는 그대로). 라벨 줄을 더 늘리면 이 값도 함께 키운다.
- **연출 배치**: BusinessScene `FrontCameraRig/DeliveryRise`. `CharactersBackContainer` 바로 다음, `TableBackground`보다 앞 형제 순서여야 테이블 뒤에서 솟아오른다. 씬에 놓인 위치가 올라온 위치이고 `riseDistance`(900)만큼 아래가 숨은 위치다. 오브젝트는 항상 켜 두고 `Image.enabled`만 토글한다.

## 가니쉬

- `GarnishDef : LiquorBottleDef`. 상속 구조라 상점·술 선택 공간·`GameProgress` 재고가 병과 같은 경로로 동작한다.
  - 재고 단위는 **개수**다. `unitVolume = 1`이므로 가격·기본 소지 개수가 그대로 개당 값이 된다.
  - `item`(ItemDef)은 비워 둔다. 재고 ID는 `id`(예: `garni_1001`)를 그대로 쓴다.
  - 진열·상점 이미지 = `_bottle`(`shelfInSprite`/`shopBlankSprite`), 꺼냈을 때 = `_garni`(`garnishSprite`).
- 에셋 위치: `Features/Bartending/Content/Garnishes/` (`garnish.asset` 카테고리, `garni_1001` 나낭나 필, `garni_1002` 합성 레몬 필).
  - **CSV 임포터가 관리하지 않는다.** 임포트 폴더(`Generated/`) 밖에 두어 보관 처리되지 않게 했다. `PlanningCsvAssetImporter`는 카탈로그를 재구성할 때 `GarnishDef` 항목을 CSV 재료 뒤에 그대로 보존하고, 카탈로그 일치 검사·에셋 검증에서도 제외한다.
  - 새 가니쉬는 SO를 직접 만들어 `Liquor Bottle Catalog`와 `LiquorShopCatalog.bottles`에 추가한다.
- `GarnishController`: 집기, 빈 슬롯에 놓기, 술 선택 공간 반환만 한다. 콜라이더는 트리거라 병·잔·액체와 부딪히지 않는다. 잔에 올리기·레시피 판정은 아직 없다.

## 입력과 가드

| 입력 | 동작 |
|---|---|
| A / D (제조 모드) | 이전/다음 대분류. 기존 줄이 반대편으로 빠지고 새 줄이 들어와 가운데 멈춤. 슬라이드 중 재입력 시 진행 중인 전환을 즉시 확정 |
| 호버 | 제조 세션 준비됨 + 들고 있는 물체 없음 + 포인터 아래 제작 공간 물체 없음일 때만 올라옴 |
| 좌클릭 (재료) | 위 조건과 같음. 병·가니쉬를 꺼내 손에 쥠 |
| 좌클릭 (도구 공간 잔·도구) | 위 조건 + 그 도구가 보관 중일 때. 꺼내 손에 쥠 |
| 병·가니쉬를 들고 술 선택 공간 안에서 좌클릭 | 술장 반환(`BottleReturnZones`) |
| 잔·도구를 들고 도구 공간 양쪽 줄 안에서 좌클릭 | 그 도구 자리로 반납(`ToolReturnZones`). 얼음 버킷은 반납 시 가득 참 |

- **놓기에 소비된 누름**: 물체 놓기·반납은 **누른 프레임**(`Input.GetMouseButtonDown`)에 처리되고 슬롯 클릭(`OnPointerClick`)은 **뗀 프레임**에 온다. 뗄 때는 이미 손이 비어 가드를 통과해 버리므로, `SelectionSlotUI`가 두 가지로 막는다.
  - `OnPointerDown` 시점에 손에 든 것이 있었는지 기억한다.
  - 반납 등록소의 `LastReturnFrame`이 누른 프레임 이후인지 확인한다(아이템 `Update`가 EventSystem보다 먼저 돌아 누르는 순간 이미 손이 비는 경우 대비). 도구 공간은 술 선택 줄과 겹치므로 두 등록소를 모두 확인한다.
- **제작 공간 물체 우선**: 제작 공간이 두 진열 공간보다 위에 그려지므로, 물체가 아이콘을 덮고 있으면 물체 조작이 우선한다(`IsPointerOverWorldItem`).
- **방금 꺼낸 병**: `BottleController`는 `Start`에서 카메라·렌더러를 캐싱하므로 생성 직후에는 집을 수 없다. `RequestPickupOnStart()`로 예약하고 `Start` 끝에서 회전축을 포인터에 맞춘 뒤 집는다. 예약 중에도 `IsPickedUp`이 true라 그 사이 다른 물체를 꺼내지 못한다.
- **도감·주문서 단축키**: 도감 Q, 주문서 E.

## 도구 공간

- `table_serve_2`(2560×1440, 양쪽 아래 블록만 불투명)를 배경으로 쓴다. 왼쪽 블록에 잔 4종(락·마티니·하이볼·허리케인), 오른쪽 블록에 지거·셰이커·바스푼.
- **얼음 버킷은 기획 변경으로 진열에서 뺐다**(`ToolSelectionUI.ToolOrder`). 카탈로그·캐비닛 인스턴스와 얼음 판정(`iceRequirement`)은 새 얼음 기획이 정해질 때까지 그대로 둔다 — 얼음 필수 레시피는 지금 진열대에서 얼음을 얻을 수 없다.
- A/D와 무관하게 항상 같은 자리에 있다.
- 도구는 **정의당 월드 인스턴스 1개**다(기존 도구장과 같은 정책). 세션 시작 시 `PrepareCabinetInventory`가 인스턴스를 숨겨진 캐비닛 슬롯에 넣고, 클릭하면 같은 인스턴스를 꺼낸다.
- **진열 이미지**: 술장처럼 단일 이미지 한 장이다. 보관 중 `_in`(`GlassDef`/`ToolDef.shelfInSprite`), 꺼낸 동안 `_dis`(`shelfDisabledSprite`). 꺼낸 월드 도구와 제작대 위 모습은 기존 레이어 구조(`worldLayers`)를 그대로 쓴다.
  - 아트: 잔 `Art/Glasses/{rock|cocktail|highball|hurricane}_in/_dis.png`, 도구 `Art/ToolCabinet/Tools/{cobbler|jigger|barspoon}_in/_dis.png`(310×590). 메뉴 `Tools/Slainte/Tool Cabinet/Apply Shelf Sprites`가 이 두 필드만 연결한다(`Install Or Repair`도 마지막에 같은 연결을 수행).
  - `shelfInSprite`가 비어 있는 정의는 예전처럼 `cabinetLayers`를 겹쳐 그리고 꺼낸 동안 반투명으로 남긴다.
  - 단일 이미지는 진열 방향 그대로 그려져 있으므로 바스푼도 뒤집지 않는다(레이어 폴백일 때만 뒤집음).
- **아이콘 크기** = 스프라이트 원본 픽셀 × 정의의 `worldScale` × `iconScale`. 월드 도구가 `BartendingNativeSpriteSizer`로 같은 식으로 맞춰지므로, 세션이 없어도 제작 테이블 위 크기와 같은 비율로 진열된다. 현재 아트는 모두 310×590 프레임이라 `iconScale` 1이면 칸 하나가 217×413이다. 블록 폭에 맞게 `iconScale`·`slotSpacing`(음수 가능)으로 조정한다.
- **윤곽선**: 단일 이미지도 "레이어 1장"으로 같은 경로를 탄다. 레이어마다 윤곽선을 그리면 레이어 경계가 안쪽에 선으로 남는다. 그래서 윤곽선용 복제본을 실제 레이어 전체보다 뒤(`OutlineLayers`)에 깔아 바깥 실루엣만 보이게 한다.
- 아이콘은 아래 가운데 기준으로 서 있으므로 밑부분이 제작 테이블(`CraftingSpace` 배경)에 가려진다.
- **호버 라벨 위치**: 프리팹에 배치한 위치가 아니라 런타임에 아이콘 프레임 윗변 + `labelGap`(기본 12)으로 덮어쓴다. 아트가 모두 같은 310×590 프레임이라 모든 잔·도구의 라벨이 같은 높이에 뜬다(그려진 부분의 실제 윗변은 쓰지 않음 — 의도된 동작).
- 씬에 `ToolSelectionUI`가 없거나 `useToolCabinet`이 꺼져 있으면 고정 도구를 테이블에 바로 놓는 기존 방식으로 폴백한다.

## 슬롯 위치 기준

물리 슬롯(`BartendingSlot_0~7`)은 캔버스 밖 바텐딩 월드에 런타임으로 생성된다.

- **가로 간격과 아이템 배율**: `BarCounter/TableSlots`(`UIDropSlot` 가이드)에서 계산한다. 이 템플릿은 런타임에 비활성화되지만 **지우면 안 된다.**
- **슬롯 줄 중심**:
  - 씬에 `CraftingSlotRow` RectTransform이 있으면 그 사각형 중심에 맞춘다.
  - 없으면 기존처럼 `BarCounter`의 보이는 영역 중심에 맞춘다.
  - 병 바닥은 슬롯 중심 높이에 놓인다.
- **렌더 출력 위치**:
  - 씬에 `CraftingSpace`가 있으면 바텐딩 렌더 출력(`BartendingViewport` RawImage)을 그 아래 마지막 자식으로 붙인다. 형제 순서는 코드로 바꾸지 않는다.
  - 없으면 기존처럼 `BarCounter` 옆에 끼워 넣는다.

## 씬 세팅

1. `FrontCameraRig` 아래 형제 순서: 바 배경 → `CustomerStage` → 바테이블 배경 → `BarCounter` → `IngredientSelection` → `ToolSelection` → `CraftingSpace`. `DrawerArea`는 제거한다.
2. `CraftingSpace`
   - 전체 크기로 늘린다(앵커 0~1, 오프셋 0).
   - 재료·도구 아래쪽을 가리는 배경 이미지를 자식으로 두고, **Raycast Target을 끈다.**
   - 자식 `CraftingSlotRow` 사각형의 세로 중심을 병이 설 높이에 맞춘다.
3. `IngredientSelection`
   - `IngredientSelectionUI`를 붙이고 `catalog`(`LiquorBottleCatalog`), `categoryOrder`(비우면 카탈로그 순서 — 지정했다면 `garnish`도 추가), `slotPrefab`을 연결한다.
   - `rowViewport`: `RectMask2D`. 대분류 줄이 이 안에 런타임으로 생성된다. 호버로 올라온 아이콘과 라벨이 잘리지 않을 만큼 위쪽에 여유를 둔다. 좌우는 도구 공간 블록 뒤까지 넓혀 두면 A/D 슬라이드가 블록 뒤로 들어가는 것처럼 보인다.
   - 숨김/표시 슬라이드가 없어졌으므로, 에디터에 배치한 위치가 그대로 표시 위치다.
4. `ToolSelection`
   - 전체 크기 `Image`(`table_serve_2`)를 배경으로 둔다. **Raycast Target을 끈다** — 켜 두면 가운데 투명 영역까지 클릭을 막아 술 선택 공간이 반응하지 않는다.
   - `ToolSelectionUI`를 붙이고 `slotPrefab`, `glassRow`(왼쪽 블록 위 사각형), `toolRow`(오른쪽 블록 위 사각형)를 연결한다. 두 줄의 사각형은 반납 영역도 겸하므로 블록 전체를 덮게 잡는다. `HorizontalLayoutGroup`은 없으면 런타임에 추가된다.
   - `glassCategoryLabel` = `잔`, `toolCategoryLabel` = `도구`(호버 시 이름 아래 고정 문구). 색은 각 `CategoryColor`.
   - `iconScale`, `slotSpacing`, `rowAlignment`(기본 LowerCenter)로 크기·간격을 조정한다.
5. 재료 슬롯 프리팹(`IngredientSlotUI`)
   - 루트: `RectTransform` 크기 = 한 칸 크기. **투명 `Image`(Raycast Target 켬)를 히트 영역으로 둔다.** 아이콘과 라벨은 코드에서 레이캐스트를 꺼서, 올라가는 동안 호버가 깜빡이지 않게 한다.
   - `visualRoot`: 자식. 호버 때 이것만 `hoverRaise`만큼 올라간다.
     - `icon`(`Image`, Material = `SpriteSolidOutline` 셰이더 머티리얼) + `hoverOutline`(`IngredientOutlineEffect`, 비활성으로 시작)
     - 기본 `Outline` 컴포넌트는 복사본을 "텍스처 색 × 지정 색"으로 그려 검정 외에는 단색 경계가 되지 않으므로 쓰지 않는다. 머티리얼을 프리팹이 직접 참조해야 셰이더가 빌드에 포함된다
     - `hoverLabelRoot` > `nameLabel`(크게, 흰색) / `subCategoryLabel`(작게, 색은 대분류 색으로 자동 설정)
6. 도구 슬롯 프리팹(`ToolSlotUI`): 재료 슬롯 프리팹을 복제해 `IngredientSlotUI`를 `ToolSlotUI`로 바꾸고 같은 필드를 연결한다.
   - 루트 크기는 런타임에 아이콘 크기로 덮어쓴다.
   - `icon`은 레이어 복제용 템플릿이라 런타임에 꺼지고, 복제본마다 스프라이트를 덮어쓴다. Source Image는 비워 둬도 된다. 머티리얼(`SpriteSolidOutline`)과 `IngredientOutlineEffect`는 그대로 두어야 윤곽선 복제본에 함께 복사된다.
   - 런타임에 `visualRoot` 아래 `OutlineLayers`(윤곽선 켠 복제본, 호버 때만 표시)와 `Layers`(실제 레이어)가 생성된다.
   - `hoverLabelRoot` 위치는 런타임에 레이어 기준으로 덮어쓰므로 프리팹에서 맞출 필요가 없다.
7. `LiquorShelfPanel`을 끄거나 제거한다.
8. `InputRouter.ingredientSelection`, `GameModeManager.ingredientSelection`을 연결한다.
9. `GameModeManager.cameraRig`를 연결한다(비워 두면 런타임에 씬에서 찾는다). 제조 진입은 카메라가 QHD 전체로 축소된 뒤에 일어나므로 제조 중 가로 보정은 필요 없다([character-presentation.md](../gameplay/character-presentation.md#카메라-확대-포커스-frontcamerarig)).

## 검증

- 메뉴 `Slainte/Bartending/Validate Business Shelf Spawn`이 확인하는 항목(레거시 슬롯 자동 배치 경로 `TryPlaceBottleFromShelf` 기준):
  - 따 둔 병 우선
  - 같은 재료 중복 꺼내기
  - 재고 0에서 거부
  - 소비량만큼 총량 감소
  - 제조 종료 시 자동 반환
  - 병 콜라이더 기하
- 메뉴 `Slainte/품질 검증/기획 CSV 에셋 검증`은 가니쉬를 제외한 CSV 재료 15종만 검증한다.
- `DeliverySystemValidator`는 기존 배송 패널 기준(카탈로그 15종)이라 현재 실패한다.
