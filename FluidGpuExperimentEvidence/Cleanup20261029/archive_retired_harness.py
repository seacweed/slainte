"""Preserve the old disposable project's complete non-cache contents before removal."""
from pathlib import Path
import hashlib
import json
import zipfile

ROOT = Path(__file__).resolve().parents[2]
HERE = Path(__file__).resolve().parent
target = (ROOT / 'FluidGpuExperimentEvidence/HarnessProject').resolve()
expected = (ROOT / 'FluidGpuExperimentEvidence').resolve()
if target.parent != expected or target.name != 'HarnessProject':
    raise SystemExit('Unexpected retired project path.')
archive = HERE / 'retired-harness-source.zip'
if archive.exists():
    raise SystemExit('Retired project snapshot already exists.')
all_files = [p for p in target.rglob('*') if p.is_file()]
unique = [p for p in all_files if target / 'Library' not in p.parents]
manifest = {}
with zipfile.ZipFile(archive, 'x', compression=zipfile.ZIP_DEFLATED, compresslevel=3) as saved:
    for path in unique:
        relative = path.relative_to(target).as_posix()
        content = path.read_bytes()
        manifest[relative] = {'bytes': len(content), 'sha256': hashlib.sha256(content).hexdigest()}
        saved.writestr(relative, content)
with zipfile.ZipFile(archive) as saved:
    if saved.testzip():
        raise SystemExit('Retired project snapshot CRC failure.')
result = {'path': str(target), 'files': len(all_files), 'bytes': sum(p.stat().st_size for p in all_files),
          'archived_source_files': len(unique), 'archive_bytes': archive.stat().st_size,
          'crc_ok': True, 'sources': manifest}
(HERE / 'retired-harness-manifest.json').write_text(json.dumps(result, indent=2), encoding='utf-8')
print(json.dumps({k:v for k,v in result.items() if k != 'sources'}))
