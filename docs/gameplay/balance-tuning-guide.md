# 기획자용 밸런스 조정 안내서

기준일: 2026-08-24

이 문서는 코드나 Unity YAML을 직접 수정하지 않고 Inspector에서 영업·경제·레시피 밸런스를 조정하는 방법을 설명한다. 현재 프로젝트가 실제로 사용하는 기존 에셋을 그대로 편집하며, 별도의 복제 데이터는 만들지 않는다.

## 이 도구가 수정하는 것

`밸런스 설정 열기`는 새로운 밸런스 파일이나 별도의 프리셋을 만드는 도구가 아니다. 여러 위치에 이미 존재하는 실제 런타임 에셋을 한 Inspector에서 찾아 편집하기 쉽게 모아 보여주는 도구다.

| 빠른 편집 섹션 | 실제로 수정되는 원본 |
|---|---|
| Inspector 위쪽 영업 설정 | `Assets/Resources/Business/BusinessOrderFlowSettings.asset` |
| 칵테일 | `Assets/Resources/Recipes/Planning`의 `CocktailRecipeDef` |
| 재료 | 상점 카탈로그에 연결된 `LiquorBottleDef`와 해당 `ItemDef` |
| TV 방송 | `Assets/Resources/TV/TVBroadcastDatabase.asset` |
| 손님 등장·주문 가중치 | 현재 `CustomerVisitDatabase`에 연결된 방문·주문 에셋 |
| 업그레이드 가격 | `Assets/Data/UpgradeData`의 `UpgradeDef` |

따라서 빠른 편집에서 값을 바꾼 뒤 원본 에셋을 직접 선택해도 같은 변경값이 보인다. 반대로 원본 에셋에서 바꾼 값도 `목록 새로고침` 후 빠른 편집에 반영된다.

### 문서에서 말하는 현재값과 기본값

- **현재값**은 현재 프로젝트의 `.asset` 또는 Scene에 직렬화되어 있으며 실제 실행에서 읽히는 값이다.
- C# 코드에 적힌 초기값은 새 에셋을 만들 때의 폴백이다. 이미 존재하는 에셋의 현재값을 덮어쓰지 않는다.
- 이 도구에는 `기본값으로 초기화` 버튼이 없다. 값을 바꾸는 즉시 해당 원본 에셋의 수정 사항이 되므로, 저장 전에 Inspector의 Undo 또는 버전 관리 diff로 확인한다.
- 아래 가격표와 확률표는 기준일의 스냅샷이다. 실제 편집 화면의 숫자가 문서와 다르면 Inspector에 표시되는 원본 에셋 값을 우선한다.

## 가장 빠른 사용 방법

1. Unity 메뉴에서 `Slainte > 데이터 > 밸런스 설정 열기`를 선택한다.
2. 선택된 `BusinessOrderFlowSettings` Inspector에서 기존 영업 설정을 수정한다.
3. Inspector 아래쪽의 `기획 밸런스 빠른 편집`을 펼친다.
4. `칵테일`, `재료`, `TV 방송`, `손님 등장·주문 가중치`, `업그레이드 가격` 중 원하는 항목을 편집한다.
5. `밸런스 유효성 검사`를 실행한다.
6. `Ctrl+S` 또는 `File > Save Project`로 저장하고 Play Mode에서 확인한다.

빠른 편집 영역은 목록을 복사해 보관하지 않는다. 가격을 바꾸면 해당 `CocktailRecipeDef` 또는 `LiquorBottleDef` 원본 에셋 자체가 수정된다. 각 행의 `선택` 버튼을 누르면 원본 에셋의 전체 Inspector를 열 수 있다.

상단 버튼의 역할은 다음과 같다.

| 버튼 | 용도 |
|---|---|
| `목록 새로고침` | 새로 생성·삭제되었거나 다른 Inspector에서 변경된 에셋 목록을 다시 읽는다. |
| `설명서 선택` | Project 창에서 이 Markdown 설명서를 선택한다. |
| `밸런스 유효성 검사` | 음수 가격, 잘못된 용량·가중치, 누락 연결 등 기획 데이터의 기본 오류를 검사한다. |
| `영업 구조 검증` | 영업 흐름과 시간 기반 구조가 현재 설정으로 동작 가능한지 검사한다. |
| `TV 구조 검증` | 방송 ID, 효과 종류, 가중치와 배율 연결을 검사한다. |

