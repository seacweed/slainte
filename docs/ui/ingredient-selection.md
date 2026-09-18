# 술 선택 공간 (IngredientSelection)

## 개요

영업 씬 바텐딩 화면은 세 공간으로 나뉜다. 그리는 순서는 다음과 같다.

**바 배경 → 손님 → 바테이블 배경 → 손님 바 테이블(`BarCounter`) → 술 선택 공간 → 제작 공간**

술 선택 공간은 기존 우측 술장(`LiquorShelfUI`)을 대체한다.

- 대분류별 재료가 가운데 정렬로 한 줄에 나열된다.
- 재료는 윗부분만 보이고, 아래쪽은 제작 공간 배경에 가려진다.
- A/D 키로 대분류를 순환 전환한다.
- 제조 모드에서만 제작 공간 뒤에서 올라온다. 다른 모드에서는 아래로 내려가 완전히 가려진다. 사용자가 직접 여닫을 수는 없다.
- `FrontCameraRig` 아래에 있어서 도구장처럼 카메라의 세로 이동을 따라간다. 가로 팬은 제조 중에만 역보정된다(아래 [카메라 가로 팬 보정](#카메라-가로-팬-보정)).

해금되지 않은 재료, 잔여량 표시, 배송 버튼은 임시로 제외했다. 기존 `LiquorShelfUI`와 배송 코드는 삭제하지 않았으므로 복원할 때 참고한다([liquor-shelf.md](liquor-shelf.md)).

## 스크립트 구성 (`Assets/_Project/Features/Bartending/Runtime/IngredientSelection/`)

| 스크립트 | 역할 |
|---|---|
| `IngredientSelectionUI` | 대분류 줄 생성, A/D 슬라이드 전환, 제조 가능 여부에 따른 숨김/표시, 호버·스폰 가드, 병 반환 영역(`IBottleReturnZone`) |
| `IngredientSlotUI` | 재료 하나의 표시와 입력 전달. 호버 시 위로 슬라이드, 윤곽선, 이름·소분류 라벨, 재고 없음 반투명 |
| `IngredientOutlineEffect` | 스프라이트 모양을 따르는 단색 윤곽선(기본 노란색 5px). 메시를 8방향으로 복사하고 전용 셰이더 `Slainte/UI/SpriteSolidOutline`(`Art/Shaders/UI/SpriteSolidOutline.shader`)이 복사본을 단색으로 칠함 |
| `BartendingBottleStock` | 꺼낸 병과 `GameProgress` 재고를 잇는 재고 규칙(`BusinessBartendingBootstrap` 소유) |
| `BottleReturnZones` | 병 반환 영역 등록소. `BottleController`가 특정 UI 클래스를 모르도록 반환 판정을 분리하고, 같은 프레임 중복 반환을 막음 |

## 재고 규칙

`GameProgress`의 병 잔량(`GetBottleAmount`)은 **술장과 제작대에 나와 있는 병을 합친 총량**이다. 저장 형식은 그대로다.

- **술장 재고** = 총량 − 제작대에 나와 있는 같은 재료 병들의 현재 잔량 합(`GetShelfAmount`)
- **꺼내는 양** = 술장 재고 % 병 용량(`ItemDef.capacityMl`). 나머지가 0이면 가득 찬 한 병이다.
  - 따 둔 병이 먼저 나온다.
  - 예: 800ml(700ml 병) → 100ml 병 → 700ml 병
- **같은 재료를 여러 병 꺼낼 수 있다.** 빈 슬롯이 없거나 술장 재고가 0이면 거부한다.
- **따를 때**는 `BottleController.CapacityChanged`로 줄어든 양만 총량에서 뺀다. 병이 비어도 자동으로 채우지 않는다.
- **반환·제조 종료**: 병 오브젝트만 없앤다. 총량은 이미 소비량만큼만 줄어 있으므로 남은 잔량이 자동으로 술장에 합쳐진다.
  - 예: 300ml 병과 500ml 병을 반환하면 → 다음에 100ml 병, 그다음 700ml 병이 나온다.
- 재고 변화는 `BusinessBartendingBootstrap.StockChanged(inventoryId)`로 알린다. UI는 해당 재료 슬롯만 갱신한다.

## 입력과 가드

| 입력 | 동작 |
|---|---|
| A / D (제조 모드) | 이전/다음 대분류. 기존 줄이 반대편으로 빠지고 새 줄이 들어와 가운데 멈춤. 슬라이드 중 재입력 시 진행 중인 전환을 즉시 확정 |
| 호버 | 조작 가능 + 들고 있는 물체 없음 + 포인터 아래 제작 공간 물체 없음일 때만 올라옴 |
| 좌클릭 | 위 조건과 같음. 병을 꺼내 가장 오른쪽 빈 슬롯에 스냅 |
| 병을 들고 영역 안에서 좌클릭 | 술장 반환(`BottleReturnZones`). 같은 누름으로 새 병이 나오지 않도록 `LastReturnFrame`으로 클릭을 무시 |
| 잔·도구를 들고 좌클릭 | 슬롯 안착 등 놓기만 일어난다. 재료는 나오지 않는다 |

- **놓기에 소비된 누름**: 물체 놓기는 **누른 프레임**(`Input.GetMouseButtonDown`)에 처리되고 슬롯 클릭(`OnPointerClick`)은 **뗀 프레임**에 온다. 뗄 때는 이미 손이 비어 호버·스폰 가드를 통과해 버리므로, `IngredientSlotUI`는 `OnPointerDown` 시점의 "손에 든 것이 있었는지"(`IngredientSelectionUI.HasHeldItem()`)를 기억해 그 누름의 클릭을 무시한다. 술장 반환의 `LastReturnFrame` 가드와 같은 계열의 방어다.
- **제작 공간 물체 우선**: 제작 공간이 술 선택 공간보다 위에 그려지므로, 물체가 재료 아이콘을 덮고 있으면 물체 조작이 우선한다(`IsPointerOverWorldItem`).
- **조작 가능 시점**: 제조 모드로 바뀐 뒤 올라오는 슬라이드가 끝나야 조작할 수 있다. 내려가기 시작하는 순간 조작이 막힌다.
- **도감·주문서 단축키**: 도감 Q, 주문서 E(기존 A/Tab에서 변경).

## 카메라 가로 팬 보정

에피소드 대화 중에는 `EpisodeRunner`가 캐릭터를 화면 중앙에 두려고 `FrontCameraRig`를 가로로 팬한다(`PanToWorldCenterX`). 팬은 `frontWorld` RectTransform 자체를 옮기므로, 그 자식인 제작 공간과 술 선택 공간도 함께 밀려 제조에 들어가면 재료 줄이 한쪽으로 치우친다.

`FrontCameraRig.SetHorizontalFixed(bool)`이 `horizontalFixedPanels`(`CraftingSlotRow`, `IngredientSelection`)의 `anchoredPosition.x`를 `기준값 - _focusX`로 밀어 화면 가로 위치를 고정한다. **서랍(`SetDrawer`)이 `drawerArea.x = -_focusX`로 자리를 잡는 것과 같은 방식**이며, `GameModeManager.RefreshPanels()`가 `CraftingMode` 여부로 켜고 끈다.

- **보간하지 않고 한 번에 옮긴다.** 재료가 올라오기 전에 자리만 바꿔 놓아야 한다. 보간하면 제작 시퀀스 진입 때 재료들이 좌우로 미끄러지며 등장해 부자연스럽다.
- **제조 중에만 켠다.** 대화 중에도 고정하면 슬롯 줄만 바 카운터·배경에서 떨어져 미끄러진다.
- **`CraftingSpace`가 아니라 `CraftingSlotRow`를 넣는다.** 제작 공간 전체를 옮기면 배경까지 함께 튀므로, 슬롯 기준 사각형만 옮겨 배경은 그대로 두고 병이 놓이는 줄만 화면 중앙에 맞춘다. 렌더 출력(`BartendingViewport` RawImage)은 `CraftingSpace` 전체를 덮고 있어 그대로 두어도 슬롯의 화면 위치는 따라 움직이고, 포인터 매핑도 뷰포트 rect 기준이라 어긋나지 않는다.
- **기준값은 `Awake` 시점의 X**를 쓰므로, 런타임에 이 패널들의 X를 따로 옮기는 코드를 추가하면 안 된다. 같은 이유로 서랍(`DrawerArea`)은 이 배열에 넣지 않는다 — 이미 `SetDrawer`가 같은 값을 관리하므로 두 로직이 충돌한다.
- **모드 전환 시점에 호출되는 것이 중요하다.** 슬롯 줄 정렬(`AlignSlotLayoutToVisibleTable`)은 세션 생성 때 한 번만 계산되는데, `RefreshPanels()`가 `OnModeChanged`(= 세션 생성)보다 먼저 실행되므로 보정된 위치가 반영된다.
- `_focusX`가 바뀌는 `PanToWorldCenterX`/`ResetPan`에서도 다시 적용해, 고정이 켜진 동안 팬이 일어나도 기준이 어긋나지 않게 한다.

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

1. `FrontCameraRig` 아래 형제 순서: 바 배경 → `CustomerStage` → 바테이블 배경 → `BarCounter` → `IngredientSelection` → `DrawerArea` → `CraftingSpace`
2. `CraftingSpace`
   - 전체 크기로 늘린다(앵커 0~1, 오프셋 0). 도구장 서랍 쪽으로 렌더 영역을 확장하는 계산이 이 부모 기준으로 동작한다.
   - 재료 아래쪽을 가리는 배경 이미지를 자식으로 두고, **Raycast Target을 끈다.**
   - 자식 `CraftingSlotRow` 사각형의 세로 중심을 병이 설 높이에 맞춘다.
3. `IngredientSelection`
   - `IngredientSelectionUI`를 붙이고 `catalog`(`LiquorBottleCatalog`), `categoryOrder`(비우면 카탈로그 순서), `slotPrefab`을 연결한다.
   - `contentRoot`: 숨김/표시 때 세로로 움직이는 묶음. 에디터에 배치한 위치가 "올라온 위치"가 된다.
   - `rowViewport`: `contentRoot` 아래 `RectMask2D`. 대분류 줄이 이 안에 런타임으로 생성된다. 호버로 올라온 아이콘과 라벨이 잘리지 않을 만큼 위쪽에 여유를 둔다.
   - `hiddenOffsetY`: 재료가 제작 공간 배경 뒤로 완전히 가려지는 거리. `RowViewport` 바닥에서 슬롯 루트 윗변까지의 높이에 여유를 더한 값으로 시작해 조정한다.
   - 현재 대분류 이름 표시는 없다(A/D 전환은 재료 줄 슬라이드만으로 표현).
4. 슬롯 프리팹(`IngredientSlotUI`)
   - 루트: `RectTransform` 크기 = 한 칸 크기. **투명 `Image`(Raycast Target 켬)를 히트 영역으로 둔다.** 아이콘과 라벨은 코드에서 레이캐스트를 꺼서, 올라가는 동안 호버가 깜빡이지 않게 한다.
   - `visualRoot`: 자식. 호버 때 이것만 `hoverRaise`만큼 올라간다.
     - `icon`(`Image`, Material = `SpriteSolidOutline` 셰이더 머티리얼) + `hoverOutline`(`IngredientOutlineEffect`, 비활성으로 시작)
     - 기본 `Outline` 컴포넌트는 복사본을 "텍스처 색 × 지정 색"으로 그려 검정 외에는 단색 경계가 되지 않으므로 쓰지 않는다. 머티리얼을 프리팹이 직접 참조해야 셰이더가 빌드에 포함된다
     - `hoverLabelRoot` > `nameLabel`(크게, 흰색) / `subCategoryLabel`(작게, 색은 대분류 색으로 자동 설정)
5. `LiquorShelfPanel`을 끄거나 제거하고, `FrontCameraRig.verticalFollowPanels`에서 뺀다.
6. `InputRouter.ingredientSelection`, `GameModeManager.ingredientSelection`을 연결한다.
7. `FrontCameraRig.horizontalFixedPanels`에 `CraftingSlotRow`와 `IngredientSelection` **루트**를 넣는다.
   - `IngredientSelection`은 루트여야 한다. `Content` 자식을 넣으면 숨김/표시 슬라이드와 `x`를 두고 충돌한다.
   - 제작 공간은 `CraftingSpace`가 아니라 `CraftingSlotRow`를 넣는다([위 설명](#카메라-가로-팬-보정)).
   - `GameModeManager.cameraRig`도 연결한다(비워 두면 런타임에 씬에서 찾는다).

## 검증

- 메뉴 `Slainte/Bartending/Validate Business Shelf Spawn`이 확인하는 항목:
  - 따 둔 병 우선
  - 같은 재료 중복 꺼내기
  - 재고 0에서 거부
  - 소비량만큼 총량 감소
  - 제조 종료 시 자동 반환
  - 병 콜라이더 기하
- 배송이 임시로 빠져 있어 `DeliverySystemValidator`는 현재 실패한다.
