# Slainte 현재 코드 리뷰 기준

기준일: 2026-08-09
Unity: 6000.3.5f2
범위: 프로젝트 소유 C# 163개, 약 20,425행, 빌드 씬 4개, 주요 ScriptableObject·CSV·저장 경로

이 문서는 현재 작업 공간을 리뷰할 때 사용하는 기준 문서다. 2026-08-05 리뷰는 당시 발견 사항을 보존한 이력이며, 현재 구조와 맞지 않는 주문 수락·거절·고정 순서 설명이 포함되어 있다.

## 1. 현재 런타임 기준선

```text
MainMenu
→ Episode (BusinessScene)
→ Business (같은 BusinessScene)
→ Rest
→ 다음 Episode
```

- 전역 진행과 저장: `GameProgress`, `DataManager`, `SaveData`
- 하루 흐름: `DayFlowManager`, `GameManager`, `SceneTransitionManager`
- 에피소드 실행: `EpisodeData`, `EpisodeRunner`
- 영업 목록: `BusinessSequencePlanner`, `BusinessSequenceRunner`
- 공용 주문 세션: `BusinessOrderSessionController`
- 제조 런타임: `BusinessBartendingBootstrap`, `VesselLiquidTracker`
- 손님 풀: `CustomerVisitData`, `CustomerOrderData`, `CustomerSpawner`
- 휴식 화면: `EpisodeBoardManager`, `ShopUIManager`, `EpisodeUIManager`

주문은 대사 종료 후 자동으로 제조에 들어간다. 수락·거절·포기·제출·버리기 버튼은 사용하지 않으며, 잔을 전방 기준선 너머로 끌면 제출된다.

## 2. 2026-08-05 이후 반영된 주요 변경

| 영역 | 현재 상태 |
|---|---|
| 하루 흐름 | 에피소드 완료 후 같은 BusinessScene에서 영업을 시작하고, 영업 완료 후 날짜를 증가시켜 RestScene으로 이동 |
| 주문 세션 | 영업과 에피소드가 같은 제조·판정 엔진을 사용하고 후처리만 분리 |
| 주문 조작 | 수락·거절·포기·버리기 버튼 제거, 대사 종료 후 자동 수락, 잔 전진 제출 |
| 손님 풀 | 개인·커플·단체 구분 없이 `members[]`, 조건·가중치·재등장 대기·방문 이력 저장 지원 |
| 조건 평가 | 에피소드와 손님 방문이 `ProgressConditionEvaluator` 공유 |
| 레시피 데이터 | 기획 CSV 기본 18종, 배합 확정 15종 주문 가능, 숨은 Mid 변형 87종 생성 |
| 제조법 | 스터 막대와 코블러 셰이커 흔들기 기록 |
| 액체 | 입자별 재료 비율·온도·기법, 용기 소유권, 색상 혼합 |
| 김 | 뜨거운 표면 입자에서 액체 스프라이트를 재사용해 생성 |
| 입력 우선순위 | `BartendingItemOrder`가 겹친 도구의 전면 항목을 판정 |

## 3. 현재 우선 검토 사항

### P1-01. 에피소드 제조 노드 8개에 판정 레시피 ID가 없다

| 에피소드 | 제조 노드 | `craftingRecipeId` 누락 |
|---|---:|---:|
| `StrangeCoin_0` | 1 | 0 |
| `StrangeCoin_1` | 2 | 0 |
| `StrangeCoin_2` | 2 | 2 |
| `StrangeCoin_3` | 3 | 3 |
| `StrangeCoin_4` | 3 | 3 |

누락 노드는 공용 주문 세션의 필수 조건을 통과하지 못해 제조 분기가 실패한다. 기획 레시피 ID를 받은 뒤 CSV 원본과 에셋을 함께 갱신해야 한다.

### P1-02. 휴식 상점 구매가 재고와 돈을 변경하지 않는다

`ItemSlotUI.OnBuyClick()`은 현재 로그만 남긴다. `RestScene.ItemData`, 제조용 `ItemDef`, 술장용 `LiquorBottleDef`도 자동 연결되지 않는다. 구매 기능을 구현할 때 ID 통합, 돈 차감, 재고 증가, 저장을 한 작업으로 다뤄야 한다.

### P1-03. 저장이 비원자적이고 실행 위치를 완전히 복원하지 못한다

`DataManager.Save()`은 JSON을 본 파일에 직접 쓴다. 임시 파일·교체·백업·손상 복구가 없다. 또한 `GameState`, 진행 중인 에피소드 ID와 노드 ID를 저장하지 않아 앱 재실행 시 정확한 중간 지점 복원이 불가능하다.

메인 메뉴는 항상 `StrangeCoin_0`을 요청하며, `DayFlowManager.StartEpisode()`는 완료 여부를 직접 거부하지 않는다. 저장 이어하기 정책을 정한 뒤 첫 진입 경로를 분리해야 한다.

