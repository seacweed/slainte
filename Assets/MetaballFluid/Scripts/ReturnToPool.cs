using UnityEngine;

public class ReturnToPool : MonoBehaviour
{
    public float bottomLimit = -10f; // 이 좌표보다 내려가면 반납

    public bool CheckOOB()
    {
        return transform.position.y < bottomLimit;
    }
}