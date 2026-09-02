# Slainte 프로젝트 폴더 구조 규약

기준일: 2026-09-02  
적용 대상: `Assets` 아래의 프로젝트 코드, Unity 에셋, 런타임 데이터

이 문서에서 **표준**은 새 파일과 최종 구조에 적용할 규칙이고, **현재 예외**는 아직 마이그레이션하지 못한 실제 상태다. 예외를 표준의 근거로 사용하지 않으며, 표준 예시가 현재 폴더가 이미 모두 정리됐다는 뜻도 아니다.

## 1. 목적

이 규약은 새 파일의 위치를 사람마다 다르게 판단하지 않도록 다음 기준을 고정한다.

1. 최상위 분류는 파일 종류가 아니라 **소유 기능**을 따른다.
2. 기능 안에서 `Runtime`, `Editor`, `Prefabs`, `Art`, `Content`처럼 파일 역할을 나눈다.
3. 하나의 파일에는 하나의 명확한 소유 영역만 둔다.
4. Unity 특수 경로는 런타임이 실제로 요구할 때만 사용한다.
5. 폴더 이동으로 Unity GUID와 직렬화 참조가 바뀌지 않게 한다.
6. 필요하지 않은 빈 폴더는 미리 만들지 않는다.

## 2. 표준 최상위 구조

```text
Assets/
├─ _Project/
│  ├─ Core/
│  ├─ Features/
│  ├─ Shared/
│  ├─ Scenes/
│  └─ Settings/
├─ Resources/
├─ StreamingAssets/
├─ TextMesh Pro/
└─ UI Toolkit/
```

### 최상위 규칙

- 팀이 작성한 일반 코드와 에셋은 `_Project` 아래에 둔다.
- `Resources`와 `StreamingAssets`에는 런타임이 해당 Unity 경로를 요구하는 파일만 둔다.
- 외부 패키지가 소유한 폴더는 공급자가 제공한 구조를 유지한다.
- 새 최상위 폴더를 임의로 추가하지 않는다.
- `Scripts`, `Prefabs`, `Images`, `Data` 같은 종류별 최상위 폴더를 다시 만들지 않는다.
- `_Recovery`, `Temp`, `Backup`, `New Folder`, `Misc` 폴더를 `Assets` 아래에 커밋하지 않는다.

현재 실제 최상위 구조는 위 표준과 일치한다. `_Project` 아래에는 `Core`, `Features`, `Scenes`, `Settings`, `Shared`가 있고, `Resources`는 `Bartending`, `Business`, `Core`, `Narrative`, `Rest`, `StreamingAssets`는 `Bartending`, `Narrative`로 나뉜다.

## 3. 소유 영역 판단

파일을 배치할 때 다음 순서로 판단한다.

1. 특정 플레이 기능이 없어지면 파일도 같이 없어지는가? 그러면 해당 `Features`에 둔다.
2. 게임 전체 실행과 조립을 담당하는가? 그러면 `Core`에 둔다.
3. 특정 기능의 도메인 규칙·타입에 의존하지 않으며 둘 이상의 영역에서 재사용되는가? 그러면 `Shared`에 둔다.
4. Unity의 특수 로딩 경로가 필요한가? 소유 기능 이름을 유지한 채 `Resources` 또는 `StreamingAssets`에 둔다.

Scene에서 사용되는 위치는 소유권의 근거가 아니다. 예를 들어 상점 패널·화면 전환은 `Rest`, 판매되는 병과 재료 정의는 `Bartending`, 지갑과 통화 정책은 `Core/Economy`가 소유한다. 한 화면에서 함께 사용된다는 이유로 모두 같은 Feature에 넣지 않는다.

## 4. Feature 규칙

Feature는 플레이어가 인식할 수 있는 하나의 게임 기능과 그 규칙·상태·데이터를 소유한다.

현재 표준 Feature는 다음과 같다.

| Feature | 소유 책임 |
|---|---|
| `MainMenu` | 게임 시작, 이어하기, 종료, 타이틀 화면 |
| `Business` | 손님, 주문, 영업 흐름, 주문표, 영업 대화 |
| `Bartending` | 재료, 레시피, 제조 판정, 액체, 병·잔, 주류 재고 |
| `Narrative` | Episode, Chapter, 해금 조건, 내러티브 그래프와 실행 |
| `Rest` | 휴식 화면, 휴식 상호작용, 에피소드 보드, TV 표시 |

새 Feature는 다음 조건을 대부분 만족할 때만 추가한다.

