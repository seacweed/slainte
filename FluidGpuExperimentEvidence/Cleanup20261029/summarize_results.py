"""Build a factual report, retaining failed attempts and parity limitations."""
from pathlib import Path
import csv
import json
import zipfile

ROOT = Path(__file__).resolve().parents[2]
HERE = Path(__file__).resolve().parent
audit = json.loads((HERE / 'final-audit.json').read_text(encoding='utf-8'))
static = json.loads((HERE / 'gpu-cleanup-static-audit.json').read_text(encoding='utf-8'))
removed = json.loads((HERE / 'generated-copies-removed.json').read_text(encoding='utf-8-sig'))
prefix = 'Assets/_Project/Features/Bartending/FluidGpuExperiment/'
extensions = {'.cs','.hlsl','.shader','.compute'}
with zipfile.ZipFile(HERE / 'before.zip') as before:
    old_lines = sum(sum(bool(line.strip()) for line in before.read(name).decode('utf-8-sig').splitlines())
                    for name in before.namelist() if name.startswith(prefix) and Path(name).suffix in extensions)
new_lines = sum(sum(bool(line.strip()) for line in path.read_text(encoding='utf-8-sig').splitlines())
                for path in (ROOT / prefix).rglob('*') if path.suffix in extensions)
def rows(name):
    with (HERE / name / 'parity-particles.csv').open(encoding='utf-8') as source:
        return list(csv.DictReader(source))
baseline, current, repeat = (rows(name) for name in ('00-before-parity-permitted','01-after-parity','02-after-parity-repeat'))
fields = ('x','y','previousX','previousY','vx','vy','ml','temperature')
def differences(first, second):
    return {field:max(abs(float(a[field])-float(b[field])) for a,b in zip(first,second)) for field in fields}
initial = [(a,b) for a,b in zip(baseline,current) if a['tick']=='0']
parity = {'rows_per_run':len(baseline), 'initial_rows':len(initial), 'initial_exact_equal':all(a==b for a,b in initial),
          'before_after_max_absolute_difference':differences(baseline,current),
          'same_current_source_repeat_max_absolute_difference':differences(current,repeat),
          'long_trajectory_exact_equal':baseline==current,
          'interpretation':'Initial sampled state matches. Later particle trajectories vary even on repeated identical current source. Exact long-run equality is not established.'}
(HERE / 'parity-analysis.json').write_text(json.dumps(parity,indent=2),encoding='utf-8')
table = '\n'.join('| ['+item['name']+']('+item['attempt'].split('/')[-1]+'/) | '+str(item['pass_checks'])+' | PASS |'
                  for item in audit['suites'])
