using System.Collections.Generic;
using Slainte.Bartending;
using UnityEngine;

public class LiquidPool : MonoBehaviour
{
    public static LiquidPool Instance;
    public GameObject particlePrefab;
    [Min(0)] public int poolSize = 900;
    [Min(1)] public int maxAutomaticReturnsPerFrame = 16;

    private readonly Queue<GameObject> poolQueue = new Queue<GameObject>();
    private readonly List<GameObject> activeParticles = new List<GameObject>();
    private bool initialized;
    private bool missingPrefabLogged;

    public int ActiveParticleCount => activeParticles.Count;
    public int AvailableParticleCount => poolQueue.Count;
    public int TotalParticleCount => activeParticles.Count + poolQueue.Count;

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
        activeParticles.Add(particle);
        return particle;
    }

    public bool ReturnParticle(GameObject particle)
    {
        if (particle == null || !activeParticles.Remove(particle))
            return false;

        particle.SetActive(false);
        poolQueue.Enqueue(particle);
        return true;
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

    private static void SetLayerRecursively(GameObject root, int layer)
    {
        root.layer = layer;
        for (int i = 0; i < root.transform.childCount; i++)
            SetLayerRecursively(root.transform.GetChild(i).gameObject, layer);
    }

    private void Update()
    {
        int automaticReturns = 0;
        int returnLimit = Mathf.Max(1, maxAutomaticReturnsPerFrame);
        for (int i = activeParticles.Count - 1; i >= 0; i--)
        {
            GameObject particle = activeParticles[i];
            if (particle == null)
            {
                activeParticles.RemoveAt(i);
                continue;
            }

            if (automaticReturns < returnLimit
                && particle.TryGetComponent(out ReturnToPool returnToPool)
                && returnToPool.CheckOOB())
            {
                ReturnParticle(particle);
                automaticReturns++;
                continue;
            }

            if (particle.TryGetComponent(out LiquidReaction reaction))
                reaction.CheckSleepState(Time.deltaTime);
        }
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }
}
