# 알려진 미해결 이슈

기준일: 2026-08-22

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

관련: [bartending-systems.md](bartending-systems.md#metaballfluid-시스템-assetsmetaballfluid)의 MetaballFluid 절, [implementation-plan.md](../implementation-plan.md) 4.3단계.

## 브리즈 보드카 배치 다음 위스키 소환 실패

`BusinessBartendingBootstrap.TryPlaceBottleFromShelf()`로 서로 다른 병을 순서대로 배치할 때, 특정 순서(브리즈 보드카 다음 위스키)에서 배치가 실패하는 사례가 보고됐다. 원인 후보: 같은 병 ID 중복 배치 거부, `LiquorBottleDef.id`/`ItemDef.id` 불일치, 재고 부족, 8개 `sessionSlots`의 점유 상태, `FindRightmostFreeSlot()` 결과 중 하나. 정확한 재현 조건과 Console 경고를 기준으로 재조사가 필요하다.

## 재료 마스터 데이터 구조 개편 보류

Planning 에셋(`ItemDef`/`LiquorBottleDef`)과 기획 재료 CSV를 단일 기준 데이터로 통합하는 구조 개편은 도구장 작업 이후로 보류된 상태다. 재개할 때 먼저 정할 것:

1. 재료 데이터의 최종 source of truth
2. CSV가 에디터 임포트 입력인지 런타임 데이터인지
3. `ItemDef`와 `LiquorBottleDef`를 통합할지 직접 참조로 연결할지

## 관련 문서

- 병 아트·데이터 연결 누락 현황: [bartending-art-data-mismatches.md](../bartending-art-data-mismatches.md)
- 손님 등장조건·이미지·에피소드 누락 현황: [customer-availability-missing-data.md](../customer-availability-missing-data.md)
