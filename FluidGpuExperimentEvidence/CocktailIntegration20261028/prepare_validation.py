"""Copy final assets and the actual shared evaluator sources into the disposable Unity project."""
import hashlib
import json
from pathlib import Path
import shutil

ROOT = Path(__file__).resolve().parents[2]
HERE = Path(__file__).resolve().parent
TARGET = ROOT / 'Library/FluidModelDHarness'
FEATURE = Path('Assets/_Project/Features/Bartending/FluidGpuExperiment')
if not (TARGET / 'ProjectSettings/ProjectVersion.txt').exists():
    raise SystemExit('The existing disposable validation project is required')
manifest, shared = {}, {}

def copy(relative):
    source = ROOT / relative
    destination = TARGET / relative
    destination.parent.mkdir(parents=True, exist_ok=True)
    shutil.copyfile(source, destination)
    manifest[Path(relative).as_posix()] = hashlib.sha256(source.read_bytes()).hexdigest()

for source in (ROOT / FEATURE).rglob('*'):
    if source.is_file(): copy(source.relative_to(ROOT))
for name in ('CocktailEvaluator', 'CocktailOrderEvaluator', 'CocktailRecipeCatalog', 'CocktailRecipeDef',
             'CocktailRecipeDataLoader', 'CocktailOrderCatalog', 'ItemDefCatalog'):
    relative = Path('Assets/_Project/Features/Bartending/Runtime') / (name + '.cs')
    copy(relative); copy(Path(str(relative) + '.meta'))
copy(Path('Assets/_Project/Shared/Runtime/Content/ProjectRuntimeContentPaths.cs'))
copy(Path('Assets/_Project/Shared/Runtime/Content/ProjectRuntimeContentPaths.cs.meta'))
for folder in ('Assets/Resources/Bartending/Items', 'Assets/Resources/Bartending/Recipes'):
    for source in (ROOT / folder).rglob('*'):
        if source.is_file(): copy(source.relative_to(ROOT))
for relative in ('Assets/TextMesh Pro/Fonts/NotoSansKR-Regular.ttf', 'Assets/TextMesh Pro/Fonts/NotoSansKR-Regular.ttf.meta'):
    copy(Path(relative))

# These classes share files with legacy MonoBehaviours/wallet code. Extract the unchanged class
# text, rather than replacing the shared evaluation/color semantics with validation stubs.
def extracted(relative, output, end_marker):
    source = ROOT / relative
    text = source.read_text(encoding='utf-8-sig')
    generated = text[:text.index(end_marker)] + '\n}\n'
    destination = TARGET / 'Assets/ValidationShared' / output
    destination.parent.mkdir(parents=True, exist_ok=True)
    destination.write_text(generated, encoding='utf-8')
    shared[output] = {'source': relative, 'source_sha256': hashlib.sha256(source.read_bytes()).hexdigest(),
                     'generated_sha256': hashlib.sha256(destination.read_bytes()).hexdigest(), 'end_marker': end_marker}

extracted('Assets/_Project/Features/Bartending/Runtime/Liquid/Vessels/VesselLiquidTracker.cs',
          'CocktailComposition.cs', '    // 병/잔/비커')
extracted('Assets/_Project/Features/Bartending/Runtime/Liquid/Particles/LiquidParticleData.cs',
          'LiquidPayload.cs', '    public sealed class LiquidParticleData')
extracted('Assets/_Project/Core/Runtime/Economy/GameCurrency.cs',
          'GameCurrency.cs', '    public static class GameCurrencyWallet')
for source in (ROOT / 'FluidGpuExperimentEvidence/HarnessSource').glob('*.cs'):
    shutil.copyfile(source, TARGET / 'Assets/Editor' / source.name)
for source in HERE.glob('*Validation.cs'):
    shutil.copyfile(source, TARGET / 'Assets/Editor' / source.name)
(TARGET / 'source-sha256.json').write_text(json.dumps(manifest, indent=2), encoding='utf-8')
(HERE / 'shared-sources.json').write_text(json.dumps(shared, indent=2, ensure_ascii=False), encoding='utf-8')
print(json.dumps({'copied_files': len(manifest), 'extracted_actual_sources': len(shared)}))
