# 도감 검색 (Recipe Book Search)

`RecipeBookPanel` 내부에서 `Assets/Resources/Bartending/Recipes/*.asset`의 21개 기본 레시피(`appearsInRecipeBook = true`)를 맛/분위기 태그 또는 이름으로 찾아 상세 정보를 보여주는 검색 UI입니다. "재료로 찾기"는 첫 화면에 자리(비활성 버튼)만 있고 기능은 없습니다. 데이터 필드(`CocktailRecipeDef.ingredientPropertyTags`)는 남겨두었습니다. 같은 문서에 주문서 패널(틀·표정)도 정리합니다.

## 클래스

- `RecipeSearchUI` (`Assets/_Project/Features/Bartending/Runtime/RecipeBook/RecipeSearchUI.cs`) — 검색 화면 상태 전환·데이터 로딩·필터링 관리
- `RecipeSearchOptionButton` (`.../RecipeBook/RecipeSearchOptionButton.cs`) — CategoryView의 태그 버튼 하나(`button-2`)
- `RecipeListItemUI` (`.../RecipeBook/RecipeListItemUI.cs`) — 칵테일 목록 한 칸(`button-1` + 칵테일 이미지 + 이름). 태그 결과·이름 검색 결과 공용
- `RecipeDetailUI` (`.../RecipeBook/RecipeDetailUI.cs`) — 레시피 상세 화면
- `RecipeIngredientRowUI` (`.../RecipeBook/RecipeIngredientRowUI.cs`) — 상세 화면의 재료 한 줄(라벨 배경 + 재료명, 오른쪽 양)
- `RecipeTagChipUI` (`.../RecipeBook/RecipeTagChipUI.cs`) — 상세 화면의 분위기 칩(클릭 불가, `element-BG-yellow`)
- `HoverSwapButton` (`Assets/_Project/Shared/Runtime/UI/HoverSwapButton.cs`) — 공용 호버 연출(아래 참고)
- `TasteMoodTagPaletteDef` (`.../RecipeBook/TasteMoodTagPaletteDef.cs`) — 맛 6종/분위기 6종 태그 목록 SO. 지금 도감 UI는 태그 이름 목록으로만 쓴다(색은 `EpisodeCraftingBridge`의 태그 판별 등 다른 곳에서 계속 사용)

아트: `Assets/_Project/Features/Bartending/Art/Sprites/UI/RecipeBook/`(패널 틀 `레시피_screen`, `button-1/2-off/on`, `button-back`, `button-search(-BG)`, `element-BG-*`, `icon-*`). 주문서는 `Features/Business/Art/Sprites/UI/OrderTicket/`(`주문서_screen`, `order-face-1~4`).

## 화면 흐름

```
MainView — [레시피 찾기] 제목 + 검색창 + button-1 버튼 3개
 ├─ [분위기로 찾기](icon-1-1)/[맛으로 찾기](icon-2-1) → CategoryView
 ├─ [재료로 찾기](icon-3-1) — 시안대로 자리만 표시, 호버·클릭 없음(기능 미구현)
 └─ 검색(Enter/돋보기): 버튼 자리에 칵테일 목록 표시, 항목 클릭 → DetailView(뒤로가기 시 검색 상태 유지)
      └─ 검색어를 비우고 Enter → 기본 상태로 복귀
CategoryView — [<] + 카테고리 이름 + 태그 6개(button-2 세로 목록)
 └─ 태그 클릭 → ResultsView
ResultsView — [<] + 태그 이름 + 칵테일 목록(button-1 칸)
 ├─ 항목 클릭 → DetailView(뒤로가기 시 이 목록으로 복귀)
 └─ 뒤로가기 → CategoryView
DetailView — [<] + 오른쪽 정렬 이름(뒤로가기와 세로 중심 맞춤, 길면 자동 축소), 그 아래 ALC 0.0%
 ├─ 왼쪽: 칵테일 이미지 / 오른쪽: 분위기 칩 최대 2칸(아래 칸부터 채움, 맛은 표시 안 함)
 └─ 아래: 재료 라벨(element-BG-pink)+양 목록, 설명
```

