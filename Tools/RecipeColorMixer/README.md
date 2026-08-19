# Recipe Color Mixer

재료 색상과 레시피 용량을 불러와 완전히 혼합된 입자 색을 계산하는 독립형 로컬 웹 프로그램입니다.

## 실행

`RecipeColorMixer.html`을 브라우저에서 열면 됩니다. 이 파일 하나에 화면, 계산 코드, 레시피 데이터가 모두 포함되어 있으므로 Unity, 별도 설치, 빌드, 서버가 필요하지 않습니다. 파일을 Unity 프로젝트 밖으로 복사해도 그대로 작동합니다.

개발용으로는 `index.html`을 열어도 동일하게 작동합니다.

## 색상 입력

재료별 HEX는 알파 채널이 포함된 `#RRGGBBAA` 형식입니다. 예를 들어 `#A679BB71`은 RGB `#A679BB`, 알파 약 44%를 뜻합니다. 색 선택기, HEX, RGB, 알파 입력은 서로 실시간으로 동기화됩니다.

## Unity 데이터 갱신

저장소 루트에서 다음 명령을 실행합니다.

```powershell
powershell -ExecutionPolicy Bypass -File .\Tools\RecipeColorMixer\generate-data.ps1
```

이 과정은 프로그램 실행에 필요하지 않은 선택 기능입니다. 스크립트가 다음 데이터를 읽어 `recipe-data.js`와 독립 실행 파일 `RecipeColorMixer.html`을 다시 생성합니다.

- `Assets/Resources/Items/**/*.asset`의 `ItemDef`
- `Assets/Resources/Recipes/**/*.asset`의 주문 가능한 `CocktailRecipeDef`
- `Assets/StreamingAssets/Data/recipes.csv`
- `Assets/StreamingAssets/Data/recipe_ingredients.csv`

레시피를 불러온 후에도 색 선택기, HEX, RGB, 알파, 용량을 자유롭게 수정할 수 있습니다.

## 계산 방식

게임의 `LiquidPayload.EvaluateColor()`와 동일하게 각 RGBA 채널을 ml 기준으로 가중평균합니다. 0ml 이하 재료는 계산에서 제외합니다.

## 계산 테스트

Node.js가 설치된 환경에서 다음 명령을 실행합니다.

```powershell
node .\Tools\RecipeColorMixer\test-color-math.cjs
```
