"""Read-only serialized reference checks; does not claim Unity or GPU execution."""
from pathlib import Path
import hashlib
import json
import re

ROOT = Path(__file__).resolve().parents[1]
BARTENDING = ROOT / "Assets/_Project/Features/Bartending"
OUTPUT = ROOT / "PlaytestEvidence/GpuCraftingIntegration20261104"

def read(path):
    return path.read_text(encoding="utf-8-sig")

def field(text, name):
    match = re.search(r"^  " + re.escape(name) + r": (.+)$", text, re.M)
    assert match, name
    return match.group(1).strip()

def main():
    by_guid = {}
    for meta in (ROOT / "Assets").rglob("*.meta"):
        match = re.search(r"^guid: ([a-f0-9]{32})$", read(meta), re.M)
        if match:
            path = Path(str(meta)[:-5])
            assert match.group(1) not in by_guid, ("duplicate GUID", meta)
            by_guid[match.group(1)] = path

    checked = set()
    def reference(text, key):
        value = field(text, key)
        match = re.search(r"fileID: (\d+), guid: ([a-f0-9]{32})", value)
        assert match and match.group(2) in by_guid, ("unresolved", key, value)
        path = by_guid[match.group(2)]
        assert path.exists(), path
        if path.suffix in (".prefab", ".asset"):
            assert re.search(r"^--- !u!\d+ &" + match.group(1) + r"$", read(path), re.M), ("missing file ID", path, value)
        checked.add(path)
        return path

    settings = ROOT / "Assets/Resources/Bartending/BusinessBartendingSettings.asset"
    gpu_settings = reference(read(settings), "gpuCrafting")
    runtime = reference(read(gpu_settings), "runtimePrefab")
    text = read(runtime)
    for name in ("drawShader", "improvedSurfaceCompute", "improvedAccumulationShader", "improvedCompositeShader", "liquidSettings", "catalog"):
        reference(text, name)
    catalog = reference(text, "catalog")
    bottles = []
    glasses = set()
    kinds = set()
    for entry in read(catalog).split("  - id:")[1:]:
        entry_text = "  - id:" + entry
        prefab_match = re.search(r"    prefab: (.+)", entry_text)
        assert prefab_match
        match = re.search(r"guid: ([a-f0-9]{32})", prefab_match.group(1))
        assert match and match.group(1) in by_guid
        prefab = by_guid[match.group(1)]
        body = read(prefab)
        category = re.search(r"    category: (.+)", entry_text).group(1).strip()
        if category == "Bottle":
            ingredient = reference(body, "ingredient")
            bottles.append(field(read(ingredient), "id"))
            assert float(field(body, "capacityMl")) > 0
        if category == "Glass": glasses.add(field(body, "servingGlassId"))
        kinds.add(category)
        checked.add(prefab)
    assert len(bottles) == 15 and len(set(bottles)) == 15, bottles
    assert glasses == {"rock", "highball", "hurricane", "martini"}, glasses
    assert {"Jigger", "Shaker", "Spoon", "Ice"} <= kinds, kinds
    garnish_ids = re.findall(r"  - inventoryId: (.+)", read(gpu_settings))
    assert set(garnish_ids) == {"garni_1001", "garni_1002"}, garnish_ids
    for match in re.finditer(r"    prefab: \{fileID: (\d+), guid: ([a-f0-9]{32}), type: 3\}", read(gpu_settings)):
        prefab = by_guid[match.group(2)]
        assert re.search(r"^--- !u!114 &" + match.group(1) + r"$", read(prefab), re.M), prefab
        assert field(read(prefab), "kind") == "7", prefab
        checked.add(prefab)

    archive = ROOT / "docs/legacy/BusinessBartendingBootstrap.before-gpu.cs.txt"
    assert hashlib.sha256(archive.read_bytes()).hexdigest() == "c104b6661c041a8607591fd4b180a2af0cbb353649da959e688a8848bb2b3cdd"
    checked.update((BARTENDING / "Runtime/Integration").glob("*.cs"))
    checked.update(BARTENDING / "FluidGpuExperiment/Runtime" / name for name in (
        "FluidExperimentStorage.cs", "FluidExperimentInteractor.Mapping.cs", "FluidExperimentBody.cs",
        "FluidExperimentInteractor.cs", "FluidExperimentInteractor.Pointer.cs", "FluidExperimentWorld.cs",
        "FluidExperimentWorld.Environment.cs",
        "FluidExperimentCocktailSession.cs", "FluidExperimentDispenser.cs", "FluidExperimentDispenser.Emission.cs",
        "FluidExperimentRuntimeRig.cs", "FluidExperimentGpuLiquid.Rendering.cs"))
    checked.update(BARTENDING / "Runtime" / name for name in (
        "BusinessBartendingBootstrap.cs", "BusinessBartendingSettings.cs", "BartendingSessionBuilder.cs", "BartendingViewport.cs",
        "ToolSelection/ToolSelectionUI.cs", "ToolSelection/ToolSlotUI.cs", "ToolSelection/ToolReturnZones.cs",
        "IngredientSelection/IngredientSelectionUI.cs", "IngredientSelection/IngredientSlotUI.cs"))
    checked.add(ROOT / "Assets/_Project/Features/Business/Runtime/Flow/BusinessOrderSessionController.cs")
    checked.add(BARTENDING / "Editor/GpuCraftingIntegrationValidator.cs")
    checks = {
        "kind": "static source/reference checks only",
        "passed": True,
        "bottles": sorted(bottles), "glasses": sorted(glasses), "garnishes": garnish_ids,
        "legacy_bootstrap_original_preserved": True,
        "source_sha256": {str(p.relative_to(ROOT)).replace("\\", "/"): hashlib.sha256(p.read_bytes()).hexdigest() for p in sorted(checked)},
    }
    OUTPUT.mkdir(parents=True, exist_ok=True)
    (OUTPUT / "static-reference-results.json").write_text(json.dumps(checks, indent=2, ensure_ascii=False), encoding="utf-8")
    print(f"STATIC REFERENCES PASS: {len(bottles)} bottles, {len(glasses)} glasses, {len(garnish_ids)} garnishes; original Bootstrap preserved.")

if __name__ == "__main__":
    main()