- 독립적인 플레이 흐름 또는 명확한 도메인 규칙이 있다.
- 자체 상태나 데이터를 소유한다.
- 다른 Feature의 세부 구현 없이 독립적으로 테스트할 수 있다.
- 프로젝트에서 제거할 범위를 명확히 정할 수 있다.

액체, 주문표, 레시피 북처럼 상위 기능 없이는 존재 의미가 없는 것은 새 Feature가 아니라 하위 모듈이다.

### Feature 내부 표준

```text
Features/<Feature>/
├─ Runtime/
├─ Editor/
├─ Tests/
├─ Art/
├─ Audio/
├─ Prefabs/
├─ Content/
│  ├─ Source/
│  ├─ Generated/
│  └─ Legacy/
└─ Infrastructure/
```

모든 하위 폴더를 반드시 만들지는 않는다. 실제 파일이 생길 때만 만든다.

- `Runtime`: 빌드에 포함되는 실행 코드
- `Editor`: 해당 Feature만 사용하는 Unity Editor 코드
- `Tests`: 해당 Feature의 EditMode·PlayMode 테스트
- `Art`: Sprite, Texture, Material, Shader, Animation 등 시각 에셋
- `Audio`: 기능 전용 음악과 효과음
- `Prefabs`: 기능이 소유하는 Prefab
- `Content`: 원본 데이터, 생성 결과, 임시 Legacy 데이터
- `Infrastructure`: 해당 Feature가 소유하는 저수준 기술 에셋 또는 외부 시스템 어댑터. 공급자 소유 패키지 원본은 이곳으로 옮기지 않는다.

## 5. Core 규칙

`Core`는 게임 전체를 실행하고 여러 Feature를 조립한다.

Core에 둘 수 있는 대표 책임은 다음과 같다.

- 게임 상태와 날짜 진행
- Scene 전환과 애플리케이션 시작 순서
- 저장·불러오기와 전체 진행 상태
- 전역 오디오 서비스
- 여러 Feature를 연결하는 상위 조립 코드

특정 Feature의 게임 규칙을 Core로 올리지 않는다. 예를 들어 칵테일 판정은 여러 Scene에서 사용돼도 Bartending 소유다.

```text
Core/
├─ Runtime/
│  ├─ Flow/
│  ├─ Persistence/
│  ├─ Audio/
│  ├─ Cutscene/
│  └─ Settlement/
├─ Editor/
├─ Tests/
├─ Art/
├─ Prefabs/
└─ Content/
```

## 6. Shared 규칙

`Shared`는 게임을 직접 진행하지 않는 기능 중립 공용 부품이다.

Shared로 옮기려면 다음 조건을 모두 만족해야 한다.

- 둘 이상의 소유 영역에서 실제로 사용한다.
- Bartending, Business, Narrative의 도메인 타입이나 규칙에 의존하지 않는다.
- 게임 진행 순서나 Feature 상태를 직접 관리하지 않는다.
- 하위 의존성으로 안전하게 재사용할 수 있다.

현재 허용되는 예시는 다음과 같다.

- 입력 인터페이스
- `MonoSingleton`, `SceneSingleton` 같은 기반 타입
- 기능 중립 UI 도구
- 공용 경로 계약과 Editor 보조 도구

`ProjectResourcePaths`처럼 프로젝트 전체의 위치 계약을 관리하는 레지스트리는 소유 영역 이름을 상수로 열거할 수 있다. 다만 해당 Feature의 데이터 타입이나 게임 규칙을 참조해서는 안 된다.

`Utils`, `Helpers`, `Managers`라는 이유만으로 Shared에 넣지 않는다. 소유권이 불명확하면 우선 가장 가까운 Feature에 두고 실제 재사용 요구가 생겼을 때 추출한다.

## 7. 코드와 의존 방향

목표 의존 방향은 다음과 같다.

```text
Core ──> Features ──> Shared
```

- Shared는 Core나 Feature를 참조하지 않는다.
- Feature는 Shared를 참조할 수 있다.
- Core는 Feature와 Shared를 조립할 수 있다.
- Feature끼리는 상대 Feature의 내부 구현을 직접 참조하지 않는다.
- Feature 간 호출이 필요하면 소유 Feature가 공개한 계약이나 이벤트를 사용한다.
- Bartending 용어를 포함하는 계약은 Shared가 아니라 `Bartending/Runtime/Contracts`에 둔다.
- Runtime 코드는 `UnityEditor`와 Editor 어셈블리를 참조하지 않는다.
- Editor 코드는 Runtime 코드를 참조할 수 있다.

