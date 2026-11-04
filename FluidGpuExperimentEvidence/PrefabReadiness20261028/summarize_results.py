"""Build the delivery report from saved validation evidence, including retained failures."""
import json
import hashlib
from pathlib import Path

BASE = Path(__file__).resolve().parent
EXPECTED = {
    '02-authoring': '프리팹·씬 참조, ID 상속, Designer, 읽기 전용 검사',
    '07-runtime-bindings': '독립 런타임, 카메라·환경·디스펜서 연결과 Reset',
    '04-input': '기존 입력·회전·셰이커 조작',
    '05-auto-f': 'F 자동 실행과 디스펜서',
    '06-auto-g': 'G 자동 실행과 디스펜서',
    '08-shard-motion': '현재 파편 운동·충돌·액체 보존',
}
rows = []
for attempt, label in EXPECTED.items():
    directory = BASE / attempt
    results = list(directory.glob('*-result.txt'))
    exit_path = directory / 'exit.json'
    if not results or not exit_path.exists():
        raise SystemExit('Validation has not completed: ' + attempt)
    exit_info = json.loads(exit_path.read_text(encoding='utf-8-sig'))
    logs = list(directory.glob('*-validation.txt'))
    lines = [line for path in logs for line in path.read_text(encoding='utf-8-sig').splitlines()]
    application_errors = directory / 'application-errors.txt'
    passed = (exit_info['exitCode'] == 0 and not exit_info['timedOut']
              and all(p.read_text(encoding='utf-8-sig').strip() == 'PASS' for p in results)
              and not any(line.startswith('FAIL ') for line in lines)
              and (not application_errors.exists() or application_errors.stat().st_size == 0))
    rows.append({'attempt': attempt, 'label': label, 'result': 'PASS' if passed else 'FAIL',
                 'assertions': sum(line.startswith('PASS ') for line in lines), 'exit': exit_info})
legacy_failures = []
for attempt in ('03-runtime', '03b-original-prefabs-control'):
    lines = (BASE / attempt / 'runtime-assembly-validation.txt').read_text(encoding='utf-8-sig').splitlines()
    legacy_failures.append([line for line in lines if line.startswith('FAIL ')])
control_matches = bool(legacy_failures[0]) and legacy_failures[0] == legacy_failures[1]
preservation = json.loads((BASE / 'preservation.json').read_text(encoding='utf-8'))
source_manifest = json.loads((BASE / '07-runtime-bindings/source-sha256.json').read_text(encoding='utf-8-sig'))
workspace = BASE.parents[1]
source_mismatches = [path for path, digest in source_manifest.items()
                     if hashlib.sha256((workspace / path).read_bytes()).hexdigest() != digest]
summary = {'result': 'PASS' if all(r['result'] == 'PASS' for r in rows)
           and preservation['result'] == 'PASS' and control_matches and not source_mismatches else 'FAIL',
           'suites': rows, 'passed_assertions': sum(r['assertions'] for r in rows),
           'validated_feature_files': len(source_manifest), 'source_mismatches': source_mismatches,
           'legacy_runtime_failure_reproduced_with_original_prefabs': control_matches,
           'legacy_failures': legacy_failures, 'preservation': preservation}
