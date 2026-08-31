# 게임 흐름 설계 (Day Flow)

하루가 **영업(Business) 또는 에피소드(Episode)** 중 하나로 진행되고, 종료 후 **정산(Settlement)** 화면을 거쳐 Rest로 돌아가는 구조. 실제 구현 전 설계 확정 단계이며, 파일 단위 구현 계획은 별도로 진행합니다.

## GameState 확장

```
None, Rest, Business, Episode, Settlement
```

- `Business` — 실제 영업(칵테일 제작, 손님 등장, 인카운터 대사 교체 등)은 별도 담당자 구현 영역. 구현 전까지는 **스킵 버튼만 있는 스텁 화면**으로 대체해 전체 흐름을 테스트 가능하게 함
- `Episode` — 기존 `EpisodeRunner` 흐름 그대로 사용 (기본/필수 에피소드 공통)
- `Settlement` — 신규. 셔터+모니터 연출 후 `Rest`로 전환

## RestScene 분기 로직

```
RestScene
 ├─ 미완료 필수(Mandatory) 에피소드가 있으면(목표일 하루 전부터 lookahead로 미리 감지)
 │    → "에피소드 선택" 비활성(실제 구현은 사진 자체를 숨기지 않고 노출은 하되 선택해도 진행이 안 되도록 막는 방식 — 최신 동작은 [restscene-systems.md](../ui/restscene-systems.md#에피소드-보드-시스템) 참고), "영업 시작"만 노출
 │    → 영업 시작 시, 큐의 "다음 필수 에피소드 1개" 확인
 │         - mandatorySlot == BeforeBusiness → [그 에피소드] → Business(Stub) → Settlement
 │         - mandatorySlot == AfterBusiness  → Business(Stub) → [그 에피소드] → Settlement
 │       (필수 에피소드는 하루에 최대 1개만 소진 — Before/After가 같은 날 동시에 나오지 않음)
 │
 └─ 없으면 플레이어가 선택
      ├─ 영업 → Business(Stub) → Settlement
      └─ 기본(Default) 에피소드 보드에서 선택 → Episode → Settlement
 → Settlement 종료 → Rest 복귀
```

필수 에피소드 큐의 순서는 기존 `EpisodeTriggerCondition.prerequisiteEpisodeIds` 체이닝으로 보장 (별도 순서 필드 불필요).

## 에피소드 타입

| 타입 | 설명 |
|---|---|
| `Default` | Rest에서 플레이어가 직접 선택해 진행하는 일반 에피소드. 완료 후에도 정산 화면이 뜬다. `triggerCondition`(해금 조건)과 `playCondition`(플레이 조건)이 분리되어 있음 — 해금되면 작전판에 노출되고, 그중 플레이 조건까지 만족해야 Play 버튼이 활성화됨(플레이 조건이 없으면 해금 즉시 플레이 가능) |
| `Mandatory` | 튜토리얼 등 강제 진행용. 챕터마다 반복 등장 가능. `mandatorySlot(Before/AfterBusiness)`로 영업 전/후 위치 지정. 모든 필수 에피소드가 끝나기 전까지는 Rest에서 에피소드를 직접 선택할 수 없음. 해금/플레이 조건 구분 없이 `triggerCondition` 하나만 사용 |

`Encounter`(영업 중 특정 손님 대사 교체)는 이 파이프라인 밖입니다 — 다른 담당자가 별도의 경량 데이터 구조로 처리하며, `EpisodeData`/`EpisodeRunner`와는 무관합니다.

## 챕터와 에피소드 시리즈

챕터 1개 안에 여러 에피소드 시리즈(예: "이상한 동전", "아이들", "소각장 블루스")가 있고, 각 시리즈는 `_0, _1, _2...` 부(部) 단위 `EpisodeData`로 구성됩니다. 챕터명은 정산 화면 표시 및 챕터별 필수 에피소드 반복에 필요하므로 별도 SO로 관리합니다.

- 신규 `ChapterData` SO: `chapterId`, `chapterIndex`, `chapterName`

## 데이터 모델 변경사항

