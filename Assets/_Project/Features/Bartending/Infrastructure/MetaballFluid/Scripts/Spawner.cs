using UnityEngine;

public class LiquidSpawner : MonoBehaviour
{
    public GameObject waterParticlePrefab;
    public float spawnInterval = 0.1f;
    public LiquidPool liquidPool;
    private float timer;

    void Update()
    {
        timer += Time.deltaTime;
        if (timer >= spawnInterval)
        {
            // 랜덤한 위치 계산 (x축으로 -0.1 ~ +0.1 사이 랜덤)
            Vector3 randomOffset = new Vector3(Random.Range(-0.1f, 0.1f), 0, 0);
            
            // 원래 위치 + 랜덤 위치에 생성
            //Instantiate(waterParticlePrefab, transform.position + randomOffset, Quaternion.identity);
            
            // 풀에서 하나 꺼내옵니다.
            GameObject obj = liquidPool.GetParticle(transform.position + randomOffset);

            // (중요) 만약 풀이 꽉 차서 null이 올 수도 있으니 체크
            if (obj != null) 
            {
                // 꺼낸 녀석의 속도를 0으로 초기화 (이전에 쓰던 속도가 남아있을 수 있음!)
                Rigidbody2D rb = obj.GetComponent<Rigidbody2D>();
                if (rb != null)
                {
                    rb.linearVelocity = Vector2.zero; // Unity 6 (구버전은 rb.velocity)
                    rb.angularVelocity = 0f;
                }
            }

            timer = 0f;
        }
    }
}