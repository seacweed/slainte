# CLAUDE.md

이 파일은 Claude Code(claude.ai/code)가 이 저장소에서 작업할 때 참고하는 안내 문서입니다.

## 반드시 지켜야 할 점

- 코드 내에 한글 사용 금지
- 후에 다양한 기능이 추가될 수 있으므로 OOP 기반 설계, 확장성 고려한 코드 작성
- 계획부터 말하고 승인 받은 후에 작업 진행
- 최적화를 고려한 코드 작성

## 프로젝트 개요

**Slainte**는 Unity 2023.3.5f2(URP)로 제작 중인 내러티브 바텐딩 게임입니다. 이름은 아일랜드어로 "건배"를 뜻합니다. 에피소드 기반의 비주얼 노벨식 스토리텔링과 드래그-드롭 바텐딩 메커니즘을 결합한 게임입니다.

## 문서

필요한 섹션만 읽어 컨텍스트 부하를 줄이세요.

| 문서 | 내용 |
|---|---|
| [docs/unity-build.md](docs/unity-build.md) | Unity 버전, 빌드 방법, 개발 환경 |
| [docs/scene-structure.md](docs/scene-structure.md) | 씬 계층 구조 (Canvas, Panel, GameObject) |
| [docs/architecture.md](docs/architecture.md) | 핵심 시스템, 데이터 패턴, 설계 패턴 |