diagnostics = json.loads((HERE / '03-cleanup-runtime/cleanup-stationary-ice-diagnostics.json').read_text(encoding='utf-8'))
report = f'''# F 샌드박스 정리 결과 — 2026-10-29

승인된 정리 범위를 구현했다. F 응집 액체를 하나의 실행 경로로 남기고 A–E 비교 물리·선택과 별도 G 모드를 제거했다. 현재 스푼 젓기와 판정·제작 도구·개체 동작은 유지했다. 검증은 원본 프로젝트를 Unity로 열거나 저장하지 않고 `Library/FluidModelDHarness`에서 수행했다.

## 변경

- A PBF, B–D SPH, E 전용 보정·저장고·효과와 이전 표면/리본 표시를 제거했다. 컴퓨트 진입점은 41개에서 28개, 설정 필드는 40개 감소했다. F의 수치 알고리즘과 재료 점성·응집 계수는 보존했다.
- 샌드박스는 Material, 얼음 충돌, Natural/Explosive 파손, Reset만 제공한다. `FluidExperimentComparison` 이름과 씬 GUID는 저장된 배치·컴포넌트 참조를 보존하기 위해 유지했다. 구형 모드 enum은 직렬화 필드와 함께 제거했다.
- RuntimeRig와 판정에서 비교 제어기 의존성을 제거했다. 런타임·환경 빌더는 직접 설정·셰이더·재질을 연결하며 비교 씬을 열지 않는다.
- 자동 재현과 벤치마크는 [DevelopmentClient](../DevelopmentClient/README.md)로 옮겼다. 일반 실행에는 opt-in 계측 훅만 남겼다. 지원되는 검증 준비 진입점은 현재 F용 검사만 복사하며 과거 모드 코드를 다시 활성화하지 않는다.
- 런타임에서 읽지 않는 `garnishLiquidMotionTransfer`와 편집 항목, 직렬화 잔여 값을 제거했다. 참조가 없던 `ComparisonBreakageSplash.asset`은 백업 후 제거했다.
- 기능 폴더는 meta 제외 {audit['feature_nonmeta_before']}개에서 {audit['feature_nonmeta_after']}개 파일, C#/HLSL/shader/compute의 비어 있지 않은 줄은 {old_lines:,}개에서 {new_lines:,}개로 감소했다. 원문 개발 계획과 설명은 [History](History/)에 보관했다.

## 보존과 복구

[final-audit.json](final-audit.json)은 기존 게임·공용 소스·아트·설정 등 보호 파일 {audit['protected_files']:,}개의 변경이 0개임을 확인한다. 프리팹 33개, 카탈로그 28개 항목, 보존된 meta {audit['preserved_metadata']}개의 바이트/GUID를 유지했다. 최종 기능 파일은 마지막 격리 검증 manifest와 모두 일치한다.

현재 스푼의 액체 비충돌·겹침 기반 혼합·약한 흐름·벽 겹침 대기, 얼음 두 모드·가니쉬·디스펜서·쉐이커·파편 접촉·칵테일 판정과 Designer를 유지한다. 비교 씬의 파손 스플래시 비움과 샘플의 자연 스플래시·카메라·환경·초기 채움 구성을 유지했다.

- `before.zip`: 정리 전 기능·검증 이력·tmp의 15,386파일, CRC 검사 통과. `before-manifest.json`과 Git 상태·작업 트리/인덱스 패치로 기존 미커밋 상태를 구분한다.
- `before-harness-editor.zip`: 교체 전 검증 프로젝트의 Editor 원문.
- `retired-harness-source.zip`: 삭제한 옛 검증 프로젝트의 비캐시 파일 363개 전부. 해시와 CRC 검사를 통과했다.
- 위 로컬 복구 ZIP은 Evidence의 ignore 규칙으로 Git 자동 추가 대상에서 제외했다. 실패 기록과 해시별 `HarnessSnapshots`는 원위치에 보존했다.

옛 `FluidGpuExperimentEvidence/HarnessProject` {removed['removedProjectFiles']:,}파일, {removed['removedProjectBytes']:,}바이트(약 1.58GiB)와 원본 해시가 확인된 tmp 5파일을 제거했다. [삭제 기록](generated-copies-removed.json)에 대상과 복구본을 남겼다. 현재 검증 프로젝트는 Unity 재생성 데이터 위치인 Library 아래에 둔다.

## Unity와 GPU 검증

Unity 6000.3.5f2, Direct3D 11, NVIDIA GeForce RTX 3070. 현재 기준 11개 검사군, 총 **{audit['passed_checks']}개 검사 통과**, 모든 최종 프로세스 종료 코드 0. 이동한 선택적 개발 클라이언트도 격리 프로젝트에 포함해 컴파일했다.

| 검사군 | PASS 검사 수 | 최종 결과 |
| --- | ---: | --- |
{table}

별도 [GPU 정적 보존 검사](gpu-cleanup-static-audit.json)는 124개 검사를 통과했다. F 응집·접촉 수식, 물리 dispatch 순서, 스푼·회계·전달·파편·경계 코드, 남은 설정 기본값, 주요 커널 본문·바인딩을 정리 전 복구본과 비교한다. 실제 [자연 파손 전 렌더](04-runtime/runtime-natural-before.png)와 [파손 후 렌더](04-runtime/runtime-natural-after.png)도 생성해 확인했다. 오프스크린 출력의 증거이며 사람의 조작 평가와 구분한다.

## 정리 전후 수치와 관측 한계

[parity-analysis.json](parity-analysis.json)의 동일한 고정 잔·60ml·3재료 프리셋 GPU fixture에서 첫 단계 360개 입자 기록은 정리 전후 정확히 일치했다. 각 실행의 1,080개 기록에서 ml와 온도 차이는 0이었다. 장기 입자 궤적은 정확히 일치하지 않았다. 변경 후 같은 소스의 반복에서도 위치·속도 차이가 관측됐으므로 완전한 장기 궤적 일치나 그 차이의 원인 확정을 주장하지 않는다.

바닥에 지지된 고정 잔과 중력이 켜진 동적 얼음 1개·3개를 각각 5초간 매 틱 관측했다. 양·성분 유한성·잔 자세·벽 침투 조건을 통과했다. 속도·수면 상승·에너지·회전 최대값은 [진단 원문](03-cleanup-runtime/cleanup-stationary-ice-diagnostics.json)에 기록했으며 자연스러움 합격으로 바꾸지 않았다. 사람의 직접 플레이, 일반 장면의 수면 자연스러움, 전체 성능은 이번 정리로 검증하지 않았다.

접촉 셰이더의 레지스터·루프 관련 기존 컴파일 경고는 로그에 남아 있다. 경고 없는 실행을 주장하지 않는다.

## 보존한 실패와 보정

- `00-before-parity`: 샌드박스 WMI·라이선스 IPC 제한으로 진행하지 못한 실행. 동일한 격리 작업을 허용된 권한으로 실행한 `00-before-parity-permitted`는 종료 코드 0이었다.
- `03-authoring`: 비교 씬 없이 실제 재생성은 성공했지만, 초기 빈 배치 씬의 복원 fixture가 실패했다. 유효한 초기 SceneManagerSetup만 복원하도록 검사 코드를 고친 `04-authoring`은 정확한 에셋 바이트 복원까지 통과했다.
- `03-runtime`: 과거 RuntimeAssembly 검사의 natural 파편 회전 기대값에서 실패했다. 이전 [프리팹 작업 보고서](../PrefabReadiness20261028/report.md)에서도 원본 대조로 확인된 항목이다. 물리 코드를 바꾸지 않고 검사 대상 속도를 현재의 충돌 전 운동·측정 충격으로 계산된 fracture 속도로 바로잡은 `04-runtime`은 Natural/Explosive·액체 장부·테이블 접촉·초기화까지 통과했다. 최초 실패 로그는 유지했다.

## 재현

원본 Unity Editor가 검증 프로젝트를 점유하지 않는 상태에서 `python FluidGpuExperimentEvidence/HarnessSource/prepare_experiment.py` 또는 `python FluidGpuExperimentEvidence/Cleanup20261029/prepare_validation.py`로 현재 파일과 지원 fixture를 복사한다. 준비기는 폐기 가능한 대상의 오래된 기능·검사 코드를 먼저 제거하므로 퇴역 코드가 남지 않는다. 공유 의존성은 [독립 manifest](shared-dependencies-sha256.json)로 검증하며 이전 실행 폴더를 요구하지 않는다.

`python FluidGpuExperimentEvidence/Cleanup20261029/run_regressions.py --prefix NEW`로 새 실행 경로에 검사한다. 기존 Attempt를 덮어쓰지 않는다. 로컬 Unity 라이선스 IPC에 필요한 프로세스 권한은 환경에 따라 확보해야 한다. 마지막 파일·보호 범위·검사 결과는 `audit_final.py`, 본 보고서는 `summarize_results.py`로 다시 생성한다.
'''
(HERE / 'report.md').write_text(report,encoding='utf-8')
index = HERE.parent / 'README.md'
text = index.read_text(encoding='utf-8-sig')
marker = '## Current F-only sandbox cleanup (2026-10-29)'
if marker not in text:
    first, rest = text.split('\n',1)
    current_section = f'''\n\n{marker}

The active experiment is a single cohesive-liquid sandbox. A–E choices and the separate G mode were removed; Natural/Explosive remains a breakage option. Current spoon stirring, judging, 33 prefabs, the 28-entry catalog and Designer remain active. The old generated HarnessProject was removed after source archival and regeneration checks. Current isolated validation: 11 suites / {audit['passed_checks']} passing checks; older failed attempts are retained and explained in the report.

Use `python FluidGpuExperimentEvidence/HarnessSource/prepare_experiment.py` to prepare the supported current fixtures in `Library/FluidModelDHarness`. Historical sources below describe their recorded versions and should not be compiled indiscriminately against current APIs. [Cleanup report](Cleanup20261029/report.md), [final audit](Cleanup20261029/final-audit.json), [development clients](DevelopmentClient/README.md).

'''
    index.write_text(first+current_section+rest,encoding='utf-8')
print('Report generated:', HERE / 'report.md')
