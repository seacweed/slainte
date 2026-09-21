using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Slainte.Bartending.PhysicsLab.Editor
{
    public static class PhysicsLabBuilder
    {
        public const string Root = "Assets/_Project/Features/Bartending/PhysicsLab";
        public const string ScenePath = Root + "/Scenes/BartendingPhysicsSandbox.unity";
        public const string SettingsPath = Root + "/Data/PhysicsLabLiquidSettings.asset";
        private static PhysicsMaterial2D material;
        private static readonly List<PhysicsLabBody> prefabs = new List<PhysicsLabBody>();

        [MenuItem("Slainte/Physics Lab/Create or Open Sandbox")]
        public static void Open()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null) Build();
            else EditorSceneManager.OpenScene(ScenePath);
        }

        public static void Build()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play Mode before building PhysicsLab assets.");
            foreach (string dir in new[] { "Prefabs", "Scenes", "Data" }) Directory.CreateDirectory(Root + "/" + dir);
            AssetDatabase.Refresh();
            material = AssetDatabase.LoadAssetAtPath<PhysicsMaterial2D>(Root + "/Data/BodySurface.physicsMaterial2D");
            if (material == null)
            {
                material = new PhysicsMaterial2D("PhysicsLab Body Surface") { friction = .45f, bounciness = .05f };
                AssetDatabase.CreateAsset(material, Root + "/Data/BodySurface.physicsMaterial2D");
            }
            PhysicsLabLiquidSettings settings = AssetDatabase.LoadAssetAtPath<PhysicsLabLiquidSettings>(SettingsPath);
            if (settings == null)
            {
                settings = ScriptableObject.CreateInstance<PhysicsLabLiquidSettings>();
                var source = AssetDatabase.LoadAssetAtPath<BusinessBartendingSettings>("Assets/Resources/Bartending/BusinessBartendingSettings.asset");
                if (source != null) JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(source), settings);
                settings.gpuLiquidComputeShader = AssetDatabase.LoadAssetAtPath<ComputeShader>(Root + "/Shaders/PhysicsLabLiquid.compute");
                settings.gpuLiquidParticleCapacity = 4096;
                settings.gpuLiquidWorldMin = new Vector2(-25, -12);
                settings.gpuLiquidWorldMax = new Vector2(25, 20);
                AssetDatabase.CreateAsset(settings, SettingsPath);
            }
            prefabs.Clear();
            foreach (string guid in AssetDatabase.FindAssets("t:ItemDef", new[] { "Assets/Resources/Bartending/Items" }))
            {
                ItemDef item = AssetDatabase.LoadAssetAtPath<ItemDef>(AssetDatabase.GUIDToAssetPath(guid));
                if (item != null && item.type == ItemType.Bottle && item.icon != null) CreateBottle(item);
            }
            foreach (string guid in AssetDatabase.FindAssets("t:GlassDef", new[] { "Assets/Resources/Bartending/ToolCabinet" }))
            {
                GlassDef glass = AssetDatabase.LoadAssetAtPath<GlassDef>(AssetDatabase.GUIDToAssetPath(guid));
                if (glass != null) CreateGlass(glass);
            }
            PhysicsLabBody ice = CreateIce();
            foreach (string guid in AssetDatabase.FindAssets("t:ToolDef", new[] { "Assets/Resources/Bartending/ToolCabinet" }))
            {
                ToolDef tool = AssetDatabase.LoadAssetAtPath<ToolDef>(AssetDatabase.GUIDToAssetPath(guid));
                if (tool != null) CreateTool(tool, ice);
            }
            BuildScene(settings);
            AssetDatabase.SaveAssetIfDirty(settings);
            Debug.Log("[PhysicsLab] Created " + prefabs.Count + " isolated prefabs and " + ScenePath);
        }

        private static PhysicsLabBody NewBody(string name, LabItemKind kind, float mass)
        {
            var root = new GameObject(name);
            root.layer = 29;
            Rigidbody2D rb = root.AddComponent<Rigidbody2D>();
            rb.mass = mass; rb.gravityScale = 1; rb.linearDamping = .05f; rb.angularDamping = .1f;
            rb.interpolation = RigidbodyInterpolation2D.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            PhysicsLabBody body = root.AddComponent<PhysicsLabBody>();
            body.kind = kind; body.displayName = name;
            return body;
        }
        private static SpriteRenderer Visual(PhysicsLabBody body, Sprite sprite, string name, float scale, Vector2 offset, int order, float alpha = 1)
        {
            GameObject visual = new GameObject(name); visual.layer = 29;
            visual.transform.SetParent(body.transform, false);
            visual.transform.localScale = Vector3.one * scale;
            visual.transform.localPosition = offset;
            SpriteRenderer renderer = visual.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite; renderer.sortingOrder = order; renderer.color = new Color(1, 1, 1, alpha);
            return renderer;
        }
        private static void PickBox(PhysicsLabBody body, Rect rect)
        {
            BoxCollider2D c = body.gameObject.AddComponent<BoxCollider2D>();
            c.offset = rect.center; c.size = rect.size; c.isTrigger = true;
            body.pickCollider = c;
        }
        private static Vector2[] Rectangle(Rect r) => new[]
        { new Vector2(r.xMin, r.yMax), new Vector2(r.xMin,r.yMin), new Vector2(r.xMax,r.yMin), new Vector2(r.xMax,r.yMax) };
        private static void SolidHull(PhysicsLabBody body, Vector2[] path)
        {
            PolygonCollider2D hull = body.gameObject.AddComponent<PolygonCollider2D>();
            hull.points = path; hull.sharedMaterial = material;
            body.solidColliders = new Collider2D[] { hull };
            body.liquidWall = path; body.wallClosed = true;
        }
        private static void ExtraHull(PhysicsLabBody body, Vector2[] path)
        {
            var hull = body.gameObject.AddComponent<PolygonCollider2D>();
            hull.points = path; hull.sharedMaterial = material;
            var colliders = new List<Collider2D>(body.solidColliders) { hull };
            body.solidColliders = colliders.ToArray();
            var shapes = new List<PhysicsLabHull>(body.extraSolidHulls) { new PhysicsLabHull { points = path } };
            body.extraSolidHulls = shapes.ToArray();
        }
        private static Vector2 Pixel(Sprite sprite, float x, float y, float scale) =>
            ((Vector2)sprite.bounds.min + Vector2.Scale(sprite.bounds.size, new Vector2(x / 310, y / 590))) * scale;
        private static Vector2[] ClipBelow(Vector2[] path, float y)
        {
            var clipped = new List<Vector2>();
            for (int i = 0; i < path.Length; i++)
            {
                Vector2 a = path[i], b = path[(i + 1) % path.Length];
                if (a.y <= y) clipped.Add(a);
                if ((a.y <= y) != (b.y <= y)) clipped.Add(Vector2.Lerp(a, b, (y - a.y) / (b.y - a.y)));
            }
            return clipped.ToArray();
        }
        private static void VesselWalls(PhysicsLabBody body, Vector2[] path, float thickness = .075f)
        {
            var colliders = new List<Collider2D>();
            for (int i = 0; i < path.Length - 1; i++)
            {
                Vector2 a = path[i], b = path[i + 1];
                if ((b-a).sqrMagnitude < .000001f) continue;
                Vector2 normal = new Vector2(-(b-a).y, (b-a).x).normalized * thickness * .5f;
                Vector2 along = (b-a).normalized * thickness * .2f;
                PolygonCollider2D c = body.gameObject.AddComponent<PolygonCollider2D>();
                c.points = new[] { a-normal-along, a+normal-along, b+normal+along, b-normal+along };
                c.sharedMaterial = material; colliders.Add(c);
            }
            body.solidColliders = colliders.ToArray(); body.liquidWall = path; body.wallClosed = false;
            body.mouthLocal = (path[0] + path[path.Length-1]) * .5f;
            body.rotationPivotLocal = body.mouthLocal;
        }
        private static PhysicsLabBody Save(PhysicsLabBody body, string name)
        {
            GameObject saved = PrefabUtility.SaveAsPrefabAsset(body.gameObject, Root + "/Prefabs/" + name + ".prefab");
            UnityEngine.Object.DestroyImmediate(body.gameObject);
            PhysicsLabBody prefab = saved.GetComponent<PhysicsLabBody>();
            prefabs.Add(prefab); return prefab;
        }
        private static void CreateBottle(ItemDef item)
        {
            PhysicsLabBody body = NewBody(item.displayName, LabItemKind.Bottle, .7f);
            body.ingredient = item; body.capacityMl = body.remainingMl = item.capacityMl;
            Sprite sprite = item.icon;
            float scale = 3.4f / sprite.bounds.size.y;
            Visual(body, sprite, "BottleArt", scale, Vector2.zero, 3);
            Bounds bounds = sprite.bounds;
            Vector2 size = (Vector2)bounds.size * scale;
            Vector2 minimum = (Vector2)bounds.min * scale;
            bool configured = item.overrideBottleGeometry || item.overrideBottleClickCollider;
            Vector2 center = minimum + Vector2.Scale(size, configured ? item.colliderCenterNormalized : new Vector2(.5f,.5f));
            Vector2 colliderSize = Vector2.Scale(size, configured ? item.colliderSizeNormalized : new Vector2(.8f,.95f));
            Rect rect = new Rect(center-colliderSize*.5f, colliderSize);
            SolidHull(body, Rectangle(rect)); PickBox(body, rect);
            body.mouthLocal = minimum + Vector2.Scale(size, item.liquidSpawnNormalized)
                + Vector2.up * (item.liquidSpawnOutwardPixels / sprite.pixelsPerUnit * scale);
            body.rotationPivotLocal = Vector2.Lerp(rect.center, body.mouthLocal, .65f);
            Save(body, "Bottle_" + item.id);
        }
        private static void CreateGlass(GlassDef definition)
        {
            Sprite reference = definition.GetCollisionReferenceSprite();
            if (reference == null || !GlassCollisionProfiles.TryGetByGlassId(definition.glassId, out GlassCollisionProfileDefinition profile)) return;
            PhysicsLabBody body = NewBody(definition.displayName, LabItemKind.Glass, .35f);
            float scale = 3.7f / reference.bounds.size.y;
            Sprite[] layers = definition.GetWorldLayers();
            for (int i = 0; i < layers.Length; i++)
                if (layers[i] != null) Visual(body, layers[i], "GlassArt_"+i, scale, Vector2.zero, i<2 ? -2 : 4, i%2==0 ? .35f : 1);
            Vector2[] path = profile.BuildEdgePath(reference);
            for (int i=0;i<path.Length;i++) path[i] *= scale;
            VesselWalls(body,path);
            // The open liquid bowl excludes the stem and foot; author those as separate solids.
            for (int i = 1; i < profile.InteractionRectPixels.Count; i++)
            {
                Rect stem = profile.InteractionRectPixels[i];
                float bowlBottom = float.MaxValue;
                foreach (Vector2 point in path) bowlBottom = Mathf.Min(bowlBottom, point.y);
                Vector2 min = Pixel(reference, stem.xMin, Mathf.Max(profile.VisibleBottomPixel, stem.yMin), scale);
                Vector2 max = Pixel(reference, stem.xMax, stem.yMax, scale);
                max.y = Mathf.Min(max.y, bowlBottom);
                if (max.y > min.y) ExtraHull(body, Rectangle(Rect.MinMaxRect(min.x, min.y, max.x, max.y)));
            }
            body.contentRegions = new Rect[profile.ContentTriggerPixels.Count];
            for(int i=0;i<body.contentRegions.Length;i++)
            {
                Rect r=profile.BuildContentTrigger(reference,i);
                body.contentRegions[i]=new Rect(r.position*scale,r.size*scale);
            }
            Bounds b = new Bounds(path[0],Vector3.zero); foreach(Vector2 p in path)b.Encapsulate(p);
            foreach (PhysicsLabHull hull in body.extraSolidHulls) foreach (Vector2 p in hull.points) b.Encapsulate(p);
            PickBox(body,new Rect((Vector2)b.min,(Vector2)b.size));
            body.capacityMl = definition.capacityMl; body.remainingMl = 0;
            Save(body,"Glass_"+definition.glassId);
        }
        private static PhysicsLabBody CreateIce()
        {
            PhysicsLabBody body=NewBody("Ice",LabItemKind.Ice,.04f);
            Rect rect=new Rect(-.18f,-.18f,.36f,.36f);
            SolidHull(body,Rectangle(rect));PickBox(body,rect);
            Sprite ice=AssetDatabase.LoadAssetAtPath<Sprite>("Assets/_Project/Features/Bartending/Art/ToolCabinet/Ice/ice_01.png");
            if(ice!=null)Visual(body,ice,"IceArt",.4f/Mathf.Max(ice.bounds.size.x,ice.bounds.size.y),Vector2.zero,5,.85f);
            return Save(body,"Ice");
        }
        private static void CreateTool(ToolDef definition,PhysicsLabBody ice)
        {
            LabItemKind kind=definition.kind==ToolKind.Jigger?LabItemKind.Jigger:
                definition.kind==ToolKind.CobblerShaker?LabItemKind.Shaker:
                definition.kind==ToolKind.BarSpoon?LabItemKind.Spoon:LabItemKind.IceBucket;
            PhysicsLabBody body=NewBody(definition.displayName,kind,kind==LabItemKind.Spoon?.12f:.6f);
            Sprite[] layers=definition.worldLayers;
            Sprite reference=layers.Length>0?layers[0]:null;
            float scale=reference!=null?3.5f/reference.bounds.size.y:1;
            if (kind == LabItemKind.Shaker)
            {
                body.capVisual = new GameObject("RemovableLid");
                body.capVisual.layer = 29;
                body.capVisual.transform.SetParent(body.transform, false);
            }
            for(int i=0;i<layers.Length;i++)
            {
                if(layers[i]==null)continue;
                SpriteRenderer r=Visual(body,layers[i],"ToolArt_"+i,scale,Vector2.zero,i<2?-1:4,i%2==0?.55f:1);
                if(kind==LabItemKind.Shaker && i>=4)r.transform.SetParent(body.capVisual.transform, true);
            }
            if(kind==LabItemKind.Spoon)
            {
                Rect r=new Rect(-.08f,-1.6f,.16f,3.2f);
                SolidHull(body,Rectangle(r));PickBox(body,new Rect(-.23f,-1.6f,.46f,3.2f));
            }
            else
            {
                Vector2[] path;
                Rect[] regions;
                Vector2[] lowerHull = null;
                if(kind==LabItemKind.Jigger && layers.Length>1 && layers[1]!=null)
                {
                    var profile=JiggerCollisionProfiles.Standard30Ml;
                    path=profile.BuildEdgePath(layers[1]);for(int i=0;i<path.Length;i++)path[i]*=scale;
                    regions = new Rect[profile.ContentTriggerPixels.Count];
                    for (int i=0;i<regions.Length;i++)
                    { Rect raw=profile.BuildContentTrigger(layers[1],i);regions[i]=new Rect(raw.position*scale,raw.size*scale); }
                    lowerHull=profile.BuildInteractionPolygon(layers[1]);
                    for(int i=0;i<lowerHull.Length;i++)lowerHull[i]*=scale;
                    float bottom=float.MaxValue;foreach(Vector2 p in path)bottom=Mathf.Min(bottom,p.y);
                    lowerHull=ClipBelow(lowerHull,bottom);
                    body.capacityMl=30;
                }
                else
                {
                    bool bucket=kind==LabItemKind.IceBucket;
                    float top=bucket?486:314;
                    path=new[]{Pixel(reference,bucket?20:53,top,scale),Pixel(reference,bucket?53:78,12,scale),
                        Pixel(reference,155,3,scale),Pixel(reference,bucket?257:232,12,scale),Pixel(reference,bucket?290:257,top,scale)};
                    Vector2 min=Pixel(reference,bucket?65:87,22,scale),max=Pixel(reference,bucket?245:223,top-14,scale);
                    regions=new[]{Rect.MinMaxRect(min.x,min.y,max.x,max.y)};
                }
                VesselWalls(body,path);body.contentRegions=regions;
                if(lowerHull!=null && lowerHull.Length>=3)ExtraHull(body,lowerHull);
                Bounds b=new Bounds(path[0],Vector3.zero);foreach(Vector2 p in path)b.Encapsulate(p);
                if(lowerHull!=null)foreach(Vector2 p in lowerHull)b.Encapsulate(p);
                PickBox(body,new Rect((Vector2)b.min,(Vector2)b.size));
                if(kind==LabItemKind.Shaker)
                {
                    BoxCollider2D cap=body.gameObject.AddComponent<BoxCollider2D>();
                    cap.offset=body.mouthLocal;cap.size=new Vector2(Vector2.Distance(path[0],path[path.Length-1]),.08f);cap.sharedMaterial=material;
                    var list=new List<Collider2D>(body.solidColliders){cap};body.solidColliders=list.ToArray();body.capCollider=cap;body.sealedVessel=true;
                }
                if(kind==LabItemKind.IceBucket){body.icePrefab=ice;body.iceStock=20;body.exitSpeed=.8f;}
            }
            Save(body,kind.ToString());
        }
        private static void BuildScene(PhysicsLabLiquidSettings settings)
        {
            Scene scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            GameObject root=new GameObject("PhysicsLab");
            PhysicsLabWorld world=root.AddComponent<PhysicsLabWorld>();
            PhysicsLabGpuLiquid gpu=root.AddComponent<PhysicsLabGpuLiquid>();world.liquid=gpu;
            gpu.settings=settings;gpu.drawShader=AssetDatabase.LoadAssetAtPath<Shader>(Root+"/Shaders/PhysicsLabLiquid.shader");
            PhysicsLabInteractor input=root.AddComponent<PhysicsLabInteractor>();world.interactor=input;input.world=world;
            Camera camera=new GameObject("PhysicsLabCamera").AddComponent<Camera>();
            camera.transform.position=new Vector3(0,2,-20);camera.orthographic=true;camera.orthographicSize=8;
            camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.055f,.07f,.09f);camera.cullingMask=1<<29;
            input.inputCamera=camera;gpu.outputCamera=camera;
            GameObject floor=new GameObject("PhysicalTable");floor.layer=29;floor.transform.SetParent(root.transform);
            floor.transform.position=new Vector3(0,-3.5f,0);
            BoxCollider2D floorCollider=floor.AddComponent<BoxCollider2D>();floorCollider.size=new Vector2(40,.5f);floorCollider.sharedMaterial=material;
            world.floorColliders=new Collider2D[]{floorCollider};
            // Static screen boundaries use the project's default game-view aspect ratio.
            float halfWidth=camera.orthographicSize*PlayerSettings.defaultScreenWidth/PlayerSettings.defaultScreenHeight;
            float top=camera.transform.position.y+camera.orthographicSize;
            float bottom=floor.transform.position.y-floorCollider.size.y*.5f;
            const float wallThickness=.5f;
            float wallHeight=top+wallThickness-bottom;
            float wallCenterY=bottom+wallHeight*.5f;
            CreateBoundaryCollider(root.transform,"LeftBoundary",new Vector2(-halfWidth-wallThickness*.5f,wallCenterY),new Vector2(wallThickness,wallHeight));
            CreateBoundaryCollider(root.transform,"RightBoundary",new Vector2(halfWidth+wallThickness*.5f,wallCenterY),new Vector2(wallThickness,wallHeight));
            CreateBoundaryCollider(root.transform,"TopBoundary",new Vector2(0,top+wallThickness*.5f),new Vector2((halfWidth+wallThickness)*2,wallThickness));
            LineRenderer line=floor.AddComponent<LineRenderer>();line.positionCount=2;
            line.SetPositions(new[]{new Vector3(-20,-3.25f,0),new Vector3(20,-3.25f,0)});line.startWidth=line.endWidth=.09f;
            var lineMaterial=new Material(Shader.Find("Sprites/Default"));lineMaterial.color=new Color(.55f,.65f,.45f);
            string materialPath=Root+"/Data/TableLine.mat";
            Material saved=AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if(saved==null){AssetDatabase.CreateAsset(lineMaterial,materialPath);saved=lineMaterial;}else UnityEngine.Object.DestroyImmediate(lineMaterial);
            line.sharedMaterial=saved;
            var chosen=new List<PhysicsLabBody>();
            int bottles=0,glasses=0;
            foreach(PhysicsLabBody prefab in prefabs)
            {
                if(prefab.kind==LabItemKind.Bottle && bottles++<3)chosen.Add(prefab);
                else if(prefab.kind==LabItemKind.Glass && glasses++<2)chosen.Add(prefab);
                else if(prefab.kind!=LabItemKind.Bottle && prefab.kind!=LabItemKind.Glass && prefab.kind!=LabItemKind.Ice)chosen.Add(prefab);
            }
            float x=-11;
            foreach(PhysicsLabBody prefab in chosen)
            {
                GameObject item=(GameObject)PrefabUtility.InstantiatePrefab(prefab.gameObject,root.transform);
                item.transform.position=new Vector3(x,0,0);x+=2.7f;
            }
            EditorSceneManager.SaveScene(scene,ScenePath);
        }

        private static void CreateBoundaryCollider(Transform parent,string name,Vector2 position,Vector2 size)
        {
            var boundary=new GameObject(name);boundary.layer=29;
            boundary.transform.SetParent(parent,false);boundary.transform.localPosition=position;
            BoxCollider2D collider=boundary.AddComponent<BoxCollider2D>();
            collider.size=size;collider.sharedMaterial=material;
        }
    }
}
