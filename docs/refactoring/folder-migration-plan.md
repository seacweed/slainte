# Slainte 구조 리팩토링 계획

기준일: 2026-08-31
작업 브랜치: `boguk9_refactoring`

## 목표

프로젝트 전용 코드와 콘텐츠를 기능별로 모으고, 폴더 위치가 아니라 책임과 의존 방향으로 소유 영역을 구분한다.

1차 이동에서는 동작, 네임스페이스, 직렬화 데이터, 어셈블리 경계를 변경하지 않는다. 물리적 이동이 안정된 뒤 코드 리팩토링과 `.asmdef` 도입을 별도 단계로 수행한다.

## 기준선

- Unity: `6000.3.5f2`
- Runtime 빌드: 오류 0개, 기존 `CS0649` 경고 8개
- Editor 빌드: 오류 0개, 기존 `CS0649` 경고 8개
- 제품 씬: `MainMenuScene`, `CoreScene`, `BusinessScene`, `RestScene`
- 프로젝트 자체 `.asmdef`: 없음
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
| `Assets/RestScene/Scripts`, `Assets/Scripts/TV` | `Features/Rest/Runtime` | TV는 Rest 수명에 포함 |
| `Assets/Scripts/Business` | `Features/Business/Runtime` | 주문 세션과 영업 조립 담당 |
| `Assets/Scripts/Conversation` | `Features/Business/Runtime/Conversation` | 에피소드 공용 경계 재검토 |
| `Assets/Scripts/OrderTicket` | `Features/Business/Runtime/OrderTicket` | BusinessScene UI |
| `Assets/Scripts/Bartending` | `Features/Bartending/Runtime` | 가장 큰 이동 단위로 후순위 |
| `Assets/CoreScene/Scripts`, `Assets/Scripts/Core` | `Core/Runtime` | 실제 기능 소유 클래스를 먼저 분리 |
| `Assets/Editor` 루트 도구 | 담당 기능의 `Editor` | 하드코딩 에셋 경로 갱신 필요 |
| 제품·개발 씬 | `Scenes/Production`, `Scenes/Development` | Build Settings와 Editor 도구 경로 갱신 |

`CameraMove`, `DragandDrop`, `Economy`, `LiquorShelf`, `Presentation`, `RecipeBook`, `Tools`는 참조 그래프를 확인한 뒤 소유 영역을 확정한다.

## 실행 순서

### 1. 물리적 이동

1. MainMenu를 시험 이동한다.
2. Audio를 Core로, 입력 계약을 Shared로, BusinessScene 입력·알림을 Business로 이동한다.
3. Narrative를 Runtime, Editor, Content 단위로 이동한다.
4. Rest와 TV를 통합한다.
5. Business와 주문·대화 UI를 통합한다.
6. Bartending 코드, 도구, 콘텐츠를 통합한다.
7. Core와 제품 씬을 마지막에 이동한다.

각 기능 이동은 독립 커밋으로 유지한다. 이동 커밋에서는 포맷팅과 동작 변경을 하지 않는다.

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

- 이동만 수행한 커밋과 실제 코드 변경 커밋을 분리한다.
- 이동 커밋에서는 파일 내용과 포맷을 바꾸지 않는다.
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
