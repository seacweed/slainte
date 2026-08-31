# Slainte 구조 리팩토링 계획

기준일: 2026-08-31
작업 브랜치: `boguk9_refactoring`

## 목표

프로젝트 전용 코드와 콘텐츠를 기능별로 모으고, 폴더 위치가 아니라 책임과 의존 방향으로 소유 영역을 구분한다.

물리적 이동에서는 동작과 직렬화 데이터를 변경하지 않는다. 이동에 필수인 경로 상수 수정과 검증기는 같은 원자적 커밋에 포함하고, 동작·네임스페이스·어셈블리 변경은 별도 단계로 수행한다.

## 이번 브랜치 결과

완료한 범위:

- 프로젝트 코드와 에디터 도구를 `Core`, `Shared`, `Features` 소유 영역으로 이동
- 사람이 수정하는 Bartending·Business CSV 원본을 각 기능의 `Content/Source`로 분리
- 제품·개발 씬과 Unity 설정 에셋을 `_Project` 아래로 이동하고 Build Settings 및 에디터 경로 갱신
- 이동 커밋과 코드 변경 커밋을 분리해 기존 작성 이력을 추적할 수 있게 유지
- 에디터 씬 경로를 `ProjectScenePaths`로 통합하고 `EpisodeManager`의 동일 필터 로직 중복 제거
- `Shared/Input`, `Shared/Lifecycle`에 기능별 네임스페이스를 도입하고 사용처를 명시적으로 연결
- `Slainte.Shared.Input`, `Slainte.Shared.Lifecycle`, `Slainte.Shared.Editor` 어셈블리 경계를 도입
- `Slainte.Shared.Tests.EditMode` 테스트 어셈블리와 공용 계약 테스트 3개 추가
- `Assets/RestScene` 잔여 에셋을 Rest의 `Art`, `Prefabs`, `Content/Legacy`로 통합
- 팀 소유 `MetaballFluid`를 Bartending의 `Infrastructure`로 통합하고 경로 검증기 추가
- Bartending 병·잔·도구 아트를 `Features/Bartending/Art`로 통합
- Bartending 전용 장비·상호작용·주류 선반·레시피 UI Prefab 13개를 `Features/Bartending/Prefabs`로 통합
- Bartending 전용 병·칵테일·장비·주류 선반·레시피 Sprite 114개를 역할별로 통합
- Bartending 에디터의 하드코딩 자산 경로를 `BartendingAssetPaths`로 통합하고 구조 검증기를 추가

남겨둔 범위:

- 직렬화 타입 마이그레이션이 필요한 나머지 전역 네임스페이스 변경
- 기능 간 순환 의존을 먼저 제거해야 하는 Core·Business·Bartending·Rest·Narrative `.asmdef`
- 씬과 Prefab에 `Assembly-CSharp::타입명`으로 기록된 Shared UI 컴포넌트의 어셈블리 이동
- `Resources`, `StreamingAssets`, `Data`의 Source/Generated/Runtime 세분화
- 루트 `Prefabs`, `Sprites`에 남은 Business·Core·공용 자산의 소유 영역 확정과 후속 이동

현재 검증 결과:

- Unity 배치 재임포트 및 Runtime·Editor 빌드: 오류 0개, 기준선과 동일한 `CS0649` 경고 8개
- Shared EditMode 테스트: 3개 통과, 실패·건너뜀 0개
- TV/Rest 배치 검증 및 Business 순수 규칙 배치 검증: 통과
- Rest 최종 아트 검증 및 MetaballFluid 인프라 에셋 검증: 통과
- Bartending Art·Sprite 114개·Prefab 13개 로드 및 Prefab Missing Script 검증: 통과
- BusinessShift 배치 검증: 검증기 기대 영업시간 180초와 현재 설정 300초 불일치로 중단
- BartendingSystem 배치 검증: 검증기 기대 액체 알파 1.0과 현재 구현 0.2 불일치로 중단
- Unity GUI와 전체 플레이 흐름 수동 검증: 별도 확인 필요

