using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Slainte.Bartending.FluidGpuExperiment;

public static class ExperimentShardLiquidValidation
{
    const string Key = "FluidExperiment.ShardLiquidValidation";
    [InitializeOnLoadMethod] static void Register() => EditorApplication.playModeStateChanged += s =>
    {
        if (s != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(Key, false)) return;
        SessionState.EraseBool(Key); new GameObject("ShardLiquidValidation").AddComponent<ShardLiquidValidationRunner>();
    };
    public static void Begin()
    {
        EditorSceneManager.OpenScene("Assets/_Project/Features/Bartending/FluidGpuExperiment/Scenes/FluidGpuComparison.unity");
        SessionState.SetBool(Key, true); EditorApplication.isPlaying = true;
    }
}

public sealed class ShardLiquidValidationRunner : MonoBehaviour
{
    const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    readonly List<string> report = new List<string>(), errors = new List<string>();
    void Check(bool ok, string text) { report.Add((ok ? "PASS " : "FAIL ") + text); if (!ok) throw new Exception(text); }
    void Log(string message, string stack, LogType type)
    { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors.Add(message); }
    object Invoke(object o, string method, params object[] args) => o.GetType().GetMethod(method, Members).Invoke(o, args);
    IEnumerator Start()
    {
        Application.logMessageReceived += Log;
        yield return null;
        var comparison = FindFirstObjectByType<FluidExperimentComparison>();
        while (!comparison.Ready) yield return null;
        Vector2 gravity = Physics2D.gravity;
        var simulation = Physics2D.simulationMode;
        bool success = false;
        try
        {
            TestClipping();
            var world = comparison.World; var gpu = comparison.Gpu;
            comparison.automaticScenario = comparison.showControls = false;
            world.enabled = world.interactor.enabled = world.seedLiquids = false;
            gpu.automaticReadback = false;
            Physics2D.simulationMode = SimulationMode2D.Script; Physics2D.gravity = Vector2.zero;
            world.ResetSession(); gpu.ResetSimulation();
            var glass = world.Items.First(b => b.GetComponent<FluidExperimentBreakableGlass>() != null);
            var ingredient = world.Items.First(b => b.ingredient != null).ingredient;
            foreach (var body in world.Items.ToArray())
            { body.SetHeld(false); body.Teleport(new Vector2(-100, 30), 0); body.Body.simulated = false; body.pourMlPerSecond = 0; }
            glass.Teleport(Vector2.zero, 0); glass.Body.simulated = true;
            var breakable = glass.GetComponent<FluidExperimentBreakableGlass>();
            world.shardLiquidCollision = true;
            Invoke(breakable, "QueueImpact", 8f, glass.Body.mass * 8f, Vector2.zero);
            Invoke(breakable, "CompleteBreak");
            var proxies = world.GlassShards.Select(s => s.GetComponent<FluidExperimentShardLiquidProxy>()).ToArray();
            Check(proxies.Length == 16 && proxies.All(p => p != null), "Every broken fragment has an explicit liquid proxy");
            Check(proxies.Sum(p => p.Segments.Count) > 0 && proxies.Any(p => p.Segments.Count == 0),
                "Only fragments intersecting actual glass walls contribute liquid edges; interior art stays empty");
            foreach (var shard in world.GlassShards)
            {
                var proxy = shard.GetComponent<FluidExperimentShardLiquidProxy>();
                Vector2 center = breakable.profile.shards[shard.DefinitionIndex].localCenter;
                foreach (var edge in proxy.Segments)
                {
                    Vector2 point = (edge.A + edge.B) * .5f + center;
                    Check(glass.collisionProfile.solids.Any(h => FluidExperimentCollisionProfile.Contains(h.points, point)
                        || FluidExperimentCollisionProfile.EdgeDistance(h.points, point) < .0001f),
                        "Proxy segment remains inside or on its original thin wall");
                }
            }
            gpu.Step(.001f);
            Check(gpu.LastShardBoundaryCount > 0 && gpu.SkippedShardBoundaryCount == 0, "Actual GPU upload includes all hurricane wall fragments within budget");
            world.maximumShardLiquidSegments = 0; gpu.Step(.001f);
            Check(gpu.LastShardBoundaryCount == 0 && gpu.SkippedShardBoundaryCount > 0, "A zero collision budget reports skipped geometry without buffer overflow");
            world.maximumShardLiquidSegments = 512;

            var chosen = proxies.Where(p => p.Segments.Count > 0).OrderByDescending(p => p.Segments.Max(e => (e.B - e.A).sqrMagnitude)).First();
            foreach (var proxy in proxies) if (proxy != chosen) proxy.gameObject.SetActive(false);
            chosen.Body.bodyType = RigidbodyType2D.Kinematic;
            var segment = chosen.Segments.OrderByDescending(e => (e.B - e.A).sqrMagnitude).First();
            Vector2 scale = chosen.transform.lossyScale;
            Vector2 middle = Vector2.Scale((segment.A + segment.B) * .5f, scale);
            Vector2 tangent = Vector2.Scale(segment.B - segment.A, scale).normalized;
            Vector2 normal = new Vector2(-tangent.y, tangent.x);
            chosen.Body.position = -middle; chosen.Body.rotation = 0;
            Invoke(chosen, "SynchronizeHistory");
            // Compare the same free particle against real generated geometry, with the
            // collision path disabled and enabled. No hand-authored proxy substitutes it.
            Vector2 RunParticle(bool collision)
            {
                gpu.ResetSimulation(); world.shardLiquidCollision = collision;
                gpu.TryEmit(normal * .24f, -normal * 12, ingredient, .5f, 0);
                gpu.Step(.04f); gpu.ReadbackNow();
                return gpu.Snapshot.First(p => p.Active != 0).Position;
            }
            Vector2 without = RunParticle(false), with = RunParticle(true);
            report.Add($"INFO crossing normal={normal} without={without} with={with}");
            Check(Vector2.Dot(without, normal) < -.1f && Vector2.Dot(with, normal) >= gpu.Radius * .6f,
                "A fast free particle crosses when disabled and stays outside a real shard wall when enabled");
            Check(Math.Abs(gpu.Ledger.Total.ConservationErrorMl) < .0001, "Shard collision conserves volume");

            Vector3 originalScale = chosen.transform.localScale;
            chosen.transform.localScale = Vector3.Scale(originalScale, new Vector3(1.3f, .8f, 1));
            scale = chosen.transform.lossyScale;
            middle = FluidExperimentBody.Rotate(Vector2.Scale((segment.A + segment.B) * .5f, scale), 47);
            tangent = FluidExperimentBody.Rotate(Vector2.Scale(segment.B - segment.A, scale), 47).normalized;
            normal = new Vector2(-tangent.y, tangent.x);
            chosen.Body.position = -middle; chosen.Body.rotation = 47;
            Invoke(chosen, "SynchronizeHistory");
            without = RunParticle(false); with = RunParticle(true);
            Check(Vector2.Dot(without, normal) < -.1f && Vector2.Dot(with, normal) >= gpu.Radius * .6f,
                "Rotated, nonuniformly scaled real shard geometry retains collision alignment");

            gpu.ResetSimulation();
            chosen.Body.position = -middle; Invoke(chosen, "SynchronizeHistory");
            gpu.TryEmit(normal * .2f, Vector2.zero, ingredient, .5f, 0);
            chosen.Body.position += normal * .3f;
            gpu.Step(.02f); gpu.ReadbackNow();
            var moved = gpu.Snapshot.First(p => p.Active != 0);
            Check(Vector2.Dot(moved.Position, normal) > .3f && Vector2.Dot(moved.Velocity, normal) > 0,
                "Moving shard CCD pushes a stationary liquid particle and transfers its wall velocity");

            chosen.gameObject.SetActive(false);
            var tableObject = new GameObject("Explicit liquid table"); tableObject.transform.SetParent(world.transform);
            var table = tableObject.AddComponent<BoxCollider2D>(); table.size = new Vector2(3, .2f);
            world.liquidSurfaces = new[] { table, table }; world.RebuildEnvironment();
            Check(world.EnvironmentHulls.Count(h => h.Collider == table) == 1, "Environment rebinding deduplicates an explicit table surface");
            gpu.ResetSimulation(); gpu.TryEmit(new Vector2(0, .5f), new Vector2(0, -10), ingredient, .5f, 0);
            gpu.Step(.1f); gpu.ReadbackNow();
            Check(gpu.Snapshot.First(p => p.Active != 0).Position.y >= .1f + gpu.Radius * .6f,
                "Liquid collides with the configured finite table instead of falling through");
            table.enabled = false; gpu.ResetSimulation();
            gpu.TryEmit(new Vector2(0, .5f), new Vector2(0, -10), ingredient, .5f, 0); gpu.Step(.1f); gpu.ReadbackNow();
            Check(gpu.Snapshot.First(p => p.Active != 0).Position.y < -.2f, "Disabling the table returns that area to free drainage");
            world.liquidSurfaces = Array.Empty<BoxCollider2D>(); world.RebuildEnvironment();
            Check(world.EnvironmentHulls.All(h => h.Collider != table), "Rebinding removes old liquid surfaces without stale hulls");
            Destroy(tableObject);
            Check(errors.Count == 0, "No Unity errors or assertions"); success = true;
        }
        catch (Exception ex) { report.Add(ex.ToString()); }
        finally
        {
            Physics2D.gravity = gravity; Physics2D.simulationMode = simulation;
            Application.logMessageReceived -= Log; report.AddRange(errors);
            string evidence = Environment.GetEnvironmentVariable("PHYSICSLAB_EVIDENCE_DIR");
            File.WriteAllText(Path.Combine(evidence, "shard-liquid-validation.txt"), string.Join("\n", report));
            File.WriteAllText(Path.Combine(evidence, "shard-liquid-result.txt"), success ? "PASS" : "FAIL");
            EditorApplication.isPlaying = false; EditorApplication.delayCall += () => EditorApplication.Exit(success ? 0 : 1);
        }
    }
    void TestClipping()
    {
        var wall = new[] { new Vector2(-2, -.1f), new Vector2(2, -.1f), new Vector2(2, .1f), new Vector2(-2, .1f) };
        var concave = new[] { new Vector2(-1, -1), new Vector2(1, -1), new Vector2(1, 1), new Vector2(.3f, 1),
            new Vector2(.3f, -.5f), new Vector2(-.3f, -.5f), new Vector2(-.3f, 1), new Vector2(-1, 1) };
        var edges = new List<FluidExperimentShardLiquidProxy.Segment>();
        FluidExperimentShardLiquidProxy.AppendIntersection(wall, concave, Vector2.zero, edges);
        Check(edges.Count >= 8 && edges.All(e => Mathf.Abs(((e.A + e.B) * .5f).x) >= .2999f),
            "Concave clipping preserves separated wall pieces without bridging the cut-out");
        edges.Clear();
        FluidExperimentShardLiquidProxy.AppendIntersection(wall, concave, Vector2.up * 3, edges);
        Check(edges.Count == 0, "A fragment wholly inside empty space creates no liquid collision");
    }
}