아이콘(`icon-1-1`, `icon-2-1`, `icon-3-1`)은 첫 화면에서만 쓴다. 뒤로가기는 진입 경로를 기억한다(`RecipeSearchUI`의 `DetailOrigin`).

## 호버 버튼: `HoverSwapButton`

첫 화면 버튼, 태그 버튼, 칵테일 목록 칸이 같은 컴포넌트를 쓴다.

- 호버하면 배경이 off → on 스프라이트로 바뀌고, 글자색이 밝은 색(off) → 짙은 갈색(on)으로 바뀌며, `visualRoot`가 오른쪽으로 12px(0.08초) 미끄러진다. 클릭하면 바로 다음 화면으로 넘어간다(클릭으로 on 상태가 되는 방식이 아님).
- 버튼 루트(투명 Image = 히트 영역)는 고정하고 자식 `Visual`만 움직인다. 루트째 움직이면 가장자리에서 포인터가 들락거리며 깜빡인다.
- `SetInteractable(false)`면 호버·클릭에 반응하지 않는다(재료로 찾기).
- Unity Button의 색 전환(`transition`)은 꺼서 스프라이트 색이 틴트되지 않게 한다. 패널이 닫힐 때(`OnDisable`)는 즉시 원위치로 돌린다.

## 데이터 연동

- `RecipeSearchUI.Awake()`에서 `ItemDefCatalog.LoadFromResources(ProjectResourcePaths.BartendingItems)`와 `CocktailRecipeDataLoader.LoadDefault(itemCatalog)`로 기본 레시피 카탈로그를 로드한 뒤 `appearsInRecipeBook == true`인 레시피만 캐시한다.
- 맛/분위기 필터링은 `CocktailRecipe.tasteTags`/`moodTags`(대소문자 무시 `HashSet<string>`)에 태그 포함 여부로 판정.
- 이름 검색은 `displayName` 부분 일치(대소문자 무시)만 지원.
- `CocktailRecipeDef`의 `icon`(Sprite)과 `description`이 목록·상세에 쓰인다. `description`은 기획 CSV(`Assets/_Project/Features/Bartending/Content/Source/Planning/recipes.csv`)의 "설명" 컬럼으로 채우고, `icon`은 Inspector에서 직접 할당한다.

## 상세 화면 세부 (`RecipeDetailUI`)

- 도수 표기는 `ALC {expectedAbvPercent:0.0}%`(한 자리 수는 앞 0 없이 `7.8%`). 칵테일 이미지는 도수 줄과 세로로 살짝 겹치게 올려 도수와 분위기 칸 사이 여백을 줄였다.
- 설명을 뺀 상세 화면 글자(이름·도수·분위기 칩·재료명·양)는 볼드다. 첫 화면(검색 입력칸 글자 제외)과 카테고리 화면의 제목·버튼, 칵테일 목록 칸(`RecipeListItemUI`)도 볼드다.
- 칵테일 이름은 뒤로가기 버튼과 세로 중심이 맞도록 도수 줄과 함께 7px 내려 두었다(한글 글리프 중심이 TMP 중간선보다 살짝 위라서).
- 긴 제목은 TMP Auto Size로 한 줄에 맞춘다: 칵테일 이름 48→최소 34, 카테고리·목록 제목 40→최소 28. 최소 크기로도 넘치면 말줄임(…).
- 분위기 칩은 아래 정렬 세로 레이아웃(`MoodChipRoot`, LowerRight)에 넣고, 새 칩을 맨 앞 형제로 보내 첫 태그가 항상 맨 아래 칸에 오게 한다.
- 재료 라벨은 클릭 불가 장식이다. 재료 카테고리별 색 스프라이트가 아직 없어 모든 재료에 `defaultIngredientLabel`(`element-BG-pink`)을 쓴다. 색별 스프라이트가 생기면 `ResolveIngredientLabel()`에서 카테고리 → 스프라이트를 고르면 된다.
- `element-BG-*`는 Sprite Editor 테두리 좌우 24px로 9-slice되어 재료명 길이만큼 늘어난다(최소 146px).
- 재료 목록과 설명은 `Body`(세로 레이아웃) 안에 쌓여, 재료 수에 따라 설명이 아래로 밀린다.

