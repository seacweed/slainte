# PhysicsLab 실행 증거

## 최종 상태

`07-final-isolation-validation/validation.txt`: 모든 검사 통과. `exit.json`: Unity 프로세스 종료 코드 0. Unity 6000.3.5f2, NVIDIA RTX 3070, Direct3D11에서 실행했다. `initial-sandbox.png`, `pour-transfer.png`는 실제 카메라 출력이다. Batch 프레임 간격은 GPU 처리 시간 또는 게임 FPS 측정값이 아니다.

## 실행 이력

| 폴더 | 판정 |
| --- | --- |
| 00-startup-failures | 제한된 파일 접근 환경에서 Unity 시작 실패 3회. 개별 dump, exception 요약, 관측한 콘솔 오류와 명령 보존. 정상 완료로 취급하지 않음 |
| 01-prefab-build | 정상 캐시 접근 권한으로 전환 후 생성/컴파일 완료. Play Mode 검증은 아님 |
| 02-playmode-validation | 종료 1. 도구 타격 fixture가 실제 콜라이더 중심과 어긋나 쓰러짐 기준 미달 |
| 03-controlled-impact-validation | 종료 0. 콜라이더 기준 타격으로 수정한 기존 검사 통과 |
| 04-geometry-transfer-validation | 종료 1. 외부 Teleport 후 회전 시작 각도 불일치. 화면 캡처의 액체 그리기 시점도 추가 조사 |
| 05-render-lifecycle-validation | 종료 0. 각도 재취득/카메라별 그리기 수정 후 확장 검사 통과 |
| 06-settled-liquid-validation | 종료 0. 윤곽 기반 액체 소유권과 가라앉은 액체 이동 보존까지 최종 통과 |
| 07-final-isolation-validation | 종료 0. 에셋 저장 범위를 PhysicsLab으로 한정한 최종 생성·Play Mode 검사 통과 |

`Run-Unity.ps1`은 실행별 command.json/Editor.log/stdout/stderr/exit.json을 만들고 실행 중 새로 생성된 충돌 폴더를 복사한다. 같은 attempt 이름을 거부하고 재실행하지 않는다. 초기 3회는 이 래퍼 도입 전 실패여서 확보하지 못한 로그/exit code는 없다고 명시했다. 덤프 전체를 각각 심볼 분석한 것으로 주장하지 않는다.

원본 로그·스크린샷·덤프와 시작 전 ProjectSettings 사본은 이 작업 폴더에 보존한다. 커밋에는 재현 스크립트, 판정 보고서, 명령·종료 코드, 원인 분석과 증거 해시 목록만 포함한다. 프로젝트 설정 사본과 큰 원시 실행 자료는 커밋하지 않는다.

커밋용 실패 보고서에서 행 끝 공백을 정리한 경우 바이트 그대로의 `validation.raw.txt`를 별도로 보존했다. 원시 증거는 `raw-evidence-sha256.json`, 기존 파일 보존과 최종 검사 수는 `isolation-verification.json`에서 확인한다.
