# 슬런챠 프로젝트 포트폴리오 제작용 요약

이 문서는 슬런챠 프로젝트를 외부 포트폴리오 문서·웹사이트·발표 자료로 제작할 때 사용할 수 있도록, 현재 저장소의 실제 코드와 프로젝트 구조에서 확인된 내용만 정리한 자료다.

기준일: 2026-09-02

## 1. 프로젝트 개요

### 한 줄 소개

슬런챠는 **물리 입자로 칵테일을 따르고, 옮기고, 섞은 결과를 손님의 주문 판정과 영업 진행에 연결한 2D 내러티브 바 운영 게임**이다.

### 핵심 게임플레이

1. 손님의 주문과 요구 조건을 확인한다.
2. 주류 선반에서 필요한 병을 선택한다.
3. 병을 직접 들고 기울여 액체를 따른다.
4. 비커, 셰이커, 잔 사이에서 액체와 얼음을 옮기고 혼합한다.
5. 완성된 잔을 제출한다.
6. 재료별 용량, 총량, 잔, 얼음, 제조 기법을 레시피와 비교한다.
7. 판정 결과를 피드백, 자금, 평판 및 영업 진행에 반영한다.

### 클라이언트 구조

- `CoreScene`이 게임 진행, 저장 데이터, 에피소드, 정산, 씬 전환을 관리한다.
- 실제 플레이 씬은 Additive 방식으로 전환된다.
- 상위 진행 상태는 `Episode → Business → Settlement → Rest` 구조다.
- `BusinessScene` 안에서 주문 제시, 에피소드, 칵테일 제작 모드가 전환된다.
- 진행 데이터는 `GameProgress`에 모이고 JSON으로 저장된다.
- 기준 CSV에서 생성한 `ItemDef` 15개와 기본 `CocktailRecipeDef` 21개를 제품 런타임의 ScriptableObject 데이터로 사용한다.

### 액체 시스템의 정확한 기술적 정의

현재 액체는 세션 설정으로 두 구현을 선택한다. GPU 구현은
`ILiquidSimulationBackend`를 직접 구현하고, 기존 `LiquidPool`은 원본 코드를 수정하지 않은 채
`LegacyLiquidSimulationBackend` 어댑터가 세션 계약에 연결한다.

- `LegacyRigidbody2D`: Rigidbody2D·Collider2D·`LiquidPool`을 사용하는 기존 CPU 기반 2D 입자 근사 시스템.
- `GpuPbfXpbd`: Compute Shader에서 공간 격자, 밀도·lambda, 위치 보정, 속도 및 조성 혼합을 계산하는 별도 GPU PBF/XPBD 시스템.
- `Automatic`: GPU 실행 조건을 만족하면 GPU를 사용하고, 지원 또는 초기화 조건이 맞지 않으면 레거시로 폴백한다.
- 레거시 입자는 `LiquidPayload`, GPU 입자는 GPU composition buffer에 재료 구성·부피·온도·제조 기법 상태를 보관하고, 둘 다 `CocktailComposition`으로 합산해 주문 판정에 사용한다.
- 레거시는 기존 입자 프리팹의 `2 ml`, GPU는 독립 설정인 `gpuLiquidParticleVolumeMl`의 `0.5 ml`를 기본값으로 사용한다.
- 모드는 `BusinessBartendingSettings.asset`에서 선택하며, 변경 후 바텐딩 세션 또는 씬을 다시 시작해야 한다. 실행 중 핫스왑은 지원하지 않는다.
- 레거시는 기존 따르기(90도 임계값·고정 유량·가로 지터·초기속도 0)와 젓기(평균 조성 편차 10%) 판정을 유지하고, 기울기별 유량·몸체 콜라이더에서 실제 입구로 향하는 초기속도와 최대 조성 편차 판정은 GPU에서만 사용한다.
- 레거시 풀링·재활용 최적화와 GPU PBF/XPBD 구현은 별개의 기여 범위다.

## 2. 본인 역할

사용자가 직접 확인한 역할은 다음 두 가지다.

