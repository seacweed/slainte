"""Preserve the authorized cleanup boundary before editing any existing file."""
from pathlib import Path
import hashlib
import json
import subprocess
import zipfile

ROOT = Path(__file__).resolve().parents[2]
HERE = Path(__file__).resolve().parent
FEATURE = ROOT / 'Assets/_Project/Features/Bartending/FluidGpuExperiment'
EVIDENCE = ROOT / 'FluidGpuExperimentEvidence'
archive = HERE / 'before.zip'
if archive.exists():
    raise SystemExit('Snapshot already exists; refusing to replace it.')
manifest = {}
paths = []
for base in (FEATURE, EVIDENCE, ROOT / 'tmp'):
    for path in base.rglob('*'):
        if not path.is_file() or HERE in path.parents:
            continue
        if EVIDENCE / 'HarnessProject' in path.parents:
            continue
        paths.append(path)
with zipfile.ZipFile(archive, 'x', compression=zipfile.ZIP_DEFLATED, compresslevel=3) as output:
    for path in sorted(set(paths)):
        relative = path.relative_to(ROOT).as_posix()
        content = path.read_bytes()
        manifest[relative] = {'sha256': hashlib.sha256(content).hexdigest(), 'bytes': len(content)}
        output.writestr(relative, content)
with zipfile.ZipFile(archive) as saved:
    bad = saved.testzip()
    if bad:
        raise SystemExit('Snapshot CRC failure: ' + bad)
(HERE / 'before-manifest.json').write_text(json.dumps(manifest, indent=2), encoding='utf-8')
for args, name in ((['status', '--porcelain=v1', '--untracked-files=all'], 'before-git-status.txt'),
                   (['diff', '--binary'], 'before-working-tree.patch'),
                   (['diff', '--cached', '--binary'], 'before-index.patch')):
    result = subprocess.run(['git', *args], cwd=ROOT, stdout=subprocess.PIPE, check=True)
    (HERE / name).write_bytes(result.stdout)
tracked = subprocess.check_output(['git', 'ls-files', '-z'], cwd=ROOT).decode().split('\0')
protected = {}
for relative in tracked:
    if not relative or relative.startswith('Assets/_Project/Features/Bartending/FluidGpuExperiment/') or relative.startswith('FluidGpuExperimentEvidence/'):
        continue
    path = ROOT / relative
    if path.is_file():
        protected[relative] = hashlib.sha256(path.read_bytes()).hexdigest()
(HERE / 'protected-before-sha256.json').write_text(json.dumps(protected, indent=2), encoding='utf-8')
print(json.dumps({'snapshot_files': len(manifest), 'archive_bytes': archive.stat().st_size,
                  'protected_files': len(protected), 'crc_ok': True}))