## 반드시 알아둘 데이터 규칙

### Inspector 수정값과 CSV

레시피와 기본 재료는 다음 CSV로부터 생성된 에셋이다.

- `Assets/Editor/Data/Planning/items.csv`
- `Assets/Editor/Data/Planning/recipes.csv`
- `Assets/Editor/Data/Planning/recipe_ingredients.csv`

에디터를 열 때 CSV 값으로 자동 복원하는 동작은 꺼져 있다. 따라서 Inspector 수정값은 에디터를 다시 열어도 유지된다.

단, 다음 메뉴를 직접 실행하면 CSV 값이 레시피·재료 에셋을 덮어쓴다.

- `Slainte > 데이터 > 기획 CSV 임포트`
- `Slainte > 데이터 > 기준 CSV 바로 임포트`

Inspector에서 밸런스를 조정한 뒤에는 위 임포트 메뉴를 실행하지 않는다. CSV를 다시 가져와야 한다면 먼저 변경된 Inspector 값과 CSV 중 어느 쪽을 남길지 결정한다.

`Assets/StreamingAssets/Data/recipes.csv`와 `ingredients.csv`는 현재 영업 레시피의 기준 데이터가 아니다. 실제 런타임 레시피는 `Assets/Resources/Recipes`의 `CocktailRecipeDef`를 사용한다.

### 편집 시점

- 가능하면 Play Mode가 아닌 상태에서 수정한다.
- ID는 저장·주문·에셋 연결에 쓰이므로 특별한 이유 없이 바꾸지 않는다.
- 가격과 배율은 음수를 사용하지 않는다.
- 기존 저장에는 이미 저장된 재고와 재화가 있으므로 `기본 소지 병 수`를 바꿔도 기존 세이브의 재고는 즉시 바뀌지 않을 수 있다. 새 저장에서도 확인한다.

## 영업시간과 전역 판매 보상

원본 에셋은 `Assets/Resources/Business/BusinessOrderFlowSettings.asset`이다. 통합 Inspector의 위쪽 기본 영역에서 수정한다.

| Inspector 필드 | 현재값 | 실제 효과 |
|---|---:|---|
| `영업 제한시간(초)` | 180 | 한 번의 영업에 주어지는 실제 초 |
| `goodReputationReward` | 2 | Good 판매 후 평판 변화 |
| `midReputationReward` | 0 | Mid 판매 후 평판 변화 |
| `badReputationReward` | -1 | Bad 판매 후 평판 변화 |
| `badPenaltyRate` | 1.3 | Bad일 때 정가 대비 추가 차감 비율 |
| `bigFishGoodBonusRate` | 2 | 거물 Good일 때 정가에 더하는 보너스 비율 |
| `bigFishFailurePenaltyRate` | 3 | 거물 Mid/Bad일 때 정가 대비 차감 비율 |
| `satisfiedTipRate` | 0.3 | 만족 손님의 정가 대비 팁 비율 |
| `neutralTipRate` | 0 | 보통 손님의 정가 대비 팁 비율 |
| `dissatisfiedTipRate` | 0 | 불만족 손님의 정가 대비 팁 비율 |

영업 타이머는 주문 표시, 제조, 피드백, 영업 인카운터 중에도 흐른다. 명시적 일시정지 또는 `Time.timeScale == 0`일 때만 멈춘다. 시간이 끝났을 때 이미 진행 중인 주문이나 인카운터는 취소하지 않고, 해당 흐름이 끝난 뒤 영업을 마무리한다.

### 현재 판매 계산식

유효한 레시피의 정가를 `P`, 손님 상태별 팁 비율을 `T`, TV 팁 배율을 `V`, Bad 페널티 비율을 `B`라고 하면 다음과 같다.

```text
기본 판매 수익 = P
팁           = round(P × T × V)
Bad 페널티   = round(P × B)
최종 수익     = 기본 판매 수익 + 팁 - 페널티
```

