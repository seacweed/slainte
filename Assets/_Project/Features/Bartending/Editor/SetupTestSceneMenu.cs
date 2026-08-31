using UnityEngine;
using UnityEditor;
using Slainte.Bartending;
using Slainte.Bartending.EditorTools;
using Slainte.Editor;

public class SetupTestSceneMenu
{
    [MenuItem("Slainte/Setup Bottle Test Environment")]
    public static void SetupTestEnvironment()
    {
        // 0. 액체 입자 프리팹들의 물리/소팅 성능을 원천 강제 교정 (뚫림 및 정렬 버그 격파!)
        FixLiquidParticlesPhysicsAndSorting();

        // Beaker 프리팹 로드 (이미 존재하면 덮어쓰지 않고 절대 보존!)
        GameObject beakerPrefab = CreateOrGetBeakerPrefab();
        if (beakerPrefab == null)
        {
            Debug.LogError("Beaker 프리팹 로드 실패!");
            return;
        }

        // 1. Create Liquid Pool
        GameObject poolObj = GameObject.Find("LiquidPoolManager");
        if (poolObj == null)
        {
            poolObj = new GameObject("LiquidPoolManager");
        }
        LiquidPool pool = poolObj.GetComponent<LiquidPool>();
        if (pool == null)
        {
            pool = poolObj.AddComponent<LiquidPool>();
        }
        
        GameObject particlePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
            MetaballFluidAssetPaths.WaterParticlePrefab);
        if (particlePrefab != null)
        {
            pool.particlePrefab = particlePrefab;
        }
        else
        {
            Debug.LogError("Could not find water_particle.prefab!");
        }

        // 사용자 풀사이즈(Pool Size) 설정 완벽 보존
        if (pool.poolSize <= 0)
        {
            pool.poolSize = 300;
        }
        else
        {
            Debug.Log($"🔧 기존에 세팅된 소중한 Pool Size ({pool.poolSize}) 설정을 그대로 유지 및 보존합니다.");
        }

        // 2. Create Bottle Slot (SlotController 탑재 스마트 지능형 개방 슬롯으로 업그레이드!)
        GameObject slotObj = GameObject.Find("TestSlot");
        if (slotObj == null)
        {
            slotObj = new GameObject("TestSlot");
            slotObj.transform.position = new Vector3(-2.5f, -2f, 0); // 위치 좌측 정렬
            
            BoxCollider2D box = slotObj.AddComponent<BoxCollider2D>();
            box.size = new Vector2(2, 0.5f);
            box.isTrigger = true; // 트리거 활성화
            
            int slotLayer = LayerMask.NameToLayer("Slot");
            if (slotLayer == -1)
            {
                Debug.LogWarning("Layer 'Slot' does not exist. Using Default layer instead.");
                slotLayer = 0;
            }
            slotObj.layer = slotLayer;

            SpriteRenderer sr = slotObj.AddComponent<SpriteRenderer>();
            sr.color = Color.gray;

            // 스마트 슬롯 장착
            slotObj.AddComponent<SlotController>();
        }
        else
        {
            if (slotObj.GetComponent<SlotController>() == null)
            {
                slotObj.AddComponent<SlotController>();
            }
        }

        // [🚨 비커 전용 안착 슬롯 배치 및 SlotController 장착!]
        GameObject beakerSlotObj = GameObject.Find("BeakerSlot");
        if (beakerSlotObj == null)
        {
            beakerSlotObj = new GameObject("BeakerSlot");
            beakerSlotObj.transform.position = new Vector3(1.5f, -2f, 0); // 우측 배치
            
            BoxCollider2D box = beakerSlotObj.AddComponent<BoxCollider2D>();
            box.size = new Vector2(2, 0.5f);
            box.isTrigger = true; // 트리거 활성화
            
            int slotLayer = LayerMask.NameToLayer("Slot");
            if (slotLayer == -1) slotLayer = 0;
            beakerSlotObj.layer = slotLayer;

            SpriteRenderer sr = beakerSlotObj.AddComponent<SpriteRenderer>();
            sr.color = Color.gray;

            // 스마트 슬롯 장착
            beakerSlotObj.AddComponent<SlotController>();
        }
        else
        {
            if (beakerSlotObj.GetComponent<SlotController>() == null)
            {
                beakerSlotObj.AddComponent<SlotController>();
            }
        }