Feature별 `.asmdef`는 위 의존 방향에서 순환 참조가 제거된 뒤 추가한다.

### Namespace 규칙

새 타입은 `MonoBehaviour`·`ScriptableObject` 여부와 관계없이 소유 모듈의 namespace를 사용한다. 기존 모듈에 파일을 추가할 때는 그 모듈의 현재 namespace를 따르며, 폴더 이동 작업에 새 namespace 변경을 섞지 않는다.

```text
Slainte.Core
Slainte.Bartending
Slainte.Business
Slainte.Narrative
Slainte.Rest
Slainte.Shared
```

위 이름은 새 모듈의 기본 목표다. 이미 `NarrativeFlow`, `Slainte.TV`, `Slainte.Economy`처럼 독립된 namespace를 쓰는 모듈에는 기존 경계를 유지한다. 같은 모듈 안에 목표 이름과 기존 이름을 임의로 혼용하지 않는다. Shared의 현재 명시적 경계는 `Slainte.Shared.Input`, `Slainte.Shared.Lifecycle`, `Slainte.Content`, `Slainte.EditorTools`다.

하위 폴더가 의미 있는 코드 경계를 만들 때만 하위 namespace를 추가한다. 폴더 깊이를 기계적으로 namespace에 모두 복제하지 않는다.

기존 Scene·Prefab에 직렬화된 `MonoBehaviour`와 `ScriptableObject`의 namespace 또는 asmdef를 변경할 때는 별도 마이그레이션으로 처리한다. `[MovedFrom]`, `.meta` GUID, Scene·Prefab의 타입 식별자를 함께 검증한다.

## 8. Content 규칙

```text
Content/
├─ Source/
├─ Generated/
└─ Legacy/
```

- `Source`: 사람이 직접 수정하는 CSV, 그래프, 원본 설정
- `Generated`: Importer나 생성 도구가 만든 Unity 에셋 중 Unity 특수 경로가 필요하지 않은 결과
- `Legacy`: 현재 실행 경로가 참조하지 않는 이전 데이터

Generated 파일을 손으로 수정하지 않는다. 수정이 필요하면 Source 또는 Importer를 변경하고 다시 생성한다.

생성 결과가 `Resources.Load` 대상이면 `Content/Generated`와 `Resources`에 중복 보관하지 않고 `Resources/<Owner>/<LoadGroup>`에 직접 출력한다. 이 경우 원본은 `Content/Source`, 생성 절차는 해당 Feature의 `Editor`, 런타임 결과만 `Resources`가 맡는다.

Legacy에는 새 참조를 추가하지 않는다. Legacy를 유지할 때는 보존 이유와 삭제 조건을 문서 또는 README에 기록한다.

### Planning 명칭

`Planning`은 기능이나 데이터 역할이 아니라 제작 단계 이름이므로 새 폴더명으로 사용하지 않는다. 특히 런타임 출력 경로에는 두지 않는다.

```text
# 표준
Content/Source/items.csv
Content/Source/recipes.csv
Resources/Bartending/Items/item_1001.asset
Resources/Bartending/Recipes/rec_1001.asset

# 금지
Resources/Bartending/Items/Planning/item_1001.asset
Resources/Bartending/Recipes/Planning/rec_1001.asset
```

`Items/Planning`과 `Recipes/Planning`은 제거했으며, Importer도 각각 `Items`, `Recipes`에 바로 출력한다. 현재 기준 CSV가 있는 `Source/Planning`과 병 생성 결과가 있는 `LiquorBottles/Planning`은 경로 상수가 이미 사용 중인 마이그레이션 예외다. 당장은 이 위치를 기준 원본·출력으로 사용하되 새 `Planning` 폴더나 의존을 추가하지 않고, 이동할 때는 CSV 기본 경로·Importer 출력·GUID를 한 변경에서 함께 갱신한다.

## 9. Resources 규칙

`Resources`는 `Resources.Load` 또는 `Resources.LoadAll`이 필요한 런타임 에셋만 보관한다.

`Resources`는 코드 소유권을 정하는 주 구조가 아니라 Unity 로딩 제약을 위한 **런타임 배포 경계**다. 따라서 첫 단계는 반드시 소유 Feature이고, 그 아래는 타입 이름을 기계적으로 나누는 대신 관련 로더들이 공유하는 런타임 사용 단위(`LoadGroup`)로 나눈다. 현재 `Bartending/Items`와 `Bartending/Recipes`가 타입 이름처럼 보여도, 루트의 전역 `Items`, `Recipes` 분류와 달리 Bartending 소유권 안의 명시적 로딩 단위다.