현재 설정에서는 다음 결과가 나온다.

| 결과 | 일반 날 | 팁 방송 날 |
|---|---:|---:|
| Good | 약 `P × 1.30` | 약 `P × 1.45` |
| Mid | `P` | `P` |
| Bad | 약 `P × -0.30` | 약 `P × -0.30` |

TV의 1.5배는 전체 판매액이 아니라 팁 부분에만 적용된다. 기본 팁 30%가 45%가 되는 효과다.

거물 손님은 별도 계산을 사용한다.

- Good: `P + round(P × bigFishGoodBonusRate × TV 팁 배율)`
- Mid/Bad: `P - round(P × bigFishFailurePenaltyRate)`
- 현재값 기준 거물 Good은 일반 날 약 `3P`, 팁 방송 날 약 `4P`다.
- 현재값 기준 거물 Mid/Bad는 약 `-2P`다.

### 현재 사용되지 않는 필드

`goodRecipePriceMultiplier`, `midRecipePriceMultiplier`, `badRecipePriceMultiplier`는 Inspector에 표시되지만 현재 레시피 판매 계산에는 연결되어 있지 않다. 특히 `midRecipePriceMultiplier = 0.5`여도 Mid 수익은 정가 `P`다.

`goodMoneyReward`, `midMoneyReward`, `badMoneyReward`는 정상적인 레시피 가격을 찾지 못했을 때 사용하는 폴백 보상이다. 일반 영업 칵테일 가격 조정에는 사용하지 않는다.

## 칵테일 가격과 배합

통합 Inspector의 `칵테일` 섹션에서 검색하거나, `Assets/Resources/Recipes/Planning`의 개별 `CocktailRecipeDef`를 선택한다.

### 자주 수정하는 필드

| 필드 | 의미 |
|---|---|
| `price` | 일반 화폐 정가 |
| `strangeCoinPrice` | 이상한 동전 정가 |
| `isOrderable` | 일반 주문 후보 포함 여부 |
| `glassId` | 정답 잔 ID |
| `iceRequirement` | 정답 얼음 조건 |
| `requiredTechnique` | Build, Stir, Shake 등의 정답 제조법 |
| `toleranceMl` | 재료별 값이 비어 있을 때 사용하는 기본 용량 허용 오차 |
| `minTotalMl` / `maxTotalMl` | 완성 칵테일 전체 용량 범위 |
| `ingredients[].targetMl` | 각 재료의 정답 용량 |
| `ingredients[].toleranceMl` | 각 재료의 개별 허용 오차 |
| `tasteTags` / `moodTags` | 맛·분위기 조건 주문에 쓰이는 태그 |

개별 레시피 Inspector와 통합 Inspector는 총 제조 용량, 일반 재료 원가, 일반 Good 수익, TV 팁 방송 Good 수익을 자동 계산해 보여준다.

### 현재 칵테일 가격표

재료 원가는 현재 일반 상점 한 병 가격을 병 용량으로 나눈 뒤, 구조화된 배합 용량을 곱해 계산했다. 무료 기본 재고와 배송 2배 가격은 반영하지 않은 비교용 값이다.

