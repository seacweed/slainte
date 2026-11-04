using System;
using System.Reflection;
using System.IO;
using UnityEditor;
using UnityEngine;

// Include-only shader edits can otherwise reuse an earlier imported compute
// artifact in the disposable project. Force the actual program's import before
// any scene initializes GPU resources, then dispatch the requested suite.
public static class PhysicsLabValidationDispatch
{
    public static void Run()
    {
        const string shader="Assets/_Project/Features/Bartending/FluidGpuExperiment/Shaders/FluidExperimentLiquid.compute";
        AssetDatabase.ImportAsset(shader,ImportAssetOptions.ForceUpdate|ImportAssetOptions.ForceSynchronousImport);
        Application.logMessageReceived+=(message,trace,type)=>
        {
            bool unsupported=message.Contains("Skipping dispatch")||message.Contains("more than the 8 maximum");
            if(type!=LogType.Error&&type!=LogType.Exception&&type!=LogType.Assert&&!unsupported) return;
            string evidence=Environment.GetEnvironmentVariable("PHYSICSLAB_EVIDENCE_DIR");
            File.AppendAllText(Path.Combine(evidence,"application-errors.txt"),message+"\n"+trace+"\n");
            EditorApplication.delayCall+=()=>EditorApplication.Exit(1);
        };
        string entry=Environment.GetEnvironmentVariable("PHYSICSLAB_ENTRY_METHOD");
        int dot=entry.LastIndexOf('.');
        string typeName=entry.Substring(0,dot),methodName=entry.Substring(dot+1);
        foreach(var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            var type=assembly.GetType(typeName,false);
            if(type==null) continue;
            var method=type.GetMethod(methodName,BindingFlags.Static|BindingFlags.Public);
            if(method==null) throw new MissingMethodException(typeName,methodName);
            method.Invoke(null,null); return;
        }
        throw new TypeLoadException(typeName);
    }
}
