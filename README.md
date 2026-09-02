# Slainte

Unity 6000.3.5f2와 URP로 제작 중인 에피소드 기반 바텐딩 게임입니다.

## 처음 읽을 문서

- [프로젝트 폴더 구조 규약](docs/refactoring/folder-structure-conventions.md) — Core·Shared·Feature 소유권과 새 파일 배치 기준
- [아키텍처](docs/core/architecture.md) — 씬, 상태, 주문 세션, 데이터 흐름
- [구현 계획](docs/implementation-plan.md) — 현재 완료 범위와 다음 작업
- [Unity 빌드 안내](docs/core/unity-build.md) — 개발 환경과 검증 메뉴

게임의 큰 흐름은 `MainMenu → Episode → Business → Rest → Episode`이며, Episode와 Business는 같은 `BusinessScene`을 공유합니다.