## RecipeBookUI 연동

`RecipeBookUI.SetInteractable(bool)`이 **disabled → enabled로 전환될 때만**(예: `EpisodeMode` 진입 후 `OrderMode`/`CraftingMode`로 복귀) `RecipeSearchUI.ResetToMain()`을 호출해 검색 화면을 초기 상태(검색어 비움, 카테고리 버튼 표시)로 되돌린다. 단축키로 단순히 열고 닫는 동작(`Toggle()`)은 리셋되지 않는다.

**검색창 포커스 중 게임 키 차단** (`RecipeSearchUI.IsSearchFocused` → `RecipeBookUI.IsSearchFocused` → `InputRouter`): 검색어에 A/D/W/S/Tab/Space가 들어가면(예: "sad", "data") 타이핑 중에 도감 토글·술장 토글·주문서 토글·카메라 이동·대사 진행이 같이 트리거되던 문제가 있어서, `searchInputField.isFocused`(TMP_InputField 표준 프로퍼티)가 true인 동안은 `InputRouter.Update()`가 마우스 클릭을 제외한 모든 키보드 액션을 건너뛴다. 검색창 포커스를 벗어나면 즉시 정상 동작으로 복구된다.

## 씬 배치

- 패널 틀은 2680×1560 흔들림 여백 틀 위치에 잘린 스프라이트를 놓는 방식([resolution-letterbox.md](../core/resolution-letterbox.md#화면-흔들림-여백-60px)). `RecipeBookUI`는 702×1121(틀 왼쪽 아래가 게임 영역 x −1, y 379), 닫힘 위치 `closedX` −590이면 왼쪽 기둥 부분만 남는다. 기둥 위의 투명 `Button`이 열고 닫는 손잡이다.
- `SearchRoot`는 틀 안쪽 주황 화면(틀 기준 x 136~649, 위에서 193~1052)에서 위 40px·좌우 36px·아래 20px 안쪽, 441×799(`button-1` 폭과 같음). 네 화면(MainView/CategoryView/ResultsView/DetailView)은 모두 이 크기를 꽉 채운다.
- 목록은 `ScrollRect` + `RectMask2D` Viewport(내용 폭 전체) + 세로 레이아웃 Content. 14px 스크롤바(자동 숨김)는 내용 폭을 줄이지 않도록 오른쪽 여백 쪽(+24px)에 둔다.
- 프리팹: `Prefabs/UI/RecipeBook/` `SearchOption`(태그 버튼 423×88), `RecipeListItemUI`(441×151), `RecipeIngredientRowUI`, `RecipeTagChip`(249×68).

## 주문서 (`OrderTicketUI`)

- 틀은 `주문서_screen`(789×461, 오른쪽 위 기둥 포함). 닫힘은 `hideOffsetY` 330만큼 위로 올려 아래 약 70px만 남긴다.
- 안쪽 왼쪽은 기존 주문자·주문 대사 텍스트(`Viewport`), 오른쪽은 표정 이미지(`MoodFace`, 180×180).
- 표정: `SetMoodPercent(float)`가 `moodFaceThresholds`(기본 75/50/25, 이상이면 해당 표정)로 `moodFaceSprites`(order-face-1 최상 ~ 4 나쁨)를 고른다. 최상 100~75, 좋음 74~50, 보통 49~25, 나쁨 24~0. 아직 값을 주는 쪽이 없어 `defaultMoodPercent`(37.5, 보통)로 고정 표시된다.

## 추후 작업

- "재료로 찾기" 재도입 시 `RecipeSearchCategory`에 `Ingredient` 추가 + `ingredientPropertyTags` 기반 필터 UI 복원
- 검색 다중 조건(이름+태그 조합 등) 필요 시 검토
