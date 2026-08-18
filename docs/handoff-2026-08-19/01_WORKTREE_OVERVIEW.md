# 작업 트리 전체 변경 요약

## 액체 생성, 부피 및 성능

- `LiquidPayload.MixPair`와 `HasDifferentComposition`에서 매번 생성하던 `List<ItemDef>`을 공유 버퍼로 교체했다.
- `LiquidReaction.OnCollisionStay2D`의 반복 혼합 호출을 제거하고 충돌 진입 및 기존 agitation 경로를 유지했다.
- `LiquidPool`이 입자 프리팹의 `DefaultVolumeMl`을 캐시하며, `LiquidParticleData`가 없는 프리팹은 1ml fallback과 경고를 사용한다.
- `water_particle.prefab`의 렌더 스케일이 `0.04`에서 `0.06`으로 변경됐고, 입자당 기본 부피가 `2.5ml`로 설정됐다.
- 병 Pour가 초당 ml 기준으로 변경됐다. 현재 병 프리팹은 `83.333336 ml/s`, 프레임당 최대 8입자다.
- Pool 용량 자체는 이 변경에서 늘리지 않았다. `Sample_Scene`의 기존 `poolSize: 1000`은 diff 대상이 아니다.
- Compute Shader를 새로 추가하거나 수정하지 않았다.

## 액체 색과 메타볼 렌더링

- `EvaluateColor()`가 모든 Alpha를 1로 강제하지 않고 ItemDef의 실제 Alpha를 사용하도록 기본값을 `minimumAlpha = 0`으로 변경했다.
- 최종 칵테일 색 계산을 기존 `LiquidPayload.EvaluateColor()`로 재사용한다.
- 신규 누적 셰이더, 합성 셰이더, 인스턴싱 렌더러를 추가했다.
- 메인 카메라에서 Water 레이어를 제외하고, `WaterCam`의 새 렌더러가 밀도/색 텍스처를 생성하도록 `Sample_Scene`을 수정했다.
- 현재 렌더링은 미완료다. 단일 입자 가장자리 강조와 겹침 시 악화 현상을 다음 세션에서 우선 진단해야 한다.

## 계량 UI와 최종 RGBA

- 용기 내용 UI에 설정 가능한 TMP 폰트가 추가됐다.
- NotoSansKR SDF를 사용하고 주요 텍스트를 노란색 계열로 변경했다.
- 표기 형식은 `<b>재료명</b> • 양 ml`, 합계도 같은 구분 형식을 사용한다.
- 칵테일 평가 결과에 `finalColor`가 추가됐다.
- 결과 문자열은 HEX를 먼저 `#RRGGBBAA`로 표시하고 이어서 `RGBA(r, g, b, a)`를 표시한다.
- 최종 색은 제출/평가 시점의 `CocktailComposition`에서 가져온다.

## Serving Area, 슬롯 및 용기 이동

- 신규 `serving_area.png`를 Serving Target 이미지로 사용할 수 있게 했다.
- Serving Target은 고정 정규화 좌표, CanvasGroup fade, 잔을 들었을 때의 표시 상태를 사용한다.
- 바 슬롯 설정이 8개 위치로 확장됐고 UI 가이드도 필요한 수만큼 복제한다.
- Glass/Beaker의 0.1초 Snapping 상태를 제거하고 슬롯 배치를 즉시 완료한다.
- 슬롯 이동 시 용기만 순간 이동하지 않고 추적 중인 액체 입자와 얼음도 같은 delta만큼 이동한다.
- Bottle은 기울이는 동안 수평 이동하지 않도록 검증 조건이 변경됐다.

## 손님 및 영업

- 손님 방문 데이터에 선호 맛/분위기 원문 필드, 초기 활성 상태, 조건부 활성 전환 목록이 추가됐다.
- 기존 초 단위 cooldown 방식이 최근 등장 손님 2명 제외 방식으로 교체됐다.
- 후보가 부족해 최근 2명 규칙을 만족할 손님이 없으면 규칙을 완화하지 않고 해당 영업의 랜덤 손님 생성을 중단하며 남은 시간은 계속 간다.
- 주문 에셋, 주문 DB, 레시피의 연결을 영업 시작과 주문 시작 전에 검증한다.
- 주문 데이터 누락은 기술 실패로 분류하고 방문/주문/레시피 정보를 오류 이유에 포함한다.
- 손님 CSV importer가 등장 확률, 선호 원문, 가용성 전환과 주문 DB 게시를 처리하도록 확장됐다.

## 배송

- 배송 패널을 열 때 캐릭터를 즉시 표시하지 않는다.
- 구매 성공 후에만 배송 캐릭터를 보여주고 지정 시간 후 숨긴다.
- 빠른 연속 구매 시 기존 연출 코루틴을 중단하고 표시 시간을 다시 시작한다.
- 배송 패널 자체는 구매 연출 종료 후에도 열린 상태를 유지한다.
- 실패 구매, 연속 구매, 비활성 탭 및 Recipe Book 차단 상태 검증이 추가됐다.

## 데이터와 에셋

- `CustomerVisit_d1001`부터 `d1039`까지 갱신됐다.
- `CustomerVisit_d1040`, `d1041`과 주문 에셋 8개가 신규 생성됐다.
- Character, Visit, CustomerOrder 데이터베이스가 갱신됐다.
- NotoSansKR TMP 폰트 에셋에 글리프 데이터가 추가됐다.
- 기존 `docs/customer-availability-missing-data.md`가 신규 문서로 존재한다.

## 확인된 빌드 상태

- C# 빌드 오류 0개.
- 신규 셰이더 임포트 오류 없음.
- Unity Play Mode 통합 검증은 완료되지 않았다.

