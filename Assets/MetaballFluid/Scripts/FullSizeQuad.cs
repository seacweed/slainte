using UnityEngine;

public class FullScreenQuad : MonoBehaviour
{
    void Start() // 또는 해상도가 바뀔 때마다 실행해야 하므로 Update
    {
        Camera cam = Camera.main; // 메인 카메라 참조

        // 1. 카메라의 세로 크기 (Size는 절반이므로 2배)
        float height = cam.orthographicSize * 2.0f;

        // 2. 카메라의 가로 크기 (세로 * 가로세로비율)
        float width = height * cam.aspect;

        // 3. Quad의 크기(Scale) 적용
        transform.localScale = new Vector3(width, height, 1.0f);
    }
}