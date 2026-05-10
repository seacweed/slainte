using System.Collections.Generic;
using UnityEngine;

public class LiquidPool : MonoBehaviour
{
    public static LiquidPool Instance;
    public GameObject particlePrefab;
    public int poolSize = 300; // 입자 최대 개수 제한



    private Queue<GameObject> poolQueue = new Queue<GameObject>();
    private List<GameObject> activeParticles = new List<GameObject>();

    void Awake()
    {
        Instance = this;
        InitializePool();
    }

    void InitializePool()
    {
        for (int i = 0; i < poolSize; i++)
        {
            GameObject obj = Instantiate(particlePrefab, transform); // 깔끔하게 부모 밑으로 정리
            obj.SetActive(false);
            poolQueue.Enqueue(obj);
        }
    }

    public GameObject GetParticle(Vector3 position)
    {
        if (poolQueue.Count > 0)
        {
            GameObject obj = poolQueue.Dequeue();
            obj.transform.position = position;
            obj.transform.rotation = Quaternion.identity;
            obj.SetActive(true);
            activeParticles.Add(obj);
            return obj;
        }
        else
        {
            // 풀이 동났을 때: 가장 오래된 녀석을 재사용하거나, 그냥 무시(return null)
            // 여기서는 성능을 위해 그냥 무시합니다.
            return null;
        }
    }

    public void ReturnParticle(GameObject obj)
    {
        obj.SetActive(false);
        activeParticles.Remove(obj);
        poolQueue.Enqueue(obj);
    }

    void Update()
    {
        for (int i = activeParticles.Count - 1; i >= 0; i--)
        {
            GameObject obj = activeParticles[i];
            
            if (obj.TryGetComponent(out ReturnToPool rtp) && rtp.CheckOOB())
            {
                ReturnParticle(obj);
                continue;
            }

            if (obj.TryGetComponent(out LiquidReaction reaction))
            {
                reaction.CheckSleepState(Time.deltaTime);
            }
        }
    }
}