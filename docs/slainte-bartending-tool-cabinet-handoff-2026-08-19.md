# SLÁINTE 바텐딩 아트·도구장 인수인계

- 기준일: 2026-08-19
- 프로젝트: `A:\UnityHub\proj\slainte`
- 목적: 다른 기기나 세션에서 현재 맥락을 잃지 않고 작업을 이어가기 위한 기준 문서

## 0. 다음 작업자가 먼저 읽을 내용

이번 다음 구현 단위는 **술장이 아니라 도구장**이다.

- 기존 술장 배경과 술장 프레임은 변경하지 않는다.
- `BusinessScene`에 이미 존재하는 임시 도구장 `DrawerArea`와 개폐 동작을 재사용한다.
- 제공된 `tool_cabinet.png`를 기존 임시 도구장 배경에 적용한다.
- 이미지 원본의 픽셀 크기, 캔버스, 종횡비를 변경하지 않는다.
- 왼쪽 아래 구획은 빈 공간으로 둔다.
- 지거 용량은 30 ml / 45 ml이며 데이터에서 나중에 수정할 수 있어야 한다.
- 바스푼은 사용할 수 있지만 사용 여부가 평가에 직접 영향을 주면 안 된다.
- Planning 에셋과 새 재료 CSV를 기준 데이터로 바꾸는 구조 개편은 현재 보류 중이다.

도구장 구현에 들어가기 전에 씬과 코드의 현재 상태를 다시 확인하고, 이 문서와 다른 점이 있으면 먼저 보고한다.

### 2026-08-19 구현 완료 기록

이 기록은 아래의 과거 "구현 전" 상태 설명보다 우선한다.

- 도구장 배경 1개, 도구 레이어 17개, 얼음 3개를 `Assets/Art/Bartending/ToolCabinet`에 원본 바이트 그대로 반입했다.
- 21개 PNG 모두 원본 픽셀 크기와 전체 Sprite 사각형을 유지한다.
- `ToolDef`, `GlassDef`, `ToolCabinetCatalog` 및 Resources 에셋을 추가했다.
- `DrawerArea`의 기존 위치와 개폐 동작은 유지하고 런타임에 4구획 UI를 구성한다.
- 상단 왼쪽은 도구 4종, 상단 오른쪽은 잔 4종, 하단 왼쪽은 빈 공간, 하단 오른쪽은 얼음 충전 영역이다.
- 도구/잔 드래그 생성, 빈 슬롯 스냅, 빈 용기 반환, 중복 방지, 서빙 잔 1개 제한, 주문 종료 자동 정리를 구현했다.
- 내용물이 든 용기를 반환하려 하면 가장 가까운 빈 슬롯으로 안전하게 복귀한다.
- 지거 30/45 ml는 `ToolDef` 데이터이며, 빈 지거를 우클릭해 용량을 전환한다. 가득 차면 유입 차단막이 닫힌다.
- 코블러 셰이커의 `Shake`와 `WasShakenWithIce`, 완성 잔의 얼음, 잔 종류를 별도로 평가한다.
- 현재 주문 가능한 Shake 기준 레시피 `rec_1001`, `rec_1002`, `rec_1004`는 `shakeIceRequirement = Required`로 설정했다.
- 바스푼은 물리적으로 사용할 수 있으나 평가 기법을 기록하지 않는다.
- 얼음통은 교대 중 최대 20개를 유지하며 0~6 상태 이미지와 3단계 얼음 표시를 갱신한다.
- 도구장 카탈로그를 읽지 못하면 기존 고정 도구 생성으로 자동 폴백한다.
- 술장 씬·배경·프레임 코드는 변경하지 않았다.

검증 결과:

- `Assembly-CSharp.csproj`: 오류 0
- `Assembly-CSharp-Editor.csproj`: 오류 0
- 도구장 아트 계약: PNG 21개, 크기/전체 Sprite 사각형 오류 0
- Resources 도구장 에셋: Missing Script 0
- 남은 확인: Unity가 새 에셋을 Refresh한 뒤 `BusinessScene` 실제 플레이에서 드래그·제조·서빙 E2E와 여러 해상도 배치를 눈으로 확인할 것

### 2026-08-20 슬롯 의미 정정 및 후속 구현

이 절은 아래의 과거 drag/spawn/빈 용기 반환 설명보다 우선한다.