- **GPU PBF/XPBD 액체 백엔드 및 공용 바텐딩 통합 담당**
- **게임 클라이언트 개발 담당**

외부 포트폴리오에서는 다음과 같이 표현할 수 있다.

> Compute Shader 기반 GPU PBF/XPBD 액체 백엔드를 구현하고, 기존 Physics2D 액체와 선택 가능한 구조로 병 따르기·용기·혼합·주문 판정 흐름에 통합했습니다.

레거시 `LiquidPool`의 풀링·재활용·혼합 최적화는 팀원 기여로 분리한다. 개인 구현 범위를 공개할 때는 GPU 백엔드와 공용 통합 변경만 본인 범위로 설명하고, 프로젝트 전체 구조나 모든 바텐딩 코드를 혼자 구현했다고 표현해서는 안 된다.

## 3. 핵심 구현 3개

### 3.1 입자별 액체 조성 및 물리 기반 혼합

#### 구현 내용

각 액체 입자가 단순 색상만 가지는 대신 다음 상태를 `LiquidPayload`로 보유한다.

- 원재료별 부피
- 전체 부피
- 온도
- Shake, Stir 등의 제조 기법
- 조성으로부터 계산되는 표시 색상

두 입자가 충돌하거나 빠르게 교반되면 재료 비율과 온도가 혼합 강도에 따라 평형값으로 수렴한다. 각 입자의 전체 부피는 유지된다. 상대 속도가 높을수록 혼합 강도가 증가하고, 서로 다른 용기에 소유된 입자는 혼합하지 않는다.

#### 관련 코드

- `Assets/_Project/Features/Bartending/Runtime/Liquid/Particles/LiquidParticleData.cs`
  - `LiquidPayload.MixPair`
  - `LiquidPayload.EvaluateColor`
- `Assets/_Project/Features/Bartending/Runtime/Liquid/Particles/LiquidReaction.cs`
  - `TryMixNearbyParticlesByAgitation`
  - `GetRelativeVelocityMixStrength`

#### 포트폴리오 포인트

보이는 액체와 게임 판정용 데이터가 동일한 입자 상태에서 파생된다. 따라서 색 변화, 혼합 상태, 최종 레시피 판정이 서로 분리되지 않는다.

### 3.2 용기 소유권과 충돌 격리

#### 구현 내용

잔, 비커, 셰이커가 서로 겹치면 하나의 입자가 여러 Trigger 영역에 동시에 포함될 수 있다. `VesselLiquidTracker`는 각 입자에 하나의 논리적 소유 용기를 지정하고, 겹친 용기 중 우선순위가 높은 대상을 선택한다.

소유자가 결정된 뒤에는 다음 충돌을 선택적으로 무시한다.

- 서로 다른 용기에 소유된 입자끼리의 충돌
- 입자와 소유자가 아닌 용기의 고체 Collider 간 충돌

입자가 기존 용기 밖으로 나오면 소유권을 해제하고 다른 용기로 이전할 수 있다.

#### 관련 코드

- `Assets/_Project/Features/Bartending/Runtime/Liquid/Vessels/VesselLiquidTracker.cs`
  - `FindPreferredOwner`
  - `RefreshParticleIsolation`
  - `SetParticleVesselCollision`
  - `BuildComposition`

#### 포트폴리오 포인트

이 구현은 단순 입자 생성보다 실제 플레이에서 발생하는 경계 조건을 해결한다. 특히 겹친 용기에서 입자가 다른 벽에 걸리거나 두 용기의 내용물로 동시에 처리되는 문제를 다룬다.

### 3.3 물리 액체와 주문·영업 시스템의 연결

#### 구현 내용

완성된 잔을 제출하면 `VesselLiquidTracker`가 내부 입자를 `CocktailComposition`으로 집계한다. 이후 Evaluator가 다음 조건을 레시피와 비교한다.

- 재료별 실제 용량과 허용 오차
- 전체 용량
- 허용되지 않은 추가 재료
- 잔 종류
- 얼음 유무
- 요구된 제조 기법

평가 결과는 주문 피드백과 영업 보상 처리로 전달된다. 병의 잔여 용량 또한 이벤트를 통해 인벤토리 재고와 연결된다.

