using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Slainte.Bartending;
using Slainte.Bartending.FluidGpuExperiment;

public static class CocktailIntegrationValidation
{
    const string Key = "Fluid.CocktailIntegrationValidation";
    [InitializeOnLoadMethod] static void Register() => EditorApplication.playModeStateChanged += state =>
    {
        if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(Key, false)) return;
        SessionState.EraseBool(Key);
        new GameObject("CocktailIntegrationValidation").AddComponent<CocktailIntegrationRunner>();
    };
    public static void Begin()
    {
        EditorSceneManager.OpenScene("Assets/_Project/Features/Bartending/FluidGpuExperiment/Scenes/FluidGpuComparison.unity");
        SessionState.SetBool(Key, true); EditorApplication.isPlaying = true;
    }
}

public sealed class CocktailIntegrationRunner : MonoBehaviour
{
    const string Root = "Assets/_Project/Features/Bartending/FluidGpuExperiment";
    readonly List<string> report = new List<string>(), errors = new List<string>();
    FluidExperimentComparison comparison;
    FluidExperimentWorld world;
    FluidExperimentGpuLiquid gpu;
    FluidExperimentCocktailSession session;
    string Evidence => Environment.GetEnvironmentVariable("PHYSICSLAB_EVIDENCE_DIR");
    void Check(bool value, string message)
    { report.Add((value ? "PASS " : "FAIL ") + message); if (!value) throw new Exception(message); }
    void Log(string message, string stack, LogType type)
    { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors.Add(message); }

    IEnumerator Start()
    {
        Application.logMessageReceived += Log;
        yield return null; yield return null;
        var oldMode = Physics2D.simulationMode; var oldGravity = Physics2D.gravity;
        bool success = false;
        try
        {
            comparison = FindFirstObjectByType<FluidExperimentComparison>();
            comparison.automaticScenario = false; comparison.ResetComparison();
            world = comparison.World; gpu = world.Liquid; session = world.Cocktails;
            world.enabled = false; world.interactor.enabled = false; gpu.automaticReadback = false;
            Physics2D.simulationMode = SimulationMode2D.Script; Physics2D.gravity = Vector2.zero;
            Check(session != null && session.Available && session.RecipeCatalog.OrderableCount > 0,
                "Authored F Manual loads the actual shared resource recipes and evaluator adapter");
            session.showControls = false;
            var recipes = session.RecipeCatalog.OrderableRecipes.ToArray();
            foreach (var recipe in recipes)
            {
                var glass = Prepare(recipe);
                session.SelectGlass(glass); session.SelectRecipe(recipe.id);
                bool evaluated = session.EvaluateTarget();
                report.Add("INFO " + session.Message);
                Check(evaluated && session.LastOrderResult.outcome == CocktailOrderEvaluationOutcome.Good,
                    "Actual recipe " + recipe.id + " yields Good with matching GPU ingredients, glass, ice and technique");
            }
            TestGrades(recipes);
            TestSnapshots(recipes[0]);
            TestThermalAlias();
            TestTechniques();
            TestPanel();
            Check(errors.Count == 0, "No Unity, shader, buffer or application errors in cocktail integration");
            success = true;
        }
        catch (Exception error) { report.Add(error.ToString()); }
        finally
        {
            Physics2D.simulationMode = oldMode; Physics2D.gravity = oldGravity;
            Application.logMessageReceived -= Log;
            report.AddRange(errors);
            File.WriteAllLines(Path.Combine(Evidence, "cocktail-integration-validation.txt"), report);
            File.WriteAllText(Path.Combine(Evidence, "cocktail-integration-result.txt"), success ? "PASS" : "FAIL");
            EditorApplication.isPlaying = false;
            EditorApplication.delayCall += () => EditorApplication.Exit(success ? 0 : 1);
        }
    }