- 도구장 칸은 바테이블과 같은 `SlotController` 점유 계약을 사용한다. 기존 바테이블 `SlotController.cs`, 슬롯 배치, 슬롯 목록은 수정하지 않았다.
- 이동 가능한 도구와 잔은 세션 시작 때 자기 도구장 슬롯을 `Occupy`하고 같은 월드 인스턴스가 `SnapToSlot`된다.
- 도구장 아이콘을 드래그해 복제하거나 새 인스턴스를 생성하지 않는다. 점유된 슬롯을 클릭하면 `Vacate` 후 해당 인스턴스를 즉시 집는다.
- 자기 도구장 슬롯에 돌려놓으면 `Occupy`와 `SnapToSlot`을 거치며 액체·얼음 상태를 그대로 보존한다.
- 도구장 UI는 별도 인벤토리 상태를 만들지 않고 실제 `SlotController.OccupancyChanged`를 표시한다.
- 도구장 슬롯은 바테이블 슬롯 목록에 추가하지 않아 테이블의 빈 슬롯 선택이나 기존 드롭 우선순위를 바꾸지 않는다.
- 얼음통은 `useDedicatedAnchor`인 기존 고정 설비이므로 이동 슬롯에서 제외하고 기존 제빙/충전 경로를 유지한다.
- 런타임과 에디터 어셈블리 빌드 결과는 오류 0개다. `BartendingInteractionValidator`에 도구장 `Store → Occupy`, `Take → Vacate` 계약 검증을 추가했다.

---

## 1. 작업 범위

### 이번 범위

- 기존 `DrawerArea`와 `FrontCameraRig`의 도구장 열기/닫기 동작 재사용
- 임시 도구장 배경을 `tool_cabinet.png`로 교체
- 새 배경의 네 구획에 맞춰 도구·잔·제빙 영역 구성
- 도구장 UI에서 실제 바텐딩 월드 오브젝트를 꺼내는 기능
- 실제 오브젝트를 바테이블 슬롯에 배치하고 다시 반환하는 기능
- 지거, 코블러 셰이커, 바스푼, 얼음통, 잔 연결
- 임시 잔으로 제작 → 서빙 → 평가 전체 흐름 테스트
- 셰이킹, 얼음, 잔 종류 평가 연결

### 이번 단계에서 보류

- 술장 배경 교체
- Planning 에셋을 새 기준 데이터로 확정하는 작업
- 제공된 재료 CSV를 기준 데이터로 바꾸는 대규모 구조 개편
- 도구 구매, 해금, 업그레이드
- 도구 소유 및 장착 상태 저장
- 별도의 이른바 `개발 세이브` 시스템
- 최종 잔 이미지 교체
- 모든 술병 데이터 불일치 해결

`개발 세이브`라는 별도 시스템은 만들지 않는다. 나중에 도구 구매·해금 상태를 저장해야 한다면 기존 `GameProgress`와 `SaveData`를 확장한다.

---

## 2. 확정된 기획 결정

### 도구장

| 항목 | 확정 내용 |
| --- | --- |
| 상단 왼쪽 | 도구 영역 |
| 상단 오른쪽 | 잔 영역 |
| 하단 왼쪽 | 빈 공간으로 유지 |
| 하단 오른쪽 | 얼음 및 제빙기 영역 |
| 기존 임시 도구장 | 위치와 개폐 구조를 재사용 |
| 술장 | 이번 도구장 작업에서 변경하지 않음 |

### 도구

| 항목 | 확정 내용 |
| --- | --- |
| 지거 | 30 ml / 45 ml |
| 지거 수치 | 코드 상수가 아니라 나중에 수정 가능한 데이터로 관리 |
| 바스푼 | 사용할 수 있으나 사용 자체는 평가에 직접 영향 없음 |
| 셰이킹 | 단순 혼합과 별도의 평가 조건 |
| 완성 잔의 얼음 | 평가에 직접 영향 |
| 셰이킹 중 얼음 | 완성 잔 얼음과 별도로 평가 |
| 잔 종류 | 평가에 직접 영향 |
| 서빙 | 잔을 손님 쪽으로 드래그하여 제출 |

### 병 이미지

| 사용 위치 | 이미지 |
| --- | --- |
| 상점 | `*_blank.png` |
| 술장 | 뚜껑이 있는 `*_lid.png` |
| 바테이블 | 뚜껑이 없는 기본 `*.png` |
| `hotwater` | 변형이 없으므로 세 화면에서 같은 `hotwater.png` |

불일치 항목은 유사 이름으로 추정 연결하지 않는다.

- 데이터만 있으면 기존 데이터를 보존하고 새 이미지 필드는 비워 둔다.
- 아트만 있으면 Unity 에셋으로 보존하되 데이터에 임의 연결하지 않는다.
- 불일치는 문서로 남긴다.
- 레몬주스는 기존 원본 이미지를 사용하기로 했다.

### 이미지 공통 규칙

- 원본 PNG 리사이즈 금지
- 트림 및 크롭 금지
- 캔버스 크기 변경 금지
- 재인코딩 금지
- 투명 여백 제거 금지
- UI에서 `preserveAspect` 사용
- UI 표시 배율과 원본 texture 변경을 구분할 것

