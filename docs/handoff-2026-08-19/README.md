# Slainte 작업 인수인계 — 2026-08-19

이 문서는 다른 기기에서 현재 작업을 이어가기 위한 시작점이다. 작성자나 작업 Agent를 추정하지 않고, 아래 기준 커밋과 현재 작업 트리의 실제 차이만 정리했다.

## 기준 상태

- 기준 커밋: `789b875f031833659733fccb41843a77a9018c06`
- 짧은 해시: `789b875`
- 커밋 시각: `2026-08-18T02:41:52+09:00`
- 커밋 제목: `feat: 영업 보상, 배송, 손님 데이터 및 TV 시스템 확장`
- 문서 작성 전 변경량: 추적 파일 79개 수정, 미추적 파일 31개
- 추적 파일 diff: 2,390줄 추가, 722줄 삭제
- 이 인수인계 문서 자체는 위 숫자에 포함하지 않았다.
- 현재 변경은 아직 커밋하지 않았다.

## 가장 중요한 현재 상태

1. 액체 성능, 계량 UI, 최종 RGBA 출력, 손님/영업, 배송, Serving Area, 슬롯 및 병/잔 이동 변경이 한 작업 트리에 함께 있다.
2. 메타볼 렌더러는 새 누적/합성 경로까지 구현됐지만 육안 완료 상태가 아니다.
3. 최신 확인에서 단일 입자도 가장자리만 선명하고, 입자가 겹칠수록 현상이 심해졌다.
4. 새 합성 수식상 같은 RGBA의 단일 입자 내부는 균일해야 하므로, 실행 중인 Unity가 수정 전 씬/재질을 유지했는지 먼저 확인해야 한다.
5. `threshold`를 낮추는 것은 현재 증상의 근본 해결로 확정되지 않았으며 보류 상태다.
6. 외부 CSV 원본 3개는 저장소 밖 `Downloads`에만 있다. 다른 기기로 별도 복사해야 한다.
7. 미추적 Unity 에셋과 `.meta`는 커밋하거나 작업 폴더 전체를 복사하지 않으면 다른 기기로 전달되지 않는다.

## 문서 순서

- [01_WORKTREE_OVERVIEW.md](01_WORKTREE_OVERVIEW.md): 전체 변경 요약과 검증 상태
- [02_LIQUID_RENDERING_AND_PERFORMANCE.md](02_LIQUID_RENDERING_AND_PERFORMANCE.md): 액체 성능, Pool, 색/Alpha, 메타볼 현황
- [03_BARTENDING_UI_AND_INTERACTIONS.md](03_BARTENDING_UI_AND_INTERACTIONS.md): 계량 UI, RGBA 출력, Serving Area, 슬롯 및 용기 이동
- [04_CUSTOMER_BUSINESS_DELIVERY.md](04_CUSTOMER_BUSINESS_DELIVERY.md): 손님 데이터, 영업 선택, 주문 검증, 배송 변경
- [05_DATA_IMPORT_NOTES.md](05_DATA_IMPORT_NOTES.md): 외부 CSV 분석 및 아직 적용하지 않은 데이터 작업
- [06_NEXT_DEVICE_CHECKLIST.md](06_NEXT_DEVICE_CHECKLIST.md): 다른 기기에서 재개하는 순서
- [07_FILE_INVENTORY.md](07_FILE_INVENTORY.md): 기준 커밋 이후 변경 파일 목록

## 검증 상태

- `dotnet build Assembly-CSharp.csproj --no-restore`: 오류 0개
- 경고 6개: 기존 `CS0649` 직렬화 필드 경고이며 이번 액체 렌더러 변경과 직접 관련 없음
- Unity Editor 로그: 신규 메타볼 셰이더 2개 임포트 오류 없음
- Play Mode 육안 완료 검증: 미완료
- Profiler 전후 수치 비교: 미완료
- 정확한 성능 수치는 측정하지 않았으며 문서에도 추정 수치를 쓰지 않았다.

## 주의

- 작업 트리에 다른 병렬 작업 변경이 포함되어 있으므로 파일을 되돌리거나 전체 포맷팅하지 말 것.
- Git의 LF/CRLF 경고가 다수 발생한다. 내용 변경 없이 줄바꿈만 대량 변경하지 말 것.
- Unity 씬이 열린 상태에서 외부 편집된 `Sample_Scene.unity`가 런타임 Hierarchy에 반영되지 않았을 수 있다. 다음 기기에서는 씬을 새로 연 뒤 검사할 것.

