# Liquid Payload / Color Mixing Handoff

작성일: 2026-07-01

이 문서는 `Sample_Scene`에서 진행 중인 액체 기반 칵테일 판정/혼합 작업을 다른 환경의 Codex가 이어받을 수 있도록 정리한 인수인계 문서다. 현재 대화에서는 코드 파일을 직접 수정하지 않는 조건이 있었고, 이 `.md` 파일 생성만 허용되어 작성했다.

## 현재 목표

현재 목표는 액체 입자의 의미 데이터를 `LiquidPayload`로 들고 가고, 최종적으로 잔 안에 실제로 남아 있는 액체 입자들의 payload를 모아 레시피 평가에 사용하는 것이다.

핵심 설계 원칙:

- 술병 데이터의 원천은 `ItemDef`다.
- 술병에서 생성된 액체 입자는 `LiquidParticleData`를 가진다.
- `LiquidParticleData.payload`는 해당 입자의 현재 조성이다.
- 액체가 충돌/혼합되면 각 입자의 payload가 점진적으로 변한다.
- 색상도 별도 상태값이 아니라 payload에서 계산된 결과여야 한다.
- 투명한 잔/비커 안에서는 실제 액체 입자를 계속 보이게 두고, 컨테이너는 "저장소"가 아니라 필요 시 현재 입자를 감지하는 tracker 역할만 한다.
- 고객에게 제출했을 때, 제출된 잔 안에 남아 있는 실제 입자들의 payload를 집계해서 레시피 평가를 수행한다.

## 현재 관련 파일

- `Assets/Scripts/DragandDrop/ItemDef.cs`
- `Assets/Scripts/Bartending/BottleController.cs`
- `Assets/Scripts/Bartending/LiquidParticleData.cs`
- `Assets/MetaballFluid/Scripts/LiquidReaction.cs`
- `Assets/MetaballFluid/Prefabs/water_particle.prefab`
- `Assets/Scenes/Sample_Scene.unity`
- `Assets/Resources/Items/breeze_vodka.asset`
- `Assets/Resources/Items/lemon_juice.asset`

## 현재 코드 상태 요약

### ItemDef

`ItemDef`가 병/잔/도구를 모두 표현하는 공통 아이템 데이터다. 병 전용 데이터도 이미 들어 있다.

```csharp
public enum ItemType
{
    Bottle = 0,
    Glass = 1,
    Tool = 2
}

public enum TasteTag
{
    Sweet,
    Bitter,
    Sour,
    SweetSour,
    RichSweet,
    Neutral
}

public enum BottleCategory
{
    Spirit,
    Liqueur,
    Syrup,
    Juice,
    NonAlcohol,
    Other
}

public enum BottleLiquidType
{
    Vodka,
    Whisky,
    Rum,
    Gin,
    Brandy,
    Juice,
    Syrup,
    Other
}

[CreateAssetMenu(menuName = "Bartending/Item")]
public class ItemDef : ScriptableObject
{
    [Header("Common")]
    public string id;
    public string displayName;
    public ItemType type;
    public Sprite icon;
    public int price;

    [Header("Bottle Only")]
    public TasteTag tasteTag;
    public float abvPercent;
    public BottleCategory bottleCategory;
    public BottleLiquidType liquidType;
    public float capacityMl = 700f;
    public Color liquidColor = Color.white;
    public float density = 1f;

    [Header("Rules")]
    public bool dragMovesObject = false;
    public bool toolPlaceableOnTable = false;
}
```

현재 테스트용 에셋 상태:

- `breeze_vodka.asset`
  - `id: breeze_vodka`
  - `type: Bottle`
  - `tasteTag: Neutral`
  - `abvPercent: 40`
  - `bottleCategory: Spirit`
  - `liquidType: Vodka`
  - `capacityMl: 700`
  - `liquidColor: { r: 1, g: 1, b: 1, a: 0.40392157 }`
  - `density: 0.95`
- `lemon_juice.asset`
  - `id: lemon_juice`
  - `displayName: 레몬 주스`
  - `type: Bottle`
  - `tasteTag: Sour`
  - `abvPercent: 0`
  - `bottleCategory: Juice`
  - `liquidType: Juice`
  - `capacityMl: 300`
  - `liquidColor: { r: 1, g: 0.913274, b: 0.28490567, a: 0.5058824 }`
  - `density: 1.03`