---

## 3. 현재 프로젝트에 이미 반영된 작업

### 병 아트

- 병 PNG 43개가 다음 경로에 반입되어 있다.

  `Assets/Art/Bartending/Bottles`

- 상점/술장/바테이블 이미지를 분리하는 코드가 존재한다.
- `LiquorBottleDef`에 다음 필드가 존재한다.

  - `useContextImages`
  - `shopBlankSprite`
  - `shelfLidSprite`
  - `barSprite`

- 관련 주요 파일:

  - `Assets/Scripts/LiquorShelf/LiquorBottleDef.cs`
  - `Assets/RestScene/Scripts/ItemSlotUI.cs`
  - `Assets/Scripts/LiquorShelf/LiquorBottleSlotUI.cs`
  - `Assets/Scripts/Bartending/BusinessBartendingBootstrap.cs`
  - `Assets/Scripts/Bartending/BottleController.cs`
  - `Assets/Editor/BartendingBottleArtSetup.cs`

### 임시 잔

- 임시 잔 PNG 4개가 다음 경로에 반입되어 있다.

  `Assets/Art/Bartending/GlassCollisionTests`

- sprite 이름을 기준으로 충돌 프로필을 적용하는 코드가 존재한다.
- 이미지 크기와 Transform을 변경하지 않고 수동 `EdgeCollider2D`와 별도 내용물 trigger를 생성한다.
- 자동 `PolygonCollider2D`는 사용하지 않는다.

관련 파일:

- `Assets/Scripts/Bartending/GlassCollisionProfile.cs`
- `Assets/Scripts/Bartending/GlassController.cs`
- `Assets/Editor/GlassCollisionProfileValidator.cs`

### 임시 잔 프로필

| 파일 | `glassId` | 용량 | 구성 |
| --- | --- | ---: | --- |
| `200rock.png` | `rock` | 200 ml | U형 open edge 9점, trigger 1개 |
| `200coc.png` | `martini` | 200 ml | V형 open edge 9점, trigger 4개 |
| `400high.png` | `highball` | 400 ml | U형 open edge 7점, trigger 1개 |
| `400hurricane.png` | `hurricane` | 400 ml | 곡선 U형 open edge 17점, trigger 5개 |

임시 잔 PNG에는 용량 표기와 색상 표시 픽셀도 alpha에 포함되어 있다. sprite 자동 충돌 형태를 사용하면 글씨까지 collider로 잡힐 수 있으므로 반드시 수동 프로필을 유지한다.

### 컴파일 기록

병/잔 코드 적용 직후 다음 두 프로젝트가 오류 0으로 컴파일된 기록이 있다.

- `Assembly-CSharp.csproj`
- `Assembly-CSharp-Editor.csproj`

다음 기기에서는 Unity가 스크립트를 다시 임포트한 뒤 Console 오류를 재확인한다.

---

## 4. 기존 임시 도구장 상태

임시 도구장은 이미 `Assets/BusinessScene.unity` 안에 존재한다.

### `DrawerArea`

- 활성 상태
- 현재 크기: `2560 × 1034.6667`
- 현재 위치: `y = -1034`
- 하단 pivot 사용
- 임시 배경과 `DrawerContent`를 자식으로 가짐

### `FrontCameraRig`

- 기존 도구장 개폐 이동을 담당한다.
- 현재 주요 값:

  - `drawerShiftY = 800`
  - `moveTime = 0.35`
  - `drawerArea`에 현재 `DrawerArea` 연결

관련 파일:

`Assets/Scripts/CameraMove/FrontCameraRig.cs`

### `DrawerContent`

- `DrawerArea` 전체 stretch
- 현재 비활성 상태
- 기존 `HorizontalLayoutGroup` 존재
- 자식:

  - `ToolInvenPanel`
  - `GlassInvenPanel`

### `ToolInvenPanel`

- 현재 크기: `1000 × 700`
- 자식 없음
- 반투명 임시 `Image`
- 실제 도구 데이터 및 월드 프리팹과 연결되지 않음

### `GlassInvenPanel`

- 현재 크기: `1000 × 700`
- 자식 없음
- 반투명 임시 `Image`
- 실제 잔 데이터 및 월드 프리팹과 연결되지 않음

### 기존 레거시 UI 코드

다음 코드는 실제 바텐딩 도구 시스템이 아니라 UI 복사·이동 프로토타입이다.

- `DrawerUI`
- `ShelfUI`
- `UIItemDraggable`
- `UIDropSlot`

이 코드를 새 도구 데이터의 기준점으로 사용하지 않는다.

### 현재 런타임 문제

`BartendingSessionBuilder`는 현재 다음 장비를 도구장과 무관하게 고정 생성한다.

- Beaker
- CobblerShaker
- ServingGlass

