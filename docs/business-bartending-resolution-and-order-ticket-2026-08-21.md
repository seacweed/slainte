# BusinessScene 바텐딩 크기·메카닉·주문표 변경 설명서

- 기준일: 2026-08-21
- 대상 씬: `Assets/BusinessScene.unity`
- 기준 해상도: QHD `2560 × 1440`
- 목적: 오늘 확정한 크기 규칙과 메카닉을 다음 작업자가 임의로 되돌리지 않도록 기록
- 이전 문서와 충돌할 경우 이 문서의 최종 상태를 우선 적용

## 1. 최종 결론

이번 작업의 핵심은 다음과 같다.

1. 도구, 잔, 얼음통, 술병, 재료, 얼음은 원본 스프라이트의 픽셀 캔버스와 종횡비를 먼저 월드 크기에 반영한다.
2. 그 결과에 공통 배율 `0.7`을 적용한다.
3. 도구장 슬롯 크기에 맞추기 위해 월드 오브젝트를 별도로 축소하지 않는다.
4. 도구장 안에서는 기존 UI 아이콘을 표시하고, 실제 월드 아이템은 보관 상태로 숨긴다.
5. 잔 용량은 Martini/Rock `200 ml`, Highball/Hurricane `400 ml`를 유지한다.
6. 보이는 만수위 조정은 잔 Transform이 아니라 `water_particle`의 입자 크기와 입자당 용량으로 처리한다.
7. 코블러 셰이커의 스트레이너는 얼음을 막고 액체를 중앙 출구로 유도한다.
8. 주문표 본문은 `주문: 칵테일명`이 아니라 실제 주문 대사를 사용한다.
9. 스프라이트 필터는 전체 `Point` 전환을 하지 않고 `Bilinear`를 유지한다.

## 2. QHD 기준과 원본 크기의 의미

### 2.1 기준 해상도

`BusinessScene`의 `CanvasScaler` 기준 해상도는 실제로 `2560 × 1440`이다.

- UI Scale Mode: Scale With Screen Size
- Reference Resolution: `2560 × 1440`
- Match Width Or Height: `0.5`
- Reference Pixels Per Unit: `100`

`BusinessBartendingSettings.renderTextureSize`도 `2560 × 1440`을 기본값으로 가진다. 다만 실제 RenderTexture는 유효한 뷰포트 Rect가 있으면 그 화면 픽셀 크기를 따라 다시 생성된다.

### 2.2 원본 스프라이트 크기

여기서 "원본 크기"란 PNG의 불투명 픽셀만 잘라낸 크기가 아니라 `Sprite.rect` 전체 픽셀 크기를 뜻한다.

- 투명 여백을 제거하지 않는다.
- Sprite를 트림하거나 크롭하지 않는다.
- 원본 캔버스 안에서 도형이 차지하는 상대 위치와 크기를 유지한다.
- 종횡비를 강제로 슬롯 비율에 맞추지 않는다.

예를 들어 원본 Sprite 캔버스가 `310 × 590`이면 먼저 QHD 논리 캔버스의 `310 × 590 px`에 해당하는 월드 크기로 맞춘다. 이후 공통 배율 `0.7`을 적용하므로 최종 논리 캔버스 크기는 `217 × 413 px`에 해당한다.

### 2.3 월드 크기 변환 공식

`BartendingViewport.TryConvertCanvasPixelsToWorld()`가 논리 캔버스 픽셀을 월드 크기로 변환한다.

```text
screenPixelSize = sourceCanvasPixelSize × Canvas.scaleFactor
worldWidth      = cameraWorldWidth  × screenPixelWidth  / viewportScreenWidth
worldHeight     = cameraWorldHeight × screenPixelHeight / viewportScreenHeight
finalWorldSize  = convertedWorldSize × 0.7
```

실제 Transform 보정은 `BartendingNativeSpriteSizer`가 담당한다.

