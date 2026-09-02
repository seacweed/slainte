# 바텐딩 시스템

BusinessScene의 CraftingMode에서 동작하는 도구 인터랙션과 액체 입자 시뮬레이션 시스템입니다.

---

## 바텐딩 도구 (`Assets/_Project/Features/Bartending/Runtime/`)

### 영업 도구장

`BusinessBartendingSettings.useToolCabinet`이 활성화되고 Resources 카탈로그가 유효하면 영업 제조 세션은 고정 도구 대신 `DrawerArea` 도구장을 사용한다. 카탈로그가 없거나 손상되면 기존 고정 Beaker/Shaker/Glass/IceBin 생성으로 폴백한다.

- `ToolCabinetController`: 기존 서랍 배경·구획 UI, 도구장 슬롯 표시, 클릭 집기, 반환, 얼음 충전
- `BusinessBartendingBootstrap`: 세션별 도구장 `SlotController`와 월드 인스턴스 등록·정리, 동적 서빙 잔 교체
- `ToolDef` / `GlassDef` / `ToolCabinetCatalog`: 도구·잔 데이터와 아트 연결
- `ToolCabinetWorldFactory`: 지거·셰이커 레이어, 바스푼, 얼음통, 임시 잔 월드 오브젝트 생성

이동 가능한 도구와 잔은 세션 시작 시 각각 실제 `SlotController` 하나를 점유한다. 도구장 아이콘 드래그로 새 오브젝트를 생성하지 않는다. 슬롯을 클릭하면 같은 인스턴스를 `Vacate`하고 즉시 집으며, 자기 도구장 슬롯에 반환하면 `Occupy`와 `SnapToSlot`을 그대로 사용한다. 내용물과 얼음은 반환할 때 폐기하지 않는다. 기존 바테이블 슬롯 코드와 슬롯 목록은 변경하지 않으며 도구장 슬롯도 그 목록에 섞지 않는다.

정책은 도구별 1개, 서빙 잔 1개, 주문 종료 자동 정리다. 지거는 데이터 기반 30/45 ml 전환과 용량 차단을 사용한다. 바스푼은 물리 혼합만 수행하며 평가 기법을 기록하지 않는다. 얼음통은 이동 슬롯과 분리된 기존 전용 앵커를 유지하고 최대 20개이며 하단 오른쪽 제빙 영역에서 충전한다.

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
| `CobblerShakerTechniqueController` | 스트레이너 일체형 코블러 셰이커의 흔들기 감지와 `Shake` 기록 | `BeakerController` 상태 사용 |
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

### 프리팹 (`Assets/_Project/Features/Bartending/Prefabs/Equipment/`)

| 프리팹 | 비고 |
|---|---|
| `Glass.prefab` | GlassController 부착 |
| `Beaker.prefab` | BeakerController 부착 |
| `CobblerShaker.prefab` | BeakerController + CobblerShakerTechniqueController 부착, 스트레이너 일체형 |
| `Bottle.prefab` | BottleController 부착 |

코블러 셰이커는 집어 든 상태에서 빠른 왕복 이동의 방향 전환을 감지한다. 기준 횟수를 넘으면 내부 액체 입자에 `CocktailTechnique.Shake`를 기록한다. 스트레이너는 코블러 뚜껑에 일체형인 것으로 취급하므로 별도 스트레이너 도구를 제조 화면에 추가하지 않는다.

### 얼음 (`IceCubeController` / `IceBinController`)

얼음은 잔의 상태 값이 아니라 `LiquidParticleData`와 동일한 방식으로 `VesselLiquidTracker`가 소유권을 추적하는 실제 오브젝트다. `IceBinController`는 최대 20개의 `IceCubeController` 인스턴스를 관리하는 제빙 영역이며, 이동 슬롯과 분리된 전용 앵커를 사용한다(위 "영업 도구장" 절 참고).

### 코블러 셰이커 스트레이너 충돌

`CobblerShakerTechniqueController`는 스트레이너·캡 장착 상태에 따라 콜라이더 3종을 조합해 액체·얼음의 통과 여부를 각각 제어한다.

| 콜라이더 | 역할 |
|---|---|
| `IceOnlyVesselBarrier`(자식 오브젝트, `BoxCollider2D`) | 얼음만 걸러내고 액체는 통과 |
| 좌우 `EdgeCollider2D` 가이드 | `cobbler_strainer.png`의 불투명 내부 경계 픽셀 좌표를 기준점으로 정렬. 좌표를 못 찾으면 비율 기반 폴백 사용 |
| `__CobblerCapBarrier`(`BoxCollider2D`) | 캡 장착 시 출구 자체를 막음 |

