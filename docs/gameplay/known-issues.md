# 알려진 미해결 이슈

기준일: 2026-09-02

병합된 다른 작업자 브랜치의 임시 인수인계 문서를 정리하며 확인된, 아직 해결 여부가 검증되지 않은 항목을 모은다. 항목이 해결되면 이 문서에서 제거한다.

## 메타볼 액체 렌더링 — 입자 중심부가 가장자리보다 흐리게 보임

증상: 잔 전면 스프라이트를 끈 상태에서 단일 입자만 봐도 가장자리만 선명하고 중심이 흐리게 보이며, 입자가 겹칠수록 심해진다.

현재 누적 셰이더 수식은 스프라이트 알파를 coverage로 사용한다.

```text
D     = Σ coverage
RGB_N = Σ particleRGB × coverage
A_N   = Σ max(particleAlpha, 0.05) × coverage
finalRGB   = RGB_N / D
finalAlpha = A_N / D
```

같은 색·Alpha의 단일 입자라면 coverage가 나눗셈에서 소거되어 내부가 균일해야 하므로, 위 증상은 이 수식만으로는 설명되지 않는다. 사용 중인 `metaball_4 1.png`(800×800, 중심 Alpha 255 → 외곽 0, RGB 흰색)는 색 누적용으로 적합한 형태라 유력 원인에서 제외됐다. 실제 셰이더 실측 재검증은 아직 이루어지지 않았다.

관련: [bartending-systems.md](bartending-systems.md#metaballfluid-표현-인프라-assets_projectfeaturesbartendinginfrastructuremetaballfluid)의 MetaballFluid 절, [implementation-plan.md](../implementation-plan.md) 4.3단계.

## 브리즈 보드카 배치 다음 위스키 소환 실패

`BusinessBartendingBootstrap.TryPlaceBottleFromShelf()`로 서로 다른 병을 순서대로 배치할 때, 특정 순서(브리즈 보드카 다음 위스키)에서 배치가 실패하는 사례가 보고됐다. 원인 후보: 같은 병 ID 중복 배치 거부, `LiquorBottleDef.id`/`ItemDef.id` 불일치, 재고 부족, 8개 `sessionSlots`의 점유 상태, `FindRightmostFreeSlot()` 결과 중 하나. 정확한 재현 조건과 Console 경고를 기준으로 재조사가 필요하다.

## 이전 바텐딩 데이터의 중복 경로

재료·레시피의 기준 원본은 `Features/Bartending/Content/Source/Planning`의 세 CSV로 확정됐고, 임포터가 `Resources/Bartending/Items`, `Resources/Bartending/Recipes`, `Content/Generated/LiquorBottles/Planning`을 갱신한다. 제품 레시피는 `Resources/Bartending/Recipes`만 읽는다.

다만 다음 이전 데이터가 아직 병존한다.

- `Content/Source/Legacy/ItemData.csv`와 이전 `ItemDataImporter`
- `Content/Legacy/Planning`의 이전 생성 결과
- `Content/Generated/LiquorBottles` 바로 아래의 이름 기반 병 에셋

`ItemDef`와 `LiquorBottleDef`는 현재 서로 다른 화면·역할을 담당하며 동일 아이템 ID로 연결된다. 당장 하나의 타입으로 합치는 대상은 아니다. 위 이전 에셋은 Scene·Prefab·카탈로그 참조를 확인한 뒤 별도 변경에서 제거한다.

## 통합 Resources 구조 검증기의 이전 개수 기준

`RuntimeResourceStructureValidator`는 아직 바텐딩 `ItemDef` 18개, `ItemData` 120개, `CocktailRecipeDef` 94개를 기대한다. 현재 실제 기준은 `ItemDef` 15개, `ItemData` 0개, 기본 `CocktailRecipeDef` 21개이므로 이 통합 검증기는 현재 상태에서 실패한다. 바텐딩 전용 `기획 CSV 에셋 검증`은 새 개수와 직접 판정을 기준으로 통과한다. 통합 검증기의 기대값 또는 개수 검사 방식을 별도 수정해야 한다.

## 관련 문서

- 병 아트·데이터 연결 누락 현황: [bartending-art-data-mismatches.md](../bartending-art-data-mismatches.md)
- 손님 등장조건·이미지·에피소드 누락 현황: [customer-availability-missing-data.md](../customer-availability-missing-data.md)