## 기준선

- Unity: `6000.3.5f2`
- Runtime 빌드: 오류 0개, 기존 `CS0649` 경고 8개
- Editor 빌드: 오류 0개, 기존 `CS0649` 경고 8개
- 제품 씬: `MainMenuScene`, `CoreScene`, `BusinessScene`, `RestScene`
- 프로젝트 자체 `.asmdef`: `Slainte.Shared.Input`, `Slainte.Shared.Lifecycle`, `Slainte.Shared.Editor`, `Slainte.Shared.Tests.EditMode`
- `Resources`와 `StreamingAssets`를 사용하는 런타임 경로가 존재함

## 목표 폴더

```text
Assets/
├─ _Project/
│  ├─ Core/
│  │  ├─ Runtime/
│  │  ├─ Editor/
│  │  └─ Tests/
│  ├─ Features/
│  │  ├─ MainMenu/
│  │  ├─ Narrative/
│  │  ├─ Business/
│  │  ├─ Bartending/
│  │  └─ Rest/
│  ├─ Shared/
│  │  ├─ Runtime/
│  │  ├─ Editor/
│  │  └─ Tests/
│  ├─ Scenes/
│  │  ├─ Production/
│  │  ├─ Development/
│  │  └─ Templates/
│  └─ Settings/
├─ Resources/
├─ StreamingAssets/
├─ ThirdParty/
└─ TextMesh Pro/
```

`Resources`와 `StreamingAssets`는 데이터 원본과 생성물의 관계를 확정하기 전까지 현재 위치를 유지한다.

## 소유 영역 후보

