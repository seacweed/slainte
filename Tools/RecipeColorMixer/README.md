# Recipe Color Mixer

재료 색상과 레시피 용량을 불러와 완전히 혼합된 입자 색을 계산하는 독립형 로컬 웹 프로그램입니다.

## 실행

`RecipeColorMixer.html`을 브라우저에서 열면 됩니다. 이 파일 하나에 화면, 계산 코드, 레시피 데이터가 모두 포함되어 있으므로 Unity, 별도 설치, 빌드, 서버가 필요하지 않습니다. 파일을 Unity 프로젝트 밖으로 복사해도 그대로 작동합니다.

개발용으로는 `index.html`을 열어도 동일하게 작동합니다.

## 색상 입력

재료별 HEX는 알파 채널이 포함된 `#RRGGBBAA` 형식입니다. 예를 들어 `#A679BB71`은 RGB `#A679BB`, 알파 약 44%를 뜻합니다. 색 선택기, HEX, RGB, 알파 입력은 서로 실시간으로 동기화됩니다.

## Unity 데이터 갱신 상태

독립 실행 파일에는 기존에 생성된 데이터 스냅샷이 포함되어 있어 그대로 열 수 있습니다. 다만 현재 `generate-data.ps1`은 구조 개편 전 경로(`Assets/Resources/Items`, `Assets/Resources/Recipes`, `Assets/StreamingAssets/Data`)를 읽기 때문에, 아래 명령으로는 현재 Unity 데이터가 갱신되지 않습니다.

```powershell
powershell -ExecutionPolicy Bypass -File .\Tools\RecipeColorMixer\generate-data.ps1
```

현재 기준 데이터 위치는 다음과 같습니다.

- `Assets/Resources/Bartending/Items/*.asset`의 `ItemDef` 15개
- `Assets/Resources/Bartending/Recipes/*.asset`의 주문 가능한 `CocktailRecipeDef` 21개
- `Assets/_Project/Features/Bartending/Content/Source/Planning/recipes.csv`
- `Assets/_Project/Features/Bartending/Content/Source/Planning/recipe_ingredients.csv`

자동 갱신을 다시 사용하려면 생성 스크립트의 경로와 wide 배합 CSV 파서를 먼저 현재 형식에 맞춰야 합니다. 그 전까지 위 명령으로 생성물을 덮어쓰지 않습니다. 화면에서 불러온 스냅샷의 색 선택기, HEX, RGB, 알파, 용량은 자유롭게 수정할 수 있습니다.

## 계산 방식

게임의 `LiquidPayload.EvaluateColor()`와 동일하게 각 RGBA 채널을 ml 기준으로 가중평균합니다. 0ml 이하 재료는 계산에서 제외합니다.

## 계산 테스트

Node.js가 설치된 환경에서 다음 명령을 실행합니다.

```powershell
node .\Tools\RecipeColorMixer\test-color-math.cjs
```
