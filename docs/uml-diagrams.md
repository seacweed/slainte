# UML 다이어그램

기준일: 2026-08-09

코드리뷰용 PlantUML 원본입니다.

- `uml-usecase.puml`: 플레이어의 하루 흐름, 주문 자동 수락, 잔 전진 제출, Editor 임포트·검증 사용 사례
- `uml-class-diagram.puml`: 코어, 에피소드, 손님 풀, 공용 주문 세션, 바텐딩, RestScene, Editor 데이터 파이프라인의 핵심 관계

Unity, TMPro, UGUI, GraphView와 패키지 캐시 클래스는 외부 기반 타입으로 축약했습니다. 프로젝트 C# 163개를 모두 나열하지 않고 런타임 경계와 리뷰 대상 의존 관계를 우선 표시합니다.

현재 에피소드 제품 경로는 `EpisodeData + EpisodeRunner`입니다. 그래프 편집기는 `EpisodeData` 생성 경로로 사용할 수 있고, `NarrativeManager`의 베이크 JSON 런타임은 별도 경로로 취급합니다.

PlantUML 미리보기를 지원하는 Rider, VS Code 확장 또는 PlantUML 도구에서 `.puml` 파일을 열어 확인합니다.