1. 참조 `SpriteRenderer.bounds`의 현재 월드 크기를 구한다.
2. 원본 `Sprite.rect.size`가 차지해야 할 목표 월드 크기를 구한다.
3. 루트 Transform의 X/Y Scale을 각각 보정한다.
4. 마지막에 데이터의 `worldScale`을 곱한다.

따라서 PPU만 믿고 고정 Scale을 사용하는 방식보다 Canvas, 카메라, 뷰포트 크기 변경에 안전하다.

## 3. 오브젝트별 최종 크기 규칙

| 대상 | 기준 Sprite | 최종 규칙 | 비고 |
| --- | --- | --- | --- |
| 지거 | 레이어 중 활성 참조 Sprite | 원본 픽셀 크기 × `0.7` | 30/45 ml 메카닉 유지 |
| 코블러 셰이커 | 활성 참조 Sprite | 원본 픽셀 크기 × `0.7` | 레이어 정렬과 스트레이너 상태 유지 |
| 바스푼 | 바스푼 Sprite | 원본 픽셀 크기 × `0.7` | 세로 반전 표시 유지 |
| 얼음통 | 얼음통 참조 Sprite | 원본 픽셀 크기 × `0.7` | 상태 Sprite 전환 유지 |
| Rock 잔 | `200rock` | 원본 픽셀 크기 × `0.7` | 200 ml |
| Martini 잔 | `200coc` | 원본 픽셀 크기 × `0.7` | 200 ml |
| Highball 잔 | `400high` | 원본 픽셀 크기 × `0.7` | 400 ml |
| Hurricane 잔 | `400hurricane` | 원본 픽셀 크기 × `0.7` | 400 ml |
| 술장 클릭으로 생성한 병 | 병의 바테이블용 Sprite | 원본 픽셀 크기 × `0.7` | 병 입구·따르기 로직 유지 |
| 얼음통에서 생성한 얼음 | 선택된 Ice Sprite | 원본 픽셀 크기 × `0.7` | Sprite 변경 후 Collider 재정렬 |

도구장 Resources 에셋의 최종 `worldScale`은 모두 `0.7`이다.

- `Jigger`
- `CobblerShaker`
- `BarSpoon`
- `IceBucket`
- `Rock`
- `Martini`
- `Highball`
- `Hurricane`

새 도구나 잔을 추가할 때도 `월드 생성 → 원본 Sprite 크기 보정 → worldScale 적용` 순서를 유지해야 한다.

## 4. 도구장 최종 동작

### 4.1 UI 아이콘 방식 유지

도구장은 월드 오브젝트를 직접 보이게 배치하는 방식으로 바꾸지 않았다. 최종 상태는 기존 UI 아이콘 방식이다.

- 슬롯에는 `Image` 레이어로 도구나 잔을 표시한다.
- 아이콘의 종횡비는 `preserveAspect`로 유지한다.
- 아이콘 크기는 실제 월드 아이템의 투영 크기를 기준으로 계산한다.
- 아이콘을 슬롯 Rect에 꽉 채우기 위해 월드 오브젝트 Scale을 바꾸지 않는다.

즉, 슬롯 안의 아이콘 크기 계산은 표시 전용이며 실제 도구, 잔, 술병, 재료의 월드 크기에 영향을 주지 않는다.

### 4.2 실제 아이템 보관 상태

도구장 슬롯은 실제 `SlotController` 점유 상태와 연결된다.

보관할 때:

1. 슬롯을 `Occupy`한다.
2. 실제 아이템을 슬롯 위치로 `SnapToSlot`한다.
3. Rigidbody2D 시뮬레이션을 멈춘다.
4. 실제 아이템과 내용물의 Renderer/Collider를 숨긴다.
5. UI 아이콘만 표시한다.

꺼낼 때:

1. 슬롯을 `Vacate`한다.
2. 저장했던 Renderer/Collider 상태를 복구한다.
3. Rigidbody2D 시뮬레이션을 복구한다.
4. 같은 월드 인스턴스를 집는다.

