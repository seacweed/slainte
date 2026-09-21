using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Slainte.Bartending.PhysicsLab.Editor
{
    [CustomEditor(typeof(PhysicsLabBody))]
    public sealed class PhysicsLabBodyEditor : UnityEditor.Editor
    {
        private void OnSceneGUI()
        {
            var body = (PhysicsLabBody)target;
            foreach(var c in body.solidColliders)
            {
                if(c==null || !c.enabled)continue;
                Handles.color=Color.green;
                foreach(var path in PhysicsLabGeometryPreview.ColliderPaths(c))
                    Handles.DrawAAPolyLine(3,path.Concat(new[]{path[0]}).Select(p=>c.transform.TransformPoint(p+c.offset)).ToArray());
            }
            Handles.color=Color.cyan;
            if(body.collisionProfile!=null)
            {
                foreach(var hull in body.collisionProfile.solids)Draw(body,hull.points,true);
                if(body.sealedVessel && body.collisionProfile.lid.Length>0)Draw(body,body.collisionProfile.lid,true);
                Handles.color=new Color(1,.4f,0);Draw(body,body.collisionProfile.interior,true);
            }
            else { Draw(body,body.liquidWall,body.wallClosed);foreach(var hull in body.extraSolidHulls)Draw(body,hull.points,true); }
            if(body.pickCollider!=null)
            {
                Handles.color=Color.yellow;
                foreach(var path in PhysicsLabGeometryPreview.ColliderPaths(body.pickCollider))
                    Handles.DrawAAPolyLine(1,path.Concat(new[]{path[0]}).Select(p=>body.pickCollider.transform.TransformPoint(p+body.pickCollider.offset)).ToArray());
            }
        }
        private static void Draw(PhysicsLabBody body,Vector2[] p,bool closed)
        {
            if(p.Length<2)return;
            Handles.DrawAAPolyLine(1,(closed?p.Concat(new[]{p[0]}):p).Select(x=>body.transform.TransformPoint(x)).ToArray());
        }
    }

    public static class PhysicsLabGeometryPreview
    {
        public static IEnumerable<Vector2[]> ColliderPaths(Collider2D collider)
        {
            if(collider is PolygonCollider2D polygon)
                for(int i=0;i<polygon.pathCount;i++)yield return polygon.GetPath(i);
            else if(collider is BoxCollider2D box)
            {
                Vector2 h=box.size*.5f;
                yield return new[]{new Vector2(-h.x,h.y),-h,new Vector2(h.x,-h.y),h};
            }
        }
        public static void CaptureAtlas(string filename)
        {
            var scene=EditorSceneManager.NewPreviewScene();
            var cameraObject=new GameObject("GeometryPreviewCamera");SceneManager.MoveGameObjectToScene(cameraObject,scene);
            var camera=cameraObject.AddComponent<Camera>();camera.scene=scene;
            camera.orthographic=true;camera.orthographicSize=9;camera.transform.position=new Vector3(0,0,-20);
            camera.backgroundColor=new Color(.05f,.065f,.08f);camera.clearFlags=CameraClearFlags.SolidColor;
            var material=new Material(Shader.Find("Sprites/Default"));
            var target=new RenderTexture(1800,1800,24);var old=RenderTexture.active;
            Texture2D image=null;
            try
            {
                var paths=AssetDatabase.FindAssets("t:Prefab",new[]{PhysicsLabBuilder.Root+"/Prefabs"})
                    .Select(AssetDatabase.GUIDToAssetPath).OrderBy(x=>x).ToArray();
                for(int i=0;i<paths.Length;i++)
                {
                    var root=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(paths[i]));
                    SceneManager.MoveGameObjectToScene(root,scene);
                    root.transform.position=new Vector3((i%6-2.5f)*2.8f,(1.5f-i/6)*4.3f,0);
                    var body=root.GetComponent<PhysicsLabBody>();root.GetComponent<Rigidbody2D>().simulated=false;
                    foreach(var collider in body.solidColliders)
                        if(collider!=null && collider.enabled)
                            foreach(var path in ColliderPaths(collider))Line(root,path.Select(p=>(Vector2)root.transform.InverseTransformPoint(collider.transform.TransformPoint(p+collider.offset))).ToArray(),true,Color.green,.022f);
                    if(body.collisionProfile!=null)
                    {
                        foreach(var hull in body.collisionProfile.solids)Line(root,hull.points,true,Color.cyan,.009f);
                        if(body.sealedVessel && body.collisionProfile.lid.Length>0)Line(root,body.collisionProfile.lid,true,Color.cyan,.009f);
                        Line(root,body.collisionProfile.interior,true,new Color(1,.4f,0),.009f);
                    }
                    else {Line(root,body.liquidWall,body.wallClosed,Color.cyan,.009f);foreach(var hull in body.extraSolidHulls)Line(root,hull.points,true,Color.cyan,.009f);}
                }
                void Line(GameObject root,Vector2[] points,bool closed,Color color,float width)
                {
                    if(points.Length<2)return;
                    var go=new GameObject("Outline");go.transform.SetParent(root.transform,false);
                    var line=go.AddComponent<LineRenderer>();line.useWorldSpace=false;line.loop=closed;line.positionCount=points.Length;
                    line.SetPositions(points.Select(p=>(Vector3)p).ToArray());line.startWidth=line.endWidth=width;
                    line.sharedMaterial=material;line.startColor=line.endColor=color;line.sortingOrder=30;
                }
                camera.targetTexture=target;camera.Render();RenderTexture.active=target;
                image=new Texture2D(1800,1800,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,1800,1800),0,0);image.Apply();
                Directory.CreateDirectory(PhysicsLabValidator.EvidenceDirectory);
                File.WriteAllBytes(Path.Combine(PhysicsLabValidator.EvidenceDirectory,filename),image.EncodeToPNG());
            }
            finally
            {
                RenderTexture.active=old;camera.targetTexture=null;target.Release();Object.DestroyImmediate(target);
                if(image!=null)Object.DestroyImmediate(image);Object.DestroyImmediate(material);EditorSceneManager.ClosePreviewScene(scene);
            }
        }
    }
}
