# Slainte

Unity 6000.3.5f2와 URP로 제작 중인 에피소드 기반 바텐딩 게임입니다.

## 처음 읽을 문서

- [프로젝트 코드 이해 가이드](docs/project-understanding-guide.md) — 실행 시작점, 씬과 상태, 에피소드·영업·제작 호출 흐름, 데이터 위치, 디버깅 진입점
- [전체 코드 리뷰](docs/code-review-2026-08-05.md) — 현재 결함, 위험도, 수정 우선순위
- [Unity 빌드 안내](docs/core/unity-build.md) — 개발 환경과 빌드 방법

게임의 큰 흐름은 `MainMenu → Episode → Business → Rest → Episode`이며, Episode와 Business는 같은 `BusinessScene`을 공유합니다.