따라서 현재 도구장은 외형과 개폐 골격만 있고, 실제 도구 인벤토리 기능은 없다.

---

## 5. 제공된 도구장·도구 아트

### 외부 원본 경로

- 도구장 배경:

  `C:\Users\boguk\Downloads\아트\아트\tool_cabinet.png`

- 도구:

  `C:\Users\boguk\Downloads\아트\아트\도구\`

- 얼음:

  `C:\Users\boguk\Downloads\아트\아트\얼음\`

### 규격

| 종류 | 개수 | 원본 크기 |
| --- | ---: | ---: |
| 도구장 배경 | 1 | 2560×820 |
| 도구 이미지 | 17 | 모두 310×590 |
| 얼음 이미지 | 3 | 모두 70×70 |

### 도구 이미지 구성

- `barspoon.png`
- `jigger_back.png`
- `jigger_front.png`
- `bucket_back.png`
- `bucket_front_0.png` ~ `bucket_front_6.png`
- `cobbler_cup_back_line.png`
- `cobbler_cup_back_white.png`
- `cobbler_cup_front_line.png`
- `cobbler_cup_front_white.png`
- `cobbler_lid.png`
- `cobbler_strainer.png`

### 현재 반입 상태

도구장 배경, 도구 17개, 얼음 3개가 `Assets/Art/Bartending/ToolCabinet`에 반입되어 있다.

병 43개와 임시 잔 4개도 기존 `Assets/Art/Bartending` 경로에 유지되어 있다.

### Import 설정 권장

- Texture Type: `Sprite (2D and UI)`
- Sprite Mode: `Single`
- Full Rect 사용
- Mip Map 비활성화
- Wrap Mode: `Clamp`
- Compression: `None`
- Alpha Is Transparency 활성화
- 원본 종횡비 보존

`tool_cabinet.png`는 폭이 2560이므로 Max Texture Size를 최소 4096으로 설정해야 한다. 2048로 두면 Unity 임포트 시 2048×656으로 축소된다.

---

## 6. 도구장 구획 구성

| 구획 | 용도 | 내용 |
| --- | --- | --- |
| 상단 왼쪽 | 도구 | 지거, 코블러 셰이커, 바스푼, 얼음통 |
| 상단 오른쪽 | 잔 | rock, martini, highball, hurricane |
| 하단 왼쪽 | 빈 공간 | 아무 슬롯이나 버튼도 배치하지 않음 |
| 하단 오른쪽 | 제빙 | 얼음통 충전 및 얼음 보급 |

### 기존 씬에 적용할 방법

1. `DrawerArea` root와 `FrontCameraRig` 개폐 코루틴은 유지한다.
2. 현재 임시 배경 `Image`를 `tool_cabinet.png`로 교체한다.
3. 배경 wrapper를 원본 `2560×820` 기준으로 맞춘다.
4. `DrawerContent`를 활성화한다.
5. 기존 `HorizontalLayoutGroup` 중심 임시 배치를 제거하거나 새 구획 anchor 구조로 교체한다.
6. `ToolInvenPanel`을 상단 왼쪽 도구 영역으로 재구성한다.
7. `GlassInvenPanel`을 상단 오른쪽 잔 영역으로 재구성한다.
8. 하단 왼쪽에는 인터랙션 오브젝트를 만들지 않는다.
9. 하단 오른쪽에 얼음통 충전 영역을 연결한다.
10. 실제 노출 높이에 맞춰 `drawerShiftY`를 최종 조정한다.

술장 UI, 술장 배경, 술장 프레임은 수정하지 않는다.

---

## 7. 도구별 기능

### 7.1 지거

- 기본 용량: 30 ml / 45 ml
- 두 값은 직렬화 데이터로 관리
- 코드에 수치를 고정하지 않음
- 지거 사용 자체는 평가 기법이 아님
- 평가에는 실제 완성 결과의 재료 용량만 반영

렌더 순서:

```text
jigger_back
→ 액체
→ jigger_front
```

구현 선택 사항:

- 양쪽 컵을 별도 충돌 용기로 구현
- 또는 하나의 물리 용기에서 현재 측정 용량을 전환

어느 방식을 택하더라도 외부 데이터에는 30/45 ml가 유지되어야 한다.

### 7.2 코블러 셰이커

- 단순 혼합과 실제 셰이킹을 구분
- 기존 `CobblerShakerTechniqueController`의 왕복 이동 감지 재사용
- 셰이킹 성공 시 `CocktailTechnique.Shake` 기록
- 얼음을 넣고 흔들었는지는 별도 상태로 기록
- 스트레이너는 코블러 뚜껑 일체형으로 취급
- 별도 스트레이너 도구를 만들지 않음

렌더 순서:

```text
cobbler_cup_back_white / back_line
→ 내용물
→ cobbler_cup_front_white / front_line
→ cobbler_strainer / cobbler_lid
```

- `white` 레이어는 반투명
- `line` 레이어는 불투명

### 7.3 바스푼

- 내용물을 흘리지 않고 섞는 조작 도구
- 사용할 수 있어야 함
- 사용 여부, 사용 시간, 사용 횟수는 평가에 직접 반영하지 않음
- `Stir` 상태가 기존 코드에 있더라도 필수 제조법이나 가점·감점 조건으로 사용하지 않음

### 7.4 얼음통

- 기본 보유량: 20개
- 유니콘 빌드에서는 업그레이드 제외
- 사용 시 얼음 개수 감소
- 바테이블 아래 제빙기에서 다시 충전
- 20개를 초과하여 충전되지 않도록 제한

렌더 순서:

```text
bucket_back
→ ice_01 / ice_02 / ice_03 및 내용물
→ bucket_front_N
```

#### 잔량 이미지 매핑

| 이미지 | 잔량 |
| --- | --- |
| `bucket_front_6` | 100% |
| `bucket_front_5` | 99~80% |
| `bucket_front_4` | 79~60% |
| `bucket_front_3` | 59~40% |
| `bucket_front_2` | 39~20% |
| `bucket_front_1` | 19~1% |
| `bucket_front_0` | 0% |

평가에서 다음 두 항목은 별도다.

1. 완성 잔에 얼음이 있는가
2. 셰이킹할 때 셰이커 안에 얼음이 있었는가

---

## 8. 권장 데이터 구조

기존 `ItemData`, `DrawerUI` 프로토타입을 도구장 기준 데이터로 사용하지 않는다.

### `ToolDef`

권장 필드:

- stable ID
- 표시 이름
- 도구장 아이콘 또는 sprite
- world prefab
- `ToolKind`
- 용량 profile
- 행동 profile
- 기본 정렬 레이어

예상 `ToolKind`:

- Jigger
- CobblerShaker
- BarSpoon
- IceBucket

### `GlassDef`

권장 필드:

- stable ID
- 표시 이름
- 도구장 sprite
- world prefab
- `glassId`
- capacity ml
- collision profile

### `ToolCabinetCatalog`

- 도구장에 표시할 `ToolDef` 목록
- 표시할 `GlassDef` 목록
- 각 항목의 구획
- 정렬 순서

### `ToolCabinetController`

책임:

- 도구장 열기 상태 연동
- 도구와 잔 아이콘 표시
- 드래그 시작
- 실제 월드 prefab 생성
- 사용 중 아이콘 상태 변경
- 도구 반환
- 주문 종료 시 상태 정리

### Spawn 연결

`BartendingSessionBuilder` 또는 `BusinessBartendingBootstrap`에 다음 역할을 연결한다.

- 빈 `SlotController` 탐색
- `ToolDef.worldPrefab` 생성
- `IBartendingItem` 등록
- 바테이블 슬롯에 snap
- 반환 시 점유 해제 및 오브젝트 제거

---

## 9. 권장 조작 흐름

```text
도구장 열기
→ 도구 또는 잔 아이콘 드래그
→ 바테이블 빈 슬롯에 드롭
→ 실제 world prefab 생성
→ IBartendingItem으로 제작에 사용
→ 도구장으로 반환
→ 월드 인스턴스 제거 및 아이콘 복구
```

### 적용된 기본 정책

- 도구별 실물 인스턴스는 한 번에 하나
- 내용물이나 얼음이 든 용기는 도구장으로 반환 불가
- 서빙 잔은 동시에 하나만 사용
- 주문 종료 시 도구 자동 정리
- 반환이 거절된 용기는 가장 가까운 빈 슬롯으로 복귀

### 고정 자동 소환 제거 순서

현재 `BartendingSessionBuilder`의 고정 시작 도구를 먼저 삭제하면 제작 세션이 비게 된다.

1. 도구장 spawn/return 경로를 fallback과 함께 추가
2. 도구장에서 꺼낸 도구로 제작·서빙 검증
3. 고정 시작 도구 생성을 설정으로 비활성화
4. 회귀 테스트 후 fallback 제거 여부 결정

---

## 10. 평가 연결

| 요소 | 직접 평가 | 방식 |
| --- | --- | --- |
| 지거 사용 | 아니오 | 실제 재료 용량만 평가 |
| 바스푼 사용 | 아니오 | `Stir` 사용 여부를 점수 조건으로 사용하지 않음 |
| 셰이킹 | 예 | `Shake` 기법 수행 기록 |
| 셰이킹 중 얼음 | 예 | 별도 `WasShakenWithIce` 상태 필요 |
| 완성 잔의 얼음 | 예 | 최종 조성의 `HasIce` |
| 잔 종류 | 예 | `glassId` 비교 |
| 서빙 | 예 | 잔을 손님 영역에 드래그하여 제출 |

현재 시스템은 최종 `HasIce`와 `Shake` 기법은 표현할 수 있지만, 셰이킹 시점에 얼음이 있었는지는 별도 상태가 필요하다.

권장 상태 구분:

```text
CocktailComposition.HasIce
TechniqueState.HasShake
TechniqueState.WasShakenWithIce
GlassController.GlassId
```

---

## 11. 임시 잔 E2E 테스트

### 실제 물리 경로로 검증할 것

1. 병에서 잔으로 직접 붓기
2. 병에서 지거로 붓기
3. 지거에서 잔으로 옮기기
4. 셰이커에 재료와 얼음 넣기
5. 실제 흔들기 동작 수행
6. 셰이커에서 잔으로 옮기기
7. 잔 내부 액체와 얼음 유지 확인
8. 잔을 손님 영역으로 드래그
9. `ServeRequested` 1회 발생 확인
10. `BuildComposition()` 생성
11. `CocktailOrderEvaluator` Good/Mid/Bad 판정
12. 영업 또는 에피소드 결과로 전달

### 반드시 비교할 시나리오

- 바스푼을 사용한 조성과 사용하지 않은 동일 조성의 평가 결과가 같은가
- 액체는 섞였지만 셰이킹하지 않은 경우와 실제 셰이킹한 경우가 구분되는가
- 얼음 없이 셰이킹한 경우와 얼음을 넣고 셰이킹한 경우가 구분되는가
- 셰이커에는 얼음이 없고 완성 잔에만 얼음이 있는 경우가 구분되는가
- 네 종류 잔이 각각 올바른 `glassId`로 평가되는가
- 주문 종료 후 도구와 슬롯 상태가 다음 주문에 남지 않는가

### 이미지 회귀

- `tool_cabinet.png`가 Unity 임포트 후에도 2560×820인가
- 도구 17개가 모두 310×590인가
- 얼음 3개가 모두 70×70인가
- 원본과 Assets 파일의 해시 또는 바이트가 같은가
- 여러 해상도에서 이미지가 찌그러지지 않는가
- 클릭 및 드래그 영역이 그림과 일치하는가

---

## 12. 권장 구현 순서

1. 도구장·도구·얼음 아트를 원본 그대로 `Assets`에 반입한다.
2. importer 설정과 원본 크기 검증기를 만든다.
3. 기존 `DrawerArea`에 새 배경과 구획 레이아웃을 적용한다.
4. `ToolDef`, `GlassDef`, `ToolCabinetCatalog`를 추가한다.
5. `ToolCabinetController`와 drag-out/spawn/return 경로를 구현한다.
6. 지거 30/45 ml 기능과 렌더 레이어를 연결한다.
7. 코블러 셰이커 렌더 레이어와 기존 Shake 감지를 연결한다.
8. 바스푼 기능을 연결하되 평가에서는 제외한다.
9. 얼음통 20개, 잔량 이미지, 제빙기 충전을 연결한다.
10. 기존 고정 시작 도구를 새 도구장 경로로 점진적으로 대체한다.
11. `WasShakenWithIce` 등 부족한 평가 상태를 추가한다.
12. 임시 잔 4종으로 제작·서빙·평가 E2E를 검증한다.
13. 주문 반복 회귀를 통과한 뒤 병 데이터 보류 작업으로 돌아간다.

### 1차 완료 조건

- 기존 입력으로 도구장이 정상적으로 열린다.
- `tool_cabinet.png`의 비율과 원본 픽셀 크기가 유지된다.
- 상단 왼쪽에 도구가 표시된다.
- 상단 오른쪽에 잔이 표시된다.
- 하단 왼쪽은 빈 공간이다.
- 하단 오른쪽에서 얼음통을 충전할 수 있다.
- 지거 30/45 ml를 코드 수정 없이 데이터에서 바꿀 수 있다.
- 바스푼 사용 여부가 평가 결과를 바꾸지 않는다.
- 셰이킹과 셰이킹 중 얼음이 별도로 평가된다.
- 네 종류 잔 모두 제작·서빙·평가를 완료한다.
- 주문 종료 후 도구, 슬롯, 아이콘 상태가 초기화된다.
- 술장 배경과 기존 술장 기능에 회귀가 없다.

---

## 13. 병 이미지·데이터 후속 메모

### Strict context 동작

`LiquorBottleDef.HasContextVisuals`가 참이면 각 화면은 자기 컨텍스트 필드만 사용한다.

- 상점은 `shopBlankSprite`
- 술장은 `shelfLidSprite`
- 바테이블은 `barSprite`

필드가 null이어도 다른 컨텍스트 이미지나 레거시 sprite로 자동 대체하지 않는다.

### 레몬주스

사용자가 기존 레몬주스 원본 이미지를 쓰기로 결정했다.

현재 `Assets/Data/LiquorBottle/lemonJuice.asset` 상태:

- 기존 `sprite`는 보존됨
- `useContextImages = true`
- `shopBlankSprite = null`
- `shelfLidSprite = null`
- `barSprite = null`

따라서 현재 화면에서는 비어 보일 수 있다.

후속 병 작업에서 다음 중 하나로 수정해야 한다.

1. 기존 레몬주스 sprite를 세 컨텍스트 필드에 명시적으로 연결
2. 레몬주스만 strict context 모드를 해제하여 기존 sprite fallback 사용

목표 결과는 기존 레몬주스 원본 이미지가 다시 표시되는 것이다.

### 대표 불일치

| 항목 | 상태 | 처리 |
| --- | --- | --- |
| `lemon_juice` | 기존 이미지가 있으나 새 컨텍스트 필드가 비어 있음 | 기존 원본 사용으로 보정 필요 |
| `item_1023` 꿀 | 데이터는 있으나 새 아트 없음 | 비워 둠 |
| `minutefizz` | 3종 아트는 있으나 대응 데이터 없음 | 에셋만 보존 |
| `lans_whiskey` | `ItemDef`만 있고 대응 `LiquorBottleDef`와 새 아트 없음 | 기준 데이터 결정 전 미연결 |
| 레거시 병 10종 | `LiquorBottleDef`는 있으나 같은 ID의 `ItemDef` 없음 | 액체 데이터 추정 생성 금지 |

상세 문서:

`docs/bartending-art-data-mismatches.md`

---

## 14. 브리즈 보드카 다음 위스키 소환 실패

현재 `TryPlaceBottleFromShelf`는 서로 다른 병을 하나만 허용하도록 작성되어 있지 않다.

실패 가능한 주요 분기:

- 같은 병 ID가 이미 테이블에 있음
- 해당 `LiquorBottleDef.id`와 같은 `ItemDef.id`가 없음
- 연결된 `ItemDef`가 Bottle 타입이 아님
- 재고가 0
- 테이블에 빈 슬롯이 없음
- 병 prefab 생성 실패

현재 `lans_whiskey`는 `ItemDef`는 있으나 대응 `LiquorBottleDef`가 없다.

따라서 다음에 이 문제를 조사할 때는 먼저 Unity Console의 정확한 경고를 기록한다.

```text
[LiquorShelf] <failure message>
```

확인 순서:

1. 클릭한 위스키 슬롯의 `LiquorBottleDef`가 무엇인지 확인
2. `LiquorBottleDef.id` 확인
3. `Resources/Items`에서 같은 ID의 `ItemDef` 확인
4. `ItemDef.type == Bottle` 확인
5. 재고 확인
6. 현재 8개 `sessionSlots`의 점유 상태 확인
7. `FindRightmostFreeSlot()` 결과 확인

도구장이 도입되면 고정 도구가 슬롯을 선점하는 현재 구조도 함께 바뀌므로, 슬롯 점유 회귀를 반드시 테스트한다.

---

## 15. CSV와 기준 데이터 구조

사용자가 제공한 파일:

`C:\Users\boguk\Downloads\Data_slainte.csv - 재료.csv`

현재 결정:

- Planning 에셋은 새 기준점으로 확정하지 않았다.
- 새 CSV를 기존 데이터에 바로 덮어쓰는 구조도 확정하지 않았다.
- 기존 `ItemDef`, `LiquorBottleDef`, `ItemData`의 중복 구조를 먼저 정리해야 할 가능성이 있다.
- 이 구조 개편은 도구장 작업 이후로 보류했다.

나중에 재개할 때 먼저 정할 것:

1. 재료 데이터의 최종 source of truth
2. CSV가 에디터 임포트 입력인지 런타임 데이터인지
3. `ItemDef`와 `LiquorBottleDef`를 통합할지 직접 참조로 연결할지
4. 기존 ID와 Planning ID를 어떻게 마이그레이션할지
5. 기존 레거시 에셋을 언제 제거할지

현재 도구장 구현은 이 결정에 종속되지 않도록 `ToolDef`와 `GlassDef`를 별도 안정 ID로 만드는 것이 안전하다.

---

## 16. 적용된 UX 정책

안전 구현 승인에 따라 다음 정책을 적용했다.

- 도구별 실물 한 개
- 내용물 또는 얼음이 든 용기 반환 금지
- 반환 시 내용물을 자동 폐기하지 않음
- 서빙 잔 한 개
- 주문 종료 시 자동 정리
- 거절된 반환은 월드 오브젝트를 파괴하지 않고 빈 슬롯으로 복귀

---

## 17. 주요 파일

### 도구장과 씬

- `Assets/BusinessScene.unity`
- `Assets/Scripts/CameraMove/FrontCameraRig.cs`
- `Assets/Scripts/DragandDrop/DrawerUI.cs`
- `Assets/Scripts/DragandDrop/UIItemDraggable.cs`
- `Assets/Scripts/DragandDrop/UIDropSlot.cs`

### 바텐딩 세션과 도구

- `Assets/Scripts/Bartending/BartendingSessionBuilder.cs`
- `Assets/Scripts/Bartending/BusinessBartendingBootstrap.cs`
- `Assets/Scripts/Bartending/BusinessBartendingSettings.cs`
- `Assets/Scripts/Bartending/SlotController.cs`
- `Assets/Scripts/Bartending/BeakerController.cs`
- `Assets/Scripts/Bartending/CobblerShakerTechniqueController.cs`
- `Assets/Scripts/Bartending/StirringRodController.cs`
- `Assets/Scripts/Bartending/IceBinController.cs`

### 잔과 평가

- `Assets/Scripts/Bartending/GlassController.cs`
- `Assets/Scripts/Bartending/GlassCollisionProfile.cs`
- `Assets/Scripts/Bartending/VesselLiquidTracker.cs`
- `Assets/Scripts/Bartending/CocktailEvaluator.cs`
- `Assets/Scripts/Business/BusinessOrderSessionController.cs`
- `Assets/Scripts/Business/EpisodeCraftingBridge.cs`

### 병과 데이터

- `Assets/Scripts/LiquorShelf/LiquorBottleDef.cs`
- `Assets/Scripts/LiquorShelf/LiquorBottleSlotUI.cs`
- `Assets/RestScene/Scripts/ItemSlotUI.cs`
- `Assets/Scripts/Bartending/BottleController.cs`
- `Assets/Scripts/Bartending/ItemDefCatalog.cs`
- `Assets/Editor/BartendingBottleArtSetup.cs`
- `Assets/Editor/PlanningCsvAssetImporter.cs`

### 관련 문서

- `docs/bartending-art-data-mismatches.md`
- `docs/cocktail-csv-evaluation-handoff.md`
- `docs/gameplay/bartending-systems.md`
- `docs/implementation-plan.md`

---

## 18. 원본 기획 자료

- `C:\Users\boguk\Downloads\[슬런챠]개발 기획 (4).pdf`
  - 도구를 도구장에서 바테이블로 드래그
  - 셰이킹 평가
  - 바스푼 사용은 평가에 직접 영향 없음
  - 완성 잔 얼음과 셰이킹 중 얼음 평가
  - 잔 종류 평가와 손님에게 서빙

- `C:\Users\boguk\Downloads\[슬런챠]개발 기획 (5).pdf`
  - 얼음 이미지
  - 얼음통 기본 용량 20
  - 잔량 이미지 구간
  - 제빙기 충전
  - 바테이블과 하부 수납 컨셉

- `C:\Users\boguk\Downloads\아트\아트\`
  - 도구장 배경
  - 도구 이미지
  - 병 이미지
  - 얼음 이미지
  - 임시 잔 이미지

---

## 19. 다음 세션용 시작 문구

다음 내용을 새 세션에 그대로 전달할 수 있다.

> `docs/slainte-bartending-tool-cabinet-handoff-2026-08-19.md`를 먼저 읽어라. 이번 작업은 술장이 아니라 `BusinessScene`의 기존 임시 도구장 `DrawerArea`를 완성하는 것이다. 술장 배경은 건드리지 말고 이미지 원본 크기를 변경하지 마라. 왼쪽 아래는 빈 공간, 지거는 30/45 ml 데이터화, 바스푼은 평가에 직접 반영하지 않는다. 구현 전에 현재 씬과 코드를 다시 검사하고 문서와 다른 점을 먼저 보고하라.

---

## 20. 현재 최종 체크포인트

- 병 이미지와 임시 잔 관련 코드 및 아트는 프로젝트에 존재한다.
- 레몬주스 기존 이미지 복원은 아직 남아 있다.
- 브리즈 보드카 다음 위스키 소환 실패는 정확한 Console 경고를 기준으로 재조사해야 한다.
- 도구장 배경·도구·얼음 아트는 원본 그대로 Assets에 반입되어 있다.
- 도구장 데이터, UI, 월드 생성·반환, 지거, 셰이커, 바스푼, 얼음통, 동적 잔·서빙·평가 연결이 구현되어 있다.
- 기존 `DrawerArea`와 `FrontCameraRig` 개폐 구조 및 주류 선반은 유지된다.
- C# 런타임·에디터 컴파일과 정적 아트 계약은 통과했다.
- 다음 작업은 Unity Refresh 후 `BusinessScene` 플레이 모드 E2E 및 해상도별 시각 QA다.