### LiquidParticleData

현재 `LiquidParticleData.cs`에는 `LiquidPortion`, `LiquidPayload`, `LiquidParticleData`가 있다.

현재 구조:

- `LiquidPayload.portions`는 `List<LiquidPortion>`이다.
- 각 `LiquidPortion`은 `ItemDef sourceItem`과 `float volumeMl`를 가진다.
- `SetSingle()`로 단일 재료 입자를 초기화한다.
- `MixPair()`로 두 payload를 점진적으로 서로 닮게 만든다.
- `MixPair()`는 두 입자의 총량은 유지하면서 비율만 `Mathf.Lerp`로 교환한다.

현재 한계:

- payload 기준 색상 계산 함수가 없다.
- `SetPayload()`가 payload만 설정하고 시각 색상을 갱신하지 않는다.
- 색상은 아직 `LiquidReaction`과 `BottleController`에서 별도로 만지고 있다.

### BottleController

현재 `BottleController.SpawnLiquid()`는 병에서 입자를 꺼내고 다음을 수행한다.

```csharp
LiquidParticleData particleData = obj.GetComponent<LiquidParticleData>();
if (particleData != null)
    particleData.SetPayload(bottleData, 1f);

SpriteRenderer particleRenderer = obj.GetComponent<SpriteRenderer>();
if (particleRenderer != null)
    particleRenderer.color = bottleData.liquidColor;
```

현재 한계:

- payload 설정과 색상 설정이 분리되어 있다.
- 앞으로는 `SetPayload()`가 색상까지 갱신해야 하므로 `BottleController`에서 `SpriteRenderer.color`를 직접 설정하는 부분은 제거하는 것이 맞다.

### LiquidReaction

현재 `LiquidReaction.MixAttributes()`는 충돌 시 다음 순서로 동작한다.

```csharp
MixPayload(other);

Rigidbody2D otherRb = other.rb;

Color myColor = spriteRenderer.color;
Color otherColor = other.spriteRenderer.color;

if (AreColorsSimilar(myColor, otherColor) && Mathf.Approximately(rb.mass, otherRb.mass))
{
    return;
}

Color averageColor = (myColor + otherColor) / 2f;
this.spriteRenderer.color = averageColor;
other.spriteRenderer.color = averageColor;
```

문제:

- `MixPayload(other)`는 먼저 실행되므로 payload는 변할 수 있다.
- 하지만 색이 비슷하고 질량이 같으면 바로 `return`되어 색상 갱신은 건너뛰어진다.
- 색상이 payload에서 계산되는 것이 아니라 기존 `SpriteRenderer.color` 평균으로 계산된다.
- 따라서 평가 데이터와 화면 색상이 서로 다른 원천을 바라보게 된다.

### water_particle.prefab

현재 검사 기준:

- GameObject layer: `4`, 프로젝트의 `TagManager`상 `Water`
- `CircleCollider2D.m_IsTrigger: 0`
- `Rigidbody2D.m_CollisionDetection: 0` 즉 Discrete
- `Rigidbody2D.m_SleepingMode: 1`
- `LiquidReaction.mixSpeed: 0.1`
- `LiquidReaction.timeToSleep: 0.5`
- 현재 `LiquidParticleData` 컴포넌트는 한 개만 확인된다.

추가 확인 사항:

- Unity Editor에서 `Project Settings > Physics 2D > Layer Collision Matrix`의 `Water` 대 `Water` 충돌이 켜져 있어야 `OnCollisionEnter2D`가 돈다.

### Sample_Scene 렌더 구조

`Sample_Scene`에는 메타볼 렌더링 구조가 있다.

- `WaterCam`
  - `m_CullingMask.m_Bits: 16`, 즉 Water 레이어만 촬영
  - `m_TargetTexture: WaterRT`
- `WaterScreen`, `MetaballQuad`
  - `FullScreenQuad` 사용
  - `MetaballMat` 사용
- `Main Camera`
  - `m_TargetTexture: null`
  - 전체 레이어를 렌더링 중

