# 바텐딩 시스템

BusinessScene의 CraftingMode에서 동작하는 도구 인터랙션과 액체 입자 시뮬레이션 시스템입니다.

---

## 바텐딩 도구 (`Assets/Scripts/Bartending/`)

### IBartendingItem

모든 바텐딩 도구가 구현하는 공통 인터페이스.

```csharp
interface IBartendingItem
{
    GameObject GameObject { get; }
    bool IsPickedUp { get; }
    void SnapToSlot(Transform slotTransform, SlotController slot);
    void OnPickedUp();
    void OnDropped();
}
```

### 도구 컨트롤러

| 클래스 | 역할 | 상태 열거형 |
|---|---|---|
| `GlassController` | 칵테일 잔 | `GlassState` (Idle / PickedUp / Tilting / Returning) |
| `BeakerController` | 비커 (셰이커) | `BeakerState` (Idle / PickedUp / Tilting / Returning) |
| `BottleController` | 술병 | — |

세 컨트롤러 모두 `[ExecuteInEditMode]` + `[RequireComponent(EdgeCollider2D, Collider2D)]` 선언.
**EdgeCollider2D**로 용기 내부 물리 경계를 프로시저럴하게 생성 — `edgeRadius` 두께를 부여해 Discrete 충돌 감지에서도 액체 입자 관통을 방지합니다.

**인터랙션 흐름**

1. LMB 클릭 → `Idle → PickedUp`: 드래그, `SortingOrder += PICKUP_SORTING_ORDER_BASE(100)`
2. 드래그 중 Slot 영역 호버 → `SlotController` 하이라이트
3. 드롭 → 슬롯 위이면 `SnapToSlot()`, 없으면 원위치
4. PickedUp 상태에서 마우스 좌우 이동 → `Tilting`: 기울이기로 따르기 시뮬레이션
5. 마우스 업 → `Returning`: `AnimationCurve` 이즈로 원래 각도 복귀

**공통 Inspector 필드**

```csharp
[Header("형태 설정")]
float bottomWidth, topWidth, height, cornerRadius;
int curveSegments;

[Header("물리 설정")]
float edgeRadius;        // EdgeCollider2D 두께 (관통 방지)
float colliderYOffset;   // 콜라이더 Y축 오프셋

[Header("인터랙션 설정")]
float maxTiltAngle;      // 최대 기울기 각도
float tiltSensitivity;
float returnSpeed;
AnimationCurve returnEase;
LayerMask slotLayer;     // 빈 값이면 "Slot" 레이어를 자동 탐색
```

### SlotController

`BoxCollider2D(Trigger)` 기반 도구 거치대.

| 메서드 | 설명 |
|---|---|
| `Occupy(item)` | 슬롯 점유 및 하이라이트 초기화 |
| `Vacate()` | 점유 해제 |

트리거 Enter/Stay/Exit에서 픽업 중인 도구에만 에메랄드 녹색(`#66FF66, alpha 0.5`) 하이라이트를 표시하며, 이미 점유된 슬롯에는 하이라이트 없음.

### 프리팹 (`Assets/Prefabs/`)

| 프리팹 | 비고 |
|---|---|
| `Glass.prefab` | GlassController 부착 |
| `Beaker.prefab` | BeakerController 부착 |
| `Bottle.prefab` | BottleController 부착 |

---

## MetaballFluid 시스템 (`Assets/MetaballFluid/`)

2D 메타볼 알고리즘으로 칵테일 액체를 실시간 시각화합니다.
CraftingMode에서만 `BubblePanel`이 활성화되어 화면 최상단에 렌더링됩니다.

### 렌더링 구조

```
별도 Camera (출력: WaterRT RenderTexture)
  └─ 액체 입자 레이어만 촬영
       └─ FullScreenQuad (MetaballMat 적용)
            └─ MetaballShader.shadergraph
                  입자들을 메타볼 알고리즘으로 뭉쳐 보이게 처리
```

`FullScreenQuad`는 orthographic 카메라 기준으로 매 프레임 전체 화면 크기를 재계산합니다.
`sortingLayerName`과 `sortingOrder(기본 12)`로 다른 UI 위에 렌더링 순서를 제어합니다.

### 스크립트

| 스크립트 | 클래스명 | 역할 |
|---|---|---|
| `ObjPooling.cs` | `LiquidPool` | 최대 `poolSize(900)` 입자 오브젝트 풀 관리. 소진 시 스킵 |
| `Spawner.cs` | `LiquidSpawner` | `spawnInterval`마다 풀에서 입자 꺼내 스폰, 초기 속도 0 보장 |
| `LiquidReaction.cs` | `LiquidReaction` | 입자 충돌 시 색상·물리 속성 평균화 혼합, 수면 최적화 |
| `ReturnToPool.cs` | `ReturnToPool` | 화면 밖(OOB) 입자 자동 풀 반환 |
| `FullSizeQuad.cs` | `FullScreenQuad` | MetaballMat 적용 전체화면 쿼드, 해상도 변화에 실시간 대응 |
| `drag.cs` | — | 마우스 드래그 가능한 물리 오브젝트 |

### LiquidReaction 최적화 레이어

1. `isLogicallySleeping` 상태인 입자는 충돌 연산을 주도하지 않음
2. `reactionCooldown`으로 동일 프레임 내 중복 연산 방지
3. `TryGetComponent`로 충돌 대상 타입 체크
4. 색상과 mass가 이미 거의 동일(`< 2% 차이`)하면 물리 속성 덮어쓰기 스킵

수면 조건: `rb.linearVelocity.sqrMagnitude < sleepVelocityThreshold` 상태가 `timeToSleep(2.0s)` 지속.
수면 해제: `WakeUp()` 명시 호출 또는 다른 활성 입자의 충돌.

### 프리팹 (`Assets/MetaballFluid/Prefabs/`)

| 프리팹 | 설명 |
|---|---|
| `water_particle` | 기본 액체 입자 (흰색) |
| `BlueLiquid`, `redLiquid`, `greenLiquid` | 색상별 입자 변형 |
| `BubblePanel` | 렌더링 패널 전체 (Camera + LiquidPool + FullScreenQuad 포함) |
