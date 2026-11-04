using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Slainte.Bartending.FluidGpuExperiment;

// The identical fixture is run against the preserved pre-cleanup project and current source.
public static class CleanupParityValidation
{
    const string Key = "Fluid.CleanupParity";
    [InitializeOnLoadMethod] static void Register() => EditorApplication.playModeStateChanged += state =>
    {
        if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(Key, false)) return;
        SessionState.EraseBool(Key); new GameObject("CleanupParity").AddComponent<CleanupParityRunner>();
    };
    public static void Begin()
    {
        EditorSceneManager.OpenScene("Assets/_Project/Features/Bartending/FluidGpuExperiment/Scenes/FluidGpuComparison.unity");
        SessionState.SetBool(Key, true); EditorApplication.isPlaying = true;
    }
}

public sealed class CleanupParityRunner : MonoBehaviour
{
    readonly StringBuilder csv = new StringBuilder("material,tick,index,x,y,previousX,previousY,vx,vy,ml,temperature,owner,technique,state\n");
    string error;
    void Log(string message, string trace, LogType type)
    { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) error += message + "\n" + trace + "\n"; }
    IEnumerator Start()
    {
        Application.logMessageReceived += Log; yield return null; yield return null; yield return null;
        var previousMode = Physics2D.simulationMode; var previousGravity = Physics2D.gravity; bool success = false;
        try
        {
            var sandbox = FindFirstObjectByType<FluidExperimentComparison>();
            var world = sandbox.World; var gpu = world.Liquid;
            if (!gpu.IsOperational) throw new Exception(gpu.Error);
            sandbox.enabled = false; world.enabled = false; world.interactor.enabled = false;
            world.Cocktails.showControls = false; gpu.automaticReadback = false;
            Physics2D.simulationMode = SimulationMode2D.Script; Physics2D.gravity = new Vector2(0, -9.81f);
            var glass = world.Items.First(b => b.kind == LabItemKind.Glass && b.name.Contains("highball"));
            var ingredient = world.Items.First(b => b.kind == LabItemKind.Bottle).ingredient;
            foreach (var body in world.Items.ToArray()) if (body != glass) body.gameObject.SetActive(false);
            glass.SetHeld(false); glass.Body.bodyType = RigidbodyType2D.Kinematic; glass.Teleport(new Vector2(0, 1), 0);
            glass.SetSealed(true); Physics2D.SyncTransforms();
            foreach (var material in new[] { FluidExperimentMaterial.Auto, FluidExperimentMaterial.Syrup, FluidExperimentMaterial.Milk })
            {
                gpu.cohesiveMaterial = material; gpu.ResetSimulation(); gpu.Fill(glass, ingredient, 60);
                for (int tick = 0; tick <= 120; tick++)
                {
                    gpu.Step(.02f);
                    if (tick == 0 || tick == 40 || tick == 120)
                    {
                        gpu.ReadbackNow();
                        for (int i = 0; i < gpu.Snapshot.Length; i++)
                        {
                            var p = gpu.Snapshot[i]; if (p.Active == 0) continue;
                            csv.Append(material).Append(',').Append(tick).Append(',').Append(i);
                            foreach (float v in new[] { p.Position.x, p.Position.y, p.PreviousPosition.x, p.PreviousPosition.y,
                                p.Velocity.x, p.Velocity.y, p.VolumeMl, p.TemperatureC })
                            { if (float.IsNaN(v) || float.IsInfinity(v)) throw new Exception("Nonfinite particle"); csv.Append(',').Append(v.ToString("R", CultureInfo.InvariantCulture)); }
                            csv.Append(',').Append(p.VesselId).Append(',').Append(p.TechniqueFlags).Append(',').Append(p.StateFlags).Append('\n');
                        }
                    }
                }
            }
            if (error != null) throw new Exception(error); success = true;
        }
        catch (Exception exception) { error += exception.ToString(); }
        finally
        {
            Physics2D.simulationMode = previousMode; Physics2D.gravity = previousGravity; Application.logMessageReceived -= Log;
            string evidence = Environment.GetEnvironmentVariable("PHYSICSLAB_EVIDENCE_DIR");
            File.WriteAllText(Path.Combine(evidence, "parity-particles.csv"), csv.ToString());
            File.WriteAllText(Path.Combine(evidence, "parity-result.txt"), success ? "PASS" : error);
            EditorApplication.isPlaying = false; EditorApplication.delayCall += () => EditorApplication.Exit(success ? 0 : 1);
        }
    }
}
