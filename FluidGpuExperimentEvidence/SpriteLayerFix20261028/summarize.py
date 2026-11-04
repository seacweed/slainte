import hashlib
import json
from pathlib import Path
import re

ROOT = Path(__file__).resolve().parents[2]
HERE = Path(__file__).resolve().parent
TARGET = ROOT / 'Library/FluidModelDHarness'
FEATURE = ROOT / 'Assets/_Project/Features/Bartending/FluidGpuExperiment'
runs = [
    ('04-rendering', '겹침, 소유 액체 합성, 선택, 부품, 복원, 자원 수명', 39),
    ('05-input', '기존 입력, 회전, 셰이커 조작', 1925),
    ('06-runtime', '독립 Runtime, 디스펜서, Reset', 29),
    ('07-auto-f', 'F 자동 실행', 29),
    ('08-auto-g', 'G 자동 실행', 29),
    ('09-shards', '파편 운동, 접촉, 액체 보존', 76),
    ('10-garnish', '가니시 물리, 소유권, 공급, 모드 전환', 170),
    ('11-cocktails-final', '기존 실제 레시피, 조성, 등급, 제조법, 패널', 61),
    ('12-authoring', '프리팹 참조와 반복 생성 보존', 5),
]

def digest(path): return hashlib.sha256(path.read_bytes()).hexdigest()

summary = {'suites': [], 'source_mismatches': [], 'harness_mismatches': [], 'metadata_errors': []}
final_manifest = json.loads((TARGET / 'source-sha256.json').read_text(encoding='utf-8-sig'))
for relative, expected in final_manifest.items():
    for label, base in (('workspace', ROOT), ('harness', TARGET)):
        if digest(base / relative) != expected:
            summary['source_mismatches'].append(label + ':' + relative)
for attempt, label, expected_checks in runs:
    folder = HERE / attempt
    result_files = list(folder.glob('*result.txt'))
    checks = sum(sum(line.startswith('PASS ') for line in p.read_text(encoding='utf-8-sig').splitlines())
                 for p in folder.glob('*validation.txt'))
    exit_info = json.loads((folder / 'exit.json').read_text(encoding='utf-8-sig'))
    passed = bool(result_files) and all(p.read_text().strip() == 'PASS' for p in result_files)
    passed &= checks == expected_checks and exit_info['exitCode'] == 0 and not exit_info['timedOut']
    source_manifest = json.loads((folder / 'source-sha256.json').read_text(encoding='utf-8-sig'))
    if source_manifest != final_manifest: summary['source_mismatches'].append(attempt + ':manifest')
    harness_manifest = json.loads((folder / 'harness-sha256.json').read_text(encoding='utf-8-sig'))
    for filename, expected in harness_manifest.items():
        if digest(TARGET / 'Assets/Editor' / filename) != expected:
            summary['harness_mismatches'].append(attempt + ':' + filename)
    summary['suites'].append({'attempt': attempt, 'label': label, 'result': 'PASS' if passed else 'FAIL', 'checks': checks})
guids = {}
for path in FEATURE.rglob('*.meta'):
    match = re.search(r'^guid: (\S+)', path.read_text(encoding='utf-8-sig'), re.MULTILINE)
    if not match or not re.fullmatch('[0-9a-f]{32}', match[1]): summary['metadata_errors'].append(str(path))
    elif match[1] in guids: summary['metadata_errors'].append(str(path) + ': duplicate ' + str(guids[match[1]]))
    else: guids[match[1]] = path
summary['preservation'] = json.loads((HERE / 'preservation.json').read_text(encoding='utf-8-sig'))
summary['passing_checks'] = sum(run['checks'] for run in summary['suites'] if run['result'] == 'PASS')
summary['validated_source_files'] = len(final_manifest)
summary['result'] = 'PASS' if all(run['result'] == 'PASS' for run in summary['suites']) and not any(
    summary[key] for key in ('source_mismatches', 'harness_mismatches', 'metadata_errors')) and not summary['preservation']['unexpected'] else 'FAIL'
(HERE / 'validation-summary.json').write_text(json.dumps(summary, indent=2, ensure_ascii=False), encoding='utf-8')
print(json.dumps(summary, indent=2, ensure_ascii=False))
raise SystemExit(summary['result'] != 'PASS')