| 현재 경로 | 목표 영역 | 비고 |
|---|---|---|
| `Assets/Scripts/MainMenu` | `Features/MainMenu/Runtime` | 첫 시험 이동 |
| `Assets/Scripts/Audio` | `Core/Runtime/Audio` | 앱 수명의 전역 오디오 서비스 |
| `Assets/Scripts/Input`의 인터페이스 | `Shared/Runtime/Input` | UI·게임플레이 공용 계약 |
| `Assets/Scripts/Input/InputRouter.cs` | `Features/Business/Runtime/Input` | BusinessScene의 입력 조립기 |
| `Assets/Scripts/UI/Notifications` | `Features/Business/Runtime/UI/Notifications` | BusinessScene의 호감도 알림 표시 |
| `Assets/Scripts/Narrative/Data` | `Features/Narrative/Runtime/Data` | Narrative ScriptableObject 타입 |
| `Assets/Scripts/Narrative/Runtime` | `Features/Narrative/Runtime/Baked` | 베이크 JSON 호환 실행 경로 |
| `Assets/Editor/Narrative` | `Features/Narrative/Editor` | USS·Template 하드코딩 경로 동시 수정 |
| `Assets/Narrative/Graphs` | `Features/Narrative/Content/Graphs` | Narrative Graph 원본 에셋 |
| `Assets/RestScene/Scripts` | `Features/Rest/Runtime` | RestScene 런타임 코드 |
| `Assets/Scripts/TV`, `Assets/RestScene/Scripts/TV*.cs` | `Features/Rest/Runtime/TV` | TV 데이터·판정·표시 통합 |
| `Assets/RestScene/Prefabs`, `Sprites` | `Features/Rest/Prefabs`, `Art/Sprites` | Rest 실사용 프리팹과 이미지 |
| `Assets/RestScene/Episode`, `ShopItem` | `Features/Rest/Content/Legacy` | 현재 코드·에셋 참조가 없는 이전 데이터 보존 |
| `Assets/Scripts/Business` | `Features/Business/Runtime/Flow` | 주문 세션과 영업 조립 담당 |
| `Assets/Scripts/Conversation` | `Features/Business/Runtime/Conversation` | 에피소드 공용 경계 재검토 |
| `Assets/Scripts/OrderTicket` | `Features/Business/Runtime/OrderTicket` | BusinessScene UI |
| `Assets/Scripts/Economy` | `Core/Runtime/Economy` | 영업·휴식·정산·내러티브 공용 통화 정책 |
| `Assets/Scripts/Presentation` | `Features/Business/Runtime/Presentation` | BusinessScene 캐릭터 표시와 서빙 대상 |
| `Assets/Scripts/Bartending` | `Features/Bartending/Runtime` | 바텐딩 도메인 핵심 |
| `Assets/Scripts/CameraMove` | `Features/Bartending/Runtime/Camera` | 바텐딩 화면 카메라 |
| `Assets/Scripts/DragandDrop` | `Features/Bartending/Runtime/Interaction` | 병·도구 배치와 UI 드래그 |
| `Assets/Scripts/LiquorShelf` | `Features/Bartending/Runtime/LiquorShelf` | 재고·술장·배달 상점 |
| `Assets/Scripts/RecipeBook` | `Features/Bartending/Runtime/RecipeBook` | 제조 레시피 탐색 UI |
| `Assets/MetaballFluid`의 에셋 | `Features/Bartending/Infrastructure/MetaballFluid` | 팀 소유 액체 표현 에셋(셰이더·머티리얼·Prefab·물리) |
| `Assets/MetaballFluid`의 스크립트 | `Features/Bartending/Runtime/Liquid`, `Runtime/Interaction` | 풀링·혼합·회수·렌더링 실행 책임을 Bartending 런타임으로 통합 |
| `Assets/Art/Bartending` | `Features/Bartending/Art` | 병·잔·도구 캐비닛·충돌 기준 이미지 통합 완료 |
| Bartending 전용 루트 Prefab | `Features/Bartending/Prefabs` | 장비·상호작용·LiquorShelf·RecipeBook 기준으로 통합 완료 |
| `Assets/Sprites/bottles`, `cocktails`, Bartending UI Sprite | `Features/Bartending/Art/Sprites` | 병·칵테일·장비·LiquorShelf·RecipeBook 기준으로 통합 완료 |
| `Assets/CoreScene/Scripts`의 앱 흐름 | `Core/Runtime/Flow` | 씬 전환과 하루 진행 |
| `Assets/CoreScene/Scripts`의 저장 코드, `Assets/Scripts/GameProgress.cs` | `Core/Runtime/Persistence` | 저장 데이터와 런타임 진행 상태 |
| `Assets/CoreScene/Scripts`의 컷씬·정산 코드 | `Core/Runtime/Cutscene`, `Core/Runtime/Settlement` | 전역 화면 흐름 |
| `Assets/CoreScene/Scripts`의 싱글턴 기반 클래스 | `Shared/Runtime/Lifecycle` | 여러 기능이 쓰는 MonoBehaviour 수명 계약 |
| `Assets/Scripts/Core` | `Features/Business/Runtime/Mode` 및 `Conversation/Episode` | 실제로는 BusinessScene 모드와 에피소드 조건 코드 |
| `Assets/Scripts/Tools` | `Shared/Runtime/UI` | 여러 UI에서 재사용하는 표시·레이아웃 도구 |
| `Assets/Editor` 루트 도구 | `Core/Editor`, `Shared/Editor`, `Features/*/Editor` | Bartending·Business·Rest·공용 도구를 소유 기능별 분리 |
| `Assets/Editor/Data/Planning` | `Features/Bartending/Content/Source/Planning` | 사람이 수정하는 기획 CSV 원본 |
| `Assets/Editor/Data/CustomerDialogue` | `Features/Business/Content/Source/CustomerDialogue` | 사람이 수정하는 주문 대사 CSV 원본 |
| 제품·개발 씬 | `Scenes/Production`, `Scenes/Development` | Build Settings와 Editor 도구 경로 동시 갱신 |
| `Assets/Settings` | `_Project/Settings` | URP·Renderer2D·씬 템플릿 설정 에셋 |

`CameraMove`, `DragandDrop`, `LiquorShelf`, `RecipeBook`은 Bartending 런타임 하위 모듈로 분류한다. `Tools`는 참조 그래프를 확인한 뒤 소유 영역을 확정한다.

