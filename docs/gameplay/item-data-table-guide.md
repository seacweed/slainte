# 바텐딩 기획 CSV 작성 가이드

기준일: 2026-09-02

## 1. 기준 원본

현재 바텐딩 재료와 레시피의 기준 원본은 다음 세 파일이다.

- 아이템: `Assets/_Project/Features/Bartending/Content/Source/Planning/items.csv`
- 레시피: `Assets/_Project/Features/Bartending/Content/Source/Planning/recipes.csv`
- 배합: `Assets/_Project/Features/Bartending/Content/Source/Planning/recipe_ingredients.csv`

현재 유효 데이터는 아이템 15종, 레시피 21종, 레시피별 배합 21행이다. 빈 ID·이름 행과 이름에 `임시 비워둠`이 포함된 행은 임포트 대상에서 제외된다.

## 2. 생성 결과

`PlanningCsvAssetImporter`는 같은 ID의 에셋을 새로 중복 생성하지 않고 기존 에셋을 갱신한다. 대상 에셋이 없을 때만 새로 만든다.

| 입력 | 생성·갱신 대상 |
|---|---|
| `items.csv` | `Assets/Resources/Bartending/Items/<item ID>.asset`의 `ItemDef` |
| `items.csv` | `Assets/_Project/Features/Bartending/Content/Generated/LiquorBottles/Planning/<item ID>.asset`의 `LiquorBottleDef` |
| `recipes.csv` + `recipe_ingredients.csv` | `Assets/Resources/Bartending/Recipes/<recipe ID>.asset`의 `CocktailRecipeDef` |

레시피 에셋은 `Recipes` 바로 아래에 둔다. 평가 결과별 하위 폴더나 파생 레시피 에셋은 만들지 않는다.

## 3. 실행 메뉴

- 파일을 직접 선택해 임포트: `Slainte > 데이터 > 기획 CSV 임포트`
- 저장소의 위 세 기준 CSV를 즉시 임포트: `Slainte > 데이터 > 기준 CSV 바로 임포트`
- 현재 생성 에셋 검증: `Slainte > 품질 검증 > 기획 CSV 에셋 검증`

임포터는 UTF-8 BOM, 따옴표로 감싼 셀, 셀 안의 쉼표를 처리한다. 임포트 전에 중복 ID, 음수 가격, 잘못된 배합 용량, 없는 아이템 참조를 검사하며 치명적인 오류가 있으면 생성을 중단한다.

## 4. `items.csv`

현재 파일의 핵심 열은 다음과 같다.

| 열 | 의미 | 예시 |
|---|---|---|
| `qt` | 아이템 고유 ID | `item_1001` |
| `Name` | 한국어 표시 이름 | `열대 주스` |
| `Name_eng` | 영어 표시 이름 | `Tropical Juice` |
| `맛 Flavor` | 맛 태그 | `새콤달콤함_Sweet and Sour` |
| `대분류`, `소분류` | 재료 분류 | `논알콜_Non-Alchole`, `과일 주스_Fruit Juice` |
| `ABV` | 알코올 도수 | `0`, `40` |
| `Size(ml)` | 병 용량 | `1000` |
| `가격` | 일반 가격 | `500` |
| `가격_이상한 상점` | 특수 상점 가격 | `5` |
| `기본 소지 개수` | 시작 재고 | `3` |
| `IconName` | 세 병 스프라이트의 공통 기본 이름 | `icon_tropicalJuice` |
| `RGBA` | 액체 표시 색 | `F26A0FFF` |

ID는 한 번 정하면 이름이 바뀌어도 유지한다. 가격·용량·재고는 숫자로 작성하고 음수 값을 넣지 않는다. 현재 활성 15행은 `IconName`과 `RGBA`가 모두 입력되어 있다.

`RGBA`는 `#RRGGBB`, `#RRGGBBAA`, `R,G,B,A` 형식을 지원한다. 비어 있으면 기존 에셋의 색을 유지하며, 새 에셋은 흰색으로 시작하고 경고를 남긴다.

`IconName` 하나로 다음 세 스프라이트를 찾는다.

- `ItemDef.icon`: `IconName`
- `LiquorBottleDef.shelfSprite`: `IconName + "_lid"`
- `LiquorBottleDef.shopSprite`: `IconName + "_blank"`

정확한 이름의 스프라이트를 찾지 못하면 같은 표시 이름을 가진 기존 에셋의 참조를 재사용한다. 새 아이콘을 추가한 뒤에는 세 Sprite 이름과 실제 연결을 검증한다.

## 5. `recipes.csv`

