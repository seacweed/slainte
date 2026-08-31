# TV 방송 시스템

휴식 화면과 영업 화면에 공통으로 영향을 주는 하루 단위 랜덤 이벤트. 데이터·런타임 판정과 휴식 화면 표시 코드는 `Assets/_Project/Features/Rest/Runtime/TV/`에 함께 둔다.

## 데이터 (`TVBroadcastDatabase.cs`)

`TVBroadcastDatabase`(ScriptableObject, `Resources/TV/TVBroadcastDatabase`)가 `TVBroadcastEntry` 목록을 보관한다.

```
TVBroadcastEntry
 ├── id / title / tickerText          (식별자, 제목, 하단 자막)
 ├── weight                            (추첨 가중치)
 ├── presenterSprite / eventSprite     (진행자/이벤트 카드 연출 이미지)
 ├── effectType                        (TVBroadcastEffectType)
 ├── targetTag                         (태그 부스트 효과 대상)
 ├── effectMultiplier                  (부스트 배율)
 ├── minimumAbvPercent                 (고도수 주문 부스트 전용, 음수면 미사용)
 └── restrictionReason                 (이용 제한 시 표시할 사유 문구)
```

`TVBroadcastDatabase.PickWeighted(random)`이 `weight` 기준 가중치 추첨을 담당한다.

## 효과 종류 (`TVBroadcastEffectType`)

| 값 | 효과 |
|---|---|
| `DisableDelivery` | 영업 중 술장의 배송(Delivery) 탭 이용 금지 |
| `DisableRestShop` | 휴식 화면 일반 상점 클릭 금지 |
| `BoostOrderTagWeight` | `targetTag`를 가진 주문(또는 `minimumAbvPercent` 이상 도수의 주문)의 추첨 가중치를 `effectMultiplier`배로 |
| `BoostTips` | 판매 팁 배율 `effectMultiplier`배 |
| `BoostCustomerTagWeight` | `targetTag`를 가진 손님 방문의 추첨 가중치를 `effectMultiplier`배로 |

## 예보(Forecast) → 활성(Active) 하루 지연 구조 (`TVBroadcastRuntime.cs`)

`GameProgress`에 저장되는 4개 필드로 상태를 관리한다: `TVForecastBroadcastId`/`TVForecastRevealed`(휴식에서 다음 방송을 미리 보여줄 예고), `TVActiveBroadcastId`/`TVActiveBusinessDay`(실제 효과가 적용 중인 활성 방송과 그 날짜).

1. **예보 확정** — `EnsureForecast(progress, database)`: 예보가 비어있으면 `PickWeighted()`로 하나 뽑아 `SetTVForecast()`로 저장. 이미 있으면 그대로 반환(같은 날 재방문해도 같은 예고 유지). `TVRestBootstrap`이 휴식 화면 시작 시 호출해 예보가 항상 준비되도록 하고, `TVUIManager.OnOpen()`도 TV 팝업을 열 때 다시 호출해 예보를 확인하며 `MarkTVForecastRevealed()`로 "이미 본 예고" 상태를 남긴다.
2. **활성화** — `BusinessFlowBootstrap`이 영업 시작 시 `ActivateForecastForBusiness(progress, database)`를 호출해 그날의 예보를 `ActivateTVForecastForBusiness()`로 활성 방송으로 승격시킨다. 즉 **휴식에서 예고를 본 방송이 다음 영업에서 실제 효과를 낸다** — 같은 영업 안에서 즉시 적용되는 게 아니라 하루 지연되는 디자인.
3. **활성 조회** — `GetActiveBroadcast(progress, database)`는 `TVActiveBusinessDay == progress.CurrentDay`일 때만 값을 반환한다(날짜가 지나면 자동 만료).

이후 `IsActiveEffect()`/`GetTipMultiplier()`/`GetTaggedWeightMultiplier()`/`GetOrderWeightMultiplier()`/`OrderMatchesActiveBoost()`/`CustomerMatchesActiveBoost()`/`IsRestShopDisabled()` 등 판정 헬퍼가 활성 방송 하나를 기준으로 각 효과를 계산한다. `BoostOrderTagWeight`의 도수 판정(`MatchesOrder`)은 `CocktailRecipeDataLoader`로 레시피 카탈로그를 지연 로딩해 정적으로 캐싱한다(`ResetRuntimeCache()`가 씬 재시작 시 초기화).

## 게임플레이 연결점

- **영업 배송 금지**: `BusinessFlowBootstrap`이 영업 시작 시 활성 방송이 `DisableDelivery`면 `LiquorShelfUI.SetDeliveryAvailable(false, reason)`을 호출 — 술장 배송 탭이 비활성화되고 사유가 로그로 남는다(자세한 배송 탭 동작은 [liquor-shelf.md](liquor-shelf.md#배송delivery-탭) 참고).
- **휴식 상점 금지**: 일반 상점 `ObjectInteraction`의 `disableWhenRestShopRestricted`를 켜두면 `IsRestShopDisabled()`로 클릭 자체를 막고 회색 오버레이로 표시(자세한 내용은 [restscene-systems.md](restscene-systems.md#objectinteraction--objectinteractionboard) 참고). TV/작전판 등 제한 대상이 아닌 오브젝트는 이 플래그를 꺼둔다.
- **손님·주문 가중치**: `BusinessSequencePlanner`가 방문·주문 가중치를 계산할 때 `GetTaggedWeightMultiplier`/`GetOrderWeightMultiplier`를 곱해 반영한다.

## 휴식 화면 표시

- `TVSystemController`(월드 오브젝트에 부착): `ObjectInteraction`과 짝을 이뤄, 화면 공간 Canvas 아래에 `TVUIManager` 패널을 인스턴스화하고 `interaction.targetUIManager`로 연결한다. 프리팹 본체는 씬 월드에 남고 팝업만 Canvas 자식으로 생성되는 구조.
- `TVUIManager`(`BaseUIManager` 상속): `OnOpen()`에서 예보를 확인·확정하고 `MarkTVForecastRevealed()` 후 즉시 저장, `Bind(entry)`로 제목/진행자·이벤트 이미지/자막을 채운다. 이미지가 비어있으면 `fallbackPresenterSprite` 또는 안내 텍스트로 대체 표시. 열기/닫기는 `CanvasGroup` 알파 + `tvFrame` 스케일(0.92→1.0) 트윈.
- `TVTicker`: 자막 텍스트를 두 개의 `TextMeshProUGUI`(`firstText`/`secondText`)로 복제해 `pixelsPerSecond`만큼 매 프레임 좌측 이동시키고, 화면 밖으로 나가면 반대쪽 끝(`gap`만큼 띄워서)으로 재배치해 끊김 없이 순환하는 가로 스크롤을 구현한다.

## 관련 파일

- `Assets/_Project/Features/Rest/Runtime/TV/TVBroadcastDatabase.cs`, `TVBroadcastRuntime.cs`
- `Assets/_Project/Features/Rest/Runtime/TV/TVRestBootstrap.cs`, `TVSystemController.cs`, `TVUIManager.cs`, `TVTicker.cs`
- `Assets/_Project/Features/Business/Runtime/Flow/BusinessFlowBootstrap.cs`(영업 시작 시 활성화 호출)
- `Assets/Scripts/GameProgress.cs`(예보/활성 방송 상태 저장)