| ID | 칵테일 | 판매가 | 이상한 동전 | 총 용량 | 재료 원가 | Good 수익 | TV Good 수익 |
|---|---|---:|---:|---:|---:|---:|---:|
| rec_1001 | 번햄 사워 | 307 G | 3 | 90 ml | 219.64 G | 399 G | 445 G |
| rec_1002 | 코튼 사워 | 347 G | 3 | 105 ml | 247.50 G | 451 G | 503 G |
| rec_1003 | 갓레이디 | 336 G | 3 | 60 ml | 240 G | 437 G | 487 G |
| rec_1004 | 갓로드 | 336 G | 3 | 60 ml | 240 G | 437 G | 487 G |
| rec_1005 | 셰이디 | 462 G | 3 | 90 ml | 330 G | 601 G | 670 G |
| rec_1006 | 한여름의 나낭나 | 189 G | 3 | 120 ml | 135 G | 246 G | 274 G |
| rec_1007 | 한여름의 주스 | 97 G | 3 | 120 ml | 69.64 G | 126 G | 141 G |
| rec_1008 | 아이리시 커피 | 1,050 G | 10 | 210 ml | 751.43 G | 1,365 G | 1,523 G |
| rec_1009 | 블랙 커피 | 1,470 G | 14 | 180 ml | 1,050 G | 1,911 G | 2,132 G |
| rec_1010 | 핫 테디 | 727 G | 7 | 170 ml | 369.29 G | 945 G | 1,054 G |
| rec_1011 | 레몬 피즈 | 136 G | 1 | 145 ml | 97.14 G | 177 G | 197 G |
| rec_1012 | 로닌즈 | 322 G | 3 | 160 ml | 230 G | 419 G | 467 G |
| rec_1013 | 보일링 포인트 | 385 G | 4 | 190 ml | 275 G | 501 G | 558 G |
| rec_1014 | 조니 독스 니트 | 210 G | 2 | 30 ml | 150 G | 273 G | 305 G |
| rec_1015 | 조니 독스 온더락 | 210 G | 2 | 30 ml | 150 G | 273 G | 305 G |
| rec_1016 | 번햄 버번 니트 | 210 G | 2 | 30 ml | 150 G | 273 G | 305 G |
| rec_1017 | 번햄 버번 온더락 | 210 G | 2 | 30 ml | 150 G | 273 G | 305 G |
| rec_1018 | 바하 니트 | 210 G | 2 | 30 ml | 150 G | 273 G | 305 G |
| rec_1019 | 바하 온더락 | 210 G | 2 | 30 ml | 150 G | 273 G | 305 G |
| rec_1020 | 브리즈 니트 | 210 G | 2 | 30 ml | 150 G | 273 G | 305 G |
| rec_1021 | 브리즈 온더락 | 210 G | 2 | 30 ml | 150 G | 273 G | 305 G |

Good 및 TV Good 수익은 현재 30% 팁과 TV 1.5배 설정을 사용한 스냅샷이다. Inspector에서 팁 비율이나 TV 배율을 바꾸면 미리보기 값도 함께 바뀐다.

## 재료 가격과 재고

통합 Inspector의 `재료` 섹션에서 검색하거나 `Assets/Data/LiquorBottle/Planning`의 개별 `LiquorBottleDef`를 선택한다.

| 필드 | 의미 |
|---|---|
| `price` | 일반 상점에서 한 병을 사는 가격 |
| `strangeCoinPrice` | 이상한 상점에서 한 병을 사는 가격 |
| `unitVolume` | 한 병에 들어 있는 양 |
| `defaultBottleCount` | 새 저장에서 처음 물질화되는 기본 병 수 |
| `bottleCount` | 최대 보관 병 수 |
| `unlockFlagKey` | 재료 해금에 필요한 진행 플래그 |
| 연결된 `ItemDef.abvPercent` | 제조 및 완성 칵테일 도수 계산에 쓰이는 재료 도수 |
| 연결된 `ItemDef.servingTemperatureC` | 새 액체 입자의 시작 온도 |

`LiquorBottleDef`는 상점 가격과 저장 재고를 담당하고 연결된 `ItemDef`는 제조 속성을 담당한다. 통합 Inspector에서 두 에셋의 가격·용량이 다르면 경고가 나오며, 사용자가 버튼을 누를 때만 상점 값을 제조 데이터에 맞춘다.

### 현재 재료 가격표

