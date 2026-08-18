# 레시피/재료 CSV 분석 메모

## 저장소 밖 원본 파일

다음 파일은 현재 PC의 `C:/Users/boguk/Downloads`에만 있다. 다른 기기로 별도 복사해야 한다.

- `Data_slainte.csv - 레시피.csv`
- `Data_slainte.csv - 레시피_재료.csv`
- `Data_slainte.csv - 재료.csv`

이 세 파일은 아직 저장소 에셋으로 완전히 재import하지 않았다.

## 구조 확인

- 레시피 CSV: 99행 중 실제 데이터 21행, 빈 placeholder 78행
- 레시피_재료 CSV: 레시피 21행, wide 형식의 재료 항목을 정규화하면 45개
- 재료 CSV: 99행 중 실제 재료 15행, 빈 행 84개
- 재료 CSV 첫 열 이름은 `qt`이며 기존 item importer가 이 헤더를 지원한다.
- 레시피가 참조하는 `item_1001` ~ `item_1015`는 모두 존재한다.
- 중복 item ID는 없고 15개 RGBA는 모두 유효한 8자리 HEX다.

## 현재 재료 RGBA

| ID | 이름 | HEX | Alpha |
|---|---|---:|---:|
| item_1001 | Tropical Juice | `#F26A0FFF` | 1.000 |
| item_1002 | Siltrop | `#FFF0D4B3` | 0.702 |
| item_1003 | Synthetic Lemon | `#EBFF00B3` | 0.702 |
| item_1004 | Slop | `#CCBFB7E6` | 0.902 |
| item_1005 | Nanangna | `#FFA400CC` | 0.800 |
| item_1006 | Cotton | `#E35700CC` | 0.800 |
| item_1007 | Hectar | `#2F050ECC` | 0.800 |
| item_1008 | Bless | `#FF000CCC` | 0.800 |
| item_1009 | Breeze Vodka | `#FFFFFF33` | 0.200 |
| item_1010 | Johnny Dogs | `#A95107CC` | 0.800 |
| item_1011 | Burnham Bourbon | `#F85405CC` | 0.800 |
| item_1012 | Beatha | `#FF8F26CC` | 0.800 |
| item_1013 | Minute Fizz | `#D2FFEE66` | 0.400 |
| item_1014 | Hot Water | `#FFFFFF33` | 0.200 |
| item_1015 | Coffee Powder | `#382014FF` | 1.000 |

현재 원본 데이터에는 Alpha 0 재료가 없고 최소값은 0.2다.

## 설명과 구조화 수치 불일치

구조화된 재료량 열을 기준값으로 사용하는 것이 권장된다.

- `rec_1008`: 설명 Siltrop 5ml, 구조화 열 10ml
- `rec_1010`: 설명 Hot Water 200ml, 구조화 열 150ml
- `rec_1011`: 설명 Lemon 15ml/Fizz 60ml, 구조화 열 30ml/100ml; 설명에 `15m` 오타
- `rec_1012`: 설명 Fizz 60ml, 구조화 열 100ml
- `rec_1013`: 설명 whiskey 45ml/Fizz 60ml, 구조화 열 40ml/150ml

구조화 양으로 계산한 ABV가 CSV 목표에 거의 정확히 맞는 사례:

- `rec_1008`: 7.62 → CSV 7.6
- `rec_1012`: 7.5 → CSV 7.5
- `rec_1013`: 8.42 → CSV 8.4

ABV가 뒤바뀐 것으로 보이는 항목:

- `rec_1009 Black Coffee`: CSV 7.8, 구조화 재료 기준 0
- `rec_1010 Hot Teddy`: CSV 0, 구조화 재료 기준 약 7.83

권장 정리:

- 구조화 volume 열을 source of truth로 사용
- 설명문을 구조화 volume에서 다시 생성
- `rec_1009` ABV를 0, `rec_1010` ABV를 7.8로 검토

## 기존 importer와 현재 CSV의 차이

- `Assets/Editor/PlanningCsvAssetImporter.cs`의 기본 item 파일명은 `Data_slainte.csv - 아이템.csv`다.
- 실제 전달 파일명은 `Data_slainte.csv - 재료.csv`다.
- item importer는 `qt`와 RGBA를 지원한다.
- recipe ingredient reader는 `recipeId,itemId,targetMl,toleranceMl` 정규형만 지원한다.
- 현재 전달된 `레시피_재료.csv`의 wide 형식은 직접 지원하지 않는다.
- 기존 `Assets/Editor/Data/slainte_recipe_ingredients.csv`는 27행/15레시피이며 새 데이터와 다른 오래된 item mapping이 있다.
- 현재 `Assets/Resources/Items/Planning`의 15개 에셋은 색이 흰색이며 새 원본 이름/색과 불일치하는 항목이 있다.
- 오래된 `item_1023` ~ `item_1025` Planning 에셋도 남아 있다.
- `ValidateImportedAssets`와 variant mapping에 과거 hardcoded item ID가 있다.

다음 구현 순서:

1. 구조화 volume 열을 기준으로 사용할지 최종 승인.
2. importer가 `재료.csv` 파일명과 wide ingredient 형식을 직접 읽거나 45행 정규형으로 자동 변환하도록 수정.
3. hardcoded validator/variant mapping 갱신.
4. ItemDef 15개와 RecipeDef 21개 재import.
5. 생성 폴더 안의 obsolete Planning 에셋만 확인 후 정리.
6. variant와 정확한 최종색 재검증.

## 현재 혼합식으로 계산한 임시 레시피 색

아래 값은 전달 CSV의 구조화 volume과 현재 `LiquidPayload.EvaluateColor()`를 사용한 사전 계산이다. 실제 에셋 import가 끝나기 전까지 확정 런타임 결과로 보지 말 것.

| Recipe | 예상 HEX |
|---|---:|
| rec_1001 | `#E8AE62CC` |
| rec_1002 | `#DE8D34D0` |
| rec_1003 | `#F1AB8080` |
| rec_1004 | `#C65404CC` |
| rec_1005 | `#B71E0ACC` |
| rec_1006 | `#F5790BF2` |
| rec_1007 | `#F38D26EC` |
| rec_1008 | `#F6DEC860` |
| rec_1009 | `#F3F1F040` |
| rec_1010 | `#FDDFC364` |
| rec_1011 | `#DCFDBA7E` |
| rec_1012 | `#BCA09A8C` |
| rec_1013 | `#C9DABD7B` |
| rec_1014 / rec_1015 | `#A95107CC` |
| rec_1016 / rec_1017 | `#F85405CC` |
| rec_1018 / rec_1019 | `#FF8F26CC` |
| rec_1020 / rec_1021 | `#FFFFFF33` |

주의: 현재 논리 혼합은 RGB를 volume으로 평균하고 Alpha도 별도로 volume 평균한다. Alpha가 낮은 흰색 물/보드카도 RGB 평균에서는 큰 비중을 차지하므로 Coffee/Hot 계열이 매우 연하게 계산될 수 있다. 혼합 공식을 임의로 바꾸지 말고, 시각 결과가 문제라면 먼저 원본 RGBA를 검토할 것.

