# Unity 빌드 & 개발

이 프로젝트는 Unity 프로젝트입니다. 실제 플레이·씬·에셋 검증은 Unity 에디터에서 진행합니다. IDE 지원은 프로젝트 루트의 `slainte.sln` 또는 Unity가 생성한 C# 프로젝트를 사용합니다.

- Unity 버전: **6000.3.5f2**
- 렌더 파이프라인: **Universal Render Pipeline 17.3.0**
- 목표 해상도: **2560×1440**

## 실행 진입점

- 전체 하루 흐름: `Assets/MainMenuScene.unity`
- 주문·제조 단독 확인: `Assets/BusinessScene.unity`
- 휴식 UI 단독 확인: `Assets/RestScene/RestScene.unity`
- `CoreScene`은 `CoreSceneAutoLoader`가 추가 방식으로 불러오므로 일반적으로 직접 실행할 필요가 없습니다.

## 품질 검증 메뉴

- `Slainte > 품질 검증 > 기획 CSV 에셋 검증`
- `Slainte > 품질 검증 > 손님 풀 검증`
- 바텐딩 세로 절단·에셋 검증 메뉴는 `Assets/Editor/BartendingSystemValidator.cs`와 `BusinessFlowSceneSetup.cs`를 기준으로 확인합니다.

현재 `.asmdef`와 Unity Test Framework 테스트 파일은 없습니다. IDE 컴파일 성공만으로 씬 직렬화, Resources 참조, 실제 물리·입력 동작을 검증할 수 없으므로 Unity 플레이 검증이 필요합니다.
