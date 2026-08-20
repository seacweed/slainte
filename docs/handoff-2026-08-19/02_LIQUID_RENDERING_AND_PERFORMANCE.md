# 액체 렌더링 및 성능 인수인계

## 의도한 렌더링 규칙

- 입자 하나 내부의 RGB와 논리 Alpha는 균일해야 한다.
- 같은 RGBA 입자가 붙으면 중심, 가장자리, 연결부가 같은 RGBA를 가져야 한다.
- 같은 Alpha `0.2` 입자가 두 개 겹쳐도 `0.4`가 아니라 `0.2`여야 한다.
- 다른 Alpha `0.2`와 `0.6`이 같은 밀도로 겹치면 `0.4`가 되어야 한다.
- 다른 색의 입자는 접촉 영역에서 밀도 가중 혼합되고, 기존 물리 혼합이 진행되면 입자별 색도 기존 로직대로 가까워져야 한다.
- 잔 전체에 하나의 최종색을 강제로 덮지 않는다.
- Alpha 0은 데이터와 최종 RGBA 확인값에서는 0을 유지한다. 화면에서만 최소 표시 Alpha `0.05`를 사용하도록 구현했다.

## 현재 구현 수식

누적 셰이더는 스프라이트 알파를 coverage로 사용한다.

```text
D     = Σ coverage
RGB_N = Σ particleRGB × coverage
A_N   = Σ max(particleAlpha, 0.05) × coverage

finalRGB   = RGB_N / D
finalAlpha = A_N / D
```

합성 셰이더는 `D < threshold`인 픽셀을 버리고, 나머지를 premultiplied alpha로 출력한다.

## 관련 파일

- `Assets/MetaballFluid/Graphics/LiquidMetaballAccumulation.shader` — 신규, 미추적
- `Assets/MetaballFluid/Graphics/LiquidMetaballAccumulation.mat` — 신규, 미추적
- `Assets/MetaballFluid/Graphics/LiquidMetaballComposite.shader` — 신규, 미추적
- `Assets/MetaballFluid/Graphics/MetaballMat.mat` — 새 합성 셰이더 참조
- `Assets/MetaballFluid/Scripts/LiquidMetaballRenderer.cs` — 신규, 미추적
- `Assets/Scenes/Sample_Scene.unity` — WaterCam/MetaballQuad 구성
- `Assets/Scripts/Bartending/LiquidParticleData.cs` — 실제 RGBA 및 공유 item 버퍼
- `Assets/Scripts/Bartending/VesselLiquidTracker.cs` — 활성 입자 노출 및 최종 색
- `Assets/MetaballFluid/Scripts/LiquidReaction.cs` — CollisionStay 중복 혼합 제거
- `Assets/MetaballFluid/Scripts/ObjPooling.cs` — 입자당 ml 캐시
- `Assets/MetaballFluid/Prefabs/water_particle.prefab` — 스케일/기본 ml

## `Sample_Scene`의 예상 구성

- Main Camera culling mask: Water 레이어 비포함 (`4294967279`)
- WaterCam culling mask: Water 레이어만 (`16`)
- 기존 `WaterScreen` MeshRenderer: 비활성
- `WaterCam/LiquidMetaballRenderer`
  - accumulation material: `LiquidMetaballAccumulation.mat`
  - output renderer: `MetaballQuad` MeshRenderer
  - texture size: `240 × 135`
  - threshold: `0.3`
  - 코드 기본 minimum visible alpha: `0.05`
- `MetaballQuad` shader: `Slainte/LiquidMetaballComposite`

## 현재 미해결 렌더링 문제

최신 육안 확인:

- 잔 전면 스프라이트를 끈 상태에서도 개별 입자 모양이 보였다.
- 단일 입자도 가장자리만 선명하고 중심이 흐리게 보였다.
- 입자가 겹칠수록 현상이 더 심해졌다.

이 결과는 위 정규화 수식과 맞지 않는다. 같은 색/Alpha의 단일 입자라면 coverage가 나눗셈에서 소거되어 내부가 균일해야 한다.

가장 먼저 확인할 것:

1. Unity Play Mode 종료.
2. `Sample_Scene`을 닫았다가 다시 열기.
3. 런타임 Inspector에서 위 WaterCam/MetaballQuad 구성이 실제로 적용됐는지 확인.
4. 기존 Shader Graph 재질이나 기존 Water render texture 경로가 실행 중이지 않은지 확인.
5. 고립된 입자 하나로 다시 테스트.
6. 여전히 발생하면 `DensityTex`, `ColorTex.rgb`, `ColorTex.a / DensityTex`를 각각 디버그 출력.
7. 단일 입자 비율부터 무너지면 instanced `_ParticleColor` 전달과 두 패스 대응을 확인.
8. 두 패스가 실제로 달라진다면 한 번의 draw에서 두 RT에 쓰는 MRT 방식 검토.

현재 단계에서 하지 말 것:

- 원인 확인 없이 `threshold`만 낮추기
- 기본 메타볼 스프라이트 교체
- 입자 수 대폭 감소
- 잔 전체에 최종색 강제 적용
- 기존 물리 혼합 공식 변경

## 기본 스프라이트 확인 결과

- 사용 중: `Assets/MetaballFluid/Sprites/metaball_4 1.png`
- 800×800, 중심 Alpha 255, 바깥으로 갈수록 감소, 외곽 Alpha 0
- RGB는 흰색이므로 색 누적용으로 적합
- `metaball_4.png`는 RGB가 검정이어서 교체하면 색 누적이 검게 될 수 있음
- 따라서 스프라이트 자체는 현재 유력 원인이 아니다.

## 성능 관련 변경과 판단

- MixPair와 composition 비교의 임시 List 할당을 공유 버퍼로 제거했다.
- `OnCollisionStay2D` 혼합 중복 호출을 제거했다.
- 렌더러는 최대 1023개 단위 `DrawMeshInstanced`를 사용한다.
- density/color pass가 같은 행렬/색 배치를 공유하도록 변경했다.
- RenderTexture 크기는 240×135이며 `RHalf` + `ARGBHalf`를 사용한다.
- 런타임 Pool 고갈이 병목이라는 근거를 확인하지 못했으므로 Pool 용량을 늘리지 않았다.
- Profiler 전후 비교는 아직 하지 못했다.

## 빌드/검증

- C# 빌드 오류 0개.
- Unity Editor에서 두 신규 셰이더 임포트 오류 없음.
- 수식 정적 검증:
  - `0.2 + 0.2`, 동일 밀도 → `0.2`
  - `0.2 + 0.6`, 동일 밀도 → `0.4`
  - Alpha 0 표시 하한 → `0.05`
- 실제 화면 결과는 위 수식과 달라 아직 완료로 표시하면 안 된다.

