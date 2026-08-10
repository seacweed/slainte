# Slainte 문서 진입점

기준일: 2026-08-09

코드리뷰는 다음 순서로 읽습니다.

1. [프로젝트 이해 가이드](project-understanding-guide.md) — 실제 하루 호출 흐름과 데이터 경계
2. [현재 코드 리뷰](code-review-2026-08-09.md) — 현재 위험 항목과 검증 기준
3. [프로젝트 코드 지도](core/project-code-map.md) — 시스템별 클래스와 진입점
4. [아키텍처](core/architecture.md) — 수명, 상태 머신, 의존 방향
5. [씬 구조](core/scene-structure.md) — 정적·런타임 Hierarchy
6. [UML 안내](uml-diagrams.md) — PlantUML 클래스·사용 사례 다이어그램
7. [구현 계획](implementation-plan.md) — 완료 상태와 다음 작업

`code-review-2026-08-05.md`는 당시 상태를 보존한 과거 리뷰입니다. 최신 판단에는 사용하지 않고 이력 비교가 필요할 때만 참고합니다.

Markdown은 Rider 또는 VS Code의 미리보기로 열고, `.puml` 파일은 PlantUML 미리보기 확장으로 확인합니다. 전체 플레이 흐름은 Unity에서 `Assets/MainMenuScene.unity`, 주문·제조 단독 확인은 `Assets/BusinessScene.unity`를 실행합니다.