중요:

- 화면에 보이는 물은 원본 입자 SpriteRenderer만이 아니라 `WaterCam -> WaterRT -> MetaballQuad/WaterScreen` 경로를 통해 보일 수 있다.
- 먼저 Play Mode에서 실제 입자의 `SpriteRenderer.color`가 바뀌는지 확인하고, 그 값은 바뀌는데 화면만 안 바뀌면 렌더 경로를 따로 봐야 한다.

## 현재 관찰된 문제

사용자가 확인한 증상:

- 충돌/혼합 중 일부 값은 변한다.
- 하지만 색깔은 안 변한다.

현재 원인 판단:

1. 충돌 자체는 어느 정도 도는 것으로 보인다.
2. `MixPayload()`가 먼저 실행되므로 payload는 변할 수 있다.
3. 색상은 payload가 아니라 `SpriteRenderer.color` 평균으로 처리 중이다.
4. 색상이 비슷하거나 질량이 같으면 `AreColorsSimilar(...) && Mathf.Approximately(...)` 조건으로 색상 평균 처리가 건너뛰어진다.
5. 따라서 "payload는 섞이는데 색은 그대로"인 상태가 가능하다.

## 변경 방향

색상은 독립 상태값이 아니라 payload의 파생값으로 만들어야 한다.

기존:

```text
SpriteRenderer.color A + SpriteRenderer.color B
        -> 평균
        -> 새 SpriteRenderer.color
```

변경 후:

```text
ItemDef.liquidColor
        -> LiquidPayload.portions
        -> payload 비율로 색상 계산
        -> SpriteRenderer.color 갱신
```

이렇게 해야 다음이 일관된다.

- 액체 평가 기준
- 액체 시각 색상
- 혼합 진행도
- 제출 시 최종 조성

## 변경해야 하는 코드

### 1. LiquidPayload에 색상 계산 추가

파일: `Assets/Scripts/Bartending/LiquidParticleData.cs`

`LiquidPayload` 클래스 안에 추가:

```csharp
public Color EvaluateColor(float minimumAlpha = 0.75f)
{
    float validTotal = 0f;
    for (int i = 0; i < portions.Count; i++)
    {
        LiquidPortion portion = portions[i];
        if (portion.sourceItem != null && portion.volumeMl > 0f)
            validTotal += portion.volumeMl;
    }

    if (validTotal <= 0f)
        return Color.clear;

    float r = 0f;
    float g = 0f;
    float b = 0f;
    float a = 0f;

    for (int i = 0; i < portions.Count; i++)
    {
        LiquidPortion portion = portions[i];
        if (portion.sourceItem == null || portion.volumeMl <= 0f)
            continue;

        float weight = portion.volumeMl / validTotal;
        Color sourceColor = portion.sourceItem.liquidColor;

        r += sourceColor.r * weight;
        g += sourceColor.g * weight;
        b += sourceColor.b * weight;
        a += Mathf.Max(sourceColor.a, minimumAlpha) * weight;
    }

    return new Color(
        Mathf.Clamp01(r),
        Mathf.Clamp01(g),
        Mathf.Clamp01(b),
        Mathf.Clamp01(a)
    );
}
```

`minimumAlpha`를 두는 이유:

- 현재 메타볼은 RenderTexture/Alpha 기반 후처리를 사용한다.
- 병 데이터의 alpha가 낮으면 색상 문제가 아니라 물 형태 자체가 약해 보일 수 있다.
- 테스트 중에는 `0.75f` 정도로 보장하는 편이 확인하기 쉽다.

### 2. LiquidParticleData가 자기 시각 상태를 갱신하게 만들기

파일: `Assets/Scripts/Bartending/LiquidParticleData.cs`

`LiquidParticleData`에 SpriteRenderer 캐시와 시각 갱신 메서드 추가:

```csharp
public sealed class LiquidParticleData : MonoBehaviour
{
    public LiquidPayload payload = new();
    public bool hasBeenCollected;

    private SpriteRenderer spriteRenderer;

    private void Awake()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
    }

    public void SetPayload(ItemDef sourceItem, float volumeMl)
    {
        hasBeenCollected = false;
        payload.SetSingle(sourceItem, volumeMl);
        ApplyVisualFromPayload();
    }

    public void MixPayloadWith(LiquidParticleData other, float strength)
    {
        if (other == null)
            return;

        LiquidPayload.MixPair(payload, other.payload, strength);
    }

    public void ApplyVisualFromPayload()
    {
        if (spriteRenderer == null)
            spriteRenderer = GetComponent<SpriteRenderer>();

        if (spriteRenderer == null)
            return;

        spriteRenderer.color = payload.EvaluateColor();
    }
}
```

주의:

- `MixPayloadWith()`는 `LiquidPayload.MixPair(payload, other.payload, strength)`를 호출하므로 양쪽 payload를 모두 변경한다.
- 하지만 `ApplyVisualFromPayload()`는 자기 SpriteRenderer만 갱신한다.
- 따라서 충돌 처리 쪽에서 양쪽 입자 모두 `ApplyVisualFromPayload()`를 호출해야 한다.

### 3. LiquidReaction에서 색 평균 로직 제거

파일: `Assets/MetaballFluid/Scripts/LiquidReaction.cs`

권장 구조:

- `LiquidReaction`이 `LiquidParticleData`를 캐시한다.
- 충돌 시 payload를 섞는다.
- 섞인 후 양쪽 입자의 색을 payload에서 다시 계산한다.
- 물리 속성 평균은 색상 로직과 분리한다.

수정 예시:

```csharp
using UnityEngine;
using Slainte.Bartending;

public class LiquidReaction : MonoBehaviour
{
    [HideInInspector] public SpriteRenderer spriteRenderer;
    [HideInInspector] public Rigidbody2D rb;
    [HideInInspector] public LiquidParticleData particleData;

    [Header("Mix Settings")]
    public float mixSpeed = 0.1f;
    public float reactionCooldown = 0.1f;
    private float lastReactionTime;

    [Header("Optimization Settings")]
    public float sleepVelocityThreshold = 0.05f;
    public float timeToSleep = 2.0f;
    private float settleTimer = 0f;
    private bool isLogicallySleeping = false;

    void Awake()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
        rb = GetComponent<Rigidbody2D>();
        particleData = GetComponent<LiquidParticleData>();
    }

    public void CheckSleepState(float deltaTime)
    {
        if (isLogicallySleeping) return;

        if (rb.linearVelocity.sqrMagnitude < sleepVelocityThreshold)
        {
            settleTimer += deltaTime;
            if (settleTimer >= timeToSleep)
                GoToSleep();
        }
        else
        {
            settleTimer = 0f;
        }
    }

    void GoToSleep()
    {
        isLogicallySleeping = true;
        rb.Sleep();
    }

    public void WakeUp()
    {
        isLogicallySleeping = false;
        settleTimer = 0f;
        if (!rb.IsAwake()) rb.WakeUp();
    }

    void OnCollisionEnter2D(Collision2D collision)
    {
        if (isLogicallySleeping) return;
        if (Time.time < lastReactionTime + reactionCooldown) return;

        if (collision.gameObject.TryGetComponent(out LiquidReaction otherParticle))
        {
            MixAttributes(otherParticle);
            lastReactionTime = Time.time;
        }
    }

    void MixAttributes(LiquidReaction other)
    {
        if (other == null)
            return;

        MixPayloadAndVisuals(other);
        MixPhysicalAttributes(other);
    }

    void MixPayloadAndVisuals(LiquidReaction other)
    {
        if (particleData == null || other.particleData == null)
            return;

        particleData.MixPayloadWith(other.particleData, mixSpeed);

        particleData.ApplyVisualFromPayload();
        other.particleData.ApplyVisualFromPayload();
    }

    void MixPhysicalAttributes(LiquidReaction other)
    {
        Rigidbody2D otherRb = other.rb;
        if (rb == null || otherRb == null)
            return;

        if (Mathf.Approximately(rb.mass, otherRb.mass)
            && Mathf.Approximately(rb.linearDamping, otherRb.linearDamping)
            && Mathf.Approximately(rb.gravityScale, otherRb.gravityScale))
        {
            return;
        }

        float averageMass = (rb.mass + otherRb.mass) / 2f;
        rb.mass = averageMass;
        otherRb.mass = averageMass;

        float averageLinearDamping = (rb.linearDamping + otherRb.linearDamping) / 2f;
        rb.linearDamping = averageLinearDamping;
        otherRb.linearDamping = averageLinearDamping;

        float averageGravityScale = (rb.gravityScale + otherRb.gravityScale) / 2f;
        rb.gravityScale = averageGravityScale;
        otherRb.gravityScale = averageGravityScale;
    }
}
```