    void ParkAndReset()
    {
        foreach (var body in world.Items.ToArray())
        {
            if (body.kind == LabItemKind.Dispenser) continue;
            body.SetHeld(false); body.gameObject.SetActive(false); Destroy(body.gameObject);
        }
        gpu.ResetSimulation(); session.ObserveTechniques(.01f);
    }
    FluidExperimentBody Spawn(string name, Vector2 position)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Root + "/Prefabs/" + name + ".prefab");
        var instance = Instantiate(prefab, world.transform).GetComponent<FluidExperimentBody>();
        instance.Teleport(position, 0); instance.Body.bodyType = RigidbodyType2D.Kinematic; instance.pourMlPerSecond = 0;
        Physics2D.SyncTransforms();
        return instance;
    }
    IEnumerable<Vector2> Points(FluidExperimentBody vessel)
    {
        Vector3 scale = vessel.transform.lossyScale;
        float dx = gpu.Radius * 1.75f / Mathf.Abs(scale.x), dy = gpu.Radius * 1.75f / Mathf.Abs(scale.y);
        foreach (Rect region in vessel.contentRegions)
            for (float y = region.yMin + dy; y < region.yMax - dy; y += dy)
                for (float x = region.xMin + dx; x < region.xMax - dx; x += dx)
                    if (vessel.ContainsLiquidDisk(new Vector2(x, y), gpu.Radius))
                        yield return vessel.LocalToWorld(new Vector2(x, y));
    }
    void Emit(FluidExperimentBody vessel, IEnumerable<KeyValuePair<ItemDef, float>> mixture)
    {
        var parts = mixture.ToArray();
        var points = Points(vessel).ToArray(); int index = 0;
        // Exact-dose adapter fixtures use variable ml particles to fit the existing authored grid.
        // This does not alter delivered particle settings or claim manual pouring/capacity parity.
        float dose = Mathf.Max(gpu.ParticleVolumeMl, parts.Sum(p => p.Value) / Mathf.Max(1, points.Length - parts.Length) * 1.05f);
        report.Add("INFO Exact-dose fixture " + vessel.displayName + ": " + points.Length + " grid points, up to " + dose + " ml per particle");
        foreach (var pair in parts)
            for (float remaining = pair.Value; remaining > .0001f;)
            {
                float ml = Mathf.Min(dose, remaining);
                if (index >= points.Length) throw new Exception("Fixture exceeds authored glass particle grid");
                if (!gpu.TryEmit(points[index++], Vector2.zero, pair.Key, ml, vessel.Id)) throw new Exception("Fixture emission rejected");
                remaining -= ml;
            }
        gpu.Step(.0001f); gpu.ReadbackNow();
    }
    void SetIce(FluidExperimentBody vessel, bool present)
    {
        bool closed = vessel.sealedVessel;
        vessel.SetSealed(false);
        foreach (var ice in world.Items.Where(b => b.kind == LabItemKind.Ice && b.ContainingVesselId == vessel.Id).ToArray())
        { ice.Teleport(new Vector2(-32, 16), 0); ice.Body.simulated = false; }
        if (present) Spawn("Ice", Points(vessel).First());
        world.RefreshIceContainment();
        vessel.SetSealed(closed);
    }
    FluidExperimentBody Prepare(CocktailRecipe recipe)
    {
        ParkAndReset();
        var glass = Spawn("Glass_" + recipe.glassId, new Vector2(0, 2)); glass.SetSealed(true);
        Emit(glass, recipe.ingredients.Select(part => new KeyValuePair<ItemDef, float>(part.item, part.targetMl)));
        SetIce(glass, IceRequirementRules.ResolvePresenceRule(recipe.iceRequirement, recipe.requiredIceCount) == IceRequirement.Required);
        gpu.MarkCocktailTechnique(glass, recipe.requiredTechnique, recipe.requiredTechnique == CocktailTechnique.Stir,
            recipe.shakeIceRequirement == IceRequirement.Required);
        return glass;
    }
    void Evaluate(FluidExperimentBody glass, CocktailRecipe recipe)
    {
        session.SelectGlass(glass); session.SelectRecipe(recipe.id);
        Check(session.EvaluateTarget(), "Read current GPU contents for " + recipe.id);
    }
    void TestGrades(CocktailRecipe[] recipes)
    {
        var recipe = recipes.First(r => r.iceRequirement != IceRequirement.Any);
        var glass = Prepare(recipe); Evaluate(glass, recipe);
        string originalGlass = glass.servingGlassId;
        glass.servingGlassId = originalGlass == "rock" ? "highball" : "rock";
        Evaluate(glass, recipe);
        Check(session.LastOrderResult.outcome == CocktailOrderEvaluationOutcome.MidGlass, "Wrong known glass yields MidGlass");
        SetIce(glass, recipe.iceRequirement != IceRequirement.Required); Evaluate(glass, recipe);
        Check(session.LastOrderResult.outcome == CocktailOrderEvaluationOutcome.MidIceGlass, "Wrong glass and ice yield MidIceGlass");
        glass.servingGlassId = originalGlass; Evaluate(glass, recipe);
        Check(session.LastOrderResult.outcome == CocktailOrderEvaluationOutcome.MidIce, "Wrong serving ice yields MidIce");
        gpu.ResetSimulation(); Evaluate(glass, recipe);
        Check(session.LastOrderResult.outcome == CocktailOrderEvaluationOutcome.Bad, "An empty glass yields Bad rather than a technical fallback");
        var other = recipes.First(r => r.id != recipe.id);
        glass = Prepare(other); Evaluate(glass, recipe);
        Check(session.LastOrderResult.outcome == CocktailOrderEvaluationOutcome.MidWrongMenu, "Another valid actual recipe yields MidWrongMenu");
        glass = Prepare(recipe);
        var part = recipe.ingredients[0];
        gpu.ResetSimulation();
        Emit(glass, recipe.ingredients.Select(p => new KeyValuePair<ItemDef, float>(p.item, p.targetMl + (p == part ? 50 : 0))));
        Evaluate(glass, recipe);
        Check(!session.LastOrderResult.requestedRecipeResult.ingredientsValid, "Per-ingredient excess uses the game's existing ml tolerances");
        glass = Prepare(recipe);
        var extra = session.ItemCatalog.ItemsById.Values.First(item => recipe.ingredients.All(p => p.item != item));
        // Place an additional distinct ingredient without altering the existing GPU mixture.
        if (!gpu.TryEmit(Points(glass).Last(), Vector2.zero, extra, 5, glass.Id)) throw new Exception("Extra fixture rejected");
        gpu.Step(.0001f); Evaluate(glass, recipe);
        Check(!session.LastOrderResult.requestedRecipeResult.extrasValid || recipe.allowExtraIngredients,
            "Unexpected ingredient volume follows the original extra-ingredient rule");
    }
    void TestSnapshots(CocktailRecipe recipe)
    {
        var glass = Prepare(recipe); Evaluate(glass, recipe);
        var before = (GpuLiquidParticle[])gpu.Snapshot.Clone(); var composition = (float[])gpu.CompositionSnapshot.Clone();
        int revision = gpu.SnapshotRevision;
        Evaluate(glass, recipe);
        Check(gpu.SnapshotRevision > revision && gpu.Snapshot.SequenceEqual(before) && gpu.CompositionSnapshot.SequenceEqual(composition),
            "Evaluation reads a fresh coherent snapshot and preserves every particle and mixture value");
        var other = Spawn("Glass_highball", new Vector2(5, 2)); other.SetSealed(true);
        Emit(other, new[] { new KeyValuePair<ItemDef, float>(recipe.ingredients[0].item, 20) });
        Evaluate(glass, recipe);
        Check(Mathf.Abs(session.LastComposition.TotalVolumeMl - recipe.ingredients.Sum(p => p.targetMl)) < .01f,
            "Other vessels are excluded from the submitted glass composition");
        gpu.ReleaseOwner(glass.Id); Evaluate(glass, recipe);
        Check(session.LastComposition.TotalVolumeMl < .001f, "Spilled owner-free particles are excluded from serving contents");
        session.ConfigureCatalogs(new ItemDefCatalog(), session.RecipeCatalog);
        session.SelectGlass(other);
        Check(!session.EvaluateTarget() && session.LastRecipeResult == null, "Unmapped ingredients fail explicitly without publishing a previous result");
        session.ReloadData();
        Check(!session.SelectGlass(other.World.Dispenser.Body), "The dispenser cannot be selected as a serving glass");
        session.SelectGlass(other); session.SelectRecipe(string.Empty); session.EvaluateTarget();
        gpu.ResetSimulation(); session.ObserveTechniques(.01f);
        Check(session.LastComposition == null && session.LastRecipeResult == null, "Generation reset clears prior results and action progress");
    }
    void TestThermalAlias()
    {
        ParkAndReset(); var glass = Spawn("Glass_highball", new Vector2(0, 2)); glass.SetSealed(true);
        var hot = world.Dispenser.hotWater;
        session.ItemCatalog.TryGet(hot.id, out var canonical);
        Emit(glass, new[] { new KeyValuePair<ItemDef, float>(hot, 10), new KeyValuePair<ItemDef, float>(canonical, 10) });
        Check(gpu.TryBuildCocktailComposition(glass, session.ItemCatalog, out var composition, out var error), "Hot water alias maps by actual ingredient ID: " + error);
        Check(composition.Volumes.Count == 1 && Mathf.Abs(composition.GetVolume(canonical) - 20) < .001f,
            "Separate hot and ordinary ItemDef references combine under one canonical recipe ingredient");
        Check(Mathf.Abs(composition.AverageTemperatureC - (hot.servingTemperatureC + canonical.servingTemperatureC) * .5f) < .001f,
            "Canonical recipe mapping preserves actual particle temperature samples");
    }
    void TestTechniques()
    {
        ParkAndReset(); var glass = Spawn("Glass_highball", new Vector2(0, 2)); glass.SetSealed(true);
        var ingredient = session.ItemCatalog.ItemsById.Values.First();
        Emit(glass, new[] { new KeyValuePair<ItemDef, float>(ingredient, 40) });
        var spoon = Spawn("Spoon", new Vector2(0, 2));
        Vector2 tip = spoon.collisionProfile.solids.SelectMany(h => h.points).OrderBy(p => p.y).First();
        Vector2 offset = spoon.LocalToWorld(tip) - spoon.Position;
        var liquid = gpu.Snapshot.Where(p => p.Active != 0 && p.VesselId == glass.Id).ToArray();
        Vector2 center = liquid.Aggregate(Vector2.zero, (sum, p) => sum + p.Position) / liquid.Length;
        Vector2 home = center - offset;
        spoon.Teleport(home, 0); spoon.SetHeld(true);
        for (int i = 0; i < 50; i++) Motion(spoon, home + Vector2.right * (.025f * Mathf.Sin(i * .5f)));
        var observationField = typeof(FluidExperimentCocktailSession).GetField("observations", BindingFlags.Instance | BindingFlags.NonPublic);
        var observations = (System.Collections.IDictionary)observationField.GetValue(session);
        report.Add("INFO Spoon held=" + spoon.IsHeld + " position=" + spoon.Position + " target=" + spoon.TargetPosition
            + " bounds=" + spoon.SolidBounds + " volume=" + gpu.VolumeIn(glass.Id) + " observations=" + observations.Count);
        foreach (System.Collections.DictionaryEntry observation in observations)
        {
            var observationType = observation.Value.GetType();
            report.Add("INFO Observed " + observation.Key + " stirTime=" + observationType.GetField("stirTime").GetValue(observation.Value)
                + " attempted=" + observationType.GetField("attempted").GetValue(observation.Value));
        }
        gpu.TryBuildCocktailComposition(glass, session.ItemCatalog, out var attempted, out _);
        Check(attempted.StirAttempted && !attempted.MatchesRequiredTechnique(CocktailTechnique.Build),
            "Actual held-spoon motion records a stir attempt and invalidates Build");
        for (int i = 50; i < 170; i++) Motion(spoon, home + Vector2.right * (.025f * Mathf.Sin(i * .5f)));
        gpu.TryBuildCocktailComposition(glass, session.ItemCatalog, out var stirred, out _);
        Check(stirred.MatchesRequiredTechnique(CocktailTechnique.Stir), "Sustained actual spoon motion and uniform composition complete Stir");
        spoon.SetHeld(false); spoon.Teleport(new Vector2(-30, 12), 0); spoon.Body.simulated = false;
        if (!gpu.TryEmit(Points(glass).Last(), Vector2.zero, ingredient, .5f, glass.Id)) throw new Exception("Post-stir fixture rejected");
        gpu.Step(.0001f);
        gpu.TryBuildCocktailComposition(glass, session.ItemCatalog, out var added, out _);
        Check(!added.MatchesRequiredTechnique(CocktailTechnique.Stir), "Fresh liquid after stirring does not inherit completed Stir");

        ParkAndReset(); var shaker = Spawn("Shaker", new Vector2(0, 2)); shaker.SetShakerClosure(FluidExperimentShakerClosure.Closed);
        Emit(shaker, new[] { new KeyValuePair<ItemDef, float>(ingredient, 40) });
        SetIce(shaker, true); shaker.SetHeld(true);
        for (int i = 0; i < 260; i++) Motion(shaker, new Vector2(.04f * (i % 20 < 10 ? i % 10 : 10 - i % 10), 2));
        gpu.TryBuildCocktailComposition(shaker, session.ItemCatalog, out var shaken, out _);
        Check(shaken.MatchesRequiredTechnique(CocktailTechnique.Shake) && shaken.WasShakenWithIce,
            "Closed held shaker reversals complete Shake and record actual contained ice");
        shaker.SetHeld(false);
        var receiver = Spawn("Glass_highball", new Vector2(4, 2)); receiver.SetSealed(true);
        gpu.SwapContents(shaker.Id, receiver.Position - shaker.Position, receiver.Id, shaker.Position - receiver.Position);
        gpu.TryBuildCocktailComposition(receiver, session.ItemCatalog, out var transferred, out _);
        Check(transferred.MatchesRequiredTechnique(CocktailTechnique.Shake) && transferred.WasShakenWithIce && !transferred.HasIce,
            "Technique and shake-ice history follow transferred liquid while serving ice is counted separately");
    }
    void Motion(FluidExperimentBody body, Vector2 position)
    {
        body.SetHeldPose(position, 0); world.SendMessage("FixedUpdate"); Physics2D.SyncTransforms(); Physics2D.Simulate(.01f); world.TickLiquid(.01f);
    }
    void TestPanel()
    {
        session.showControls = true; session.panelOpen = true;
        Check(session.BlocksPointer(new Vector2(Screen.width - 20, 25)), "Cocktail UI blocks world interaction under its visible controls");
        session.showControls = false;
        Check(!session.BlocksPointer(new Vector2(Screen.width - 20, 25)), "Hidden controls do not block the existing interactor");
    }
}
