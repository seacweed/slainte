# 도감 검색 (Recipe Book Search)

`RecipeBookPanel` 내부에서 `Assets/Resources/Recipes/Planning/*.asset`의 21개 레시피(`appearsInRecipeBook = true`)를 맛/분위기 태그 또는 이름으로 찾아 상세 정보를 보여주는 검색 UI입니다. "재료로 찾기"는 추후 재도입 가능성이 있어 데이터 필드(`CocktailRecipeDef.ingredientPropertyTags`)는 남겨두었지만, 현재 UI에는 없습니다.

## 클래스

- `RecipeSearchUI` (`Assets/_Project/Features/Bartending/Runtime/RecipeBook/RecipeSearchUI.cs`) — 검색 화면 상태 전환·데이터 로딩·필터링 관리
- `RecipeSearchOptionButton` (`Assets/_Project/Features/Bartending/Runtime/RecipeBook/RecipeSearchOptionButton.cs`) — 맛/분위기 태그 하나를 표시하는 공용 버튼(클릭 가능/불가능 모두 지원)
- `RecipeListItemUI` (`Assets/_Project/Features/Bartending/Runtime/RecipeBook/RecipeListItemUI.cs`) — 레시피 리스트 한 줄(아이콘+이름)
- `RecipeDetailUI` (`Assets/_Project/Features/Bartending/Runtime/RecipeBook/RecipeDetailUI.cs`) — 레시피 상세 화면
- `RecipeIngredientRowUI` (`Assets/_Project/Features/Bartending/Runtime/RecipeBook/RecipeIngredientRowUI.cs`) — 상세 화면의 재료 한 줄(재료명+양)
- `TasteMoodTagPaletteDef` (`Assets/_Project/Features/Bartending/Runtime/RecipeBook/TasteMoodTagPaletteDef.cs`) — 맛 6종/분위기 6종 태그별 배경색·글자색 ScriptableObject

## 화면 흐름

```
MainView (검색창 + 검색버튼 + [맛으로 찾기]/[분위기로 찾기])
 ├─ 기본 상태: 카테고리 버튼 2개 표시
 ├─ 검색 활성 상태(검색어 입력 후 Enter/검색버튼): 카테고리 버튼 숨김, 검색창 아래 레시피 리스트 표시
 │      └─ 항목 클릭 → DetailView (뒤로가기 시 검색어/결과가 유지된 이 상태로 복귀)
 │      └─ 검색어를 비우고 Enter → 기본 상태로 복귀
 └─ [맛으로 찾기]/[분위기로 찾기] 클릭 → CategoryView
CategoryView — 뒤로가기 + 카테고리 제목/아이콘 + 태그 버튼 6개(세로 목록)
 └─ 태그 클릭 → ResultsView
ResultsView — 뒤로가기 + 제목 자리에 클릭한 태그 버튼(비활성) + 레시피 리스트
 ├─ 항목 클릭 → DetailView (뒤로가기 시 이 ResultsView로 복귀, 리스트 재사용)
 └─ 뒤로가기 → CategoryView
DetailView — 뒤로가기만 존재(제목 없음)
 └─ 왼쪽: 레시피 아이콘
 └─ 오른쪽 상단: 이름 + 도수
 └─ 오른쪽 하단: 재료명+양 목록 | 맛 태그 최대 3개(비활성 버튼) | 분위기 태그 최대 2개(비활성 버튼)
 └─ 하단: 설명
```

뒤로가기는 진입 경로를 기억한다(`RecipeSearchUI`의 `DetailOrigin`): 태그로 들어간 상세는 ResultsView로, 검색으로 들어간 상세는 검색 상태가 유지된 MainView로 복귀한다.

## 데이터 연동

- `RecipeSearchUI.Awake()`에서 `ItemDefCatalog.LoadFromResources("Items")` + `CocktailRecipeDataLoader.LoadDefault(itemCatalog)`로 카탈로그를 로드(`BusinessOrderSessionController`와 동일한 패턴)한 뒤 `appearsInRecipeBook == true`인 레시피만 캐시. 잔·얼음 변형(`Variants/` 하위 73개)은 전부 `appearsInRecipeBook = false`라 자동 제외됨.
- 맛/분위기 필터링은 `CocktailRecipe.tasteTags`/`moodTags`(대소문자 무시 `HashSet<string>`)에 태그 포함 여부로 판정.
- 이름 검색은 `displayName` 부분 일치(대소문자 무시)만 지원.
- `CocktailRecipeDef`에는 `icon`(Sprite)과 `description`(TextArea) 필드가 있으며 `ToRuntime()`을 통해 `CocktailRecipe.icon`/`description`으로 그대로 전달됨. `description`은 기획 CSV(`Assets/Editor/Data/Planning/recipes.csv`)에 "설명" 컬럼을 추가하면 `PlanningCsvAssetImporter`가 자동으로 채움. `icon`은 Sprite 참조라 CSV로 채울 수 없어 Inspector에서 직접 할당해야 함.

## 태그 색상: `TasteMoodTagPaletteDef`

맛 6종(씁쓸함/달콤함/새큼함/달큰함/새콤달콤함/무맛), 분위기 6종(포근한/고급스러운/정열적인/청량한/깔끔한/화려한)의 배경색·글자색을 하나의 SO 에셋에서 관리한다. `RecipeSearchUI`(CategoryView 옵션 버튼, ResultsView 제목)와 `RecipeDetailUI`(태그 칩)가 동일 에셋을 참조하므로 색상 정의가 한 곳으로 통일된다. (과거에는 씬에 직접 하드코딩되어 있었고, 분위기 태그 중 하나가 실제 레시피 데이터에 없는 "시원한"으로 잘못 입력되어 있었음 — 재설계하며 실제 데이터 기준 "정열적인"으로 바로잡음)

