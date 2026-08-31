# Slainte 어셈블리 경계 감사

기준일: 2026-09-01
대상 브랜치: `boguk9_refactoring`

## 결론

이번 구조 리팩토링에서는 Unity에 의존하지 않는 공통 경로 계약만 `Slainte.Shared.Content`로 분리한다. Core·Business·Bartending·Narrative·Rest 전체에 `.asmdef`를 추가하는 작업은 기능 간 상호 의존과 전역 네임스페이스 타입을 먼저 정리한 뒤 별도 변경으로 진행한다.

## 현재 독립 경계

- `Slainte.Shared.Content`: Resources와 StreamingAssets 경로 계약
- `Slainte.Shared.Input`: 카메라 입력 계약
- `Slainte.Shared.Lifecycle`: 씬 수명 계약
- `Slainte.Shared.Editor`: 공용 에디터 도구
- `Slainte.Shared.Tests.EditMode`: 공용 계약 테스트

`Slainte.Shared.Content`, `Input`, `Lifecycle`는 Unity 엔진 참조 없이 컴파일된다.

## 기능 어셈블리를 보류한 근거

현재 C# 파일 282개 중 133개가 전역 네임스페이스에 남아 있다. 이 타입들에는 씬과 Prefab에 직렬화된 `MonoBehaviour`와 `ScriptableObject`가 포함되므로 네임스페이스와 어셈블리 변경을 폴더 이동과 한 번에 처리하면 복구 범위가 지나치게 커진다.

명시적인 기능 참조만 보더라도 다음 결합이 존재한다.

| 소유 영역 | 현재 참조하는 다른 기능 |
|---|---|
| Core | Business, Bartending, Economy, TV |
| Bartending | Economy |
| Business | Bartending, Economy, TV |
| Narrative | Bartending, Business, Economy |
| Rest | Business, Bartending, Economy |

특히 Business가 Rest 소유의 TV 정책을 참조하고 Rest의 TV 조립 코드가 Business를 참조한다. 전역 타입 참조까지 포함하면 단순히 폴더마다 `.asmdef`를 만드는 방식으로는 순환 참조가 발생한다.

## 다음 분리 순서

1. 전역 타입을 일반 C# 데이터·정책과 Unity 직렬화 컴포넌트로 나눈다.
2. 직렬화되지 않는 타입부터 기능 네임스페이스로 이동하고 컴파일·EditMode 테스트를 고정한다.
3. Business에서 사용하는 TV 판정 계약을 기능 중립 계약으로 추출하고, Rest에는 표시와 휴식 화면 조립만 남긴다.
4. Business의 Bartending 의존은 주문 계약과 제조 구현으로 분리하고, 에피소드 제조 브리지는 상위 조립 영역으로 옮긴다.
5. Economy·진행도 계약을 하위 어셈블리로 고정한 뒤 Bartending, Narrative, Rest, Business 순서로 Runtime `.asmdef`를 추가한다.
6. 마지막에 Core를 애플리케이션 조립 어셈블리로 두고 기능 구현을 참조하게 한다.

각 단계에서는 네임스페이스 변경, `.asmdef` 추가, 씬·Prefab 직렬화 변경을 서로 다른 커밋으로 유지한다.
