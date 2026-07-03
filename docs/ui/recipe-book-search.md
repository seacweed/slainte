# 도감 검색 (Recipe Book Search)

`RecipeBookPanel` 내부에 레시피 검색 UI의 틀만 구현한 상태입니다. 실제 칵테일 데이터/필터링 로직은 아직 없습니다.

## 클래스

- `RecipeSearchUI` (`Assets/Scripts/RecipeBook/RecipeSearchUI.cs`) — 검색 화면 상태 전환 관리
- `RecipeSearchOptionButton` (`Assets/Scripts/RecipeBook/RecipeSearchOptionButton.cs`) — 카테고리뷰의 개별 옵션 버튼

## 화면 흐름 (3단 드릴다운)

`MainView` → `CategoryView` → `ResultsView`

| 뷰 | 내용 | 뒤로가기 |
|---|---|---|
| MainView | 검색창 + '분위기로 찾기'/'맛으로 찾기'/'재료로 찾기' 버튼 3개 (각 버튼·헤더는 MainView 자체에 포함, 뒤로가기 버튼 없음) | — |
| CategoryView | 선택한 카테고리 제목 + 아이콘(`headerIcon`)이 자체 헤더로 표시 + 옵션 버튼 목록(세로 스크롤) | MainView로 |
| ResultsView | 클릭한 옵션 라벨이 헤더로 올라감(배경·글씨 색을 옵션 버튼과 동일하게 유지) + 칵테일 목록(미구현, 프리팹만 배치) | 직전 `CategoryView`로 (MainView로 가지 않음 — `_currentCategory`에 저장된 카테고리 기억) |

## 데이터 구조

- `RecipeSearchCategory` enum: `Mood` / `Taste` / `Ingredient`
- `CategoryConfig`(private nested): `category`, `title`, `headerIcon`(CategoryView 헤더에 표시될 스프라이트, MainView 버튼 아이콘과는 별도 에셋), `List<OptionEntry> options`
- `OptionEntry`(private nested): `label`, `color`(배경색), `textColor`(글씨색)

모두 `RecipeSearchUI` Inspector의 `categoryConfigs` 배열에서 입력하는 placeholder 데이터이며, 실제 레시피 데이터베이스와는 연결되어 있지 않습니다.

## 옵션 버튼 → 결과 헤더 색 전달

옵션 버튼 클릭 시 `NotifyOptionClicked(label, color, textColor)`로 색 정보까지 같이 전달되어, `ResultsView` 헤더(`resultsTitleText` + `resultsTitleBackground`)가 클릭한 옵션 버튼과 동일한 배경·글씨 색으로 표시됩니다. 클릭이 곧 화면 전환(드릴다운)이므로 선택 상태를 토글하는 하이라이트 색(selectedColor)은 사용하지 않습니다.

## RecipeBookUI 연동

`RecipeBookUI.SetInteractable(bool)`이 **disabled → enabled로 전환될 때만**(예: `EpisodeMode` 진입 후 `OrderMode`/`CraftingMode`로 복귀) `RecipeSearchUI.ResetToMain()`을 호출해 검색 화면을 초기 상태로 되돌립니다. Tab 키로 단순히 열고 닫는 동작(`Toggle()`)은 리셋되지 않습니다.

## 추후 작업

- 실제 칵테일 데이터 모델 및 `ResultsView` 목록 동적 생성
- 검색창 텍스트 기반 필터링
- 옵션 선택 방식 확장(다중 선택, AND/OR 조합 등) 필요 시 검토
