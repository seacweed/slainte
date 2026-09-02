# Slainte 리팩터링 브랜치 인수인계

기준일: 2026-09-02
작업 브랜치: `boguk9_refactoring`
Unity 버전: `6000.3.5f2`

이 문서는 현재 작업 트리의 코드와 에셋 구조를 기준으로 한다. 과거 `Assets/Scripts`, `Assets/Editor`, 타입별 `Resources` 구조를 설명하는 인수인계 내용은 더 이상 유효하지 않다.

## 현재 구조

- 프로젝트 코드와 제작 자산: `Assets/_Project/`
- 공통 기반: `Assets/_Project/Core/`
- 기능별 코드·자산: `Assets/_Project/Features/`
- 두 기능 이상이 함께 쓰는 작은 공용 모듈: `Assets/_Project/Shared/`
- 런타임 `Resources.Load` 대상: `Assets/Resources/<기능>/`
- 런타임 원시 파일: `Assets/StreamingAssets/<기능>/`

주요 기능 폴더는 `Bartending`, `Business`, `MainMenu`, `Narrative`, `Rest`다. 자세한 배치 규칙은 `docs/refactoring/folder-structure-conventions.md`를 따른다.

## 바텐딩 기획 데이터 흐름

사람이 수정하는 기준 원본은 아래 세 CSV다.

- `Assets/_Project/Features/Bartending/Content/Source/Planning/items.csv`
- `Assets/_Project/Features/Bartending/Content/Source/Planning/recipes.csv`
- `Assets/_Project/Features/Bartending/Content/Source/Planning/recipe_ingredients.csv`

`PlanningCsvAssetImporter`가 다음 에셋을 ID 기준으로 생성하거나 갱신한다.

- 활성 아이템 15종 → `Assets/Resources/Bartending/Items/`
- 술장 정의 15종 → `Assets/_Project/Features/Bartending/Content/Generated/LiquorBottles/Planning/`
- 주문 가능한 기본 레시피 21종 → `Assets/Resources/Bartending/Recipes/`

제품 런타임의 레시피 단일 소스는 `Resources/Bartending/Recipes`다. `CocktailRecipeDataLoader`는 이 경로의 `CocktailRecipeDef`만 읽는다. `StreamingAssets/Bartending`의 CSV 로더와 샘플 데이터는 개발·검증 용도이며 제품 레시피 로딩 경로가 아니다.

레시피 결과별 중복 에셋은 사용하지 않는다. 기본 레시피가 맞으면 `Good`, 배합·기법은 맞지만 잔 또는 얼음만 다르면 `MidGlass`/`MidIce`/`MidIceGlass`, 주문과 다른 기본 레시피를 정확히 만들면 `MidWrongMenu`, 그 밖에는 `Bad`로 런타임에서 직접 판정한다.

## 주요 파일

- `Assets/_Project/Features/Bartending/Editor/PlanningCsvAssetImporter.cs`
- `Assets/_Project/Features/Bartending/Editor/BartendingAssetPaths.cs`
- `Assets/_Project/Features/Bartending/Runtime/CocktailRecipeDataLoader.cs`
- `Assets/_Project/Features/Bartending/Runtime/CocktailEvaluator.cs`
- `Assets/_Project/Features/Bartending/Runtime/CocktailOrderEvaluator.cs`
- `Assets/_Project/Features/Bartending/Runtime/CocktailRecipeDef.cs`
- `Assets/_Project/Shared/Runtime/Content/ProjectRuntimeContentPaths.cs`
- `docs/gameplay/item-data-table-guide.md`
- `docs/refactoring/folder-structure-conventions.md`

## 임포트와 검증

- 기준 CSV 선택 임포트: `Slainte > 데이터 > 기획 CSV 임포트`
- 저장소 기준 CSV 즉시 임포트: `Slainte > 데이터 > 기준 CSV 바로 임포트`
- 생성 에셋 검증: `Slainte > 품질 검증 > 기획 CSV 에셋 검증`

현재 변경에서 확인한 결과:

- `dotnet build Assembly-CSharp-Editor.csproj --no-restore`: 오류 0, 기존 `CS0649` 경고 8
- Unity `6000.3.5f2` 배치 모드 `PlanningCsvAssetImporter.ValidateImportedAssets`: 통과
- 검증 대상: 아이템·술장 15종, 기본·주문 가능 레시피 21종, 가격·재고·도수·얼음·`Good`·`Mid`·`MidWrongMenu`

## 남은 구조 예외와 후속 확인

- `Content/Source/Planning`과 `Content/Generated/LiquorBottles/Planning`에는 제작 단계 이름이 남아 있다. 새 런타임 의존을 추가하지 말고, 별도 GUID 보존 마이그레이션에서 정리한다.
- `Content/Source/Legacy/ItemData.csv`, `Content/Legacy/Planning`, 기존 이름 기반 `Generated/LiquorBottles` 에셋이 병존한다. 참조를 먼저 조사한 뒤 제거해야 한다.
- `RuntimeResourceStructureValidator`의 바텐딩 개수 기대값(18/120/94)은 현재 실제 값(15/0/21)보다 오래됐다. 현재는 바텐딩 전용 `기획 CSV 에셋 검증`을 사용하고 통합 검증기는 별도 수정한다.
- 기존 Scene·Prefab 직렬화 호환성 때문에 모든 런타임 타입을 일괄 namespace/asmdef로 이동하지 않았다. 변경 시 `.meta` GUID와 직렬화 타입을 함께 검증한다.
- 자동 검증은 통과했지만 제품 Scene에서 주문 생성 → 제조 → 잔 제출 → 보상까지의 수동 스모크 테스트는 병합 전에 별도로 수행한다.

## 작업 시 주의

- `Resources`에 CSV 원본이나 제작 중간물을 넣지 않는다.
- 새 `Planning`, `Data`, `Misc`, `_Recovery` 폴더를 만들지 않는다.
- `Resources` 경로 문자열은 직접 쓰지 않고 `ProjectResourcePaths`를 사용한다.
- 레시피 결과를 표현하기 위한 파생 레시피 에셋을 다시 만들지 않는다. 결과 차이는 평가 로직의 책임이다.
