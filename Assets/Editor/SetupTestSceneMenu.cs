using UnityEngine;
using UnityEditor;
using Slainte.Bartending;

public class SetupTestSceneMenu
{
    [MenuItem("Slainte/Setup Bottle Test Environment")]
    public static void SetupTestEnvironment()
    {
        // 1. Create Liquid Pool
        GameObject poolObj = new GameObject("LiquidPoolManager");
        LiquidPool pool = poolObj.AddComponent<LiquidPool>();
        GameObject particlePrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/MetaballFluid/Prefabs/water_particle.prefab");
        if (particlePrefab != null)
        {
            pool.particlePrefab = particlePrefab;
        }
        else
        {
            Debug.LogError("Could not find water_particle.prefab!");
        }
        pool.poolSize = 300;

        // 2. Create Slot
        GameObject slotObj = new GameObject("TestSlot");
        slotObj.transform.position = new Vector3(0, -2, 0);
        BoxCollider2D box = slotObj.AddComponent<BoxCollider2D>();
        box.size = new Vector2(2, 0.5f);
        
        int slotLayer = LayerMask.NameToLayer("Slot");
        if (slotLayer == -1)
        {
            Debug.LogWarning("Layer 'Slot' does not exist. Using Default layer instead. Please add 'Slot' to your layers for accurate testing.");
            slotLayer = 0;
        }
        slotObj.layer = slotLayer;

        SpriteRenderer sr = slotObj.AddComponent<SpriteRenderer>();
        sr.color = Color.gray;

        // 3. Create Bottle
        GameObject bottlePrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Bottle.prefab");
        if (bottlePrefab != null)
        {
            GameObject bottleObj = PrefabUtility.InstantiatePrefab(bottlePrefab) as GameObject;
            bottleObj.transform.position = new Vector3(0, 1, 0);

            BottleController controller = bottleObj.GetComponent<BottleController>();
            if (controller != null)
            {
                // Create spawn point
                GameObject spawnPoint = new GameObject("LiquidSpawnPoint");
                spawnPoint.transform.SetParent(bottleObj.transform);
                spawnPoint.transform.localPosition = new Vector3(0, 1.5f, 0); // 대략적인 병 입구 위치

                // Set references via SerializedObject to modify prefab instance correctly
                SerializedObject so = new SerializedObject(controller);
                so.Update();
                so.FindProperty("liquidSpawnPoint").objectReferenceValue = spawnPoint.transform;
                so.FindProperty("slotLayer").intValue = 1 << slotLayer;
                so.ApplyModifiedProperties();
            }
        }
        else
        {
            Debug.LogError("Could not find Bottle.prefab at Assets/Prefabs/Bottle.prefab");
        }

        // 4. Create Metaball Renderer (FullScreen Quad + Material)
        GameObject quadObj = GameObject.CreatePrimitive(PrimitiveType.Quad);
        quadObj.name = "MetaballQuad";
        quadObj.transform.position = new Vector3(0, 0, 10f); // 카메라 뒤쪽이나 특정 위치로 조정 (테스트용)
        
        Material metaballMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/MetaballFluid/Graphics/MetaballMat.mat");
        if (metaballMat != null)
        {
            quadObj.GetComponent<MeshRenderer>().material = metaballMat;
        }

        // 첨부해온 FullSizeQuad 컴포넌트 추가
        quadObj.AddComponent<FullScreenQuad>();

        Debug.Log("✅ Test Environment Setup Complete! You can now play the scene and test the bottle.");
    }
}
