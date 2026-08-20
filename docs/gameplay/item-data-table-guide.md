# 아이템 데이터 테이블 작성 가이드

## 바텐딩 기획 CSV 임포터

현재 바텐딩 제작 데이터는 다음 두 기획 CSV와 배합 CSV를 함께 사용한다.

- 아이템 CSV: `Data_slainte.csv - 아이템.csv`
- 레시피 CSV: `Data_slainte.csv - 레시피.csv`
- 배합 CSV: `Assets/Editor/Data/slainte_recipe_ingredients.csv`
- 임포트 메뉴: `Slainte > 데이터 > 기획 CSV 임포트`
- 다운로드 폴더 기본 파일 즉시 반영: `Slainte > 데이터 > 다운로드 폴더 CSV 바로 임포트`
- 검증 메뉴: `Slainte > 품질 검증 > 기획 CSV 에셋 검증`

임포트 결과는 다음 위치에 ID 기준으로 저장된다.

- `ItemDef`: `Assets/Resources/Items/Planning/`
- `LiquorBottleDef`: `Assets/Data/LiquorBottle/Planning/`
- 기본 `CocktailRecipeDef`: `Assets/Resources/Recipes/Planning/`
- 숨은 Mid 판정 레시피: `Assets/Resources/Recipes/Planning/Variants/`

같은 ID를 다시 임포트하면 기존 에셋을 갱신한다. 빈 행과 이름이 `임시 비워둠`인 행은 건너뛴다. 일반적인 CSV 따옴표, 쉼표가 들어간 셀, UTF-8 BOM을 지원한다.

레시피 CSV에는 정확한 재료와 용량이 없으므로 배합 CSV가 반드시 필요하다. 배합이 없는 레시피도 에셋은 생성하지만 무작위·지정 주문에는 사용할 수 없다. PDF 기준으로 배합이 확정된 기본 레시피 15종은 주문 가능하며, 변형 87종은 레시피북과 주문 후보에서는 숨기고 Mid 판정에만 사용한다.

`RGBA`는 `#RRGGBB`, `#RRGGBBAA`, `R,G,B,A` 형식을 지원한다. 값이 비어 있으면 기존 에셋의 색을 유지하고 새 에셋은 흰색을 사용하며, 임포트 결과에 누락 경고가 표시된다.

`IconName`은 재료별 3종 스프라이트의 공통 기본 이름이다(예: `beatha` → `beatha.png`/`beatha_lid.png`/`beatha_blank.png`).

- `ItemDef.icon`(바테이블 표시용): `IconName`과 이름이 같은 Sprite
- `LiquorBottleDef.shelfSprite`(술장 표시용): `IconName + "_lid"` Sprite
- `LiquorBottleDef.shopSprite`(상점 표시용): `IconName + "_blank"` Sprite

정확한 이름의 스프라이트가 없으면 각각 같은 표시 이름(`displayName`)의 기존 에셋에서 해당 필드를 재사용한다.

---

## 기존 상점 ItemData CSV

이 문서는 기획자가 상점 아이템 데이터를 작성할 때 사용하는 `ItemData.csv`의 컬럼과 작성 규칙을 정의한다.

## 파일 위치

- 원본 CSV: `Assets/Resources/Data/ItemData.csv`
- 생성 결과: `Assets/Resources/Items/Item_{ID}_{Name}.asset`
- 임포트 메뉴: `Tools > Import Item Data (CSV)`

## 테이블 정의

| 컬럼 | 자료형 | 필수 | 설명 | 예시 |
|---|---|---:|---|---|
| `ID` | 정수 | O | 아이템 고유 번호. 한번 배정한 번호는 변경하거나 다른 아이템에 재사용하지 않는다. | `1001` |
| `Name` | 문자열 | O | 게임 화면에 표시할 아이템 이름이다. | `보드카` |
| `Category` | 열거형 | O | 아이템 분류다. 아래의 지정값 중 하나만 사용한다. | `Alcohol` |
| `Price` | 정수 | O | 상점 구매 가격이다. 0 이상의 정수만 입력한다. | `3000` |
| `Desc` | 문자열 | O | 상점에서 표시할 짧은 아이템 설명이다. | `러시아산 독한 술` |
| `IconName` | 문자열 | O | Unity 프로젝트에 등록된 Sprite 이름이다. 파일 확장자는 입력하지 않는다. | `Icon_Vodka` |

## Category 지정값

