import hashlib
import importlib.util
import json
from pathlib import Path

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[1]
spec = importlib.util.spec_from_file_location('prefab_audit', HERE.parent / 'PrefabReadiness20261028/audit_prefabs.py')
audit_module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(audit_module)
audit = audit_module.audit()
(HERE / 'references.json').write_text(json.dumps(audit, indent=2, ensure_ascii=False), encoding='utf-8')
suites = {
    '09-integration': '실제 레시피·등급·최신 조성·온수·제조법·소유권 이동',
    '10-authoring': '프리팹 참조·명시적 연결·반복 생성 보존',
    '11-input': '기존 입력·회전·셰이커 조작',
    '12-runtime': '독립 런타임·디스펜서·Reset',
    '13-auto-f': 'F 자동 실행',
    '14-auto-g': 'G 자동 실행',
    '15-shard-motion': '파편 운동·접촉·액체 보존',
}
rows = []
for attempt, label in suites.items():
    folder = HERE / attempt
    if not (folder / 'exit.json').exists():
        raise SystemExit('Validation is not finished: ' + attempt)
    exit_info = json.loads((folder / 'exit.json').read_text(encoding='utf-8-sig'))
    results = list(folder.glob('*-result.txt'))
    lines = [line for file in folder.glob('*-validation.txt') for line in file.read_text(encoding='utf-8-sig').splitlines()]
    error_log = folder / 'application-errors.txt'
    ok = (exit_info['exitCode'] == 0 and not exit_info['timedOut'] and bool(results)
          and all(file.read_text(encoding='utf-8-sig').strip() == 'PASS' for file in results)
          and not any(line.startswith('FAIL ') for line in lines)
          and (not error_log.exists() or error_log.stat().st_size == 0))
    rows.append({'attempt': attempt, 'label': label, 'result': 'PASS' if ok else 'FAIL',
                 'checks': sum(line.startswith('PASS ') for line in lines)})
preservation = json.loads((HERE / 'preservation.json').read_text(encoding='utf-8'))
manifest = json.loads((HERE / '10-authoring/source-sha256.json').read_text(encoding='utf-8-sig'))
mismatches = [path for path, digest in manifest.items() if hashlib.sha256((ROOT / path).read_bytes()).hexdigest() != digest]
shared = json.loads((HERE / 'shared-sources.json').read_text(encoding='utf-8'))
shared_mismatches = [name for name, value in shared.items()
                     if hashlib.sha256((ROOT / value['source']).read_bytes()).hexdigest() != value['source_sha256']]
result = 'PASS' if all(row['result'] == 'PASS' for row in rows) and not (
    mismatches or shared_mismatches or preservation['unexpected'] or audit['errors']) else 'FAIL'
total = sum(row['checks'] for row in rows)
summary = {'result': result, 'suites': rows, 'passing_checks': total, 'validated_source_files': len(manifest),
           'source_mismatches': mismatches, 'shared_source_mismatches': shared_mismatches,
           'reference_errors': audit['errors'], 'preservation': preservation}