스트레이너/캡 장착 상태가 바뀔 때마다 `VesselLiquidTracker.RefreshCollisionGeometry()`를 다시 호출해 입자·얼음의 `Physics2D.IgnoreCollision` 관계를 갱신한다.

### 크기 변환 규칙

QHD(`2560×1440`) 화면에서 도구·잔·병·얼음은 원본 `Sprite` 픽셀 캔버스를 `BartendingNativeSpriteSizer.TryMatchRootToSprite()`로 먼저 월드 크기에 맞춘 뒤, 호출부(병 배치, 얼음 스폰 등)가 공통 배율 `0.7`을 추가로 곱한다. 슬롯 크기에 맞추기 위해 별도로 축소하지 않는다. 필터 모드는 `Bilinear`로 고정한다 — 배율이 정수가 아니고(0.7) 회전도 발생하므로 `Point` 필터는 계단 현상이 두드러진다.

### 병 따르기 (`BottleController`)

프레임 기반 타이머 대신 `pourMlPerSecond`(용기별 필드, `Bottle.prefab` 실제값 `83.33 ml/s`)로 초당 배출량을 계산해 입자를 스폰한다. 프레임당 최대 `maxParticlesPerFrame`(기본 8개)까지만 한 번에 catch-up 스폰해 프레임 드랍 시 순간 폭발적 스폰을 막는다. 병 내부 `currentCapacity`가 소진되면 더 이상 부피를 차감하지 않는다.

---

## 액체 의미 데이터 계층 (`Assets/_Project/Features/Bartending/Runtime/Liquid/`)

MetaballFluid(아래 절)는 순수 시각 레이어이고, 판정에 쓰이는 실제 재료 구성은 이 계층이 담당한다.

- `LiquidPayload`: 입자 하나가 담는 재료 구성. `portions`(재료별 `volumeMl` 리스트), `temperatureC`, `techniques`(`CocktailTechnique` 플래그), `wasShakenWithIce`를 가진다. **색상은 상태로 저장하지 않고 `EvaluateColor()`가 `portions`의 volume 가중 평균으로 매 호출 계산하는 파생값이다** — 개별 컴포넌트는 이 값을 그대로 `SpriteRenderer.color`에 반영할 뿐 직접 색을 설정하지 않는다.
- `LiquidPayload.MixPair(left, right, strength)`: 두 입자가 충돌했을 때 온도·기법·재료 비율을 `strength` 세기로 평형에 가깝게 보간한다. `LiquidReaction`(MetaballFluid)이 물리 충돌 시점에 호출한다.
- `LiquidParticleData`(MonoBehaviour): 입자 하나 = `payload` 하나. `DefaultVolumeMl`은 스폰 시 이 입자가 나타내는 재료 ml. `VesselOwner`로 현재 소속된 `VesselLiquidTracker`를 추적한다.
- `VesselLiquidTracker`(`[RequireComponent(Collider2D)]`): 잔·비커·셰이커 등 용기에 부착. 트리거 콜라이더 진입/유지 시 입자·얼음(`IceCubeController`)의 소유권을 점유(`TryAssignVesselOwner`)하며, 콜라이더가 겹치는 용기가 여러 개면 `interactionPriority`가 더 높은 쪽이 우선한다. 소유권이 다른 용기 소속 입자·얼음끼리는 `Physics2D.IgnoreCollision`으로 물리 충돌 자체를 차단해 서로 다른 용기의 내용물이 섞이지 않게 격리한다.
- `VesselLiquidTracker.BuildComposition()`: 현재 추적 중인 입자·얼음을 모아 `CocktailComposition`(재료별 합산 volume, 부피 가중 평균 온도, 잔 종류, 얼음 개수, 기법 플래그, `WasShakenWithIce`)을 생성한다 — 판정과 최종 색 표시는 모두 이 스냅샷을 사용한다.
- `CocktailComposition.EvaluateFinalColor()`: `LiquidPayload.EvaluateColor()`를 재사용하며, `Add()` 호출 시 dirty 플래그를 세워 다음 조회 때만 재계산한다(캐시).

## 레시피 판정 (`CocktailEvaluator.cs`)