        // 3. Create Bottle
        GameObject bottlePrefab =
            AssetDatabase.LoadAssetAtPath<GameObject>(BartendingAssetPaths.BottlePrefab);
        if (bottlePrefab != null)
        {
            GameObject bottleObj = GameObject.Find("BottleInstance");
            if (bottleObj == null)
            {
                bottleObj = PrefabUtility.InstantiatePrefab(bottlePrefab) as GameObject;
                bottleObj.name = "BottleInstance";
            }
            bottleObj.transform.position = new Vector3(-2.5f, 1f, 0);

            BottleController controller = bottleObj.GetComponent<BottleController>();
            if (controller != null)
            {
                Transform spawnPoint = bottleObj.transform.Find("LiquidSpawnPoint");
                if (spawnPoint == null)
                {
                    GameObject spawnPointObj = new GameObject("LiquidSpawnPoint");
                    spawnPointObj.transform.SetParent(bottleObj.transform);
                    spawnPointObj.transform.localPosition = new Vector3(0, 1.5f, 0);
                    spawnPoint = spawnPointObj.transform;
                }

                int slotLayer = LayerMask.NameToLayer("Slot");
                if (slotLayer == -1) slotLayer = 0;

                SerializedObject so = new SerializedObject(controller);
                so.Update();
                so.FindProperty("liquidSpawnPoint").objectReferenceValue = spawnPoint;
                so.FindProperty("slotLayer").intValue = 1 << slotLayer;
                so.ApplyModifiedProperties();
            }
        }
        else
        {
            Debug.LogError("Could not find Bottle.prefab!");
        }

        // 4. Beaker 인스턴스 씬에 스폰 (Z = 0f)
        GameObject beakerInstance = GameObject.Find("BeakerInstance");
        if (beakerInstance == null)
        {
            beakerInstance = PrefabUtility.InstantiatePrefab(beakerPrefab) as GameObject;
            beakerInstance.name = "BeakerInstance";
        }
        
        // 사용자가 맞춰둔 마음에 드는 프리팹 수치와 세팅은 100% 완벽 보존하고,
        // 씬에서 부모 관계(캔버스 자식)만 떼어내서 월드 스페이스 루트로 완벽하게 독립시킵니다.
        beakerInstance.transform.SetParent(null); 
        beakerInstance.transform.position = new Vector3(1.5f, -1f, 0f); // 우측 배치

        // 비커 인스턴스의 픽업 슬롯 감지 레이어 보정 갱신
        BeakerController instController = beakerInstance.GetComponent<BeakerController>();
        if (instController != null)
        {
            int slotLayer = LayerMask.NameToLayer("Slot");
            if (slotLayer == -1) slotLayer = 0;
            
            SerializedObject soInst = new SerializedObject(instController);
            soInst.Update();
            soInst.FindProperty("slotLayer").intValue = 1 << slotLayer;
            soInst.ApplyModifiedProperties();
        }

