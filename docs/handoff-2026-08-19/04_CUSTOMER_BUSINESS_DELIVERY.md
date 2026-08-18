# 손님, 영업 및 배송 변경

## 손님 방문 데이터 모델

`CustomerVisitData` 변경:

- `preferredTasteKey`: 기획 CSV 원문 보존용, 실제 주문 선택에는 아직 미사용
- `preferredAtmosphereKey`: 기획 CSV 원문 보존용, 실제 주문 선택에는 아직 미사용
- `initiallyAvailable`: 시작 시 등장 가능 여부
- `availabilityTransitions`: 조건 만족 시 활성/비활성을 순서대로 변경
- `cooldownGroupKey`를 `reappearanceGroupKey`로 마이그레이션
- `GetCooldownKey()`를 `GetReappearanceKey()`로 교체
- 초 단위 `cooldownSeconds` 제거

## 손님 CSV importer

- 기본 입력은 `Downloads/Data_slainte.csv - 손님 (1).csv`가 있으면 우선 사용하고 없으면 기존 `손님.csv`를 사용한다.
- `등장 확률`을 invariant culture float로 읽고 0 이상인지 검사한다.
- 설명 행처럼 보이는 ID 없는 행은 오류 대신 warning으로 건너뛴다.
- 선호 맛/분위기 원문을 저장한다.
- 속성 값에 따라 초기 활성 및 에피소드/플래그 기반 availability transition을 구성한다.
- 시작 활성 손님에게 presentation sprite가 없으면 임시 비활성화하고 warning을 남긴다.
- 게시 시 Character DB, Visit DB뿐 아니라 CustomerOrder DB도 함께 갱신한다.
- 게시된 주문을 동일 key와 동일 asset reference로 다시 조회할 수 있는지 검증한다.
- Batch Mode에서는 데이터 오류를 예외로 반환한다.

## 생성/갱신 데이터

- 수정: `CustomerVisit_d1001` ~ `CustomerVisit_d1039`
- 신규: `CustomerVisit_d1040`, `CustomerVisit_d1041`
- 신규 주문 8개:
  - `CustomerOrder_d1001_rec_1014`
  - `CustomerOrder_d1008_rec_1012`
  - `CustomerOrder_d1009_rec_1012`
  - `CustomerOrder_d1011_rec_1012`
  - `CustomerOrder_d1014_rec_1012`
  - `CustomerOrder_d1039_rec_1006`
  - `CustomerOrder_d1040_rec_1007`
  - `CustomerOrder_d1041_rec_1007`
- `CharacterDatabase.asset`, `CustomerVisitDatabase.asset`, `CustomerOrderDatabase.asset` 갱신
- 별도 미확정 데이터 목록: `docs/customer-availability-missing-data.md`

## 영업 손님 선택

- cooldown dictionary와 fallback 선택을 제거했다.
- 최근 등장한 reappearance key 2개를 Queue + HashSet으로 보관한다.
- 최근 2명은 다음 weighted selection 후보에서 제외한다.
- 고유 후보가 부족해 선택할 수 없으면 최근 제한을 무시하지 않는다.
- 그 영업의 랜덤 손님 생성을 중단하고 남은 영업 시간은 계속 진행한다.
- episode entry는 trigger condition을 추가 확인한다.
- 방문 condition과 availability transition을 모두 통과해야 후보가 된다.

## 주문 데이터 검증

- `CocktailOrderGenerator.CanGenerateOrder(recipeId)`가 추가됐다.
- 주문 key, recipe ID, 주문 가능 recipe, CustomerSpawner, CustomerOrderDatabase reference를 검사한다.
- 영업 시작 시 invalid visit을 오늘의 frozen pool에서 제외한다.
- 주문 표시가 실패하면 방문/주문/레시피 ID를 포함한 기술 실패 결과를 반환한다.
- 정상 플레이 실패와 기술 실패의 로그 심각도를 구분한다.
- `CustomerSpawner.ShowVisit`가 성공 여부를 bool로 반환한다.

## 배송 UI

- 배송 패널을 열 때 캐릭터는 숨겨진 상태다.
- 구매 실패 시 캐릭터를 표시하지 않는다.
- 구매 성공 시 캐릭터를 보여주고 `slide duration + hold duration` 후 숨긴다.
- 빠른 두 번째 구매는 기존 presentation coroutine을 취소하고 시간을 다시 시작한다.
- 캐릭터가 숨겨져도 배송 패널과 Recipe Book 차단은 유지된다.
- 배송을 비활성화하면 패널이 닫히고 탭이 반투명/비활성화되며 Recipe Book 차단이 해제된다.

## Validator 변경

- `BusinessCustomerPoolStressTestTools`: 50,000회 weighted draw와 최근 2명 제한 검증
- `BusinessShiftValidator`: 주문 DB 연결, availability, 최근 제한, 누락 주문 기술 실패 검증
- `BusinessCustomerPoolStressBootstrap`: cooldown 표시 제거, spawning stopped 표시
- `BusinessIntegrationPlaytestBootstrap`: 최근 2명 규칙 안내 및 상태 표시
- `DeliverySystemValidator`: 실패 구매, 성공 후 캐릭터 표시, 빠른 연속 구매, panel/Recipe Book 상태 검증
- `TVSystemValidator`: cooldown shared key 대신 reappearance shared key 사용

## 다음 검증

1. 실제 CSV를 다시 import/publish한 뒤 d1001~d1041 연결 확인.
2. 이미지 없는 시작 손님이 예상대로 비활성인지 확인.
3. 최근 2명 제한에서 후보가 2명뿐일 때 영업 타이머가 계속 가는지 확인.
4. 잘못된 주문이 있는 방문만 제외되고 정상 방문은 유지되는지 확인.
5. 배송 실패 구매 후 캐릭터가 나오지 않는지 확인.
6. 빠른 연속 구매 후 캐릭터가 너무 일찍 숨지 않는지 확인.

