"""Compile current Unity sources against existing local Unity/project DLL references.

This does not import Unity assets, compile shaders or build a player. Unity-generated
csproj files, existing ScriptAssemblies and a locally installed .NET SDK are required.
"""
from pathlib import Path
import xml.etree.ElementTree as ET
import subprocess, re, json
r=Path(__file__).resolve().parent.parent
out=r/'Temp/GpuCraftingCompile'
out.mkdir(parents=True,exist_ok=True)
report={}
for project in ['Assembly-CSharp','Assembly-CSharp-Editor']:
    tree=ET.parse(r/(project+'.csproj'))
    def find(tag): return tree.findall('.//'+tag)
    defines=tree.find('.//DefineConstants').text
    sources=[r/n.attrib['Include'] for n in find('Compile')]
    if project=='Assembly-CSharp':
        for p in (r/'Assets/_Project/Features/Bartending/Runtime/Integration').glob('*.cs'):
            if p not in sources: sources.append(p)
        for name in ['FluidExperimentStorage.cs','FluidExperimentInteractor.Mapping.cs']:
            p=r/'Assets/_Project/Features/Bartending/FluidGpuExperiment/Runtime'/name
            if p not in sources: sources.append(p)
    if project=='Assembly-CSharp-Editor':
        p=r/'Assets/_Project/Features/Bartending/Editor/GpuCraftingIntegrationValidator.cs'
        if p not in sources: sources.append(p)
    refs=[]
    missing=[]
    for n in find('Reference'):
        h=n.find('HintPath')
        if h is not None:
            p=Path(h.text)
            if not p.is_absolute(): p=r/p
            if p.exists(): refs.append(p)
            else: missing.append(str(p))
    for n in find('ProjectReference'):
        name=Path(n.attrib['Include']).stem
        p=(out/'Assembly-CSharp.dll') if name=='Assembly-CSharp' else r/'Library/ScriptAssemblies'/(name+'.dll')
        if p.exists(): refs.append(p)
        else: missing.append(str(p))
    missing += [str(p) for p in sources if not p.exists()]
    if missing:
        print('Missing inputs:',json.dumps(missing[:15]))
        raise SystemExit(2)
    args=['-utf8output','-nostdlib+','-target:library','-langversion:9.0','-nowarn:0169;USG0001','-define:'+defines,'-out:"'+str(out/(project+'.dll'))+'"']
    args += ['-reference:"'+str(p)+'"' for p in dict.fromkeys(refs)]
    args += ['"'+str(p)+'"' for p in sources]
    response=out/(project+'.rsp');response.write_text('\n'.join(args),encoding='utf-8-sig')
    sdk=sorted(Path('C:/Program Files/dotnet/sdk').glob('*/Roslyn/bincore/csc.dll'))[-1]
    result=subprocess.run(['dotnet',str(sdk),'-noconfig','@'+str(response)],cwd=r,capture_output=True,text=True,encoding='utf-8',errors='replace')
    (out/(project+'.log')).write_text(result.stdout+result.stderr,encoding='utf-8')
    print(project, 'exit',result.returncode, 'sources',len(sources),'references',len(refs))
    print('Errors:', '\n'.join(line for line in result.stdout.splitlines() if 'error CS' in line))
    report[project]={'exit_code':result.returncode,'source_count':len(sources),'reference_count':len(refs)}
    if result.returncode: break
if result.returncode == 0:
    source=(out/'Assembly-CSharp.rsp').read_text(encoding='utf-8-sig')
    lines=[]
    for line in source.splitlines():
        if line.startswith('-define:'):
            symbols=line[len('-define:'):].split(';')
            line='-define:'+';'.join(d for d in symbols if not d.startswith('UNITY_EDITOR'))
        if line.startswith('-out:'): line='-out:\"'+str(out/'Assembly-CSharp-PlayerSymbols.dll')+'\"'
        lines.append(line)
    response=out/'Assembly-CSharp-PlayerSymbols.rsp'
    response.write_text('\n'.join(lines),encoding='utf-8-sig')
    result=subprocess.run(['dotnet',str(sdk),'-noconfig','@'+str(response)],cwd=r,capture_output=True,text=True,encoding='utf-8',errors='replace')
    (out/'Assembly-CSharp-PlayerSymbols.log').write_text(result.stdout+result.stderr,encoding='utf-8')
    report['Assembly-CSharp-PlayerSymbols']={'exit_code':result.returncode}
    print('Player-symbol C# compilation exit',result.returncode)
    print('\n'.join(line for line in result.stdout.splitlines() if 'error CS' in line))
(out/'report.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
raise SystemExit(result.returncode)