복제 아이템을 생성하는 구조가 아니므로 잔이나 셰이커 안의 액체·얼음 상태도 보존된다.

### 4.3 샌드박스 분리

개발용 `BartendingSandboxBootstrap`은 `useToolCabinetOverride: false`로 세션을 만든다. 도구장 변경 때문에 기존 샌드박스 고정 도구 테스트가 영향을 받지 않도록 분리한 것이다.

## 5. 잔 용량과 액체 입자

### 5.1 잔 데이터

| 잔 | `capacityMl` |
| --- | ---: |
| Martini | 200 ml |
| Rock | 200 ml |
| Highball | 400 ml |
| Hurricane | 400 ml |

이 값은 잔 종류와 주문 판정에서 사용하는 기준 용량이다. 잔 모양마다 내부 물리 면적이 다르므로 `capacityMl`에 도달한 순간 네 잔의 시각적 수면 높이가 완전히 같을 필요는 없다.

### 5.2 물 파티클 설정

`Assets/MetaballFluid/Prefabs/water_particle.prefab`의 최종 핵심 값은 다음과 같다.

| 속성 | 이전 | 현재 |
| --- | ---: | ---: |
| Transform Scale | `0.06` | `0.04` |
| `defaultVolumeMl` | `2.5` | `0.5` |

입자가 더 작아지고 한 입자가 나타내는 용량도 줄었다. 따라서 작은 잔에서도 수면과 용량 변화가 더 세밀해지고, 200/400 ml 구간을 맞추기 쉬워진다.

향후 만수위를 조정할 때 우선순위는 다음과 같다.

1. `water_particle.defaultVolumeMl` 조정
2. 필요할 경우 입자 Transform Scale 미세 조정
3. 잔별 판정 용량은 기획이 바뀌지 않는 한 유지
4. 만수위를 맞추기 위해 잔 Sprite나 월드 Scale을 변경하지 않음

입자당 용량을 줄이면 같은 총 ml를 표현하는 데 더 많은 입자가 필요하므로 성능과 풀 크기도 함께 확인해야 한다.

## 6. Bilinear와 Point 필터 결정

### 6.1 현재 설정

바텐딩 관리 대상 이미지는 다음 Import 설정을 사용한다.

- Filter Mode: `Bilinear`
- Compression: `Uncompressed`
- Mipmap: Off
- Sprite Mesh Type: `FullRect`
- Pixels Per Unit: `100`
- Wrap Mode: Clamp

바텐딩 RenderTexture도 `FilterMode.Bilinear`를 사용한다.

### 6.2 Bilinear에서 생길 수 있는 현상

- 원본 픽셀 경계가 약간 부드럽게 보일 수 있다.
- 얇은 선이 비정수 배율에서 조금 흐려질 수 있다.
- RenderTexture가 화면 해상도와 다른 크기로 표시되면 추가 보간이 발생할 수 있다.

하지만 현재 오브젝트는 `0.7`이라는 비정수 배율을 사용하고 회전도 한다. 이 환경에서는 Bilinear가 계단 현상과 회전 시 깜빡임을 줄이는 장점이 있다.

### 6.3 전체 Point 전환 시 발생하는 일

모든 Sprite를 `Point (no filter)`로 바꾸면 다음 현상이 생긴다.

- 픽셀 경계는 선명해진다.
- `0.7` 배율에서 픽셀 열과 행이 불균등하게 생략되어 외곽선 두께가 흔들릴 수 있다.
- 병과 도구를 회전할 때 계단 현상과 shimmering이 증가할 수 있다.
- Sprite만 Point로 바꾸고 RenderTexture가 Bilinear면 최종 화면은 다시 보간되므로 기대한 효과가 완전하지 않다.
- RenderTexture까지 Point로 바꾸면 UI와 월드 전체가 거칠게 보일 수 있다.

