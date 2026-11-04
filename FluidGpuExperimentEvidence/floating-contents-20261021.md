# F/G 얼음 모드·내용물 관성·가니쉬 표시 수정

2026-10-21 / 기준 커밋 `3d538dce16e9fcb32b32464ace40f086f5e4a60b` / Unity 6000.3.5f2 / Windows / Direct3D11.
검증은 격리 프로젝트 `Library/FluidModelDHarness`에서 실행했다.

## 사용 방법

F/G 패널의 **Ice / liquid**에서 두 얼음 버전을 선택한다.

| 선택 | 부력·유속 저항 | 얼음의 GPU 액체 충돌 | 얼음 액체 표시 마스크 |
| --- | --- | --- | --- |
| `No collision` (기본) | 적용 | 제외 | 제외 |
| `Collision` | 적용 | 적용 | 적용 |

가니쉬는 두 선택 모두 액체를 밀지 않는 단방향 반응을 사용한다. 얼음·가니쉬와 잔 벽, 다른 고체의 Physics2D 접촉은 유지한다. 전환은 액체·강체·생성 대기 상태를 초기화하지 않으며 R과 F/G 변경 후에도 선택을 유지한다. 재사용 구성은 `FluidExperimentRuntimeRig.iceLiquidMode`, 직접 구성은 `FluidExperimentGpuLiquid.iceLiquidMode`로 선택한다. A–E의 기존 얼음 충돌·운반·해제 정책은 유지한다.

`Collision`은 기존 얼음 접촉 반응을 남긴 비교 버전이다. 아래 여러 얼음의 큰 액체 튐이 이 버전에서 모두 해결됐다고 주장하지 않는다. 정확한 강체–유체 운동량 교환을 새로 구현한 것도 아니다. `No collision`은 해당 반작용을 생성하지 않는다.

## 원인과 변경

- **잡힌 잔에 붙던 얼음:** `TransportContainedIce`가 잔의 이동·회전을 얼음 자세에 직접 전달했고 해제 때 잔 속도도 더했다. F/G의 독립 강체는 두 경로에서 제외했다. 완료된 월드 공간 액체 스냅샷의 잠김·상대 유속으로 부력과 저항을 힘으로 받는다. 과거 스냅샷을 새 잔 자세에 붙이거나 속도를 잔 각도만큼 회전시키지 않는다. 일반 적분·회전·중력·접촉은 동적 Rigidbody2D가 처리한다. 초기 기울어진 잔을 집어 세울 때의 일회성 좌표계 보정과 빠른 벽 횡단 복구는 유지한다.
- **놓기에서 액체 속도 초기화:** 실제 클릭의 `Drop → TryResolveRelease → SwapContents`는 위치 보정이 없어도 액체 속도를 0으로 만들고 이전 위치를 덮어썼다. 수정 전 `20261021-held-contents-moving-release`에서 액체 `(1.2, -0.4)`가 놓는 순간 `(0, 0)`으로 바뀌었다. F/G는 `TranslateContentsPreservingMotion`을 사용해 보정이 없으면 상태를 유지한다. 보정이 있으면 활성/GPU 예약 입자의 두 위치 이력과 CPU 대기 위치만 같은 양만큼 이동한다. 속도·성분·ml·온도·상태·예약 시각은 보존하며 잔 속도를 복사하거나 더하지 않는다.
- **얼음이 액체를 크게 튀김:** 부력·저항을 공유한 뒤에도 얼음에만 액체를 밀어내는 GPU 경계가 남아 있었다. 이를 선택 가능한 충돌 버전으로 분리했다. 비충돌 버전은 가니쉬와 같은 단방향 액체 반응이다.
- **가니쉬 한 프레임 표시 이탈:** 보간 위치가 실제 강체 위치보다 늦었고, 물리 후 위치 보정도 Transform 동기화 이후 실행돼 표시에는 이전 위치가 남았다. F/G의 잡힌 잔과 내부 강체에 한해 보간을 잠시 해제하고 LateUpdate에서 완료 자세를 표시한다. 속도·상대 위치를 잔에 동기화하지 않는다. 놓거나 범위에서 벗어나면 이전 보간 설정을 복원한다. 보간 해제만 적용한 `garnish-render-fixed-01`은 실패했고 완료 자세 동기화까지 추가해 재검사했다.

`Liquid Settings > Floating solids`: 상대 밀도 얼음 0.917 / 가니쉬 0.87, 완전 잠김 저항 8/s / 6/s, 회전 저항 4/s, 최대 스냅샷 나이 0.25초, 최대 예측 시간 0.1초. 얼음에는 가니쉬 긴 축 복원 토크를 적용하지 않는다. 2D 잠김 근사이며 열교환·용융은 포함하지 않는다.

## 재현 수치

정지한 60ml 잔에 세 얼음을 순서대로 넣은 `20261021-rest-before-02`에서 액체 최대 속도 15.09u/s, 최고점 상승 9.25u, 잔량 57.5ml, 마지막 얼음 높이 변화 0.402u가 관측됐다. 비충돌 최종 검사 `20261021-solid-rest-v2`는 F/G 모두 60ml를 유지했다. 세 개 투입의 액체 최대 속도는 F 0.095u/s / G 0.104u/s, 최고점 상승 0.0089u / 0.0094u, 마지막 높이 변화 0.00038u / 0.00033u였다. 액체만, 단일 투입, 얼음·가니쉬 혼합 투입도 검사했다. 잠긴 고체가 부력으로 수면 쪽으로 움직이는 동작은 유지한다.