#### 관련 코드

- `Assets/_Project/Features/Bartending/Runtime/Liquid/Vessels/VesselLiquidTracker.cs`
  - `BuildComposition`
- `Assets/_Project/Features/Bartending/Runtime/CocktailEvaluator.cs`
  - `EvaluateRecipe`
- `Assets/_Project/Features/Business/Runtime/Flow/BusinessOrderSessionController.cs`
  - `SubmitOrder`
- `Assets/_Project/Features/Bartending/Runtime/BusinessBartendingBootstrap.cs`
  - 병 배치, 재고 분배 및 잔여 용량 동기화 코드

#### 포트폴리오 포인트

액체 기술 데모에 머무르지 않고, 플레이어의 물리 조작 결과가 실제 게임 규칙, 피드백, 보상 및 진행 상태로 이어진다.

## 4. Problem–Solution 사례 2개

### 사례 1. 겹친 용기에서 발생하는 입자 충돌 문제

**Problem**

용기 두 개가 겹치면 하나의 입자가 여러 Trigger에 동시에 포함되고, 이동 대상이 아닌 용기의 벽과 충돌해 입자가 걸리거나 잘못 추적될 수 있다.

**Approach**

각 입자에 단일 소유 용기를 부여하고, 용기의 상호작용 우선순위와 인스턴스 순서로 소유자를 결정했다.

**Implementation**

`VesselLiquidTracker.FindPreferredOwner`가 입자를 포함한 활성 용기 중 소유자를 선택한다. `RefreshParticleIsolation`은 소유자가 다른 입자끼리의 충돌과 비소유 용기의 벽 충돌을 `Physics2D.IgnoreCollision`으로 갱신한다. 입자가 Trigger를 벗어나면 기존 소유권을 해제한다.

**Result**

겹친 용기에서도 입자를 하나의 용기 내용물로 일관되게 처리하고, 용기 밖으로 나온 입자를 다른 용기로 이전할 수 있는 구조가 만들어졌다.

### 사례 2. 물리 입자를 레시피 판정 데이터로 변환하는 문제

**Problem**

입자가 위치와 색상만 가진다면 플레이어가 실제로 어떤 재료를 얼마나 섞었는지 판단할 수 없고, 물리 결과를 주문 성공 여부에 사용할 수 없다.

**Approach**

입자마다 재료별 부피, 온도와 제조 기법을 저장하고, 제출 시 용기 내부의 모든 입자를 판정용 조성으로 집계하도록 설계했다.

**Implementation**

`LiquidPayload.MixPair`가 입자의 재료 비율과 온도를 혼합한다. `VesselLiquidTracker.BuildComposition`이 잔 내부 상태를 집계하고, `CocktailEvaluator.EvaluateRecipe`가 재료 오차, 전체 용량, 추가 재료, 잔, 얼음, 제조 기법을 평가한다. `BusinessOrderSessionController.SubmitOrder`가 결과를 영업 흐름으로 전달한다.

**Result**

플레이어가 직접 따르고 섞은 물리 상태를 주문 판정과 피드백에 사용할 수 있게 됐다.

## 5. 기술 스택

| 구분 | 사용 기술 | 실제 사용 내용 |
|---|---|---|
| 엔진 | Unity 6000.3.5f2 | 2D 게임 클라이언트 및 씬 구성 |
| 언어 | C# | 게임플레이, 물리 상호작용, UI, 상태 및 데이터 처리 |
| 렌더 파이프라인 | URP 17.3.0, 2D Renderer | 2D 월드와 UI 렌더링 |
| 물리 | Unity Physics2D + GPU Compute Shader | 레거시 Rigidbody2D 입자와 GPU PBF/XPBD 백엔드를 선택 가능하게 구성 |
| 액체 표현 | 레거시 메타볼 + GPU accumulation | 레거시 입자 렌더링과 GPU 버퍼 기반 렌더링을 백엔드별로 처리 |
| UI | uGUI, TextMeshPro | 주문표, 레시피, 선반, 피드백 등 |
| 입력 | Legacy Input + Input System 일부 | 마우스 선택, 드래그, 커서 잠금, 기울기 조작 |
| 화면 합성 | Camera, RenderTexture, RawImage | UI 내부에 별도 물리 월드 표시 |
| 데이터 | ScriptableObject, CSV | 아이템과 칵테일 레시피 데이터 |
| 저장 | JSON | 날짜, 자금, 평판, 인벤토리, 에피소드 상태 등 |
| GPU 액체 | HLSL Compute Shader | 공간 격자, PBF/XPBD 제약 계산, 조성 혼합과 색상 갱신 |
| 개발 도구 | Custom Editor Validator, `LiquidStressHarness` | GPU 구성 검증과 입자 부하·p95/p99 프레임 시간 계측 |