        // [🍷 범용 잔(Glass) 인스턴스 씬에 스폰 및 셋업!]
        GameObject glassPrefab = CreateOrGetGlassPrefab();
        if (glassPrefab != null)
        {
            GameObject glassInstance = GameObject.Find("GlassInstance");
            if (glassInstance == null)
            {
                glassInstance = PrefabUtility.InstantiatePrefab(glassPrefab) as GameObject;
                glassInstance.name = "GlassInstance";
            }
            
            glassInstance.transform.SetParent(null); 
            glassInstance.transform.position = new Vector3(4.0f, -1f, 0f); // 비커 우측에 정렬 배치
            
            GlassController gInstController = glassInstance.GetComponent<GlassController>();
            if (gInstController != null)
            {
                int slotLayer = LayerMask.NameToLayer("Slot");
                if (slotLayer == -1) slotLayer = 0;
                
                SerializedObject soInst = new SerializedObject(gInstController);
                soInst.Update();
                soInst.FindProperty("slotLayer").intValue = 1 << slotLayer;
                
                // [온더락 정밀 락글래스 기하수치 동적 동기화]
                soInst.FindProperty("bottomWidth").floatValue = 2.0f;
                soInst.FindProperty("topWidth").floatValue = 2.2f;
                soInst.FindProperty("height").floatValue = 2.0f;
                soInst.FindProperty("cornerRadius").floatValue = 0.4f;
                
                soInst.ApplyModifiedProperties();
                
                // 디폴트 수평 직선 복원
                gInstController.glassProfile = AnimationCurve.Constant(0f, 1f, 1f);
                
                // [실시간 스프라이트 갱신 보완] 씬에 스폰된 GlassInstance의 자식들을 glass_rock 이미지들로 강제 업데이트
                Sprite s01 = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Sprites/UI/glass_rock/glass_rock_01.png");
                Sprite s02 = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Sprites/UI/glass_rock/glass_rock_02.png");
                Sprite s03 = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Sprites/UI/glass_rock/glass_rock_03.png");

                Transform t01 = glassInstance.transform.Find("Sprite_01");
                if (t01 != null && s01 != null) t01.GetComponent<SpriteRenderer>().sprite = s01;
                Transform t02 = glassInstance.transform.Find("Sprite_02");
                if (t02 != null && s02 != null) t02.GetComponent<SpriteRenderer>().sprite = s02;
                Transform t03 = glassInstance.transform.Find("Sprite_03");
                if (t03 != null && s03 != null) t03.GetComponent<SpriteRenderer>().sprite = s03;

                gInstController.GenerateCurvedCollider();
            }
        }

        // 5. Create Metaball Renderer (MeshRenderer 기반 3D Quad)
        GameObject quadObj = GameObject.Find("MetaballQuad");
        if (quadObj != null)
        {
            Object.DestroyImmediate(quadObj); // 기존 쿼드 제거
        }

        quadObj = GameObject.CreatePrimitive(PrimitiveType.Quad);
        quadObj.name = "MetaballQuad";
        quadObj.transform.position = new Vector3(0, 0, -0.5f);
        
        MeshCollider meshCol = quadObj.GetComponent<MeshCollider>();
        if (meshCol != null)
        {
            Object.DestroyImmediate(meshCol);
        }

        Material metaballMat = AssetDatabase.LoadAssetAtPath<Material>(
            MetaballFluidAssetPaths.MetaballMaterial);
        if (metaballMat != null)
        {
            quadObj.GetComponent<MeshRenderer>().material = metaballMat;
        }

        // FullScreenQuad 장착 및 2D 렌더 소팅 지정 (Order = 12)
        FullScreenQuad fsq = quadObj.AddComponent<FullScreenQuad>();
        fsq.sortingLayerName = "Default";
        fsq.sortingOrder = 12;