```text
Resources/
├─ Bartending/
│  ├─ Items/
│  ├─ Recipes/       # 기본 레시피 21개와 TasteMoodPalette; 결과별 복사본 금지
│  ├─ Shop/
│  └─ ToolCabinet/
├─ Business/
├─ Core/
├─ Narrative/
└─ Rest/
```

- 첫 하위 폴더는 반드시 소유 영역 이름을 사용한다.
- Importer 입력 CSV나 작업 중인 원본 파일을 Resources에 넣지 않는다.
- `Final`, `New`, `Old`, `Planning`, `Temp` 같은 제작 상태 이름을 사용하지 않는다.
- 코드에서 Resources 경로 문자열을 직접 작성하지 않는다.
- 런타임 경로는 `ProjectResourcePaths`에 정의한다.
- 같은 타입과 ID의 중복 에셋을 여러 하위 폴더에 두지 않는다.
- 같은 런타임 사용 단위의 소수 보조 에셋은 관련 로더가 명확한 경로로 읽는 조건에서 같은 LoadGroup에 둘 수 있다. `TasteMoodPalette.asset`이 `Bartending/Recipes`에 있는 이유가 여기에 해당한다.
- `Resources`에 있다는 이유로 해당 에셋의 도메인 소유권이 사라지지 않는다. 생성기와 런타임 타입은 계속 원래 Feature가 소유한다.

## 10. StreamingAssets 규칙

`StreamingAssets`에는 런타임이 파일 경로로 직접 읽어야 하는 CSV·JSON 등의 원시 파일만 둔다.

```text
StreamingAssets/
├─ Bartending/
└─ Narrative/
```

- 첫 하위 폴더는 소유 Feature 이름을 사용한다.
- `Data`처럼 소유권이 드러나지 않는 폴더명을 사용하지 않는다.
- 파일명과 폴더명은 `ProjectStreamingAssetPaths`에 정의한다.
- Unity 에셋 참조로 충분한 파일을 StreamingAssets에 중복 보관하지 않는다.
- Source와 런타임 출력이 다르면 생성 절차와 어느 쪽이 원본인지 문서화한다.
- 개발·검증 전용 원시 파일은 가능하면 Feature의 `Tests`, `Content/Source` 또는 Development Scene 쪽에 둔다. 제품 빌드에서 파일 경로 접근이 필요하지 않다면 `StreamingAssets`에 두지 않는다.

## 11. Scene 규칙

```text
Scenes/
├─ Production/
└─ Development/
   └─ Samples/
```

- 실제 빌드 Scene은 `Production`에 둔다.
- 테스트, 샌드박스, 스트레스 테스트 Scene은 `Development`에 둔다.
- Unity Scene 템플릿 설정은 `_Project/Settings/Scenes`에 둔다.
- Build Settings에는 Production Scene만 등록한다.
- 코드에서 Scene 경로를 직접 작성하지 않고 `ProjectScenePaths`를 사용한다.
- 새 Scene 이름은 역할이 드러나는 PascalCase를 사용한다.
- 복구 Scene과 개인 작업 사본은 커밋하지 않는다.

현재 Production Scene은 다음 네 개다.

- `MainMenuScene.unity`
- `CoreScene.unity`
- `BusinessScene.unity`
- `RestScene.unity`

## 12. Editor와 경로 상수

- Feature 전용 Editor 도구는 해당 Feature의 `Editor`에 둔다.
- 전체 프로젝트를 조립하거나 검증하는 Editor 도구는 `Core/Editor`에 둔다.
- 기능 중립 Editor 도구는 `Shared/Editor`에 둔다.
- Feature 에셋 경로는 `<Feature>AssetPaths`에 정의한다.
- Scene 경로는 `ProjectScenePaths`에 정의한다.
- Runtime 특수 경로는 `ProjectResourcePaths`와 `ProjectStreamingAssetPaths`에 정의한다.
- 한 경로를 두 클래스에서 중복 정의하지 않는다.

## 13. 이름 규칙

- 폴더명은 영문 PascalCase를 기본으로 한다.
- `_Project`는 프로젝트 소유 파일을 루트 상단에 모으기 위한 유일한 밑줄 예외다.
- 숫자 접두사(`01.Scripts`)를 사용하지 않는다.
- C# 파일명과 대표 타입명을 일치시킨다.
- `Scripts`, `Data`, `Manager`, `Utils`처럼 범위가 지나치게 넓은 폴더를 만들지 않는다.
- `Test`, `Sample`, `Legacy`는 정해진 Development·Tests·Content/Legacy 경계 안에서만 사용한다.