제거 대상:

- `AreColorsSimilar(...)`
- `Color averageColor = (myColor + otherColor) / 2f;`
- `spriteRenderer.color = averageColor;`
- 색상이 비슷하면 return하는 조건

색상은 더 이상 `LiquidReaction`이 직접 평균내지 않는다.

### 4. BottleController에서 직접 색상 설정 제거

파일: `Assets/Scripts/Bartending/BottleController.cs`

현재:

```csharp
LiquidParticleData particleData = obj.GetComponent<LiquidParticleData>();
if (particleData != null)
    particleData.SetPayload(bottleData, 1f);

SpriteRenderer particleRenderer = obj.GetComponent<SpriteRenderer>();
if (particleRenderer != null)
    particleRenderer.color = bottleData.liquidColor;
```

변경 후:

```csharp
LiquidParticleData particleData = obj.GetComponent<LiquidParticleData>();
if (particleData != null)
    particleData.SetPayload(bottleData, 1f);
```

이렇게 하면 병에서 입자가 생성될 때도 색상은 payload에서 계산된다.

### 5. 선택 개선: density도 payload 기준으로 계산

현재 `ItemDef`에 `density`가 있으므로 이후에는 물리 질량도 payload 기준으로 계산할 수 있다. 지금 당장 필수는 아니지만, 색상 구조와 같은 원칙으로 가면 좋다.

`LiquidPayload`에 추가 가능:

```csharp
public float EvaluateDensity(float fallbackDensity = 1f)
{
    float validTotal = 0f;
    for (int i = 0; i < portions.Count; i++)
    {
        LiquidPortion portion = portions[i];
        if (portion.sourceItem != null && portion.volumeMl > 0f)
            validTotal += portion.volumeMl;
    }

    if (validTotal <= 0f)
        return fallbackDensity;

    float density = 0f;
    for (int i = 0; i < portions.Count; i++)
    {
        LiquidPortion portion = portions[i];
        if (portion.sourceItem == null || portion.volumeMl <= 0f)
            continue;

        float weight = portion.volumeMl / validTotal;
        density += portion.sourceItem.density * weight;
    }

    return density;
}
```

나중에 `ApplyPhysicalFromPayload()` 같은 메서드를 만들 수 있다. 다만 지금은 색상 디버깅을 먼저 끝내는 것이 좋다.

## 테스트 절차

### 1. 컴파일 확인

Unity에서 컴파일 에러가 없는지 확인한다.

특히 확인할 것:

- `LiquidParticleData.cs`가 `UnityEngine`을 이미 using 중이므로 `Color`, `SpriteRenderer`, `MonoBehaviour` 사용 가능
- `LiquidReaction.cs`에 `using Slainte.Bartending;` 유지
- `LiquidReaction`에서 `LiquidParticleData` 타입을 접근 가능

### 2. 프리팹 확인

`Assets/MetaballFluid/Prefabs/water_particle.prefab`

확인:

- `LiquidParticleData` 컴포넌트가 정확히 한 개만 붙어 있는지
- `LiquidReaction`이 붙어 있는지
- `SpriteRenderer`가 붙어 있는지
- `Rigidbody2D`가 Dynamic인지
- `CircleCollider2D`가 Trigger가 아닌지
- GameObject layer가 `Water`인지

### 3. Physics 2D 확인

Unity Editor:

`Project Settings > Physics 2D > Layer Collision Matrix`

확인:

- `Water` 대 `Water` 충돌이 켜져 있어야 한다.

### 4. Sample_Scene에서 시각 확인

테스트:

1. `Sample_Scene` 실행
2. `breeze_vodka`, `lemon_juice`에서 각각 액체 생성
3. 두 액체가 충돌하게 만들기
4. Play Mode에서 개별 particle 선택
5. `LiquidParticleData.payload.portions`가 서로 섞이는지 확인
6. `SpriteRenderer.color`가 payload 비율에 따라 변하는지 확인

기대:

- 보드카 단독 입자: 거의 흰색/반투명 기반
- 레몬 단독 입자: 노란색 기반
- 섞인 입자: 흰색과 노란색의 비율 혼합색
- 완전히 섞일수록 주변 입자들의 색상이 비슷해짐

### 5. 화면에는 안 바뀌고 Inspector만 바뀌는 경우

개별 particle의 `SpriteRenderer.color`는 바뀌는데 Game view 색상이 안 바뀌면, 문제는 액체 혼합 로직이 아니라 렌더 경로다.

확인할 것:

- `WaterCam`이 Water 레이어를 찍고 있는지
- `WaterRT`가 갱신되는지
- `MetaballQuad`/`WaterScreen`이 어느 것이 최종 화면에 보이는지
- `MetaballMat` shadergraph가 texture RGB를 BaseColor로 쓰고 있는지
- Main Camera가 원본 입자와 메타볼 쿼드를 둘 다 보여서 덮임/중첩 문제가 없는지

디버깅 방법:

- 임시로 `MetaballQuad` 또는 `WaterScreen` 중 하나를 꺼 보고 어느 쪽이 최종 표시인지 확인
- 임시로 raw particle만 보이게 하여 `SpriteRenderer.color` 변경이 화면에 보이는지 확인
- 이 작업은 씬 설정 변경이므로 커밋 전 의도한 변경인지 확인할 것

## 이후 레시피 평가 설계

색상/payload 혼합이 안정되면 다음 단계는 제출 평가다.

### 제출 기준

고객에게 낸 잔 하나를 기준으로 평가한다.

이유:

- 플레이어가 잔 여러 개를 슬롯에 올려둘 수 있다.
- 슬롯 전체나 테이블 전체를 평가하면 어떤 잔을 낸 것인지 애매하다.
- 실제 게임 흐름상 "고객에게 제출한 잔"이 평가 대상이어야 한다.

### 컨테이너 역할

비커/잔이 투명하고 실제 액체 입자가 보여야 하므로, 컨테이너가 액체를 흡수해서 저장하는 방식은 피한다.

권장 구조:

- 실제 액체 입자는 계속 씬에 존재한다.
- 잔/비커는 Trigger 영역으로 현재 내부 입자를 추적한다.
- 입자가 들어오면 tracker에 추가한다.
- 입자가 나가거나 쏟아지면 tracker에서 제거한다.
- 제출 시 tracker에 남아 있는 particle들의 payload만 집계한다.

예상 컴포넌트:

```csharp
public sealed class VesselLiquidTracker : MonoBehaviour
{
    private readonly HashSet<LiquidParticleData> particles = new();

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (other.TryGetComponent(out LiquidParticleData particle))
            particles.Add(particle);
    }

    private void OnTriggerExit2D(Collider2D other)
    {
        if (other.TryGetComponent(out LiquidParticleData particle))
            particles.Remove(particle);
    }

    public IReadOnlyCollection<LiquidParticleData> Particles => particles;
}
```

주의:

- 실제 프리팹 구조에 따라 trigger collider를 별도 child object로 둘 수 있다.
- 물리 충돌용 collider와 내부 감지용 trigger collider를 분리하는 편이 안전하다.

### 제출 시 집계

제출된 잔의 tracker에서 현재 particle payload를 모두 합산한다.

예상 구조:

```csharp
public sealed class CocktailComposition
{
    public Dictionary<ItemDef, float> volumes = new();
    public float totalVolumeMl;
}
```

집계 예시:

```csharp
public static CocktailComposition BuildFromParticles(IEnumerable<LiquidParticleData> particles)
{
    CocktailComposition composition = new CocktailComposition();

    foreach (LiquidParticleData particle in particles)
    {
        if (particle == null || particle.payload == null)
            continue;

        for (int i = 0; i < particle.payload.portions.Count; i++)
        {
            LiquidPortion portion = particle.payload.portions[i];
            if (portion.sourceItem == null || portion.volumeMl <= 0f)
                continue;

            if (!composition.volumes.ContainsKey(portion.sourceItem))
                composition.volumes.Add(portion.sourceItem, 0f);

            composition.volumes[portion.sourceItem] += portion.volumeMl;
            composition.totalVolumeMl += portion.volumeMl;
        }
    }

    return composition;
}
```

