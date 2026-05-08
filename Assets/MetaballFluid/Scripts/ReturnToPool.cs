using UnityEngine;

public class ReturnToPool : MonoBehaviour
{
    public float bottomLimit = -10f; // 이 좌표보다 내려가면 반납

    void Update()
    {
        // Y좌표가 너무 낮아지면 (화면 밖으로 떨어지면)
        if (transform.position.y < bottomLimit)
        {
            // Destroy(gameObject);  <-- 이거 대신 아래 코드 사용
            
            // 풀 관리자에게 나를 반납한다
            if (LiquidPool.Instance != null)
            {
                LiquidPool.Instance.ReturnParticle(this.gameObject);
            }
            else
            {
                // 혹시 풀이 없으면 그냥 끈다 (에러 방지)
                gameObject.SetActive(false);
            }
        }
    }
}