`CocktailEvaluator`는 `CocktailComposition`을 받아 주문 가능한 기본 레시피와 정확히 일치하는지 검사한다.

- 재료별 목표량과 총량은 고정 `±5 ml` 경계를 포함해 검사한다.
- 허용되지 않은 추가 재료는 양과 관계없이 실패한다.
- 숫자 점수와 유사도 판정은 사용하지 않는다. 기본 레시피가 모두 맞으면 `Good`, 핵심 배합·기법이 맞고 완성 잔/얼음만 다르면 `MidGlass`/`MidIce`/`MidIceGlass`, 다른 주문 가능 기본 레시피가 정확히 맞으면 `MidWrongMenu`, 나머지는 `Bad`다.
- `Build`는 별도 기법 미사용, `Stir`는 바스푼의 유효 혼합 0.35초 이상 시도 및 1초 이상·평균 조성 편차 10% 이하 완료, `Shake`는 닫힌 셰이커의 기존 동작 조건으로 구분한다.

## 레시피 에셋 (`CocktailRecipeDataLoader.cs`)

`CocktailRecipeDataLoader`는 `Resources/Bartending/Recipes`의 `CocktailRecipeDef`만 읽는다. 기준 원본은 `Features/Bartending/Content/Source/Planning`의 `recipes.csv`와 `recipe_ingredients.csv`이며, 에디터 임포터가 두 파일을 기본 레시피 에셋으로 변환한다. `StreamingAssets/Bartending`의 샘플 CSV는 개발·검증용이고 제품 런타임 레시피 소스가 아니다.

- 기본 레시피 21종이 모두 주문 가능
- 정확한 기본 레시피는 `Good`
- 핵심 배합·기법이 맞고 잔·얼음만 다르면 별도 에셋 없이 `MidGlass`/`MidIce`/`MidIceGlass`로 판정
- 다른 기본 레시피가 정확히 감지되면 `MidWrongMenu`
- 레시피 도수는 명시 값이 없을 때 `Σ(재료 용량 × 재료 ABV) / 총 용량`으로 계산
- 무작위 주문은 `isOrderable` 레시피만 선택
- `Resources/Bartending/Recipes`에는 기본 레시피 21개와 `TasteMoodPalette.asset`만 두며 결과별 파생 레시피는 만들지 않음

---

## MetaballFluid 표현 인프라 (`Assets/_Project/Features/Bartending/Infrastructure/MetaballFluid/`)

2D 메타볼 알고리즘으로 칵테일 액체를 실시간 시각화합니다.
실행 코드는 Bartending 도메인 타입에 직접 의존하므로 `Runtime/Liquid`이 소유하고,
이 폴더에는 셰이더·머티리얼·액체 프리팙·물리 에셋만 남깁니다.

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

### 런타임 스크립트 (`Assets/_Project/Features/Bartending/Runtime/Liquid/`)

| 스크립트 | 클래스명 | 역할 |
|---|---|---|
| `Particles/LiquidPool.cs` | `LiquidPool` | `poolSize(900)`만큼 입자를 미리 생성하고, 캐시가 소진되면 필요한 만큼 추가 생성. 활성 입자를 `(GameObject, LiquidParticleRecycler, LiquidReaction)` 캐시 구조로 관리 |
| `Particles/LiquidSpawner.cs` | `LiquidSpawner` | `spawnInterval`마다 풀에서 입자 꺼내 스폰, 초기 속도 0 보장 (테스트용 — 실제 게임 내 pour는 `BottleController.TrySpawnLiquid()`가 `pool.DefaultParticleVolumeMl`만큼씩 직접 `GetParticle()` 호출) |
| `LiquidReaction.cs` | `LiquidReaction` | 입자 충돌 시 색상·물리 속성 평균화 혼합, 수면 최적화, 확산(Agitation) 믹싱 |
| `Particles/LiquidParticleRecycler.cs` | `LiquidParticleRecycler` | 화면 밖(OOB) 입자 자동 풀 반환. 용기 밖 유출 입자는 왼쪽/오른쪽/아래 화면 이탈 시 즉시 회수(위쪽은 제외) |
| `Rendering/FullScreenQuad.cs` | `FullScreenQuad` | MetaballMat 적용 전체화면 쿼드, 해상도 변화에 실시간 대응 |
| `Runtime/Interaction/DraggableBar.cs` | `DraggableBar` | 마우스 드래그 가능한 물리 오브젝트 |