(BASE / 'validation-summary.json').write_text(json.dumps(summary, indent=2, ensure_ascii=False), encoding='utf-8')
table = '\n'.join(f"| {r['attempt']} | {r['label']} | {r['result']} | {r['assertions']} |" for r in rows)
report = f"""# 프리팹 설정·참조 정리 — 2026-10-28

판정 연동을 위한 잔 식별 메타데이터와 읽기 전용 참조 검사를 추가했다. 칵테일 판정 호출은 아직 연결하지 않았다.

## 변경

- 잔 4종에 `servingGlassId`: `rock`, `highball`, `hurricane`, `martini`. World의 실행 중 개체 `Id`와 구분한다.
- Designer의 Recipe identity에서 편집하고, 비어 있는 잔 ID는 저장 전 검증에서 거절한다.
- `Slainte > Fluid GPU Experiment > Validate Prefab References`: 카탈로그, 33개 개체·지원 프리팹, 저장된 비교·샘플 씬의 필수 참조를 읽기 전용으로 검사한다.
- 기존 28개 카탈로그 항목과 프리팹의 재료 참조는 일치했다. 불필요한 에셋 교체·카탈로그 ID 변경은 하지 않았다.

## 보존

변경 전 파일 해시 기준 {preservation['baseline_file_count']}개 중 {preservation['unchanged_original_file_count']}개는 바이트 그대로 보존했다. 기존 파일의 의도한 변경은 잔 프리팹 4개, Designer 코드 2개, README다. 새 코드 파일은 메타데이터 partial과 Editor 검사 도구다.

`preservation.json`은 잔 프리팹에서 ID 줄만 제외하면 원본 해시와 일치함을 확인한다. 두 씬과 18개 의미 있는 Override, 물리·입력·셰이더·재료·용량·유량·충돌·파손·디스펜서 설정, 기존 영업/판정 소스는 보존했다. 비교 씬의 허리케인 스플래시 비움과 샘플 씬의 자연 스플래시·카메라·환경·초기 채움을 유지했다. 80°C 전용 `DispenserHotWater` 참조도 유지했다.

## Unity 검증

Unity 6000.3.5f2, Direct3D 11, RTX 3070. `Library/FluidModelDHarness`에 복사한 격리 프로젝트에서 실행했으며 원본 프로젝트를 Unity로 열거나 저장하지 않았다.

| 실행 | 범위 | 결과 | PASS 검사 수 |
| --- | --- | --- | ---: |
{table}

성공한 {len(rows)}개 실행에서 총 {summary['passed_assertions']}개 검사를 통과했다. 전달한 실험 파일 {len(source_manifest)}개의 해시는 검증 소스와 일치하며 불일치는 {len(source_mismatches)}개다. 정적 참조 검사도 미해결 GUID 0개이며 `preservation.json`은 {preservation['result']}다. 자동 실행·GPU·에셋 보존의 증거이며 사람의 직접 조작감 평가를 대신하지 않는다. 기존 접촉 셰이더 경고와 Unity 종료 시 할당 진단은 원본 로그에 보존했으며 경고 없는 실행을 주장하지 않는다.

## 별도로 보존한 실패

- `01-authoring`: sandbox의 Unity 라이선스 IPC 연결 실패 뒤 Unity가 종료됐다. IPC 접근을 허용한 `02-authoring`은 컴파일·참조 검증을 통과했다.
- `03-runtime`: 기존 RuntimeAssemblyValidation의 natural 파편 회전 기대값에서 실패했다.
- `03b-original-prefabs-control`: 잔 프리팹 4개를 변경 전 바이트와 일치하도록 복원한 대조에서도 같은 항목이 실패했다. 현재 파손 코드는 충돌 전 회전을 보존하지만 이 이전 검사는 충돌 후 회전과 비교한다. 이번 ID 추가로 발생한 회귀와 구분했으며, 물리 코드나 이전 검사 기준은 수정하지 않았다. 현재 파편 운동은 `08-shard-motion`으로 검증했다.
- `before-audit.json`의 초기 미해결 GUID 표시는 Assets만 검색해 URP 패키지 참조를 제외한 진단이었다. PackageCache까지 확인한 `after-audit.json`은 누락 0개이며 해당 에셋은 교체하지 않았다.

## 재현

`python FluidGpuExperimentEvidence/PrefabReadiness20261028/prepare_validation.py`로 최종 소스를 격리 프로젝트에 동기화한다. `Run-FloatingContents.ps1`의 Method에 `PrefabReadinessValidation.Begin`을 지정하면 프리팹 검사를 실행한다. `run_regressions.ps1`은 기존 런타임·입력·F/G·파편 검사를 순서대로 실행한다. 각 Attempt는 덮어쓰지 않으므로 재실행 시 새 경로를 지정한다. `audit_prefabs.py audit`, `verify_preservation.py`, `summarize_results.py`로 정적 검사와 보고서를 재생성한다.
"""
(BASE / 'report.md').write_text(report, encoding='utf-8')
print(json.dumps({'result': summary['result'], 'suites': len(rows), 'passed_assertions': summary['passed_assertions'],
                  'legacy_control_matches': control_matches}, ensure_ascii=False))
raise SystemExit(0 if summary['result'] == 'PASS' else 1)