## 6. 추천 코드 발췌

### 6.1 최우선: 용기 소유권에 따른 충돌 격리

**파일:** `Assets/_Project/Features/Bartending/Runtime/Liquid/Vessels/VesselLiquidTracker.cs`

**추천 함수:** `FindPreferredOwner`, `RefreshParticleIsolation`

```csharp
bool ignoreParticle = particle.VesselOwner != null
    && other.VesselOwner != null
    && particle.VesselOwner != other.VesselOwner;

Physics2D.IgnoreCollision(
    particleCollider,
    other.ParticleCollider,
    ignoreParticle);
```

발췌할 때는 소유자 선택 코드와 충돌 무시 코드가 하나의 문제를 해결한다는 점이 보이도록 함께 배치한다.

### 6.2 입자 조성 및 온도 혼합

**파일:** `Assets/_Project/Features/Bartending/Runtime/Liquid/Particles/LiquidParticleData.cs`

**추천 함수:** `LiquidPayload.MixPair`

```csharp
float equilibriumTemperature =
    (left.temperatureC * leftTotal + right.temperatureC * rightTotal)
    / (leftTotal + rightTotal);

left.temperatureC = Mathf.Lerp(
    left.temperatureC,
    equilibriumTemperature,
    strength);

right.temperatureC = Mathf.Lerp(
    right.temperatureC,
    equilibriumTemperature,
    strength);
```

재료별 평형 비율 계산 부분까지 포함하되, Payload의 모든 보조 함수는 포트폴리오에 복사하지 않는 편이 좋다.

### 6.3 상대 운동에 따른 혼합 강도

**파일:** `Assets/_Project/Features/Bartending/Runtime/Liquid/Particles/LiquidReaction.cs`

**추천 함수:** `TryMixNearbyParticlesByAgitation`, `GetRelativeVelocityMixStrength`

```csharp
float relativeSpeed =
    (rb.linearVelocity - other.rb.linearVelocity).magnitude;

if (relativeSpeed < agitationVelocityThreshold)
    return 0f;

float agitation = Mathf.InverseLerp(
    agitationVelocityThreshold,
    fullMixSpeed,
    relativeSpeed);

return agitationMixSpeed * agitation;
```

주변 입자 검사 간격과 최대 혼합 대상 수 제한도 함께 표시하면 성능을 고려한 근사 방식임을 설명할 수 있다.

### 6.4 물리 결과의 레시피 판정

**파일:** `Assets/_Project/Features/Bartending/Runtime/CocktailEvaluator.cs`

**추천 함수:** `EvaluateRecipe`

```csharp
result.score = Mathf.Clamp01(
    ingredientScore * 0.7f
    + totalScore * 0.1f
    + extraScore * 0.05f
    + (glassValid ? 0.05f : 0f)
    + (iceValid ? 0.05f : 0f)
    + (techniqueValid ? 0.05f : 0f));
```

점수 계산만 단독으로 제시하기보다, `CocktailComposition`이 실제 입자에서 집계된다는 흐름을 도식으로 함께 보여주는 편이 좋다.

## 7. 추천 스크린샷 및 영상

### 7.1 대표 영상: 서로 다른 액체 혼합