## 14. 이동과 리팩터링 절차

Unity 에셋이나 코드를 이동할 때 다음 순서를 지킨다.

1. 작업 트리의 기존 변경을 확인하고 다른 사람의 변경과 겹치지 않는지 확인한다.
2. 에셋과 `.meta`를 함께 이동한다.
3. 이동 전후 GUID가 동일한지 확인한다.
4. `AssetDatabase`, Resources, StreamingAssets, Scene 경로 상수를 갱신한다.
5. Build Settings와 Scene·Prefab 직렬화 참조를 확인한다.
6. 이동 커밋과 동작 변경 커밋을 가능한 한 분리한다.
7. Unity 컴파일, 관련 EditMode 테스트와 구조 Validator를 실행한다.
8. Scene·Prefab의 Missing Script를 검사한다.
9. 의도하지 않은 Scene·Prefab 전체 재직렬화가 없는지 diff를 확인한다.

GUID가 바뀌거나 기존 GUID가 다른 에셋에 재사용되면 이동을 완료한 것으로 보지 않는다.

## 15. 새 파일 배치 빠른 판단표

| 질문 | 위치 |
|---|---|
| 특정 플레이 기능의 규칙·상태·표시인가? | `Features/<Feature>` |
| 게임 전체 시작·저장·Scene·흐름 조립인가? | `Core` |
| 기능 중립이며 둘 이상이 쓰는 기반 부품인가? | `Shared` |
| 사람이 직접 수정하는 Feature 원본인가? | `Features/<Feature>/Content/Source` |
| 생성 결과이며 Unity 특수 경로가 필요 없는가? | `Features/<Feature>/Content/Generated` |
| `Resources.Load`로 읽어야 하는가? | `Resources/<Owner>/<LoadGroup>` |
| 파일 경로로 직접 읽어야 하는 원시 데이터인가? | `StreamingAssets/<Owner>/<Purpose>` |
| 제품 Scene인가? | `Scenes/Production` |
| 테스트·샌드박스 Scene인가? | `Scenes/Development` |
| 외부 패키지가 소유하는가? | 패키지의 기존 루트 유지 |

## 16. 현재 마이그레이션 예외

다음 항목은 현재 구조에 남아 있으나 새 규약의 표준으로 간주하지 않는다.

- `Content/Source/Planning`, `Content/Generated/LiquorBottles/Planning` 제작 단계 폴더
- `StreamingAssets/Bartending`의 `ingredients.csv`, `recipes.csv`, `recipe_ingredients.csv` 개발·검증 데이터
- `BusinessOrderSessionController`에 직접 적힌 `order_templates.csv` 파일명
- 전역 namespace에 남은 기존 Unity 직렬화 타입
- Feature 간 직접 참조와 그로 인해 보류된 Feature별 `.asmdef`
- Shared와 Core로 추출하기 전의 일부 교차 기능 조립 코드
- 구조 개편 전 바텐딩 에셋 개수를 기대하는 `RuntimeResourceStructureValidator`

새 코드는 예외를 확대하지 않는다. 예외를 제거하는 변경은 GUID 보존, 컴파일, 자동 검증, Play Mode 확인을 포함한 별도 작업으로 진행한다.

## 17. 리뷰 체크리스트

- [ ] 파일의 소유 Feature를 한 문장으로 설명할 수 있다.
- [ ] Scene 위치가 아니라 규칙과 데이터 소유권으로 분류했다.
- [ ] Shared가 특정 Feature의 데이터 타입이나 게임 규칙에 의존하지 않는다.
- [ ] Runtime 코드가 Editor 코드를 참조하지 않는다.
- [ ] Resources·StreamingAssets 경로가 공용 상수에 등록됐다.
- [ ] Resources·StreamingAssets 사용이 실제 로더 요구로 설명되며 단순 정리 목적이 아니다.
- [ ] Generated 파일을 직접 수정하지 않았다.
- [ ] 새 `Planning`, `Data`, `Misc`, `_Recovery` 폴더를 만들지 않았다.
- [ ] 현재 마이그레이션 예외에 새 의존이나 파일을 추가하지 않았다.
- [ ] 이동한 에셋의 `.meta` GUID가 유지됐다.
- [ ] 관련 Validator와 테스트가 통과했다.
- [ ] 제품 흐름에 영향이 있으면 Play Mode에서 확인했다.