| ID | 재료 | 병 용량 | 일반 가격 | 이상한 동전 | 기본 병 수 | 1ml 원가 |
|---|---|---:|---:|---:|---:|---:|
| item_1001 | 열대 주스 | 1,000 ml | 500 G | 5 | 3 | 0.500 G |
| item_1002 | 실청 | 700 ml | 100 G | 1 | 4 | 0.143 G |
| item_1003 | 합성 레몬 | 200 ml | 300 G | 3 | 3 | 1.500 G |
| item_1004 | 슬롭 | 300 ml | 450 G | 5 | 6 | 1.500 G |
| item_1005 | 나낭나 | 700 ml | 2,100 G | 20 | 4 | 3.000 G |
| item_1006 | 코튼 | 700 ml | 2,100 G | 20 | 4 | 3.000 G |
| item_1007 | 헥타르 | 700 ml | 2,100 G | 20 | 4 | 3.000 G |
| item_1008 | 블레스 | 700 ml | 2,100 G | 20 | 4 | 3.000 G |
| item_1009 | 브리즈 보드카 | 700 ml | 3,500 G | 35 | 4 | 5.000 G |
| item_1010 | 조니 독스 | 700 ml | 3,500 G | 35 | 4 | 5.000 G |
| item_1011 | 번햄 버번 | 700 ml | 3,500 G | 35 | 4 | 5.000 G |
| item_1012 | 바하 | 700 ml | 3,500 G | 35 | 4 | 5.000 G |
| item_1013 | 탄산 미닛 | 300 ml | 150 G | 2 | 6 | 0.500 G |
| item_1014 | 뜨거운 물 | 500 ml | 1,000 G | 10 | 4 | 2.000 G |
| item_1015 | 커피 분말 | 200 ml | 5,000 G | 50 | 2 | 25.000 G |

## TV 방송 확률과 팁 증가량

원본은 `Assets/Resources/TV/TVBroadcastDatabase.asset`이다. 통합 Inspector 또는 원본 에셋 Inspector에서 방송 목록을 수정하면 실제 추첨 확률을 바로 확인할 수 있다.

실제 확률은 `해당 방송 weight ÷ 양수 weight 합계`다. 합계가 반드시 100일 필요는 없다.

| 방송 ID | 현재 가중치 | 현재 확률 | 효과 |
|---|---:|---:|---|
| `none` | 55 | 55% | 효과 없음 |
| `delivery_outage` | 5 | 5% | 영업 중 배송 금지 |
| `shop_maintenance` | 5 | 5% | 휴식 중 일반 상점 금지 |
| `high_abv_orders` | 10 | 10% | 대상 주문 가중치 2배 |
| `tip_bonus` | 15 | 15% | 팁 1.5배 |
| `district_9_patrol` | 10 | 10% | 대상 손님 가중치 2배 및 전용 풀 |

팁 방송의 `effectMultiplier`가 팁 증가량이다. 예를 들어 1,000 G 칵테일의 Good 팁은 평소 300 G이고, 1.5배 방송에서는 450 G다. 팁 증가량은 150 G이며 최종 판매액은 1,300 G에서 1,450 G로 증가한다.

## 손님 등장과 주문 확률

통합 Inspector의 `손님 등장·주문 가중치` 섹션에서 수정한다.

| 필드 | 의미 |
|---|---|
| `weight` | 해당 손님 방문이 선택되는 기본 상대 가중치 |
| `initiallyAvailable` | 새 진행에서 처음부터 손님 풀에 포함되는지 |
| `maxDay` | 0이면 제한 없음, 양수면 마지막 등장 가능 날짜 |
| `condition` | 최소 날짜, 플래그, 선행 에피소드 등의 등장 조건 |
| `orders[].weight` | 해당 손님이 가질 수 있는 주문 사이의 상대 가중치 |
| `orders[].condition` | 개별 주문 후보의 활성 조건 |

확률은 고정 퍼센트가 아니라 같은 시점에 조건을 만족한 후보들의 가중치 합으로 계산된다. TV 방송이 손님 또는 주문 태그를 강화하면 해당 후보의 가중치에 TV 배율이 추가로 곱해진다.

손님 원본 CSV의 기본 임포트 위치는 프로젝트 밖의 다운로드 폴더다. 손님 CSV 드래프트를 다시 임포트하고 게시하면 Inspector 수정값이 달라질 수 있으므로 주의한다.

## 배송 가격

영업 중 배송 가격은 일반 재료 가격에 배송 배율을 곱하고 올림 처리한다.

```text
배송 가격 = ceil(일반 한 병 가격 × deliveryPriceMultiplier)
```

현재 배율은 2배다. 이 값은 기존 구조를 유지하기 위해 통합 데이터로 옮기지 않았으며 `BusinessScene`의 `LiquorShelfUI` 컴포넌트에서 수정한다.