- 서로 다른 색과 재료를 가진 병 두 개를 선택한다.
- 비커 또는 셰이커에 순서대로 따른다.
- 입자들이 움직이면서 색과 조성이 변화하는 구간을 근접 촬영한다.
- 가능하면 병 용량 UI가 함께 감소하는 모습도 포함한다.
- 추천 길이: 8~15초

**추천 캡션**

> 각 입자는 재료별 부피와 온도를 보유하며, 충돌과 상대 운동에 따라 조성이 혼합됩니다.

### 7.2 용기 간 액체 이동

- 비커와 잔을 가까이 배치한다.
- 비커를 기울여 잔으로 액체를 옮긴다.
- 두 용기의 Trigger가 겹친 순간부터 소유권이 이전되는 구간을 촬영한다.
- 디버그 표시가 준비되어 있다면 실제 화면과 소유 용기 정보를 나란히 보여준다.

**추천 캡션**

> 겹친 용기에서는 단일 소유자를 결정하고 비소유 용기의 충돌을 격리해 액체 이동을 처리합니다.

### 7.3 주문부터 결과까지의 전체 게임플레이

- 손님 주문표 표시
- 선반에서 병 선택
- 액체 제작
- 잔 제출
- 레시피 판정 피드백
- 자금 또는 평판 반영

추천 길이는 15~30초다. 액체 기술이 실제 게임 루프에 연결되었다는 사실을 가장 직접적으로 보여주는 장면이다.

**추천 캡션**

> 물리적으로 제작된 칵테일의 재료, 총량, 잔, 얼음과 제조 기법을 집계해 주문 결과에 반영합니다.

### 7.4 셰이커와 얼음

- 액체와 얼음을 셰이커에 넣는다.
- 셰이커를 반복 조작한다.
- 잔으로 따를 때 스트레이너가 얼음을 유지하는 모습을 촬영한다.
- 실제 주문에서 Shake와 Ice 조건이 판정되는 경우 결과 화면까지 연결한다.

### 7.5 UI와 물리 월드 통합

- 주문표 또는 레시피 UI가 보이는 상태에서 병과 잔을 조작한다.
- 중앙의 RenderTexture 기반 바텐딩 영역과 주변 UI를 한 화면에 담는다.
- 가능하다면 서로 다른 화면 비율에서도 조작이 유지되는 모습을 비교한다.

### 7.6 레거시/GPU 백엔드 비교

- 같은 재료·용기·목표 부피 조건에서 `LegacyRigidbody2D`와 `GpuPbfXpbd`를 각각 실행한다.
- 설정 변경 후 바텐딩 세션 또는 씬을 재시작하고, 실제 활성 백엔드를 로그나 런타임 컴포넌트로 확인한다.
- 화면에는 백엔드 이름, 활성 입자 수, p95/p99 프레임 시간을 함께 표시한다.
- `Sample_Scene`의 Shader Graph 비교는 레거시 메타볼 개발 과정 자료로만 별도 표기한다.
- 두 모드의 성능 우열은 같은 하드웨어에서 측정한 수치가 있을 때만 설명한다.

## 8. 외부 제작 시 사실 경계

현재 작업 트리의 코드 근거로 `GPU Compute Shader 기반 PBF/XPBD 액체 백엔드`라고 설명할 수 있다.
다만 다음 표현은 사용할 수 없다.

- 레거시 풀링·재활용 최적화를 GPU 백엔드 구현자의 단독 기여로 설명
- GPU와 레거시를 포함한 전체 액체 시스템의 단독 구현
- 프로젝트 전체 단독 개발
- 모든 바텐딩 관련 파일의 단독 구현
- 특정 FPS, 입자 수, 메모리 절감률 등 측정하지 않은 성능 수치
- 정적 빌드 성공만으로 실제 대상 기기 플레이모드와 성능까지 검증됐다는 설명
- 개발 기간, 팀 규모, 수상, 출시 및 사용자 성과

권장 포지셔닝은 다음과 같다.

> **기존 Physics2D 액체를 보존하면서 Compute Shader 기반 GPU PBF/XPBD 백엔드를 별도로 구현하고, 두 방식을 공통 바텐딩·주문 판정 흐름에 연결한 게임 클라이언트 프로젝트**
