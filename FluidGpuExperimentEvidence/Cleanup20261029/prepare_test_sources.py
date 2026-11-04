"""Save then adapt the current regression fixtures to the simplified sandbox API."""
from pathlib import Path
import json
import shutil
import zipfile

ROOT = Path(__file__).resolve().parents[2]
HERE = Path(__file__).resolve().parent
feature = 'Assets/_Project/Features/Bartending/FluidGpuExperiment/'
legacy = ROOT / 'Library/FluidModelDHarness/Assets/Editor'
destination = HERE / 'Validation'
destination.mkdir(exist_ok=True)
archive = HERE / 'before-harness-editor.zip'
if archive.exists():
    raise SystemExit('Original harness already saved.')
with zipfile.ZipFile(archive, 'x', compression=zipfile.ZIP_DEFLATED) as saved:
    for source in legacy.glob('*'):
        if source.is_file():
            saved.write(source, source.name)
manifest = json.loads((ROOT / 'FluidGpuExperimentEvidence/CocktailIntegration20261028/10-authoring/source-sha256.json').read_text(encoding='utf-8-sig'))
(HERE / 'shared-dependencies-sha256.json').write_text(json.dumps({p:h for p,h in manifest.items() if not p.startswith(feature)}, indent=2), encoding='utf-8')
names = ('PhysicsLabValidationDispatch', 'CocktailIntegrationValidation', 'SpoonContactValidation',
         'SpoonSoftStirValidation', 'ShardMotionValidation', 'DispenserRuntimeValidation',
         'PrefabReadinessValidation', 'RuntimeAssemblyValidation', 'DesignerValidation', 'RelativePointerValidation')
for name in names:
    source = legacy / (name + '.cs')
    text = source.read_text(encoding='utf-8-sig')
    text = text.replace('comparison.automaticScenario = false; ', '').replace('comparison.automaticScenario=false;', '')
    text = text.replace('comparison.ResetComparison()', 'comparison.ResetSandbox()')
    text = text.replace('comparison.StartScenario(FluidExperimentScenario.Manual)', 'comparison.ResetSandbox()')
    text = text.replace('comparison.ActiveMode==FluidExperimentMode.FCoherentLiquid', 'comparison.Gpu.CohesivePhysicsActive')
    text = text.replace('&& rig.effects != null', '')
    text = text.replace('&& rig.liquid.referenceAccumulationShader != null && rig.liquid.referenceCompositeShader != null', '')
    text = text.replace('&& rig.liquid.referenceAccumulationShader!=null && rig.liquid.referenceCompositeShader!=null', '')
    (destination / source.name).write_text(text, encoding='utf-8')
shutil.copy2(ROOT / 'Library/FluidModelDHarness/Packages/manifest.json', destination / 'manifest.json')
print('Preserved original harness and adapted', len(names), 'current fixtures')
