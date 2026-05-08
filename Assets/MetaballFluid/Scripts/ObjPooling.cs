using System.Collections.Generic;
using UnityEngine;

public class LiquidPool : MonoBehaviour
{
    public static LiquidPool Instance;
    public GameObject particlePrefab;
    public int poolSize = 300; // 입자 최대 개수 제한

    private Queue<GameObject> poolQueue = new Queue<GameObject>();

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
        poolQueue.Enqueue(obj);
    }
}