이 방식이면:

- 플레이어가 액체를 쏟으면 해당 입자가 잔 tracker에서 빠진다.
- 다른 잔으로 부으면 목적지 잔 tracker에 들어간다.
- 제출한 잔에 남아 있는 실제 입자만 평가된다.

### 레시피 비교 방향

레시피는 `RecipeData` 같은 ScriptableObject로 관리하는 것이 좋다.

예상:

```csharp
[CreateAssetMenu(menuName = "Bartending/Recipe")]
public sealed class RecipeData : ScriptableObject
{
    public string id;
    public string displayName;
    public List<RecipeIngredient> ingredients = new();
    public float totalVolumeToleranceMl = 5f;
    public float ingredientToleranceRatio = 0.15f;
}

[Serializable]
public struct RecipeIngredient
{
    public ItemDef item;
    public float volumeMl;
}
```

평가 방식:

1. 제출 잔에서 `CocktailComposition` 생성
2. 각 레시피와 비교
3. 재료 누락/초과/비율 차이를 점수화
4. 가장 점수가 낮은 레시피를 후보로 선택
5. 허용 오차 안이면 GoodJob
6. 허용 오차 밖이면 BadJob

처음에는 브루트포스로 모든 레시피를 비교해도 괜찮다.

이유:

- 레시피 수가 수십~수백 개 수준이면 제출 시 1회 비교 비용은 작다.
- 매 프레임 계산하지 않는다.
- 최적화는 나중에 필요해졌을 때 `ingredient signature` 또는 주재료 index로 줄이면 된다.

## 성능 메모

현재 `LiquidPayload.MixPair()`는 충돌마다 `List<ItemDef> keys = new();`를 만든다. 테스트 단계에서는 괜찮지만, 입자가 많고 충돌이 많아지면 GC가 생길 수 있다.

나중에 최적화할 수 있는 부분:

- `List<ItemDef>` 재사용
- payload portion 수 제한
- 완전히 같은 조성으로 수렴한 입자끼리는 추가 혼합 생략
- `reactionCooldown` 조정
- sleeping 입자의 혼합 계산 생략
- `OnCollisionStay2D` 대신 현재처럼 `OnCollisionEnter2D` 위주 사용

다만 지금은 구조 검증이 먼저다. 색상/payload 일관성이 잡힌 뒤에 최적화해도 된다.

## 다음 작업 순서

권장 순서:

1. `LiquidPayload.EvaluateColor()` 추가
2. `LiquidParticleData.ApplyVisualFromPayload()` 추가
3. `LiquidParticleData.SetPayload()`에서 `ApplyVisualFromPayload()` 호출
4. `LiquidReaction`에서 색 평균 로직 제거
5. `LiquidReaction`에서 payload 혼합 후 양쪽 `ApplyVisualFromPayload()` 호출
6. `BottleController.SpawnLiquid()`에서 직접 `SpriteRenderer.color` 설정 제거
7. Unity 컴파일 확인
8. `Sample_Scene`에서 보드카/레몬 주스 혼합 테스트
9. Inspector의 `SpriteRenderer.color` 변화 확인
10. Game view 색상이 여전히 안 바뀌면 WaterCam/WaterRT/MetaballQuad 렌더 경로 디버깅
11. 색상 확인 후 `VesselLiquidTracker` 설계/구현
12. 제출 잔 기준 recipe evaluation 구현

## 중요 판단

지금 구조에서 가장 중요한 결정은 이것이다.

> 액체의 의미 데이터는 `LiquidPayload`이고, 색상은 그 payload의 파생값이다.

이 원칙을 지키면:

- 색상 혼합
- 맛/도수/카테고리/타입 평가
- 제출 시 레시피 판정
- 액체를 쏟거나 옮겼을 때의 상태 변화

모두 같은 데이터 모델 위에서 처리할 수 있다.