        Debug.Log("✅ Test Environment Setup Complete! Beaker & Liquid layering system works perfectly now.");
    }

    private static void FixLiquidParticlesPhysicsAndSorting()
    {
        string[] particlePaths = new string[]
        {
            MetaballFluidAssetPaths.WaterParticlePrefab,
            MetaballFluidAssetPaths.BlueLiquidPrefab,
            MetaballFluidAssetPaths.GreenLiquidPrefab,
            MetaballFluidAssetPaths.RedLiquidPrefab
        };

        foreach (string path in particlePaths)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab != null)
            {
                bool isDirty = false;
                
                // 1. SpriteRenderer Sorting Order 강제 보정 (12)
                SpriteRenderer sr = prefab.GetComponent<SpriteRenderer>();
                if (sr != null && sr.sortingOrder != 12)
                {
                    sr.sortingOrder = 12;
                    isDirty = true;
                }

                // 2. Rigidbody2D Collision Detection Mode 복구 (Discrete로 롤백하여 렉 완벽 퇴치!)
                Rigidbody2D rb = prefab.GetComponent<Rigidbody2D>();
                if (rb != null && rb.collisionDetectionMode != CollisionDetectionMode2D.Discrete)
                {
                    rb.collisionDetectionMode = CollisionDetectionMode2D.Discrete;
                    isDirty = true;
                }

                if (isDirty)
                {
                    EditorUtility.SetDirty(prefab);
                    Debug.Log($"🔧 {prefab.name} 프리팹의 물리 및 소팅 셋업 최적화 완료!");
                }
            }
        }
        AssetDatabase.SaveAssets();
    }

    private static GameObject CreateOrGetBeakerPrefab()
    {
        string prefabPath = BartendingAssetPaths.BeakerPrefab;
        GameObject existingPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);

        // 소중한 사용자 비커 프리셋 100% 영구 보존
        if (existingPrefab != null)
        {
            Debug.Log("📦 [보존] 이미 존재하고 사용자가 정교하게 맞춘 Beaker 프리팹 파일 데이터를 그대로 사용합니다.");
            return existingPrefab;
        }
        
        // 프리팹이 아예 없을 때만 최초 1회 조립 생성 수행
        GameObject root = new GameObject("Beaker");
        root.transform.position = Vector3.zero;

        Rigidbody2D rb = root.AddComponent<Rigidbody2D>();
        rb.bodyType = RigidbodyType2D.Kinematic;
        rb.useFullKinematicContacts = true;
        
        BoxCollider2D touchCol = root.AddComponent<BoxCollider2D>();
        touchCol.size = new Vector2(1.8f, 2.4f);
        touchCol.isTrigger = true;

        root.AddComponent<DraggableBar>();

        // 비커 컨트롤러 장착 및 사다리꼴 매개변수 빌드
        BeakerController controller = root.AddComponent<BeakerController>();
        controller.bottomWidth = 1.4f;
        controller.topWidth = 1.8f;
        controller.height = 2.4f;
        controller.cornerRadius = 0.3f;
        controller.curveSegments = 12;
        controller.edgeRadius = 0.08f; // 물리 두께 디폴트 세팅

        // 인터랙션 필드 기본값 세팅 (최초 생성 에디터 연동)
        int slotLayer = LayerMask.NameToLayer("Slot");
        if (slotLayer == -1) slotLayer = 0;
        
        SerializedObject so = new SerializedObject(controller);
        so.Update();
        so.FindProperty("slotLayer").intValue = 1 << slotLayer;
        so.FindProperty("maxTiltAngle").floatValue = 120f;
        so.FindProperty("tiltSensitivity").floatValue = 0.5f;
        so.FindProperty("returnSpeed").floatValue = 0.8f;
        so.ApplyModifiedProperties();

        Sprite s01 = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Sprites/UI/beaker/beaker_01.png");
        Sprite s02 = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Sprites/UI/beaker/beaker_02.png");
        Sprite s03 = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Sprites/UI/beaker/beaker_03.png");

        GameObject g01 = new GameObject("Sprite_01");
        g01.transform.SetParent(root.transform);
        g01.transform.localPosition = Vector3.zero;
        SpriteRenderer sr01 = g01.AddComponent<SpriteRenderer>();
        sr01.sprite = s01;
        sr01.sortingOrder = 10;

        GameObject g02 = new GameObject("Sprite_02");
        g02.transform.SetParent(root.transform);
        g02.transform.localPosition = Vector3.zero;
        SpriteRenderer sr02 = g02.AddComponent<SpriteRenderer>();
        sr02.sprite = s02;
        sr02.sortingOrder = 11;

        GameObject g03 = new GameObject("Sprite_03");
        g03.transform.SetParent(root.transform);
        g03.transform.localPosition = Vector3.zero;
        SpriteRenderer sr03 = g03.AddComponent<SpriteRenderer>();
        sr03.sprite = s03;
        sr03.sortingOrder = 13;

        controller.GenerateCurvedCollider();

        GameObject savedPrefab = PrefabUtility.SaveAsPrefabAssetAndConnect(root, prefabPath, InteractionMode.AutomatedAction);
        Object.DestroyImmediate(root);

        Debug.Log("📦 Tapered Beaker Prefab generated & saved: " + prefabPath);
        return savedPrefab;
    }

    private static GameObject CreateOrGetGlassPrefab()
    {
        string prefabPath = BartendingAssetPaths.GlassPrefab;
        GameObject existingPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);

        // 사용자가 커스터마이징해 둔 소중한 잔 프리셋 100% 보존
        if (existingPrefab != null)
        {
            Debug.Log("📦 [보존] 이미 존재하고 사용자가 조율한 Glass 프리팹 데이터를 그대로 보존 사용합니다.");
            return existingPrefab;
        }
        
        GameObject root = new GameObject("Glass");
        root.transform.position = Vector3.zero;

        Rigidbody2D rb = root.AddComponent<Rigidbody2D>();
        rb.bodyType = RigidbodyType2D.Kinematic;
        rb.useFullKinematicContacts = true;
        
        BoxCollider2D touchCol = root.AddComponent<BoxCollider2D>();
        touchCol.size = new Vector2(1.8f, 2.4f);
        touchCol.isTrigger = true;

        root.AddComponent<DraggableBar>();

        // 잔 컨트롤러 장착 및 온더락(Rock Glass) 최적 프리셋 치수 빌드
        GlassController controller = root.AddComponent<GlassController>();
        controller.bottomWidth = 2.0f;   // 넓적함
        controller.topWidth = 2.2f;      // 묵직함
        controller.height = 2.0f;        // Y축이 낮음
        controller.cornerRadius = 0.4f;   // 밑바닥 두터운 라운딩
        controller.curveSegments = 20;
        controller.edgeRadius = 0.08f;

        // 온더락 잔 고유의 정갈한 직선 실루엣을 위해 디폴트 1.0 수평선 배정
        controller.glassProfile = AnimationCurve.Constant(0f, 1f, 1f);

        // 인터랙션 필드 기본값 세팅
        int slotLayer = LayerMask.NameToLayer("Slot");
        if (slotLayer == -1) slotLayer = 0;
        
        SerializedObject so = new SerializedObject(controller);
        so.Update();
        so.FindProperty("slotLayer").intValue = 1 << slotLayer;
        so.FindProperty("maxTiltAngle").floatValue = 120f;
        so.FindProperty("tiltSensitivity").floatValue = 0.5f;
        so.FindProperty("returnSpeed").floatValue = 0.8f;
        so.ApplyModifiedProperties();

        // glass_rock 스프라이트를 로드하여 정교한 잔 샌드위치 입체감 틀을 조립
        Sprite s01 = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Sprites/UI/glass_rock/glass_rock_01.png");
        Sprite s02 = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Sprites/UI/glass_rock/glass_rock_02.png");
        Sprite s03 = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Sprites/UI/glass_rock/glass_rock_03.png");

        GameObject g01 = new GameObject("Sprite_01");
        g01.transform.SetParent(root.transform);
        g01.transform.localPosition = Vector3.zero;
        SpriteRenderer sr01 = g01.AddComponent<SpriteRenderer>();
        sr01.sprite = s01;
        sr01.sortingOrder = 10;

        GameObject g02 = new GameObject("Sprite_02");
        g02.transform.SetParent(root.transform);
        g02.transform.localPosition = Vector3.zero;
        SpriteRenderer sr02 = g02.AddComponent<SpriteRenderer>();
        sr02.sprite = s02;
        sr02.sortingOrder = 11;

        GameObject g03 = new GameObject("Sprite_03");
        g03.transform.SetParent(root.transform);
        g03.transform.localPosition = Vector3.zero;
        SpriteRenderer sr03 = g03.AddComponent<SpriteRenderer>();
        sr03.sprite = s03;
        sr03.sortingOrder = 13;

        controller.GenerateCurvedCollider();

        GameObject savedPrefab = PrefabUtility.SaveAsPrefabAssetAndConnect(root, prefabPath, InteractionMode.AutomatedAction);
        Object.DestroyImmediate(root);

        Debug.Log("📦 Universal Glass Prefab generated & saved: " + prefabPath);
        return savedPrefab;
    }
}