Point는 픽셀 아트, Pixel Perfect Camera, 정수 배율이 함께 적용될 때 적합하다. 현재 아트와 `0.7` 배율 구조에서는 전체 Point 전환을 권장하지 않는다.

결론적으로 해상도 문제는 필터 전체 전환보다 원본 픽셀 크기 보존과 월드 크기 계산을 바로잡는 방식으로 처리했다.

## 7. 코블러 셰이커 스트레이너 충돌

### 7.1 상태별 동작

| 스트레이너 | 캡 | 액체 | 얼음 |
| --- | --- | --- | --- |
| 장착 | 장착 | 완전 차단 | 완전 차단 |
| 장착 | 제거 | 중앙 스트레이너 출구로만 배출 | 스트레이너에서 차단 |
| 제거 | 제거 | 셰이커 입구 전체로 배출 | 셰이커 입구 전체로 배출 |

캡은 스트레이너가 없으면 장착 상태가 될 수 없다.

### 7.2 충돌 구성

스트레이너가 장착되면 세 종류의 충돌 구조가 활성화된다.

1. `IceOnlyVesselBarrier`
   - 셰이커 상단을 가로막는다.
   - 액체 파티클은 이 Collider와의 충돌을 무시한다.
   - 얼음은 충돌하므로 셰이커 안에 남는다.
2. 좌우 `EdgeCollider2D` 가이드
   - 액체가 스트레이너 옆면으로 빠져나가지 못하게 한다.
   - 돔 안쪽을 따라 중앙 목 부분으로 액체를 유도한다.
3. `__CobblerCapBarrier`
   - 캡이 장착되면 액체와 얼음을 모두 막는다.

액체를 순간이동시키거나 강제로 출구 위치에 옮기지 않는다. 기존 Rigidbody2D 물리를 유지한 채 Collider 형상만으로 흐름을 유도한다.

### 7.3 Sprite 정렬 기준

가이드 점은 `cobbler_strainer.png`의 `310 × 590` 원본 픽셀 좌표를 사용한다.

- 왼쪽: `(67,344) → (91,369) → (121,383) → (121,433)`
- 오른쪽: `(243,344) → (219,369) → (189,383) → (189,433)`

이 좌표를 Sprite Bounds와 현재 Transform을 통해 셰이커 로컬 좌표로 변환한다. Sprite 레이어를 찾지 못하면 셰이커의 `topWidth`와 `height`를 이용한 비율 기반 폴백 형상을 사용한다.

스트레이너나 캡 상태가 바뀌면 `VesselLiquidTracker.RefreshCollisionGeometry()`를 호출하여 기존 액체와 얼음의 충돌 무시 관계도 다시 계산한다.

## 8. 주문표 대사 표시

### 8.1 문제 원인

영업 주문 데이터에는 실제 주문 대사가 들어 있지만 `OrderTicketData.memo`의 기본값은 다음과 같이 저장되어 있었다.

```text
주문: 핫 테디
```

런타임 대사 Override가 전달된 주문은 `이름 + 실제 대사`가 표시되고, Override가 유실된 주문은 `이름 + 칵테일명`으로 폴백하여 주문마다 표시가 달라졌다.

### 8.2 최종 데이터 흐름

제조 진입 시 `BusinessOrderSessionController`가 다음 조건을 확인한다.

- 현재 주문이 실제 손님 주문으로 제시된 주문인지
- `CustomerSpawner.CurrentOrderData.key`와 현재 주문 키가 같은지

일치하면 `OrderTicketMemoFormatter.Build()`로 `CustomerOrderData.lines`의 대사를 순서대로 합쳐 `OrderTicketManager.Prepare(ticketKey, memoOverride)`에 다시 전달한다.

```text
CustomerOrderData.lines
    → OrderTicketMemoFormatter
    → OrderTicketManager memoOverride
    → OrderTicketUI.memoText
```