| 값 | 의미 | 아이템 예시 |
|---|---|---|
| `Alcohol` | 일반 주류 | 보드카, 진, 럼, 위스키 |
| `Liqueur` | 리큐르 | 블루 큐라소, 깔루아 |
| `NonAlcohol` | 무알코올 재료 | 주스, 우유, 탄산수 |
| `Powder` | 가루 및 분말 재료 | 설탕, 소금, 시나몬 가루 |
| `Tool` | 바텐딩 도구 | 셰이커, 지거, 바 스푼 |
| `Glass` | 칵테일 잔 | 마티니 잔, 온더락 잔 |

기존 데이터의 `Non Alcohol`도 임포터에서 인식하지만, 새 데이터는 표기 통일을 위해 `NonAlcohol`을 사용한다.

## 작성 예시

```csv
ID,Name,Category,Price,Desc,IconName
1001,보드카,Alcohol,3000,러시아산 독한 술,Icon_Vodka
1002,쉐이커,Tool,15000,칵테일을 흔드는 도구,Icon_Shaker
1007,크랜베리 주스,NonAlcohol,1500,새콤달콤한 주스,Icon_Cranberry
1009,튤립 잔,Glass,3000,우아한 곡선의 잔,Icon_TulipGlass
```

## 작성 규칙

### ID

- 모든 행에서 유일해야 한다.
- 삭제된 아이템의 ID도 다른 아이템에 재사용하지 않는다.
- 아이템 이름이나 카테고리가 바뀌어도 기존 ID는 유지한다.
- 숫자 앞에 `ITEM_` 등의 접두사를 붙이지 않는다.

### Name

- 게임 화면에 그대로 표시되므로 최종 표기명을 작성한다.
- 앞뒤 공백을 넣지 않는다.
- 같은 이름의 아이템이 필요하다면 기획 단계에서 구분 가능한 이름을 정한다.

### Category

- 대소문자를 포함해 이 문서의 지정값을 사용한다.
- 하나의 아이템에는 하나의 카테고리만 지정한다.
- 새로운 카테고리가 필요하면 임의로 추가하지 말고 개발 담당자와 먼저 협의한다.

### Price

- 쉼표와 화폐 단위를 제외한 정수만 작성한다.
- 올바른 예: `3000`
- 잘못된 예: `3,000`, `3000G`, `3천`

### Desc

- 아이템의 특징을 한 문장 이내로 작성한다.
- 현재 임포터는 일반적인 CSV 따옴표 처리를 지원하지 않으므로 쉼표와 줄바꿈을 사용하지 않는다.

### IconName

- 실제 Unity Sprite 이름과 동일하게 작성한다.
- `.png`, `.psd` 등의 파일 확장자는 제외한다.
- 동일한 이름의 Sprite가 여러 개 생기지 않도록 한다.
- 아이콘이 아직 준비되지 않았다면 임의의 이름을 만들지 말고 개발 담당자와 사용할 이름을 먼저 확정한다.

## 제출 전 확인 목록

- [ ] 헤더 이름과 순서를 변경하지 않았는가?
- [ ] 모든 ID가 숫자이며 중복되지 않는가?
- [ ] 기존 아이템의 ID를 변경하지 않았는가?
- [ ] Category가 지정값 중 하나인가?
- [ ] Price에 쉼표나 화폐 단위가 없는가?
- [ ] Name과 Desc에 쉼표 또는 줄바꿈이 없는가?
- [ ] IconName에 파일 확장자가 없는가?
- [ ] 빈 행이나 쉼표만 있는 행이 없는가?

## 현재 임포터 주의사항

- 현재 임포터는 기존 아이템 에셋을 갱신하지 않고 새 에셋을 생성한다.
- 같은 `ID`와 `Name`으로 이미 생성된 에셋이 있는 상태에서 다시 임포트하면 수정 사항이 정상 반영되지 않을 수 있다.
- ID 중복, 필수값 누락, 잘못된 가격과 존재하지 않는 아이콘을 자동으로 차단하지 않는다.
- 기획자는 CSV와 이 문서의 체크리스트를 기준으로 데이터를 검수해 전달하고, 기존 데이터 갱신 임포트는 개발 담당자가 확인한 뒤 진행한다.

## 현재 적용 범위

이 테이블은 휴식 화면의 상점 아이템 목록에 사용하는 데이터다. 칵테일 제조에 필요한 도수, 액체 색상, 용량, 밀도와 레시피 구성 정보는 이 테이블에서 작성하지 않는다.