빠른 왕복 이동의 `20261021-garnish-render-fast-before-02`는 표시 윤곽이 벽 바깥으로 0.0497u, 표시 위치 지연 0.1396u인 프레임을 검출했다. 최종 Synth Lemon 4,163프레임과 Nananga 3,606프레임에서는 한 소스 픽셀을 넘는 벽 밖 표시가 검출되지 않았다. 최대 위치 지연은 0.00423u / 0.000131u였다. 이전 보간 설정 복원과 클릭 해제의 관성 보존도 같은 실행에서 확인했다.

## 최종 실행

종료 코드·PASS/FAIL 개수·소스 범위·격리 복사본 일치는 [floating-contents-audit.json](floating-contents-audit.json)에 기록한다.

최종 10개 실행은 총 4,580개 검사 통과, 실패 0, 종료 코드 모두 0이다. 전달 에셋과 파일 메타데이터 362개가 격리 복사본과 일치한다. 준비 스크립트가 복사하지 않고 Unity가 별도로 생성하는 폴더 메타데이터 15개는 감사 목록에서 명시적으로 분리했다. 원본 폴더·파일 GUID에는 변경이 없다.

| 실행 | 범위 |
| --- | --- |
| `20261021-ice-mode-variants-02` | F/G × 두 얼음 모드의 실제 GPU 충돌/통과·마스크·전환 상태·선택 유지·부력·저항·해제 관성 |
| `20261021-floating-contents-v2` | 실제 클릭 해제·활성/GPU 예약/CPU 대기 액체·두 위치 이력/속도/성분/ml/온도/시각·독립 운동·실제 젖은 잔 이동·D 정책 |
| `20261021-solid-rest-v2` | F/G 비충돌 모드의 액체만·단일/세 얼음·혼합 투입, 액체 속도·수면 상승·안정화·잔량 |
| `20261021-garnish-render-lemon` / `-nananga` | 자동 FixedUpdate·Physics2D·비동기 읽기, 빠른 잔 이동과 표시 프레임 윤곽, 해제 관성과 보간 복원 |
| `20261021-floating-garnish-v2` | 두 가니쉬의 집기·놓기·회전·유속·다른 잔·젖은 잔 이동·얼음 혼합·리셋 |
| `20261021-floating-pickup-v2` / `-input-v2` | 기울어진 잔 집기·입력·회전/복귀·부품 회귀 |
| `20261021-floating-dispenser-v2` / `-breakage-v2` | 디스펜서·재고·경계 한도·F/G 전환·허리케인 파손 회귀 |

수치는 각 실행 폴더의 `*-validation.txt`/`held-contents-diagnosis.txt`, `floating-contents-timeline.csv`, `floating-render-timeline.csv`, `exit.json`에 저장한다. 표시 검사에서는 매 프레임의 Transform 윤곽을 실제 잔 내부 윤곽과 비교하고 그 상태를 오프스크린 렌더링했다. 사람의 조작감·모든 Game View 상황·성능 검증은 아니다. 잡힌 잔과 내부 강체 표시를 완료 물리 틱에 맞춘 범위에서는 프레임 보간을 사용하지 않는다.

실패·진단 실행은 보존하고 통과 합계에서 제외한다. 배치에서 카메라 콜백이 호출되지 않은 초기 실행, 오목한 잔 벽에 부적합한 convex SAT 측정은 유효 증거에서 제외했다. 최종 측정은 실제 내부 윤곽과 열린 입구를 구분한다. 첫 모드 검사의 지연 생성 표면 버퍼 초기화 누락도 검사 픽스처에서 수정했다. 이전 플레이 루프의 비활성 객체 자세 불일치와 소속 획득 순서 실패도 해당 폴더에 남아 있다.

이전 `20261021-floating-breakage` 실행에는 Direct3D11 장치 오류 `0x887A0005`와 Unity 비정상 종료 기록이 있다. 통과 합계에는 포함하지 않았다. 최종 `20261021-floating-breakage-v2`는 새 프로세스에서 파손 207개 검사를 통과하고 종료 코드 0으로 완료했다.

원본 씬·프리팹·그림·충돌 프로파일·GUID·전역 설정·영업용 백엔드는 수정하지 않았다. 기존 C# 직렬화 필드 및 접촉 셰이더 경고는 남아 있다.

## 다시 실행

저장소 루트에서 실행한다. Unity 실행은 순차로 완료를 확인하며 실행 중 소스 복사/재임포트를 하지 않는다. 실행 이름은 재사용하지 않는다.

```powershell
python FluidGpuExperimentEvidence/HarnessSource/prepare_experiment.py --target Library/FluidModelDHarness
powershell -NoProfile -ExecutionPolicy Bypass -File FluidGpuExperimentEvidence/Run-FloatingContents.ps1 -Attempt <새로운-이름> -Method FloatingContentsValidation.BeginModes
powershell -NoProfile -ExecutionPolicy Bypass -File FluidGpuExperimentEvidence/Run-FloatingContents.ps1 -Attempt <다른-이름> -Method FloatingContentsValidation.BeginRest
powershell -NoProfile -ExecutionPolicy Bypass -File FluidGpuExperimentEvidence/Run-FloatingContents.ps1 -Attempt <또-다른-이름> -Method FloatingContentsPlayLoopValidation.BeginNananga
python FluidGpuExperimentEvidence/HarnessSource/audit_floating_contents.py
```
