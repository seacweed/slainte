"""Run current isolated D3D11 suites sequentially and retain every attempt."""
from pathlib import Path
import argparse
import json
import shutil
import subprocess

ROOT = Path(__file__).resolve().parents[2]
HERE = Path(__file__).resolve().parent
parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--prefix', default='03')
args = parser.parse_args()
suites = [
    ('authoring', 'ExperimentCleanupRuntimeValidation.BeginAuthoring'),
    ('prefabs', 'PrefabReadinessValidation.Begin'),
    ('cocktail', 'CocktailIntegrationValidation.Begin'),
    ('spoon', 'SpoonContactValidation.Begin'),
    ('soft-stir', 'SpoonSoftStirValidation.Begin'),
    ('dispenser', 'ExperimentDispenserRuntimeValidation.Begin'),
    ('shards', 'ExperimentShardMotionValidation.Begin'),
    ('runtime', 'ExperimentRuntimeAssemblyValidation.Begin'),
    ('designer', 'ExperimentDesignerValidation.Run'),
    ('designer-play', 'ExperimentDesignerValidation.RunPlayTest'),
    ('cleanup-runtime', 'ExperimentCleanupRuntimeValidation.Begin'),
]
pwsh = shutil.which('pwsh')
if not pwsh:
    raise SystemExit('PowerShell 7 is required.')
results = []
for name, method in suites:
    attempt = 'Cleanup20261029/' + args.prefix + '-' + name
    command = [pwsh, '-NoProfile', '-File', str(ROOT / 'FluidGpuExperimentEvidence/Run-FloatingContents.ps1'),
               '-Attempt', attempt, '-Method', method, '-TimeoutSeconds', '240']
    result = subprocess.run(command, cwd=ROOT, text=True, capture_output=True)
    print(name + ': exit=' + str(result.returncode), flush=True)
    if result.stdout.strip():
        print(result.stdout.strip(), flush=True)
    if result.stderr.strip():
        print(result.stderr.strip()[-2000:], flush=True)
    results.append({'name': name, 'method': method, 'attempt': attempt, 'exit_code': result.returncode})
    (HERE / (args.prefix + '-suite-results.json')).write_text(json.dumps(results, indent=2), encoding='utf-8')
raise SystemExit(0 if all(result['exit_code'] == 0 for result in results) else 1)