이후 `CustomerDialogueCsvImporter`를 실행할 때도 새 주문표 에셋의 기본 `memo`가 실제 주문 대사로 저장된다. 따라서 향후에는 런타임 Override가 없어도 정적 칵테일명으로 쉽게 되돌아가지 않는다.

프로젝트에는 `orderDialogueAuthored`가 켜져 있으면서 대사 목록이 의도적으로 빈 주문이 6개 있다. 이 경우 칵테일명으로 폴백하지 않고 주문표 본문을 빈 상태로 유지하는 것이 현재 데이터 계약이다.

## 9. 메카닉 보존 원칙

이번 크기 변경은 표현 크기를 수정하는 작업이며 다음 메카닉을 변경하지 않는다.

- 슬롯 점유와 해제
- 도구와 잔의 집기 및 드롭
- 내용물이 든 용기의 상태 보존
- 병의 뚜껑, 입구 위치, 따르기
- 지거 30/45 ml 전환과 유입 차단
- 잔 종류 판정
- 셰이크 판정과 `wasShakenWithIce`
- 완성 잔의 얼음 판정
- 바스푼 사용 가능 상태
- 서빙 및 주문 평가

크기 문제를 해결하기 위해 Collider나 판정 용량을 임의로 Sprite 슬롯 크기에 맞추면 안 된다. Collider는 각 오브젝트의 기존 생성·프로필 코드 또는 Sprite 정렬 코드가 갱신하도록 유지한다.

## 10. 주요 변경 파일

### 크기와 해상도

- `Assets/Scripts/Bartending/BartendingViewport.cs`
  - `BartendingNativeSpriteSizer` 추가
- `Assets/Scripts/Bartending/ToolCabinetWorldFactory.cs`
  - 도구와 잔의 원본 크기 보정 후 `worldScale` 적용
- `Assets/Scripts/Bartending/BusinessBartendingBootstrap.cs`
  - 바테이블 술병 원본 크기 보정 후 `0.7` 적용
- `Assets/Scripts/Bartending/IceBinController.cs`
  - 얼음통 원본 크기 보정 후 `worldScale` 적용
- `Assets/Scripts/Bartending/IceCubeController.cs`
  - 얼음 원본 크기 보정 후 `0.7`, Collider 재정렬
- `Assets/Editor/ToolCabinetAssetInstaller.cs`
  - 재생성 시에도 공통 `worldScale = 0.7`
- `Assets/Resources/Bartending/ToolCabinet/*.asset`
  - 도구와 잔 데이터 최종 Scale

### 액체와 용량

- `Assets/MetaballFluid/Prefabs/water_particle.prefab`
  - 입자 Scale과 `defaultVolumeMl`

### 도구장 최종 동작

- `Assets/Scripts/Bartending/ToolCabinetController.cs`
  - UI 아이콘과 실제 월드 크기 투영
- `Assets/Scripts/Bartending/ToolCabinetRuntimeTag.cs`
  - 보관/꺼내기 시 물리·표시 상태 관리
- `Assets/Scripts/Bartending/BartendingSandboxBootstrap.cs`
  - 샌드박스 도구장 Override 비활성화

### 스트레이너

- `Assets/Scripts/Bartending/CobblerShakerTechniqueController.cs`
  - 얼음 차단막, 액체 가이드, 캡 차단막
- `Assets/Scripts/Bartending/VesselLiquidTracker.cs`
  - 기존 `IceOnlyVesselBarrier` 액체 무시 계약 사용

### 주문표

- `Assets/Scripts/Business/BusinessOrderSessionController.cs`
  - 제조 진입 시 현재 주문 대사 재확정
- `Assets/Scripts/Conversation/Sell/CustomerSpawner.cs`
  - 주문 대사 기반 Memo Override 준비
- `Assets/Scripts/OrderTicket/OrderTicketManager.cs`
  - Memo Override 보존과 표시
- `Assets/Editor/CustomerDialogueCsvImporter.cs`
  - 주문표 기본 Memo를 실제 대사로 저장