(HERE / 'validation-summary.json').write_text(json.dumps(summary, indent=2, ensure_ascii=False), encoding='utf-8')
table = '\n'.join(f"| {row['attempt']} | {row['label']} | {row['result']} | {row['checks']} |" for row in rows)
report = f"""# 실험 칵테일 판정 연동 — 2026-10-28

## 사용

Unity 메뉴 `Slainte > Fluid GPU Experiment > Open Comparison`에서 F/G **Manual**을 실행한다. 오른쪽 아래 **칵테일 판정 [P]**를 열고 좌우 버튼으로 잔과 요청 레시피를 선택한 뒤 **판정** 또는 **Enter**를 누른다. 요청이 자유 제조이면 기존 자동 탐지를 사용하고, 레시피를 지정하면 기존 Good/MidGlass/MidIce/MidIceGlass/MidWrongMenu/Bad를 표시한다. 판정은 내용물을 소모하지 않으며 결과는 마지막 판정 시점의 기록이다.

Runtime Sample도 기존 Runtime 프리팹을 통해 같은 기능을 상속한다. 패널은 기본적으로 닫혀 있고 A–E 및 자동 비교 시나리오에서는 표시·제조법 관찰을 끈다. UI 위의 클릭은 월드 조작과 뒤쪽 비교 UI에 전달하지 않는다. 기존 NotoSansKR 폰트를 명시적으로 참조한다.

## 구현

- `FluidExperimentCocktailSession`이 기존 Resources 카탈로그와 변경하지 않은 `CocktailEvaluator`/`CocktailOrderEvaluator`를 사용한다. 기존 영업 흐름·보상·레시피 데이터는 수정하지 않았다.
- `TryBuildCocktailComposition`은 최신 GPU 입자·혼합 스냅샷을 동기적으로 읽고 대상 잔 소속만 합산한다. 재료는 ID로 기존 ItemDef에 대응시키므로 80°C 전용 온수 정의도 레시피 재료와 합쳐지며 입자의 실제 온도는 유지된다. 생성 대기·미해결 재료·비정상 용량은 명시적인 기술적 오류이며 이전 결과를 재사용하지 않는다.
- 기법은 기존 `ApplyTechnique` 커널로 현재 액체 입자에만 기록한다. 전체 처리량의 합산 순서 차이로 완료가 누락되지 않도록 조성 총량에 대한 적용 비율을 일치시키며, 새로 넣은 미처리 액체는 완료 이력을 상속하지 않는다.
- 들고 있는 숟가락의 실제 액체 접촉과 상대 운동을 관찰한다. 0.35초에 시도, 1초의 유효 운동과 최대 혼합 편차 0.08 이하의 0.15초 유지로 Stir를 완료하며 짧은 방향 전환에는 0.15초의 여유를 둔다. 닫힌 쉐이커를 들고 2u/s 이상, 유효 시간 1초, 방향 반전 2회를 충족하면 Shake를 기록한다. 실제 얼음 사용 이력과 제출 잔의 현재 얼음은 별개다.
- `SelectGlass`, `SelectRecipe`, `EvaluateTarget`, `Evaluated`를 외부에서 사용할 수 있다. GPU 세대가 바뀌면 결과와 관찰 진행을 초기화하며, 판정 대상이나 요청 변경 시에는 이전 결과를 비운다.

## 보존

변경 전 {preservation['original_files']}개 파일 중 {preservation['unchanged']}개를 바이트 그대로 보존했다. 기존 파일의 변경은 실험의 연결·입력 차단·에디터 검사·문서 8개로 한정했다. 비교 씬과 Runtime 프리팹은 새 세션 컴포넌트 및 참조만 추가했으며, 기존 직렬화 데이터와 Override는 그대로임을 원본 해시 복원 비교로 확인했다. 개체 프리팹, 충돌·물성·유량·ml·얼음·파손·쉐이더, 기존 게임의 판정/영업 소스와 Resources 데이터는 보존했다.

## 검증

Unity 6000.3.5f2, Direct3D 11, RTX 3070. 원본 프로젝트를 열어 저장하지 않고 `Library/FluidModelDHarness`에 복사한 격리 프로젝트에서 실행했다. 공유 판정 코드는 실제 소스 그대로 복사했고, 기존 MonoBehaviour/지갑 코드와 같은 파일에 있던 CocktailComposition, LiquidPayload, GameCurrency enum은 해당 부분을 그대로 추출했다. 전체 원본·추출본 해시는 `shared-sources.json`에 기록했다.

| 실행 | 범위 | 결과 | PASS 수 |
| --- | --- | --- | ---: |
{table}

선정한 {len(rows)}개 실행에서 {total}개 검사가 통과했다. 실제 주문 가능 레시피 22개, 모든 주문 등급, 다른 잔·쏟아진 액체 제외, 온수 ID·온도, 빈 잔·미해결 재료·Reset, 공중 젓기 거절, 열린/닫힌 쉐이커, 드라이/얼음 셰이킹을 확인했다. 셰이킹한 40ml가 열린 허리케인 잔의 입구를 통과해 새 소속을 얻은 뒤에도 Shake와 얼음 사용 이력을 유지하며 현재 얼음은 0개임을 확인했다.

전달 소스 {len(manifest)}개의 해시 불일치는 {len(mismatches)}개, 공유 원본 소스 불일치는 {len(shared_mismatches)}개, 미해결 에셋 참조는 {len(audit['errors'])}개다. 초기 정량 레시피 fixture는 기존 초기 주입 격자에 맞도록 입자당 ml를 지정했다. 이 검사는 판정 데이터의 정확성을 검증하며 사람의 수동 계량·모든 레시피의 실제 제조 난이도·시각적 자연스러움을 보증하지 않는다. 기법 관찰과 입구 통과 검사는 실제 Unity 물리/GPU 틱을 사용했다. 기본 물리 설정을 바꾸거나 사람의 직접 플레이를 수행하지 않았다. 기존 접촉 셰이더 경고와 Unity 종료 진단은 원본 로그에 보존했다.

## 보존한 중간 실패

01은 새 GUI 보조 스크립트의 메타 GUID 길이 오류로 컴파일이 실패했으며 수정했다. 02는 초기 주입 격자 한도, 03은 닫힌 잔에 얼음을 넣으려 한 fixture 문제였다. 04에서는 전량 기법 커버 판정의 부동소수점 누적 순서 차이를 발견해 어댑터에서 보정했다. 05는 반복 생성한 fixture를 등록 해제하지 않아 경계 예산을 초과했다. 06에서는 방향 전환 때 젓기 진행이 매번 초기화되는 문제를 수정하고, 이동 fixture에 네이티브 물리 진행을 추가했다. 07–08은 위치 이동만으로 소속이 바뀔 것으로 가정한 fixture였으며, 기존 규칙대로 열린 잔 입구를 실제로 통과하도록 고쳤다. 기존 소유 규칙·경계 한도·물리·셰이더 기준은 약화하지 않았다. 최종 성공 실행은 09다.

## 재현

`prepare_validation.py`로 최종 실험 에셋과 실제 공유 소스를 격리 프로젝트에 복사한다. 기존 `Run-FloatingContents.ps1`의 Method를 `CocktailIntegrationValidation.Begin` 또는 `CocktailSetupValidation.Begin`으로 지정한다. `run_regressions.ps1`에 기존 입력·런타임·F/G·파편 실행 순서를 보존했다. Attempt는 덮어쓰지 않으므로 재실행할 때 새 경로를 지정한다. `preserve.py audit`와 `summarize_results.py`로 해시·참조·결과를 확인한다.
"""
(HERE / 'report.md').write_text(report, encoding='utf-8')
print(json.dumps({'result': result, 'suites': len(rows), 'passing_checks': total, 'source_files': len(manifest)}))
raise SystemExit(0 if result == 'PASS' else 1)