1. `Assets/BusinessScene.unity`를 연다.
2. `LiquorShelfUI` 컴포넌트를 가진 오브젝트를 선택한다.
3. Inspector의 `Delivery Price Multiplier`를 수정한다.

## 업그레이드 가격

`Assets/Data/UpgradeData`의 `UpgradeDef` 에셋 또는 통합 Inspector의 `업그레이드 가격` 섹션에서 `pricesPerLevel`을 수정한다. 배열 길이가 최대 레벨이고, 각 원소는 다음 레벨을 구매하는 가격이다.

현재는 구매 가격 차감과 레벨 저장까지만 연결되어 있다. 손님 인내심, 실패 페널티 감소, 얼음 용량, 적재량 증가처럼 설명에 적힌 실제 효과를 읽는 런타임 코드는 확인되지 않았다. 효과가 구현되기 전에는 경제 소모처의 가격만 조정된다고 본다.

## 에피소드 제조와 정산 보상

일반 영업이 아닌 에피소드 제조에는 노드별 `craftingPaymentMultiplier`가 있다. 레시피의 통화별 정가에 이 값을 곱한 금액이 지급된다. 무료 제공 에피소드는 보상 적용을 끈다.

에피소드 종료 시 주는 별도 정산 보상은 `EpisodeData.settlementRewards`의 조건 플래그, 표시 문구, 금액으로 관리한다. 이는 일반 칵테일 팁과 별도다.

## 조작 난이도 관련 고급 수치

다음 값도 Inspector에 있지만 경제 밸런스보다 조작감과 판정 안정성에 직접 영향을 주므로 변경 후 실제 제조 테스트가 필요하다.

- 병 `pourMlPerSecond`: 초당 붓는 양
- 레시피 재료별 `toleranceMl`: 계량 허용 오차
- `minTotalMl` / `maxTotalMl`: 전체 용량 허용 범위
- 셰이커 `requiredShakeDuration`, `minimumReversals`, `maximumReversalInterval`
- 스터러 `stirCompletionDuration`, `maximumCompositionDeviation`
- 도구 `primaryCapacityMl`, `secondaryCapacityMl`, `maxCount`

이 값들은 현재 통합 빠른 편집에서 자동으로 모으지 않는다. 개별 프리팹 또는 설정 에셋의 Inspector에서 수정한다.

## 변경 후 검증 순서

1. 통합 Inspector의 `밸런스 유효성 검사`를 실행한다.
2. 영업시간이나 손님 흐름을 바꿨다면 `영업 구조 검증`을 실행한다.
3. TV 가중치·배율을 바꿨다면 `TV 구조 검증`을 실행한다.
4. `Slainte > Playtest Launcher`에서 새 저장과 격리된 Day 테스트를 실행한다.
5. 정산 화면에서 정가, 팁, 페널티, 최종 지급 통화를 확인한다.
6. 재료를 구매하고 일반 상점 가격, 배송 가격, 구매 후 증가한 ml를 확인한다.
7. 기존 저장과 새 저장에서 기본 재고 동작을 각각 확인한다.

`기획 CSV 에셋 검증`은 CSV에서 생성된 원래 고정 스냅샷을 검사하는 도구다. Inspector에서 가격을 의도적으로 바꾼 뒤에는 고정 가격 검사가 실패할 수 있으므로, Inspector 밸런스 조정 검증에는 `밸런스 유효성 검사`를 사용한다.

## 현재 구조상 주의 또는 미구현 사항

- 업그레이드의 실제 게임플레이 효과는 연결 여부가 확인되지 않았다.
- 레시피 가격 배율 필드는 존재하지만 정상 레시피 판매 계산에는 사용되지 않는다.
- 배송 배율과 시작 재화는 아직 통합 Inspector에 포함되지 않고 기존 씬 또는 진행 데이터 위치를 유지한다.
- 기본 재고 변경은 이미 재고가 저장된 세이브보다 새 세이브에서 확인하는 것이 정확하다.
- 손님 CSV의 기준 파일이 프로젝트 외부 다운로드 폴더에 있어 팀 간 재현성이 낮다.