### 검증

- `Assets/Editor/LiquorShelfSpawnValidator.cs`
  - 생성된 술병의 원본 대비 `0.7` 크기 확인
- `Assets/Editor/BartendingIceValidator.cs`
  - 얼음 검증 대기 시간과 실패 진단 강화

## 11. 검증 상태

현재 코드 기준 빌드 결과:

- `Assembly-CSharp.csproj`: 오류 0, 기존 직렬화 경고 9
- `Assembly-CSharp-Editor.csproj`: 오류 0, 경고 0

마지막 크기·스트레이너·주문표 변경 후 Unity 실제 플레이 E2E는 사용자가 직접 확인하기로 했으므로 자동 플레이 검증은 실행하지 않았다.

## 12. 수동 플레이 체크리스트

### 도구장과 크기

- [ ] QHD `2560 × 1440`에서 도구와 잔의 종횡비가 원본과 같은가
- [ ] 지거, 셰이커, 바스푼, 얼음통, 네 종류 잔이 동일한 `0.7` 규칙으로 보이는가
- [ ] 슬롯 아이콘이 실제 월드 아이템을 추가로 축소하지 않는가
- [ ] 모든 도구와 잔을 정상적으로 집고 놓을 수 있는가
- [ ] 도구장에 반환한 내용물 있는 용기가 상태를 유지하는가
- [ ] 술장에서 생성한 병도 원본 대비 `0.7`인가
- [ ] 얼음이 기존보다 지나치게 작거나 크게 보이지 않는가

### 액체와 잔

- [ ] Martini/Rock이 약 200 ml 기준으로 동작하는가
- [ ] Highball/Hurricane이 약 400 ml 기준으로 동작하는가
- [ ] 작은 물 파티클이 Collider를 빠져나가거나 과도하게 겹치지 않는가
- [ ] 입자 수 증가로 프레임 저하가 없는가

### 스트레이너

- [ ] 캡과 스트레이너가 모두 장착되면 완전히 밀폐되는가
- [ ] 캡만 제거하면 액체가 중앙 출구로 나오는가
- [ ] 같은 상태에서 얼음은 셰이커 안에 남는가
- [ ] 스트레이너 옆면으로 액체가 새지 않는가
- [ ] 스트레이너를 제거하면 기존 셰이커 입구 전체로 배출되는가
- [ ] 셰이커를 회전해도 Sprite와 가이드 Collider가 함께 정렬되는가

### 주문표

- [ ] 손님마다 주문표에 실제 주문 대사가 표시되는가
- [ ] 더 이상 일부 주문만 `주문: 칵테일명`으로 표시되지 않는가
- [ ] 여러 줄 주문 대사가 원래 순서대로 줄바꿈되는가
- [ ] 의도적으로 빈 대사 주문은 칵테일명 폴백 없이 빈 본문을 유지하는가

## 13. 후속 조정 시 주의점

- 전체 크기를 바꾸려면 개별 Prefab Scale이 아니라 `worldScale` 공통 정책부터 검토한다.
- 특정 Sprite만 이상하면 PNG의 전체 캔버스와 불투명 도형 위치를 먼저 확인한다.
- 도구장 슬롯 크기에 맞춰 월드 아이템을 다시 스케일링하지 않는다.
- 만수위는 우선 `defaultVolumeMl`로 조정하고 잔 Sprite 크기를 바꾸지 않는다.
- 스트레이너에서 액체가 막히면 메카닉을 우회하지 말고 출구 폭 또는 `strainerGuideEdgeRadius`만 미세 조정한다.
- 모든 Sprite를 Point로 바꾸려면 Pixel Perfect Camera, 정수 배율, RenderTexture 필터까지 한 묶음으로 별도 검증한다.
- 주문 대사 CSV를 다시 게시하면 패치된 Importer가 주문표 Memo도 실제 대사로 갱신한다.