- **`EpisodeData`**: `episodeType(Default/Mandatory)`, `mandatorySlot(Before/AfterBusiness, Mandatory 전용)`, `chapterId` 필드 추가
- **`EpisodeTriggerCondition`**: `requiredCustomerAppearances: List<{characterId, count}>` 추가 — 손님 등장 횟수 기반 해금 (예: 히미코 3번 등장 시 해금)
- **`EpisodeNode`**: `episodeBranches: List<{requiredCompletedEpisodeId, nextNodeId}>` 추가 — 특정 에피소드 클리어 여부에 따른 노드 분기(`flagBranches` 다음, `varBranches` 이전에 평가). "A 에피소드 클리어 후 B 진행 시 내용이 바뀐다"를 별도 플래그 세팅 없이 지원. 그래프 에디터에서는 엣지 라벨에 별도 접두사 없이 에피소드 ID를 그대로 적으면 됨(예: `StrangeCoin_0`) — `flag == true`/`var >= 5` 형식(연산자 포함)이 아니고 `Next`/`Default`도 아닌 라벨은 전부 에피소드 완료 조건으로 해석
- **`GameProgress`**:
  - 손님 등장 횟수 저장 (affinity와 동일한 key/value 리스트 패턴)
  - `currentChapterId` — `SetCurrentChapter()` 호출 시 chapterId가 실제로 바뀌면 `currentDay`를 1로 리셋(구현 완료). 챕터가 바뀌는 시점(특정 이벤트) 자체는 이번 설계 범위 밖. 최초 게임 시작 시 `MainMenuManager`가 비어있으면 `ChapterData.LoadFirst()`(chapterIndex 최솟값)로 채움
  - `currentMoney` — 누적 보유 금액 (세이브에 영속)
  - 당일 집계용: `dayDrinkSalesCount`, `dayDrinkRevenue`, `dayTotalIncome` (음료 수익 외 다른 수입원 확장을 고려해 총소득을 별도 필드로 분리). 정산 시 `currentMoney += dayTotalIncome` 후 리셋
- **`EpisodeManager`**: 챕터 스코프로 "다음 필수 에피소드 1개"를 반환하는 조회 메서드 추가

## 정산 화면 (Settlement)

셔터가 닫히는 연출 후 모니터 UI를 띄우는 형태. 영업일/에피소드일 관계없이 **동일한 레이아웃**을 사용하고, 해당 없는 항목은 0 또는 생략으로 표시합니다.

표시 항목:
- 챕터명
- Day 수
- 음료 판매 개수
- 음료 판매 수익
- 그날의 총 소득
- 현재 보유 금액 (누적, 그날 소득 포함)

## 미결 사항 / 향후 통합 지점

- `currentChapterId` 전환 트리거 로직 — 게임 진행 중 챕터가 실제로 바뀌는 지점(특정 에피소드 완료 등)은 미정. 최초 게임 시작 시 기본 챕터 설정과 챕터 변경 시 Day 1 리셋은 구현 완료
- `Business` 상태는 스텁 구현 — 실제 영업 시스템은 별도 담당자가 통합
- `Encounter` 데이터 구조/트리거 — 별도 담당자 소관, 이 문서에서 다루지 않음

## 구현 상태

코드 레벨(Phase 1~7)은 구현 완료. 씬(Unity 에디터) 작업은 별도로 필요:
- `SettlementUI`/`SettlementManager`를 CoreScene의 영속 Canvas(페이드 캔버스와 같은 곳)에 배치하고 셔터/모니터 텍스트 필드 연결
- `BusinessStubUI`를 BusinessScene에 배치하고 스킵 버튼 연결
- `EpisodeUIManager` 필드명이 `toggleButton→startBusinessButton`으로 바뀌어 RestScene 인스펙터에서 버튼 재연결 필요
- `ChapterData` SO 에셋을 `Assets/Resources/Narrative/Chapters/`에 생성 완료(`ChapterData_0`, chapterId `Sector0`). `ChapterData.LoadFirst()`가 chapterIndex 최솟값 챕터를 반환하며 `MainMenuManager`가 새 게임 시작 시 이를 사용
- 그래프 에디터 인스펙터 UI에 `episodeType/mandatorySlot/chapterId` 편집 필드 노출은 아직 미반영 (현재는 CSV 또는 EpisodeData 에셋 직접 편집으로 설정 가능)
