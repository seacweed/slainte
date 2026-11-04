using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Slainte.Bartending.PhysicsLab.Editor
{
    public static partial class PhysicsLabGeometryAuthoring
    {
        private const string Folder = PhysicsLabBuilder.Root + "/Data/CollisionProfiles";

        [MenuItem("Slainte/Physics Lab/Update Collision Profiles and Prefabs")]
        public static void UpgradeAll()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play Mode before updating prefabs.");
            Directory.CreateDirectory(Folder); AssetDatabase.Refresh();
            var report = new List<string>();
            if (Application.isBatchMode) PhysicsLabGeometryPreview.CaptureAtlas("geometry-before.png");
            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { PhysicsLabBuilder.Root + "/Prefabs" }).OrderBy(x => x))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                GameObject root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    var body = root.GetComponent<PhysicsLabBody>();
                    Apply(body, Path.GetFileNameWithoutExtension(path));
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                    report.Add(body.name + ": " + body.collisionProfile.BoundaryCount + " edges; " + body.collisionProfile.sourceDescription);
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
            string directory = PhysicsLabValidator.EvidenceDirectory;
            Directory.CreateDirectory(directory);
            File.WriteAllLines(Path.Combine(directory, "geometry-authoring.txt"), report);
            if (Application.isBatchMode) PhysicsLabGeometryPreview.CaptureAtlas("geometry-after.png");
            Debug.Log("[PhysicsLab] Updated collision geometry on " + report.Count + " prefabs; scene and shared art untouched.");
        }

        public static void Apply(PhysicsLabBody body, string assetName)
        {
            Directory.CreateDirectory(Folder);
            string path = Folder + "/" + assetName + ".asset";
            var profile = AssetDatabase.LoadAssetAtPath<PhysicsLabCollisionProfile>(path);
            if (profile == null)
            {
                profile = Create(body);
                AssetDatabase.CreateAsset(profile, path);
            }
            Validate(profile);
            // Remove only the old physical colliders. The selection trigger has its own lifetime.
            var old = body.solidColliders.Where(x => x != null && x != body.pickCollider).Distinct().ToArray();
            PhysicsMaterial2D material = old.Length > 0 ? old[0].sharedMaterial : null;
            var main = old.FirstOrDefault(x => x != body.capCollider) as PolygonCollider2D;
            var existingCap = profile.lid.Length > 0 ? body.capCollider as PolygonCollider2D : null;
            foreach (Collider2D c in old) if (c != main && c != existingCap) UnityEngine.Object.DestroyImmediate(c);
            if (main == null) main = body.gameObject.AddComponent<PolygonCollider2D>();
            main.sharedMaterial = material;
            var solids = new List<Collider2D> { main };
            body.capCollider = null;
            if (profile.lid.Length > 0)
            {
                var cap = existingCap != null ? existingCap : body.gameObject.AddComponent<PolygonCollider2D>();
                cap.sharedMaterial = material; solids.Add(cap); body.capCollider = cap;
            }
            body.solidColliders = solids.ToArray(); body.collisionProfile = profile;
            body.ApplyCollisionProfile(); body.SetSealed(body.sealedVessel);
        }

        private sealed class Mask
        {
            public int width, height;
            public float step;
            public Vector2 origin;
            public bool[] pixels;
        }

        private static PhysicsLabCollisionProfile Create(PhysicsLabBody body)
        {
            var all = body.GetComponentsInChildren<SpriteRenderer>(true).Where(x => x.sprite != null).ToArray();
            var cup = all.Where(x => body.capVisual == null || !x.transform.IsChildOf(body.capVisual.transform)).ToArray();
            var lid = all.Except(cup).ToArray();
            var profile = ScriptableObject.CreateInstance<PhysicsLabCollisionProfile>();
            profile.name = body.name;
            Mask mask = Rasterize(body, cup);
            profile.sourcePixelSize = mask.step;
            profile.sourceDescription = string.Join(", ", cup.Select(x => x.sprite.name));
            Vector2[] outer = Envelope(mask, body.IsVessel ? body.mouthLocal.y : float.PositiveInfinity);
            if (cup.Length == 1 && !body.IsVessel && cup[0].sprite.GetPhysicsShapeCount() > 0)
            {
                var paths = new List<PhysicsLabHull>();
                for (int i = 0; i < cup[0].sprite.GetPhysicsShapeCount(); i++)
                {
                    var points = new List<Vector2>(); cup[0].sprite.GetPhysicsShape(i, points);
                    paths.Add(new PhysicsLabHull { points = points.Select(p => ToBody(body, cup[0], p)).ToArray() });
                }
                profile.solids = paths.ToArray();
            }
            else if (body.IsVessel)
            {
                float margin = mask.step * 3;
                var inner = new Vector2[body.liquidWall.Length];
                for (int i = 0; i < inner.Length; i++)
                {
                    Vector2 point = body.liquidWall[i];
                    for (int iteration = 0; iteration < 6; iteration++)
                    {
                        float nearest = float.PositiveInfinity; Vector2 q = point, n = Vector2.up;
                        // Outer path is open at the mouth: never shrink the ownership rim downward.
                        for (int e = 0; e < outer.Length - 1; e++)
                        {
                            Vector2 a = outer[e], b = outer[e + 1];
                            Vector2 candidate = PhysicsLabCollisionProfile.Closest(point, a, b);
                            float distance = Vector2.Distance(point, candidate);
                            if (distance >= nearest) continue;
                            nearest = distance; q = candidate; n = new Vector2(-(b-a).y, (b-a).x).normalized;
                        }
                        // Points on the open top are intentionally accepted when side clearance is sufficient.
                        Vector2 insideProbe = point - Vector2.up * mask.step * .01f;
                        if (PhysicsLabCollisionProfile.Contains(outer, insideProbe) && nearest >= margin) break;
                        point = q + n * margin;
                    }
                    inner[i] = point;
                }
                // Preserve the original open-mouth height, while corrected x coordinates clear the visible walls.
                inner[0].y = inner[inner.Length - 1].y = Mathf.Min(outer[0].y, outer[outer.Length - 1].y);
                profile.interior = inner;
                profile.solids = new[] { new PhysicsLabHull { points = outer.Concat(inner.Reverse()).ToArray() } };
                profile.mouth = (inner[0] + inner[inner.Length - 1]) * .5f;
            }
            else profile.solids = SolidContours(mask);
            if (!body.IsVessel)
            {
                profile.mouth = body.LipLocal;
                Vector2 center = PhysicsLabCollisionProfile.BoundsOf(outer).center;
                profile.exitDirection = body.mouthDirectionLocal.sqrMagnitude > 1e-6f ? body.mouthDirectionLocal.normalized : (profile.mouth - center).normalized;
                if (profile.exitDirection.sqrMagnitude < .5f) profile.exitDirection = Vector2.up;
            }
            if (lid.Length > 0)
            {
                profile.lid = Envelope(Rasterize(body, lid), float.PositiveInfinity);
            }
            Validate(profile); return profile;
        }

        private static Vector2 ToBody(PhysicsLabBody body, SpriteRenderer visual, Vector2 local)
        {
            if (visual.flipX) local.x = -local.x;
            if (visual.flipY) local.y = -local.y;
            return body.transform.InverseTransformPoint(visual.transform.TransformPoint(local));
        }

        // Editor-only read of the source PNG. Never enables Read/Write or changes shared import settings.
        private static Mask Rasterize(PhysicsLabBody body, SpriteRenderer[] visuals)
        {
            if (visuals.Length == 0) throw new InvalidOperationException(body.name + ": no collision artwork.");
            Vector2 min = Vector2.one * float.PositiveInfinity, max = Vector2.one * float.NegativeInfinity;
            float step = float.PositiveInfinity;
            foreach (var visual in visuals)
            {
                Sprite sprite = visual.sprite;
                Vector2 a = -sprite.pivot / sprite.pixelsPerUnit, b = (sprite.rect.size - sprite.pivot) / sprite.pixelsPerUnit;
                foreach (Vector2 point in new[] { a, b, new Vector2(a.x,b.y), new Vector2(b.x,a.y) })
                { Vector2 p = ToBody(body, visual, point); min = Vector2.Min(min,p); max = Vector2.Max(max,p); }
                Vector2 zero = ToBody(body, visual, Vector2.zero);
                step = Mathf.Min(step, Vector2.Distance(zero, ToBody(body, visual, Vector2.right / sprite.pixelsPerUnit)),
                    Vector2.Distance(zero, ToBody(body, visual, Vector2.up / sprite.pixelsPerUnit)));
            }
            int width = Mathf.CeilToInt((max.x-min.x)/step)+2, height = Mathf.CeilToInt((max.y-min.y)/step)+2;
            if (width * (long)height > 8000000) throw new InvalidOperationException(body.name + ": collision raster too large.");
            var mask = new Mask { width=width, height=height, origin=min, step=step, pixels=new bool[width*height] };
            foreach (var visual in visuals)
            {
                Sprite sprite = visual.sprite;
                var texture = new Texture2D(2,2,TextureFormat.RGBA32,false);
                try
                {
                    if (!texture.LoadImage(File.ReadAllBytes(AssetDatabase.GetAssetPath(sprite)))) throw new InvalidOperationException("Cannot read " + sprite.name);
                    var pixels = texture.GetPixels32(); Rect rect = sprite.rect;
                    // Sample the destination grid to avoid empty rows from floating-point forward splats.
                    for (int y=0; y<height; y++) for(int x=0; x<width; x++)
                    {
                        Vector2 p = visual.transform.InverseTransformPoint(body.transform.TransformPoint(min+new Vector2(x+.5f,y+.5f)*step));
                        if(visual.flipX)p.x=-p.x;if(visual.flipY)p.y=-p.y;
                        p=p*sprite.pixelsPerUnit+sprite.pivot;
                        int sx=Mathf.FloorToInt(p.x),sy=Mathf.FloorToInt(p.y);
                        if(sx<0||sy<0||sx>=(int)rect.width||sy>=(int)rect.height)continue;
                        if(pixels[((int)rect.y+sy)*texture.width+(int)rect.x+sx].a>=32)mask.pixels[y*width+x]=true;
                    }
                }
                finally { UnityEngine.Object.DestroyImmediate(texture); }
            }
            return mask;
        }

        private static Vector2[] Envelope(Mask mask, float top)
        {
            var left = new List<Vector2>(); var right = new List<Vector2>();
            for (int y=mask.height-1; y>=0; y--)
            {
                float py=mask.origin.y+(y+.5f)*mask.step;
                if (py>top) continue;
                int lo=mask.width, hi=-1;
                for(int x=0;x<mask.width;x++) if(mask.pixels[y*mask.width+x]) { lo=Mathf.Min(lo,x);hi=Mathf.Max(hi,x); }
                if(hi<lo)continue;
                left.Add(new Vector2(mask.origin.x+lo*mask.step,py));
                right.Add(new Vector2(mask.origin.x+(hi+1)*mask.step,py));
            }
            if(left.Count<2)throw new InvalidOperationException("Empty collision silhouette.");
            right.Reverse(); left.AddRange(right);
            var reduced = new List<Vector2> { left[0] }; Simplify(left,0,left.Count-1,mask.step*1.25f,reduced);
            return reduced.ToArray();
        }
        private static void Simplify(List<Vector2> p,int first,int last,float tolerance,List<Vector2> output)
        {
            int split=-1;float distance=tolerance;
            for(int i=first+1;i<last;i++)
            {
                float d=Vector2.Distance(p[i],PhysicsLabCollisionProfile.Closest(p[i],p[first],p[last]));
                if(d>distance){distance=d;split=i;}
            }
            if(split>=0){Simplify(p,first,split,tolerance,output);Simplify(p,split,last,tolerance,output);}
            else output.Add(p[last]);
        }

        public static void Validate(PhysicsLabCollisionProfile p)
        {
            if(p.solids.Length==0 || p.BoundaryCount>512)throw new InvalidOperationException(p.name+": invalid geometry budget.");
            foreach(var path in p.solids.Select(x=>x.points).Concat(p.lid.Length>0?new[]{p.lid}:Array.Empty<Vector2[]>()))
            {
                if(path.Length<3 || path.Any(x=>!float.IsFinite(x.x)||!float.IsFinite(x.y))) throw new InvalidOperationException(p.name+": invalid path.");
                for(int a=0;a<path.Length;a++)for(int b=a+2;b<path.Length;b++)
                {
                    if(a==0&&b==path.Length-1)continue;
                    Vector2 x=path[a],u=path[(a+1)%path.Length]-x,y=path[b],v=path[(b+1)%path.Length]-y;
                    float Cross(Vector2 q,Vector2 r)=>q.x*r.y-q.y*r.x;
                    float denominator=Cross(u,v);if(Mathf.Abs(denominator)<1e-8f)continue;
                    float t=Cross(y-x,v)/denominator,s=Cross(y-x,u)/denominator;
                    if(t>.0001f&&t<.9999f&&s>.0001f&&s<.9999f)throw new InvalidOperationException(p.name+": self-intersecting solid at "+a+","+b);
                }
            }
        }
    }
}
