# 게임 흐름 설계 (Day Flow)

하루는 **항상 영업(Business) 하나**로 진행되고, 종료 후 **정산(Settlement)** 화면을 거쳐 Rest로 돌아갑니다. 플레이어가 그날 할 일을 고르는 작전판은 없습니다.

```
MainMenu → [Day 1] Business → Settlement → Rest → [Day+1] Business → Settlement → Rest → ...
                                   └ 챕터 마지막 날이면 정산 후 엔딩 컷씬 → 메인메뉴
```

## 하루 영업 = 손님 슬롯 N개

- 하루에 손님이 정확히 `BusinessOrderFlowSettings.customersPerDay`(기본 5)명 등장합니다. 제한시간은 없습니다.
- 각 슬롯은 순서대로(1 → N) 처리됩니다. 슬롯 하나 = 랜덤 손님 주문 하나 **또는** 인카운터 에피소드 하나.
- 슬롯 처리 규칙 (`BusinessShiftController` + `DayScheduleResolver`):
  1. 그 (챕터, day, slot)에 배정된 에피소드 후보를 `slotPriority`가 큰 순서로 확인
  2. 미완료이고, 오늘 이미 시도하지 않았고, `triggerCondition`(등장 조건)을 만족하는 **첫 후보 하나**를 실행
  3. 만족하는 후보가 없으면 그 슬롯은 **랜덤 손님**(가중치 + 최근 2명 재등장 제한)으로 채움
- 등장 조건은 **슬롯 차례가 왔을 때** 판정합니다. 같은 날 앞 슬롯 에피소드가 세운 flag가 뒤 슬롯에 바로 반영됩니다.
- 배정 시점에 조건을 만족하지 못한 에피소드는 다른 날·슬롯으로 밀리지 않고 등장하지 않습니다.
- 랜덤 손님 후보가 최근 2명 제한 때문에 없으면 제한을 풀고 다시 뽑습니다(슬롯을 비우지 않기 위해). 손님 풀 자체가 비면 그 슬롯은 오류 로그와 함께 건너뜁니다.
- 고정 손님(특정 캐릭터 강제 등장)도 별도 시스템 없이 짧은 에피소드로 만들어 슬롯에 배정합니다.

## 에피소드 = 일정에 배정된 인카운터

에피소드 타입(Default/Mandatory/Encounter), 해금 조건, 플레이 조건, 선택 조건은 모두 없어졌습니다. 모든 에피소드는 다음 데이터로 등장 시점이 정해집니다.

| 필드 (`EpisodeData`) | CSV `#META` 열 | 의미 |
|---|---|---|
| `scheduledDay` | `day` | 등장 날짜(1부터). 0/빈칸이면 일정 미배정 → 영업에서 자동 등장하지 않음 |
| `scheduledSlot` | `slot` | 그 날의 손님 슬롯 번호(1부터) |
| `slotPriority` | `priority` | 같은 슬롯 후보 간 우선순위(큰 값 먼저, 같으면 episodeId 순) |
| `triggerCondition` | `#TRIGGER` | 등장 조건(AND). 슬롯 차례에 판정 |
| `chapterId` | `chapterId` | 소속 챕터. 현재 챕터와 같거나, 둘 중 하나가 비어 있으면 후보가 됨 |

- 같은 슬롯에 분기별 대안 에피소드(예: 앞 에피소드 결과에 따라 갈리는 A/B)를 여러 개 두고, 각자의 `triggerCondition`(flag 등)으로 하나만 등장하게 만듭니다.
- 에피소드 안의 선택지·제조 결과에 따른 flag/변수 설정과 노드 분기는 그대로 유효합니다.
- 일정 색인은 `EpisodeManager`가 로드 시 한 번 만든 `DayScheduleIndex`(`IDayScheduleSource` 구현)를 사용합니다. 플레이테스트는 같은 인터페이스로 메모리 일정표를 주입합니다.

## 챕터와 엔딩

- `ChapterData`: `chapterId`, `chapterIndex`, `chapterName`, `lastDay`
- `lastDay`(0이면 미사용)인 날의 영업이 끝나면 정산 → **엔딩 컷씬 → 메인메뉴(로고)**. 다음 챕터 전환은 기획 확정 후 이 지점에 연결합니다.
- 기존 규칙도 유지: `DayFlowController.endingEpisodeId`(지구로) 에피소드를 완료한 날도 정산 후 엔딩 컷씬.
- 첫날(메인메뉴 시작)은 `Today` 컷씬 → Day 1 영업(1번 슬롯 `Today` 에피소드), 정산 후 `FathersNote` 컷씬.
- Rest에서 다음 날로는 임시 **다음 날 버튼**(`NextDayButton`)으로 넘어간다([restscene-systems.md](../ui/restscene-systems.md#다음-날-버튼-nextdaybutton)).
- 영업 씬으로 전환될 때 `GameManager`는 씬만 로드하고 모드는 건드리지 않는다. 1번 슬롯 에피소드가 페이드 인 도중 시작되므로, 전환 완료 시점에 `OrderMode`를 강제하면 에피소드 진행이 막힌다.
- `SetCurrentChapter()`는 챕터가 실제로 바뀌면 `currentDay`를 1로 리셋합니다.

## 정산 화면 (Settlement)

셔터가 닫히는 연출 후 모니터 UI를 띄웁니다. 표시 항목: 챕터명, Day, 판매량/팁/실수, 배송 이용, 에피소드 커스텀 보상(`settlementRewards`), 총 소득, 보유 자산. 상세는 [corescene-systems.md](corescene-systems.md) 참고.

## 씬 작업 (사용자 수동)

- `ChapterData_0`에 `lastDay` 값을 입력해야 챕터 엔딩이 동작합니다.