### 두 가지 믹싱 경로

`LiquidReaction`은 서로 다른 두 트리거로 재료를 섞는다 — 같은 쌍이 한 틱에 이중으로 섞이지 않도록 `WasRecentlyMixedWith()`로 서로를 인지한다.

- **물리 충돌 기반** (`OnCollisionEnter2D` → `TryMixCollision`): 실제 콜라이더 접촉 시 `mixSpeed` 세기로, `reactionCooldown`(기본 0.05s)마다 최대 1회.
- **확산(Agitation) 기반** (`FixedUpdate` → `TryMixNearbyParticlesByAgitation`): `agitationMixInterval`(기본 0.05s)마다 반경 `agitationMixRadius`(0.16) 내 이웃을 `Physics2D.OverlapCircle`로 스캔(버퍼 32개)해, 상대 입자와의 상대 속도가 `agitationVelocityThreshold`를 넘으면 `agitationMixSpeed` 세기로 최대 `agitationMaxPartners`(3)명까지 혼합. 서로 다른 쌍을 양쪽에서 중복 처리하지 않도록 `GetInstanceID()`가 더 작은 쪽만 판정·믹싱을 수행한다.

### LiquidReaction 최적화 레이어

1. `isLogicallySleeping` 상태인 입자는 충돌·확산 연산을 주도하지 않음
2. `reactionCooldown`/`agitationMixInterval`으로 동일 상대 대상 중복 연산 방지, 두 믹싱 경로 간에도 최근 믹싱 상대를 공유해 이중 처리 방지
3. `TryGetComponent`로 충돌 대상 타입 체크
4. 색상과 mass가 이미 거의 동일(`< 2% 차이`)하면 물리 속성 덮어쓰기 스킵
5. `WakeUp()` 시 `nextAgitationMixTime`에 `[0, agitationMixInterval)` 랜덤 지터를 부여 — 같은 물리 프레임에 스폰/재활성화된 입자들이 매 틱 영구적으로 같은 프레임에 몰려 부하 스파이크를 만드는 것을 방지

수면 조건: `rb.linearVelocity.sqrMagnitude < sleepVelocityThreshold` 상태가 `timeToSleep(2.0s)` 지속.
수면 해제: `WakeUp()` 명시 호출 또는 다른 활성 입자의 충돌/혼합.

### LiquidParticleRecycler 회수 규칙

`ShouldRecycle()`는 매 프레임 `LiquidPool.Update()`가 한 번 계산한 `LiquidScreenBounds`(정사영 카메라의 `orthographicSize`/`aspect` 기반)와 `deltaTime`을 받아 판정한다.

1. `bottomLimit`/`topLimit`/`horizontalLimit` 하드 박스를 넘으면 용기 소속 여부와 무관하게 즉시 회수(4방향 전부).
2. `recycleUncontainedParticles`가 켜져 있고 용기(`VesselOwner`)가 없는 입자는:
   - 왼쪽/오른쪽/아래로 화면을 이탈하면 **정지 유예 없이 즉시 회수** (위쪽은 이 규칙에서 제외 — 기존 하드 박스만 적용).
   - 그 외엔 `settledOutsideRecycleDelay`(5s) 동안 정지 상태이거나 `maxOutsideVesselLifetime`(20s)이 지나면 회수하는 기존 유예 규칙 유지.

> **성능 참고**: 쉐이킹 중 렉의 실측 지배 비용은 스크립트가 아니라 Unity 네이티브 `Physics2D.FindNewContacts`(브로드페이즈)이며, 동시 활성 파티클 수(N)에 좌우된다. N을 줄이는 조치(위 화면 이탈 즉시 회수, `defaultVolumeMl` 상향)가 스크립트 로직 최적화보다 체감 효과가 훨씬 크다 — 최적화 작업 전 Unity Profiler CSV로 실측 검증할 것.

### 프리팹 (`Assets/_Project/Features/Bartending/Infrastructure/MetaballFluid/Prefabs/`)

| 프리팹 | 설명 |
|---|---|
| `water_particle` | 기본 액체 입자 (흰색) |
| `BlueLiquid`, `redLiquid`, `greenLiquid` | 색상별 입자 변형 |

`BubblePanel.prefab`은 현재 코드·신 참조가 없고 삭제된 `KatanaZeroSpeech`를 참조하는
이전 프로토타입이므로 `Features/Bartending/Content/Legacy/Prototypes`로 분리했습니다.