## 실행 순서

### 1. 물리적 이동

1. MainMenu를 시험 이동한다.
2. Audio를 Core로, 입력 계약을 Shared로, BusinessScene 입력·알림을 Business로 이동한다.
3. Narrative를 Runtime, Editor, Content 단위로 이동한다.
4. Rest와 TV를 통합한다.
5. Business와 주문·대화 UI를 통합한다.
6. Bartending 코드, 도구, 콘텐츠를 통합한다.
7. Core와 제품 씬을 마지막에 이동한다.

각 기능 이동은 독립 커밋으로 유지한다. 이동 커밋에서는 포맷팅과 동작 변경을 하지 않으며, 이동으로 깨지는 에디터 경로만 함께 갱신한다.

### 2. 데이터 경계 정리

각 데이터셋을 다음 중 하나로 분류한다.

- Source: 사람이 수정하는 CSV, 그래프, 원본 에셋
- Generated: 임포터가 생성하는 ScriptableObject와 JSON
- Runtime: 게임이 직접 읽는 데이터
- Legacy: 호환용 또는 미사용 이전 경로

`Resources.Load`, `Application.streamingAssetsPath`, `AssetDatabase`의 문자열 경로를 목록화하고 데이터 이동과 같은 변경에서 갱신한다.

### 3. 코드 리팩토링

1. 전역 네임스페이스를 기능별 네임스페이스로 옮긴다.
2. UI, 흐름 제어, 도메인 판정, 저장 책임을 분리한다.
3. `Singleton`, 런타임 탐색, 직접 `Resources.Load` 호출을 조립 경계로 모은다.
4. 제품 경로와 Legacy 경로가 병존하는 시스템을 하나씩 통합한다.
5. 순수 C# 판정과 정책부터 EditMode 테스트를 추가한다.

### 4. 어셈블리 경계

코드 의존 방향이 정리된 뒤 Runtime, Editor, Tests `.asmdef`를 도입한다. 폴더 이동, 네임스페이스 변경, `.asmdef` 추가를 같은 커밋에서 수행하지 않는다.

## 검증 게이트

각 이동 또는 코드 변경 뒤 다음 조건을 통과해야 다음 단계로 진행한다.

- 이동 전후 `.meta` GUID가 동일함
- Runtime과 Editor 빌드 오류 0개
- 기존 경고 수가 기준선보다 증가하지 않음
- 제품 씬과 관련 Prefab에 Missing Script가 없음
- `Resources.Load`와 임포터 출력 경로가 유효함
- 관련 기능의 Editor Validator가 통과함
- 관련 플레이 흐름을 Unity에서 직접 확인함
- `git diff`에 의도하지 않은 씬·Prefab 전체 재직렬화가 없음

## Git과 기여 이력

- 자산 이동과 그 이동에 필수인 경로 수정은 하나의 원자적 커밋으로 유지한다.
- 동작·설계 변경은 자산 이동 커밋과 분리한다.
- 이동 커밋에서는 무관한 파일 내용과 포맷을 바꾸지 않는다.
- 공동 구현은 커밋 또는 PR 설명에 기록한다.
- 대규모 기계적 변경은 필요할 경우 `.git-blame-ignore-revs` 후보로 기록한다.
- 기존 팀원의 작성 이력을 단순 이동 커밋의 신규 구현으로 간주하지 않는다.

## 중단 조건

다음 상황에서는 후속 이동을 멈추고 원인을 먼저 해결하거나 팀에 보고한다.

- 작업 트리에 예상하지 못한 동시 변경이 나타남
- `.meta` GUID가 변경됨
- 기준선에 없던 컴파일 오류가 발생함
- 씬 또는 Prefab에서 Missing Script가 발견됨
- 임포터 결과나 직렬화 데이터가 의도 없이 변경됨
- 제품 경로와 Legacy 경로의 소유권을 구분할 근거가 부족함
