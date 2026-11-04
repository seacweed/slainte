using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Slainte.Bartending.PhysicsLab.Editor
{
    public static partial class PhysicsLabGeometryAuthoring
    {
        // Explicit regeneration entry point. The usual Update command preserves hand-edited profiles.
        [MenuItem("Slainte/Physics Lab/Rebuild Solid Profiles From Artwork")]
        public static void RebuildSolidProfiles()
        {
            if(EditorApplication.isPlaying)throw new InvalidOperationException("Exit Play Mode before rebuilding profiles.");
            foreach(string guid in AssetDatabase.FindAssets("t:Prefab",new[]{PhysicsLabBuilder.Root+"/Prefabs"}))
            {
                string path=AssetDatabase.GUIDToAssetPath(guid);
                var root=PrefabUtility.LoadPrefabContents(path);
                try
                {
                    var body=root.GetComponent<PhysicsLabBody>();
                    if(body.IsVessel)continue;
                    var created=Create(body);
                    if(body.collisionProfile==null)throw new InvalidOperationException("Update profiles before regenerating artwork.");
                    EditorUtility.CopySerialized(created,body.collisionProfile);
                    EditorUtility.SetDirty(body.collisionProfile);
                    UnityEngine.Object.DestroyImmediate(created);
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
            AssetDatabase.SaveAssets();UpgradeAll();
        }

        private struct PixelEdge
        {
            public Vector2Int a,b;
            public bool used;
        }

        private static PhysicsLabHull[] SolidContours(Mask mask)
        {
            var output=new List<PhysicsLabHull>();
            SplitBand(0,mask.height);
            return output.ToArray();

            void SplitBand(int bottom,int top)
            {
                var loops=Trace(mask,bottom,top);
                // A cut through a hole makes two ordinary polygons. Both CPU and GPU therefore
                // see the same hollow handle without relying on engine-specific hole winding rules.
                var hole=loops.FirstOrDefault(p=>SignedArea(p)<-4);
                if(hole!=null)
                {
                    int split=(hole.Min(p=>p.y)+hole.Max(p=>p.y))/2;
                    if(split<=bottom||split>=top)throw new InvalidOperationException("Cannot split alpha-mask hole.");
                    SplitBand(bottom,split);SplitBand(split,top);return;
                }
                foreach(var loop in loops.Where(p=>SignedArea(p)>4))
                {
                    var points=loop.Select(p=>mask.origin+(Vector2)p*mask.step).ToList();
                    points.Add(points[0]);
                    var reduced=new List<Vector2>{points[0]};
                    Simplify(points,0,points.Count-1,mask.step*1.25f,reduced);
                    reduced.RemoveAt(reduced.Count-1);
                    if(reduced.Count>=3)output.Add(new PhysicsLabHull{points=reduced.ToArray()});
                }
            }
        }

        private static long SignedArea(List<Vector2Int> path)
        {
            long area=0;
            for(int i=0;i<path.Count;i++)
            {var a=path[i];var b=path[(i+1)%path.Count];area+=(long)a.x*b.y-(long)b.x*a.y;}
            return area;
        }

        private static List<List<Vector2Int>> Trace(Mask mask,int bottom,int top)
        {
            var edges=new List<PixelEdge>();var outgoing=new Dictionary<Vector2Int,List<int>>();
            bool Filled(int x,int y)=>x>=0&&x<mask.width&&y>=bottom&&y<top&&mask.pixels[y*mask.width+x];
            void Add(int ax,int ay,int bx,int by)
            {
                var a=new Vector2Int(ax,ay);var b=new Vector2Int(bx,by);
                if(!outgoing.TryGetValue(a,out var list))outgoing[a]=list=new List<int>();
                list.Add(edges.Count);edges.Add(new PixelEdge{a=a,b=b});
            }
            for(int y=bottom;y<top;y++)for(int x=0;x<mask.width;x++)
            {
                if(!Filled(x,y))continue;
                if(!Filled(x,y-1))Add(x,y,x+1,y);
                if(!Filled(x+1,y))Add(x+1,y,x+1,y+1);
                if(!Filled(x,y+1))Add(x+1,y+1,x,y+1);
                if(!Filled(x-1,y))Add(x,y+1,x,y);
            }
            var loops=new List<List<Vector2Int>>();
            for(int start=0;start<edges.Count;start++)
            {
                if(edges[start].used)continue;
                var loop=new List<Vector2Int>();int current=start;
                for(int remaining=edges.Count;remaining>0;remaining--)
                {
                    var e=edges[current];e.used=true;edges[current]=e;loop.Add(e.a);
                    if(e.b==edges[start].a)break;
                    var incoming=e.b-e.a;int next=-1,best=-3;
                    foreach(int candidate in outgoing[e.b])
                    {
                        if(edges[candidate].used)continue;
                        var direction=edges[candidate].b-e.b;
                        int cross=incoming.x*direction.y-incoming.y*direction.x;
                        int rank=cross>0?2:cross==0?1:0;
                        if(rank>best){best=rank;next=candidate;}
                    }
                    if(next<0)throw new InvalidOperationException("Open alpha contour.");
                    current=next;
                }
                if(edges[current].b!=edges[start].a)throw new InvalidOperationException("Alpha contour did not close.");
                loops.Add(loop);
            }
            return loops;
        }
    }
}