## 공용 버튼: `RecipeSearchOptionButton`

`Setup(string tag, Color backgroundColor, Color textColor, bool interactable, Action<string> onSelected = null, float? fontSize = null, float? preferredHeight = null)` 하나로 세 용도를 모두 처리한다:

1. CategoryView 옵션 목록 — `interactable: true`, 프리팹 기본 크기·글씨 그대로
2. ResultsView 제목 자리 — `interactable: false`, 태그 클릭마다 `optionsContent`가 아니라 `resultsHeaderContent`에 **동적으로 `Instantiate`**(고정 인스턴스 아님 — 처음엔 이렇게 설계했다가 "생성 로직 자체가 없어서 안 나옴" 버그가 나서 CategoryView 옵션·DetailView 칩과 동일한 동적 생성 방식으로 통일함)
3. DetailView 맛/분위기 태그 칩(레시피당 동적 Instantiate) — `interactable: false`, `RecipeDetailUI`의 `chipFontSize`/`chipHeight`(Inspector 설정)를 전달해 CategoryView보다 작게 표시

`fontSize`/`preferredHeight`를 안 넘기면(`null`) 프리팹 자체 값을 그대로 씀 — 위치별로 프리팹을 포크하지 않고 하나의 프리팹을 재사용하면서 크기만 다르게 주는 방식.

**내부적으로 고친 버그 2가지** (`Awake()`/`Setup()`):
- `button.transition = Selectable.Transition.None`을 강제로 꺼야 함 — 안 그러면 `interactable: false`로 설정하는 순간 Unity가 Target Graphic(=`background`)에 `disabledColor`(흐린 회색)를 자동으로 덮어써서, `Setup()`으로 넣은 고유 배경색이 사라짐(CategoryView처럼 `interactable: true`인 경우는 `normalColor`가 흰색·배율 1이라 티가 안 났을 뿐 같은 문제였음).
- `LayoutElement`가 없으면 프리팹 자신의 원래 높이(`RectTransform.sizeDelta.y`)를 `minHeight`/`preferredHeight`로 자동 채워 넣어야 함 — 안 그러면 DetailView처럼 다른 Vertical Layout Group 밑에 놓일 때 높이가 거의 0으로 찌그러짐.
- 이 초기화(`EnsureInitialized()`)는 `Awake()`뿐 아니라 `Setup()` 진입 시에도 호출한다 — 비활성 부모(예: 아직 `SetActive(false)`인 `ResultsView`/`DetailView`) 밑에서 `Instantiate`되면 Unity가 `Awake()` 호출을 활성화될 때까지 미루기 때문에, `Setup()`이 먼저 실행될 수 있음.

## RecipeBookUI 연동

`RecipeBookUI.SetInteractable(bool)`이 **disabled → enabled로 전환될 때만**(예: `EpisodeMode` 진입 후 `OrderMode`/`CraftingMode`로 복귀) `RecipeSearchUI.ResetToMain()`을 호출해 검색 화면을 초기 상태(검색어 비움, 카테고리 버튼 표시)로 되돌린다. A 키로 단순히 열고 닫는 동작(`Toggle()`)은 리셋되지 않는다.

**검색창 포커스 중 게임 키 차단** (`RecipeSearchUI.IsSearchFocused` → `RecipeBookUI.IsSearchFocused` → `InputRouter`): 검색어에 A/D/W/S/Tab/Space가 들어가면(예: "sad", "data") 타이핑 중에 도감 토글·술장 토글·주문서 토글·카메라 이동·대사 진행이 같이 트리거되던 문제가 있어서, `searchInputField.isFocused`(TMP_InputField 표준 프로퍼티)가 true인 동안은 `InputRouter.Update()`가 마우스 클릭을 제외한 모든 키보드 액션을 건너뛴다. 검색창 포커스를 벗어나면 즉시 정상 동작으로 복구된다.

## 재료명 색상 (`RecipeDetailUI`)

`RecipeDetailUI.bottleCatalog`(`LiquorBottleCatalog`, `LiquorShelfUI`가 쓰는 것과 동일한 에셋)를 참조해, 재료 목록의 각 재료명 색을 그 재료가 속한 `LiquorCategoryDef.color`(술장/상점과 같은 카테고리 고유색)로 칠한다. `ItemDef` 참조 우선 매칭, 없으면 `LiquorBottleDef.InventoryId` 문자열로 폴백 매칭. 카탈로그에 없는 재료(병으로 등록 안 된 아이템)는 `RecipeIngredientRowUI` 텍스트의 기본 색 유지.

## 씬 배치 시 주의

- `RecipeSearchUI` 필드: `tagPalette`, `searchInputField`, `searchButton`, `categoryButtonsRoot`, `searchResultsRoot`, `searchResultsContent`, `resultsHeaderContent`(빈 컨테이너 — 태그 클릭마다 여기에 `optionButtonPrefab`을 동적 생성), `resultsContent`, `recipeListItemPrefab`, `detailView`, `recipeDetailUI`, `detailBackButton`.
- `RecipeDetailUI` 필드에 `bottleCatalog`(위 항목), `chipFontSize`/`chipHeight`(태그 칩 크기, Inspector 조절 가능) 추가됨.
- `RecipeSearchOptionButton`은 `SearchOption.prefab` 하나만 그대로 재사용(스크립트 API만 확장됨) — 위치별 프리팹 분리 없음.

## 추후 작업

- "재료로 찾기" 재도입 시 `RecipeSearchCategory`에 `Ingredient` 추가 + `ingredientPropertyTags` 기반 필터 UI 복원
- 검색 다중 조건(이름+태그 조합 등) 필요 시 검토
