using UnityEngine;

public class LiquidReaction : MonoBehaviour
{
    [HideInInspector] public SpriteRenderer spriteRenderer;
    [HideInInspector] public Rigidbody2D rb;

    [Header("섞임 설정")]
    public float mixSpeed = 0.1f;
    public float reactionCooldown = 0.1f;
    private float lastReactionTime;

    [Header("최적화 (수면) 설정")]
    public float sleepVelocityThreshold = 0.05f; // 이 속도 이하로 내려가면 고인 물로 취급
    public float timeToSleep = 2.0f; // 기존 0.5f에서 2.0f로 증가 (충분히 퍼질 시간)
    private float settleTimer = 0f;
    private bool isLogicallySleeping = false;

    void Awake()
    {
        // 자신의 컴포넌트를 미리 캐싱해 둡니다.
        spriteRenderer = GetComponent<SpriteRenderer>();
        rb = GetComponent<Rigidbody2D>();
    }

    public void CheckSleepState(float deltaTime)
    {
        if (isLogicallySleeping) return;

        // 속도가 거의 0에 수렴하면(고여 있으면) 타이머 증가
        if (rb.linearVelocity.sqrMagnitude < sleepVelocityThreshold)
        {
            settleTimer += deltaTime;
            if (settleTimer >= timeToSleep)
            {
                GoToSleep();
            }
        }
        else
        {
            settleTimer = 0f;
        }
    }

    void GoToSleep()
    {
        isLogicallySleeping = true;
        // 유니티 물리 엔진(Box2D)도 해당 파티클 연산을 멈추도록 지시
        rb.Sleep();
    }

    // 풀에서 꺼내지거나 강한 충돌을 받았을 때 다시 깨우기 위함
    public void WakeUp()
    {
        isLogicallySleeping = false;
        settleTimer = 0f;
        if (!rb.IsAwake()) rb.WakeUp();
    }

    void OnCollisionEnter2D(Collision2D collision)
    {
        // 최적화: 이미 바닥에 고여서 수면 상태인 입자는 주도적으로 충돌 연산을 하지 않음
        // (새로 떨어지는 다른 입자가 얘를 치면 그쪽 스크립트에서 연산됨)
        if (isLogicallySleeping) return;

        // 1차 최적화: 쿨타임 체크로 무의미한 프레임당 연산 방어
        if (Time.time < lastReactionTime + reactionCooldown) return;

        // 2차 최적화: GetComponent 대신 속도가 훨씬 빠른 TryGetComponent 사용
        if (collision.gameObject.TryGetComponent(out LiquidReaction otherParticle))
        {
            MixAttributes(otherParticle);
            lastReactionTime = Time.time;
        }
    }

    void MixAttributes(LiquidReaction other)
    {
        // 3차 최적화: 상대방의 컴포넌트(Rigidbody2D 등)를 매번 GetComponent로 가져오지 않고,
        // 상대방 스크립트(LiquidReaction)가 미리 캐싱해둔 public 변수를 직접 읽어옵니다.
        Rigidbody2D otherRb = other.rb;

        Color myColor = spriteRenderer.color;
        Color otherColor = other.spriteRenderer.color;

        // 4차 최적화: 이미 색상과 질량이 완전히 섞여서 구별이 안 되는 상태라면, 
        // 무거운 물리 속성 덮어쓰기 연산을 아예 스킵(return)해버립니다.
        if (AreColorsSimilar(myColor, otherColor) && Mathf.Approximately(rb.mass, otherRb.mass))
        {
            return;
        }

        // [색상 평균화]
        Color averageColor = (myColor + otherColor) / 2f;
        this.spriteRenderer.color = averageColor;
        other.spriteRenderer.color = averageColor;

        // [물리 속성 평균화]
        float averageMass = (this.rb.mass + otherRb.mass) / 2f;
        this.rb.mass = averageMass;
        otherRb.mass = averageMass;

        float averageLinearDamping = (this.rb.linearDamping + otherRb.linearDamping) / 2f;
        this.rb.linearDamping = averageLinearDamping;
        otherRb.linearDamping = averageLinearDamping;

        float averageGravityScale = (this.rb.gravityScale + otherRb.gravityScale) / 2f;
        this.rb.gravityScale = averageGravityScale;
        otherRb.gravityScale = averageGravityScale;
    }

    // 두 색상이 시각적으로 구별되지 않을 만큼(약 2% 오차 이내) 비슷한지 빠르게 체크합니다.
    bool AreColorsSimilar(Color a, Color b)
    {
        float diffR = a.r > b.r ? a.r - b.r : b.r - a.r;
        float diffG = a.g > b.g ? a.g - b.g : b.g - a.g;
        float diffB = a.b > b.b ? a.b - b.b : b.b - a.b;
        float diffA = a.a > b.a ? a.a - b.a : b.a - a.a;
        
        return diffR < 0.02f && diffG < 0.02f && diffB < 0.02f && diffA < 0.02f;
    }
}