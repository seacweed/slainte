# 스푼 상하 반전

공용 실험용 Spoon.prefab의 루트 Y 스케일을 -1로 변경해 스푼의 오목한 머리가 아래를 향하도록 했다. 그림·클릭 영역·네이티브 충돌체·GPU 액체 경계에 같은 변환을 적용하며 원본 PNG와 충돌 프로파일은 유지한다. 잔 내부 표시 순서를 결정하는 끝점 선택도 스케일 부호를 반영하도록 수정했다. 비교 씬과 Runtime Sample의 프리팹 상속을 확인했다.

Unity 6000.3.5f2 / RTX 3070 / D3D11 격리 프로젝트 검증: 방향·네이티브/GPU 충돌·표시·집기/회전/놓기 46개, 실제 Stir 접촉과 기존 칵테일 판정 61개, 저장된 참조·반복 생성 보존·두 씬 상속 7개, 총 **114개 PASS**. 각 실행 exit 0, timeout false. 테스트의 스푼 배치 끝점만 새 상하 방향에 맞췄으며 젓기 조건과 평가 기대값은 유지했다. 사람의 직접 플레이 감각은 별도다.

변경 전 1,335개 파일 중 기존 2개만 수정했고 나머지 1,333개는 동일하다. PhysicsLab·기존 게임 Runtime·원본 그림·레시피·프로파일·씬·프로젝트 설정은 보존했다. 최종 소스 495개가 원본과 격리 검증 프로젝트에서 일치한다. 실행별 소스/harness 해시, 로그, 결과와 종료 코드를 보관했다.

[이전 방향](A:/UnityHub/proj/slainte/FluidGpuExperimentEvidence/SpoonFlip20261028/01-orientation/00-spoon-original-direction.png), [수정 후 실제 렌더](A:/UnityHub/proj/slainte/FluidGpuExperimentEvidence/SpoonFlip20261028/01-orientation/00-spoon-flipped-direction.png), [삽입한 스푼](A:/UnityHub/proj/slainte/FluidGpuExperimentEvidence/SpoonFlip20261028/01-orientation/06-tools-own-liquid-fixed.png), [검증 결과](A:/UnityHub/proj/slainte/FluidGpuExperimentEvidence/SpoonFlip20261028/validation-summary.json).