`recipes.csv`는 레시피의 이름, 태그, 제작 방식, 목표 도수, 얼음 사용, 잔, 가격 같은 메타데이터를 관리한다. 정확한 재료 ID와 용량은 이 파일의 설명 문장이 아니라 `recipe_ingredients.csv`를 기준으로 한다.

| 핵심 열 | 의미 |
|---|---|
| `ID` | 레시피 고유 ID. 예: `rec_1001` |
| `Name`, `Name_eng` | 한국어·영어 표시 이름 |
| `재료 속성 1~3` | 재료 속성 태그 |
| `분위기 Mood 1~2` | 분위기 태그 |
| `맛 Flaver 1~3` | 맛 태그. 현재 원본의 열 이름 철자를 그대로 사용한다. |
| `제작 방식 Skill` | 셰이킹, 스터 등 목표 기법 |
| `ABV` | 기획상 목표 도수 |
| `얼음 유무 Ice` | 얼음 사용 여부 |
| `잔 Glass` | 목표 잔 종류 |
| `가격`, `가격_이상한 상점` | 일반·특수 가격 |

배합이 없는 레시피는 에셋 자체는 생성되지만 `isOrderable = false`로 설정되어 주문 후보에서 제외된다. 현재 21종은 모두 배합이 있어 주문 가능하다.

## 6. `recipe_ingredients.csv`

현재 기준 파일은 레시피 하나를 한 행에 적는 wide 형식이다.

```csv
ID,Name,Name_eng,재료,재료1,재료1 용량,재료2,재료2 용량,재료3,재료3 용량,재료4,재료4 용량,얼음 개수
rec_1001,번햄 사워,Burnham Sour,"실청 15ml, 슬롭 30ml, 합성 레몬 15ml, 번햄 버번 위스키 30ml",item_1002 실청,15,item_1004 슬롭,30,item_1003 합성 레몬,15,item_1011 번햄 버번,30,3
```

- `재료1`~`재료4`에는 `item_####` ID를 맨 앞에 쓴다. 뒤의 표시 이름은 사람이 읽기 위한 보조 정보다.
- 각 `재료N 용량`은 0보다 큰 ml 값이어야 한다.
- `얼음 개수`는 0 이상의 정수로 쓰며 `X`도 0으로 처리된다.
- `재료` 설명 문장은 참고용이다. 런타임 데이터는 구조화된 `재료N`과 `재료N 용량` 열로 만든다.

임포터는 필요할 경우 레시피 한 재료를 한 행에 적는 정규형도 지원한다.

```csv
recipeId,itemId,targetMl,toleranceMl,iceCount
rec_1001,item_1002,15,10,3
```

정규형에서는 같은 레시피 ID가 재료 수만큼 반복될 수 있다. 다만 `iceCount`를 여러 행에 반복하면 중복 얼음 행 오류가 날 수 있으므로 레시피당 한 행에만 기록한다.

## 7. 레시피 판정과 에셋 수

제품 런타임은 `Resources/Bartending/Recipes`의 기본 `CocktailRecipeDef`만 읽는다.

- 주문 레시피를 정확히 제조: `Good`
- 배합·기법은 맞고 잔만 다름: `MidGlass`
- 배합·기법은 맞고 얼음만 다름: `MidIce`
- 잔과 얼음이 모두 다름: `MidIceGlass`
- 주문과 다른 기본 레시피를 정확히 제조: `MidWrongMenu`
- 위 조건에 해당하지 않음: `Bad`

따라서 Mid 또는 WrongMenu용 레시피 복사본은 필요하지 않다. 현재 `Recipes`에는 기본 레시피 21개와 태그 색상용 `TasteMoodPalette.asset`만 있다.

## 8. 제출 전 확인

- [ ] 기존 `qt`/`ID`를 변경하거나 재사용하지 않았는가?
- [ ] 활성 행의 `Name`과 ID가 비어 있지 않은가?
- [ ] 아이템·레시피 ID가 각 파일 안에서 중복되지 않는가?
- [ ] 가격, 용량, 재고, 얼음 개수가 허용 범위인가?
- [ ] 모든 배합의 `item_####`가 활성 아이템 또는 기존 `ItemDef`를 가리키는가?
- [ ] 모든 주문 대상 레시피에 하나 이상의 배합이 있는가?
- [ ] 임포트 후 `기획 CSV 에셋 검증`이 통과하는가?

## 9. Legacy `ItemData.csv`

`Assets/_Project/Features/Bartending/Content/Source/Legacy/ItemData.csv`와 `Tools > Import Item Data (CSV)`는 이전 상점·휴식 화면 호환 경로다. 현재 바텐딩 재료·술장·레시피의 기준 원본이 아니므로 새 데이터를 이 파일에 추가하지 않는다. 제거 여부는 `ItemData` 참조를 별도로 조사한 뒤 결정한다.
