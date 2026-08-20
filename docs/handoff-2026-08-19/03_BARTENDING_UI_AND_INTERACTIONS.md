# Bartending UI 및 상호작용 변경

## 계량 UI

- `BusinessBartendingSettings.contentsLabelFont`가 추가됐다.
- 설정 에셋은 `NotoSansKR-Regular SDF.asset`을 참조한다.
- 기본 텍스트 색은 노란색 계열 `(1, 0.82, 0.05, 1)`이다.
- 각 재료 표기는 `<b>재료명</b>  •  양 ml` 형식이다.
- 합계는 `<b>합계</b>  •  총량 ml` 형식이다.
- ml는 새로운 추정값이 아니라 입자 payload의 `LiquidPortion.volumeMl`과 `CocktailComposition` 집계를 사용한다.

관련 파일:

- `Assets/Scripts/Bartending/BusinessBartendingSettings.cs`
- `Assets/Resources/Bartending/BusinessBartendingSettings.asset`
- `Assets/Scripts/Bartending/BartendingSessionBuilder.cs`
- `Assets/TextMesh Pro/Fonts/NotoSansKR-Regular SDF.asset`

## 최종 칵테일 RGBA

- `CocktailComposition.EvaluateFinalColor()`가 기존 `LiquidPayload.EvaluateColor()`를 재사용한다.
- 계산 결과를 캐시하고 composition volume이 변경될 때 dirty 처리한다.
- `CocktailEvaluationResult.finalColor`에 결과를 저장한다.
- 제출/평가 결과 문자열에서 HEX를 먼저 출력한다.

```text
최종 색상: #RRGGBBAA | RGBA(r, g, b, a)
```

- 색 혼합 공식과 Alpha 공식 자체는 변경하지 않았다.
- 확인 시점은 `CocktailEvaluator`가 composition을 평가하는 제출/결과 확정 시점이다.

관련 파일:

- `Assets/Scripts/Bartending/VesselLiquidTracker.cs`
- `Assets/Scripts/Bartending/CocktailEvaluator.cs`
- `Assets/Scripts/Bartending/LiquidParticleData.cs`

## Serving Area

- 신규 이미지 `Assets/Art/Bartending/serving_area.png`가 추가됐다.
- `serveTargetSprite`, `serveTargetFadeDuration`, `serveTargetNormalized` 설정이 추가됐다.
- Serving Target은 손님 sprite bounds를 따라가지 않고 고정 normalized rect를 사용한다.
- Business 화면에서는 하단이 bar table 상단에 맞춰진다.
- Glass의 `HeldStateChanged` 이벤트로 들고 있을 때만 표시 조건을 갱신한다.
- CanvasGroup을 사용해 unscaled time fade를 적용한다.
- 이미지가 없을 때만 기존 4개 border fallback을 만든다.

## 바 슬롯

- 설정 에셋의 슬롯 위치가 8개로 확장됐다.
- Sandbox guide count도 8개를 사용한다.
- `EnsureSlotLayoutGuideCount`가 UI 가이드가 부족할 때 마지막 가이드를 복제한다.
- LayoutGroup이 없으면 가이드를 layout 너비에 균등 배치한다.
- Validator는 기능 슬롯이 정확히 8개인지 확인한다.

## Glass/Beaker 슬롯 이동

- `Snapping` 상태와 `slotSnapDuration`을 제거했다.
- 슬롯 배치 시 용기 위치와 회전을 즉시 확정한다.
- `VesselLiquidTracker.TranslateTrackedParticles(delta)`가 용기 속 액체와 얼음을 같이 이동한다.
- `IceCubeController.Translate(delta)`가 드래그 중이 아닌 얼음을 이동한다.
- Glass는 잡기/놓기 상태 변경 이벤트를 발생시킨다.
- Validator는 용기, 액체 입자, 얼음이 동일한 delta로 이동했는지 확인한다.

이 변경은 병렬 작업 범위에서 발생했으므로 다음 기기에서 병/잔 조작 회귀 검증이 필요하다.

## Bottle Pour

- 기존 초당 입자 timer 대신 `pourMlPerSecond`를 사용한다.
- Pool 프리팹의 `DefaultParticleVolumeMl`로 입자 방출 간격을 계산한다.
- 느린 프레임 catch-up은 프레임당 최대 8입자로 제한한다.
- Pool에서 입자를 받지 못하면 부피를 차감하지 않는다.
- 현재 Bottle/OrangeJuiceBottle prefab 값은 `83.333336 ml/s`, 최대 8입자/프레임이다.
- `water_particle` 기본 부피가 2.5ml이므로 설정상 약 33.33입자/초에 해당하지만 실제 성능 수치는 측정하지 않았다.

## 필요한 다음 검증

1. Pour 중 실제 ml 감소율과 UI ml 일치.
2. 낮은 프레임에서 최대 8입자 제한과 용량 보존.
3. Glass/Beaker 슬롯 이동 후 액체/얼음이 남겨지지 않는지 확인.
4. 잔을 들고 놓을 때 Serving Area fade가 중복 실행되지 않는지 확인.
5. 제출 결과의 HEX가 `#RRGGBBAA`이고 Alpha가 실제 데이터와 일치하는지 확인.