### P1-04. 씬 전환 재진입과 실패 복구가 부족하다

`SceneTransitionManager`에는 전환 중 중복 요청을 막는 상태가 없다. `fadeCanvasGroup`이 없을 때 일부 경로는 null 안전하지 않고, 로드 실패 시 페이드와 입력 차단 상태를 원복하지 않는다.

### P1-05. 바텐딩 커서 상태가 세션 파괴 시 남을 수 있다

`BartendingItemOrder`로 겹친 오브젝트 선택 문제는 완화됐다. 그러나 병·비커·잔은 기울이는 중 오브젝트가 비활성화되거나 제조 세션이 파괴될 때 커서 잠금과 표시 상태를 명시적으로 복원하지 않는다. `StirringRodController`만 `OnDisable()` 정리가 있다.

## 4. 높은 우선순위 안정성 항목

### P2-01. 빈 주문 대사가 영업을 멈출 수 있다

`CustomerSpawner`는 주문 대사가 없으면 `DialogueController.HideImmediate()`만 호출한다. `BusinessOrderSessionController`는 `DialogueClosed`를 받아야 제조를 시작하므로 빈 대사 에셋이 들어오면 `PresentingOrder`에 머물 수 있다. 임포트 검증 또는 즉시 제조 진입 방어가 필요하다.

### P2-02. 주문 시작 실패를 영업 진행기가 처리하지 않는다

`BusinessSequenceRunner`는 `BeginOrder()` 반환값을 확인하지 않는다. 주문 키, 레시피 ID, 필수 씬 참조가 잘못되면 다음 항목으로 이동하거나 복구하지 못한다.

### P2-03. 액체 풀은 초기 크기를 넘으면 계속 생성된다

`LiquidPool`은 큐가 비면 새 입자를 생성한다. 화면 밖 자동 반환이 있지만 총 입자 수 상한은 없다. 장시간 따르기나 반환 지연 상황에서 오브젝트 수가 증가할 수 있으므로 최대 생성 수와 초과 처리 정책이 필요하다.

### P2-04. BGM 교차 페이드 중 정지 처리

교차 페이드 도중 `StopBgm()`을 호출하면 코루틴을 중단하고 현재 `_activeSource`만 페이드아웃한다. 새 클립을 재생 중인 `_inactiveSource`가 남을 수 있다. 두 소스의 상태를 함께 정리해야 한다.

### P2-05. 빌드 입력에 테스트 단축키가 남아 있다

`InputRouter`의 숫자 1·2 테스트 입력과 직렬화된 테스트 에피소드가 제품 빌드 경로에 포함된다. 개발 빌드 조건부 컴파일 또는 별도 QA 컴포넌트로 옮기는 편이 안전하다.

## 5. 구조적 유지보수 항목

- `.asmdef`가 없어 런타임 136개와 Editor 27개가 큰 기본 어셈블리에 묶인다.
- `*Test.cs` 또는 `*Tests.cs` 자동 테스트가 없다.
- `EpisodeData/EpisodeRunner`와 그래프 JSON `NarrativeManager` 경로가 병존한다. 현재 플레이 기준은 전자다.
- `ItemDef`, `LiquorBottleDef`, `RestScene.ItemData`가 분리되어 있다.
- `CustomerOrderData`의 캐릭터 필드는 이전 데이터 호환용이며 새 손님 풀은 `CustomerVisitData.members`를 사용한다.
- 문자열 ID가 시스템 간 외래 키 역할을 하므로 데이터 검증 메뉴가 필수다.

## 6. 검증 기준

현재 문서 갱신 시 확인한 결과:

- C# 런타임·Editor 프로젝트 컴파일: 오류 0개
- 기존 직렬화 필드 미할당 경고: 6개
- `.asmdef`: 0개
- 자동 테스트 파일: 0개

Unity 플레이 검증은 다음 순서로 수행한다.

1. `Slainte > 품질 검증 > 기획 CSV 에셋 검증`
2. `Slainte > 품질 검증 > 손님 풀 검증`
3. `MainMenuScene`에서 에피소드 → 영업 → 휴식 한 사이클
4. `BusinessScene`에서 Good·Mid·Bad와 잔 전진 제출
5. 저장 후 재실행해 돈·명성·재고·영업 index·방문 이력 확인
6. 뜨거운 잔의 김과 Unity ParticleSystem 경고 확인

## 7. 리뷰할 때 먼저 읽을 파일

1. `docs/project-understanding-guide.md`
2. `docs/core/project-code-map.md`
3. `docs/core/architecture.md`
4. `docs/core/scene-structure.md`
5. `docs/uml-class-diagram.puml`
6. `docs/implementation-plan.md`

코드 진입 순서는 `DayFlowManager` → `GameManager` → `EpisodeRunner` → `BusinessFlowBootstrap` → `BusinessOrderSessionController` → `BusinessBartendingBootstrap` → `GameProgress`를 권장한다.
