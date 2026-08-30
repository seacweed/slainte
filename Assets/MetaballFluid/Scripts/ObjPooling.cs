using System.Collections.Generic;
using Slainte.Bartending;
using UnityEngine;

public class LiquidPool : MonoBehaviour
{
    private const float FallbackParticleVolumeMl = 1f;

    private struct ActiveParticle
    {
        public GameObject GameObject;
        public ReturnToPool ReturnToPool;
        public LiquidReaction LiquidReaction;
    }

    public static LiquidPool Instance;
    public GameObject particlePrefab;
    [Min(0)] public int poolSize = 900;
    [Min(1)] public int maxAutomaticReturnsPerFrame = 16;

    private readonly Queue<GameObject> poolQueue = new Queue<GameObject>();
    private readonly List<ActiveParticle> activeParticles = new List<ActiveParticle>();
    private bool initialized;
    private bool missingPrefabLogged;
    private bool missingParticleDataLogged;
    private GameObject cachedVolumePrefab;
    private float cachedDefaultParticleVolumeMl = FallbackParticleVolumeMl;

    public int ActiveParticleCount => activeParticles.Count;
    public int AvailableParticleCount => poolQueue.Count;
    public int TotalParticleCount => activeParticles.Count + poolQueue.Count;
    public float DefaultParticleVolumeMl => ResolveDefaultParticleVolumeMl();

    private void Awake()
    {
        Instance = this;
        EnsureInitialized();
    }

    private void EnsureInitialized()
    {
        if (initialized || particlePrefab == null)
        {
            if (particlePrefab == null)
                LogMissingPrefabOnce();
            return;
        }

        initialized = true;
        for (int i = 0; i < Mathf.Max(0, poolSize); i++)
        {
            GameObject particle = CreateParticle();
            if (particle == null)
                break;

            poolQueue.Enqueue(particle);
        }
    }

    public GameObject GetParticle(Vector3 position)
    {
        return GetParticle(position, null, 0f);
    }

    public GameObject GetParticle(Vector3 position, ItemDef sourceItem, float volumeMl)
    {
        EnsureInitialized();

        GameObject particle = GetAvailableParticle();
        if (particle == null)
            return null;

        PrepareParticle(particle, position, sourceItem, volumeMl);
        activeParticles.Add(new ActiveParticle
        {
            GameObject = particle,
            ReturnToPool = particle.GetComponent<ReturnToPool>(),
            LiquidReaction = particle.GetComponent<LiquidReaction>()
        });
        return particle;
    }

    public bool ReturnParticle(GameObject particle)
    {
        if (particle == null)
            return false;

        int index = FindActiveIndex(particle);
        if (index < 0)
            return false;

        ReturnParticleAt(index);
        return true;
    }

    private int FindActiveIndex(GameObject particle)
    {
        for (int i = 0; i < activeParticles.Count; i++)
        {
            if (activeParticles[i].GameObject == particle)
                return i;
        }

        return -1;
    }

    private void ReturnParticleAt(int index)
    {
        GameObject particle = activeParticles[index].GameObject;
        activeParticles.RemoveAt(index);
        particle.SetActive(false);
        poolQueue.Enqueue(particle);
    }

    private GameObject GetAvailableParticle()
    {
        while (poolQueue.Count > 0)
        {
            GameObject particle = poolQueue.Dequeue();
            if (particle != null)
                return particle;
        }

        // The pool owns visual particles only. Running out of the initial cache must
        // never be interpreted as one of the bottles running out of liquid.
        return CreateParticle();
    }

    private GameObject CreateParticle()
    {
        if (particlePrefab == null)
        {
            LogMissingPrefabOnce();
            return null;
        }

        GameObject particle = Instantiate(particlePrefab, transform);
        SetLayerRecursively(particle, gameObject.layer);
        particle.SetActive(false);
        return particle;
    }

    private static void PrepareParticle(
        GameObject particle,
        Vector3 position,
        ItemDef sourceItem,
        float volumeMl)
    {
        particle.transform.SetPositionAndRotation(position, Quaternion.identity);

        if (particle.TryGetComponent(out LiquidParticleData particleData))
            particleData.SetPayload(sourceItem, Mathf.Max(0f, volumeMl));

        if (particle.TryGetComponent(out Rigidbody2D body))
        {
            body.position = position;
            body.rotation = 0f;
            body.linearVelocity = Vector2.zero;
            body.angularVelocity = 0f;
        }

        particle.SetActive(true);

        if (particle.TryGetComponent(out LiquidReaction reaction))
            reaction.WakeUp();
        else if (body != null && !body.IsAwake())
            body.WakeUp();
    }

    private void LogMissingPrefabOnce()
    {
        if (missingPrefabLogged)
            return;

        missingPrefabLogged = true;
        Debug.LogError("[LiquidPool] Particle Prefab이 설정되지 않아 액체 입자를 생성할 수 없습니다.", this);
    }

    private float ResolveDefaultParticleVolumeMl()
    {
        if (particlePrefab == null)
            return FallbackParticleVolumeMl;

        if (cachedVolumePrefab == particlePrefab)
            return cachedDefaultParticleVolumeMl;

        cachedVolumePrefab = particlePrefab;
        cachedDefaultParticleVolumeMl = FallbackParticleVolumeMl;
        missingParticleDataLogged = false;

        if (particlePrefab.TryGetComponent(out LiquidParticleData particleData))
        {
            cachedDefaultParticleVolumeMl = particleData.DefaultVolumeMl;
        }
        else if (!missingParticleDataLogged)
        {
            missingParticleDataLogged = true;
            Debug.LogWarning(
                "[LiquidPool] Particle Prefab에 LiquidParticleData가 없어 입자당 부피를 1ml로 처리합니다.",
                this);
        }

        return cachedDefaultParticleVolumeMl;
    }

    private static void SetLayerRecursively(GameObject root, int layer)
    {
        root.layer = layer;
        for (int i = 0; i < root.transform.childCount; i++)
            SetLayerRecursively(root.transform.GetChild(i).gameObject, layer);
    }

    private void Update()
    {
        Camera cam = Camera.main;
        bool hasScreenBounds = cam != null;
        float screenLeft = 0f;
        float screenRight = 0f;
        float screenBottom = 0f;
        if (hasScreenBounds)
        {
            // FullSizeQuad.cs와 동일한 방식으로 화면의 월드 좌표 경계를 매 프레임 1회만 계산한다.
            float halfHeight = cam.orthographicSize;
            float halfWidth = halfHeight * cam.aspect;
            Vector3 camPos = cam.transform.position;
            screenLeft = camPos.x - halfWidth;
            screenRight = camPos.x + halfWidth;
            screenBottom = camPos.y - halfHeight;
        }

        int automaticReturns = 0;
        int returnLimit = Mathf.Max(1, maxAutomaticReturnsPerFrame);
        for (int i = activeParticles.Count - 1; i >= 0; i--)
        {
            ActiveParticle entry = activeParticles[i];
            if (entry.GameObject == null)
            {
                activeParticles.RemoveAt(i);
                continue;
            }

            if (automaticReturns < returnLimit
                && entry.ReturnToPool != null
                && entry.ReturnToPool.CheckOOB(hasScreenBounds, screenLeft, screenRight, screenBottom))
            {
                ReturnParticleAt(i);
                automaticReturns++;
                continue;
            }

            if (entry.LiquidReaction != null)
                entry.LiquidReaction.CheckSleepState(Time.deltaTime);
        }
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }
}
