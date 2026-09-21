using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Slainte.Bartending.PhysicsLab.Editor
{
    public sealed partial class PhysicsLabValidationRunner
    {
        private void ValidateCollisionProfiles()
        {
            Vector2 gravity=Physics2D.gravity; Physics2D.gravity=Vector2.zero;
            var ingredient=world.Items.First(x=>x.ingredient!=null).ingredient;
            var probe=new GameObject("PhysicalContactProbe");var circle=probe.AddComponent<CircleCollider2D>();circle.radius=gpu.Radius;
            PhysicsLabBody body=null;
            int count=0,edges=0,airCases=0;
            try
            {
                foreach(string path in AssetDatabase.FindAssets("t:Prefab",new[]{PhysicsLabBuilder.Root+"/Prefabs"}).Select(AssetDatabase.GUIDToAssetPath).OrderBy(x=>x))
                {
                    body=Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(path),world.transform).GetComponent<PhysicsLabBody>();
                    body.pourMlPerSecond=0;body.Body.bodyType=RigidbodyType2D.Kinematic;
                    var profile=body.collisionProfile;Require(profile!=null,"Authored sprite collision profile: "+body.name);
                    PhysicsLabGeometryAuthoring.Validate(profile);edges+=profile.BoundaryCount;
                    var polygon=(PolygonCollider2D)body.solidColliders[0];
                    Require(profile.solids.Length==polygon.pathCount && profile.solids.Select((h,i)=>h.points.SequenceEqual(polygon.GetPath(i))).All(x=>x),
                        "Body collider uses the authoritative solid surface: "+body.name);
                    foreach(float angle in new[]{0f,67f})
                    {
                        body.Teleport(new Vector2(12,7),angle);Physics2D.SyncTransforms();
                        // Probe an exterior edge with a real CircleCollider2D, then let the GPU resolve the identical disk.
                        var pathPoints=profile.solids[0].points;
                        Vector2 a=body.LocalToWorld(pathPoints[0]),b=body.LocalToWorld(pathPoints[1]);
                        Vector2 outward=new Vector2((b-a).y,-(b-a).x).normalized;
                        Vector2 start=(a+b)*.5f+outward*gpu.Radius*.45f;
                        probe.transform.position=start;Physics2D.SyncTransforms();
                        Require(Physics2D.Distance(circle,polygon).isOverlapped,"CPU disk touches sprite boundary at "+angle+" degrees: "+body.name);
                        gpu.ResetSimulation();gpu.TryEmit(start,Vector2.zero,ingredient,.5f,0);gpu.Step(.0001f);gpu.ReadbackNow();
                        Vector2 end=gpu.Snapshot.First(x=>x.Active!=0).Position;
                        probe.transform.position=end;Physics2D.SyncTransforms();
                        float separation=Physics2D.Distance(circle,polygon).distance;
                        float geometricGap=body.SolidClearance(PhysicsLabBody.Rotate(end-body.Position,-body.Angle))-gpu.Radius;
                        results.Add("CONTACT: "+body.name+" / angle="+angle+" / start="+start.ToString("F6")+" / end="+end.ToString("F6")
                            +" / CPU gap="+separation.ToString("F6")+" / geometric gap="+geometricGap.ToString("F6"));
                        Require(Vector2.Distance(start,end)>.01f && Mathf.Abs(geometricGap)<.004f
                            && Mathf.Abs(separation)<Physics2D.defaultContactOffset+.002f,
                            "GPU disk meets authored vertices and CPU contact within the engine contact offset at "+angle+" degrees: "+body.name);
                    }
                    if(body.kind==LabItemKind.Bottle || body.kind==LabItemKind.Spoon)
                    {
                        body.Teleport(new Vector2(12,7),0);
                        var box=(BoxCollider2D)body.pickCollider;bool found=false;
                        for(int y=1;y<19&&!found;y++)for(int x=1;x<19&&!found;x++)
                        {
                            Vector2 local=box.offset-box.size*.5f+Vector2.Scale(box.size,new Vector2(x/20f,y/20f));
                            if(body.SolidClearance(local)<gpu.Radius*1.5f)continue;
                            Vector2 start=body.LocalToWorld(local);
                            gpu.ResetSimulation();gpu.TryEmit(start,Vector2.zero,ingredient,.5f,0);gpu.Step(.0001f);gpu.ReadbackNow();
                            Require(Vector2.Distance(start,gpu.Snapshot.First(p=>p.Active!=0).Position)<.0001f,
                                "Transparent space inside the pick box does not collide: "+body.name);
                            airCases++;found=true;
                        }
                    }
                    if(body.name.StartsWith("Bottle_item_1001") || body.name.StartsWith("Bottle_item_1014"))
                    {
                        // Windows inside the visibly transparent handles, independent of the generated paths.
                        Rect window=body.name.StartsWith("Bottle_item_1001")?new Rect(.12f,.25f,.35f,.5f):new Rect(.52f,-.4f,.16f,.6f);
                        Vector2 local=window.center;float clearance=float.NegativeInfinity;
                        for(int y=0;y<30;y++)for(int x=0;x<30;x++)
                        {
                            Vector2 candidate=window.min+Vector2.Scale(window.size,new Vector2(x/29f,y/29f));
                            float distance=body.SolidClearance(candidate);
                            if(distance>clearance){local=candidate;clearance=distance;}
                        }
                        Require(clearance>0 && profile.solids.Length>1,"Visible handle hole is retained by compound solids: "+body.name);
                        foreach(float angle in new[]{0f,67f})
                        {
                            body.Teleport(new Vector2(12,7),angle);Physics2D.SyncTransforms();Vector2 start=body.LocalToWorld(local);
                            Require(!polygon.OverlapPoint(start),"CPU collider leaves the handle hole open at "+angle+" degrees: "+body.name);
                            if(clearance<gpu.Radius+.01f)continue; // A narrow hole must still block a disk wider than the gap.
                            gpu.ResetSimulation();gpu.TryEmit(start,Vector2.zero,ingredient,.5f,0);gpu.Step(.0001f);gpu.ReadbackNow();
                            Require(Vector2.Distance(start,gpu.Snapshot.First(p=>p.Active!=0).Position)<.0001f,
                                "GPU disk fits through the transparent handle at "+angle+" degrees: "+body.name);
                        }
                    }
                    if(body.IsVessel)
                    {
                        foreach(float angle in new[]{0f,67f})
                        {
                            body.Teleport(new Vector2(12,7),angle);body.SetSealed(false);Physics2D.SyncTransforms();
                            Vector2 rim=body.LocalToWorld(profile.mouth);
                            probe.transform.position=rim;Physics2D.SyncTransforms();
                            Require(!Physics2D.Distance(circle,polygon).isOverlapped,"Open vessel rim leaves room for a liquid disk at "+angle+" degrees: "+body.name);
                            gpu.ResetSimulation();gpu.TryEmit(rim,Vector2.zero,ingredient,.5f,0);gpu.Step(.0001f);gpu.ReadbackNow();
                            Require(Vector2.Distance(rim,gpu.Snapshot.First(p=>p.Active!=0).Position)<.0001f,
                                "Ownership rim has no GPU collision at "+angle+" degrees: "+body.name);
                            if(profile.lid.Length==0)continue;
                            body.SetSealed(true);
                            // This corner is inside the broad-phase AABB but outside the tapered bowl.
                            Vector2 escaped=profile.InteriorBounds.min+Vector2.one*.005f;
                            gpu.ResetSimulation();gpu.TryEmit(body.LocalToWorld(escaped),Vector2.zero,ingredient,.5f,body.Id);
                            gpu.Step(.0001f);gpu.ReadbackNow();var recovered=gpu.Snapshot.First(p=>p.Active!=0);
                            Require(recovered.VesselId==body.Id && body.ContainsLiquidDisk(body.WorldToLocal(recovered.Position),gpu.Radius*.98f),
                                "Sealed vessel recovery uses the actual inner contour at "+angle+" degrees: "+body.name);
                        }
                    }
                    body.gameObject.SetActive(false);Destroy(body.gameObject);body=null;count++;
                }
                Require(count==24 && edges<2048 && airCases>=10,"All 24 profiles fit the GPU edge budget and exercise former invisible collisions (edges="+edges+")");
            }
            finally
            {
                if(body!=null){body.gameObject.SetActive(false);Destroy(body.gameObject);}
                Destroy(probe);Physics2D.gravity=gravity;gpu.ResetSimulation();
            }
        }
    }
}
