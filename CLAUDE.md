# CLAUDE.md

이 파일은 Claude Code(claude.ai/code)가 이 저장소에서 작업할 때 참고하는 안내 문서입니다.

## 반드시 지켜야 할 점

- 코드 내에 한글 사용 금지
- 후에 다양한 기능이 추가될 수 있으므로 OOP 기반 설계, 확장성 고려한 코드 작성
- 계획부터 말하고 승인 받은 후에 작업 진행
- 최적화를 고려한 코드 작성
- claude.md 업데이트 시 프로젝트 전체를 아우르는 중심 내용만 이 파일에 작성, 세부 사항들은 docs의 개별 문서에 작성. 필요시 새로운 문서 생성하고 claude.md에 링크 추가.

## 프로젝트 개요

**Slainte**는 Unity 2023.3.5f2(URP)로 제작 중인 내러티브 바텐딩 게임입니다. 이름은 아일랜드어로 "건배"를 뜻합니다. 에피소드 기반의 비주얼 노벨식 스토리텔링과 드래그-드롭 바텐딩 메커니즘을 결합한 게임입니다.

**게임 진행 흐름**: Episode(BusinessScene) → Rest(RestScene) → Episode → Rest → ...

## 아키텍처 핵심

- **싱글톤**: `MonoSingleton<T>` 통일 (`GameProgress`, `GameModeManager`, `EpisodeManager`, `DataManager`, `GameManager`, `AudioManager`)
- **런타임 상태 Source of Truth**: `GameProgress` (flags, completedEpisodeIds, vars, currentDay)
- **에피소드 데이터**: `EpisodeData` 단일 SO — `Resources/EpisodeData/`에 배치, `EpisodeManager`가 일괄 로드
- **씬 전환**: `GameManager` + `SceneTransitionManager` (Additive, 페이드)
- **게임 상태**: `GameState.Episode` / `GameState.Rest`


## 문서

필요한 섹션만 읽어 컨텍스트 부하를 줄이세요.

| 문서 | 내용 |
|---|---|
| [docs/corescene-systems.md](docs/corescene-systems.md) | CoreScene 매니저 구조, 게임 흐름, 저장/로드 |
| [docs/restscene-systems.md](docs/restscene-systems.md) | RestScene UI 시스템 (에피소드 보드, 상점, 현황판, 툴팁) |
| [docs/architecture.md](docs/architecture.md) | 에피소드 실행 엔진, 캐릭터, 드래그드롭, 대화 시스템 |
| [docs/scene-structure.md](docs/scene-structure.md) | 씬 계층 구조 (Canvas, Panel, GameObject) |
| [docs/episode-csv-guide.md](docs/episode-csv-guide.md) | 에피소드 CSV 작성법 (섹션 구조, 열 설명, 예시) |
| [docs/editor-tools.md](docs/editor-tools.md) | 에디터 툴 목록 및 사용법 |
| [docs/unity-build.md](docs/unity-build.md) | Unity 버전, 빌드 방법, 개발 환경 |
