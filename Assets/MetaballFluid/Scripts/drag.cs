using UnityEngine;
using UnityEngine.EventSystems; // 필수 네임스페이스

public class DraggableBar : MonoBehaviour, IBeginDragHandler, IDragHandler
{
    private Camera mainCamera;
    private Vector3 offset; // 마우스 클릭 지점과 오브젝트 중심 간의 차이

    private void Awake()
    {
        mainCamera = Camera.main;
    }

    // 드래그 시작 시 한 번 호출
    public void OnBeginDrag(PointerEventData eventData)
    {
        // 현재 마우스 위치를 월드 좌표로 변환
        Vector3 worldPoint = mainCamera.ScreenToWorldPoint(eventData.position);
        worldPoint.z = 0;

        // "현재 내 위치"와 "마우스 위치"의 차이(Offset)를 저장
        offset = transform.position - worldPoint;
    }

    // 드래그 중 계속 호출
    public void OnDrag(PointerEventData eventData)
    {
        Vector3 worldPoint = mainCamera.ScreenToWorldPoint(eventData.position);
        worldPoint.z = 0;

        // 저장해둔 차이(Offset)만큼 더해서 이동 (자연스러운 움직임)
        Vector3 newPosition = worldPoint + offset;

        // [옵션] 막대라서 X축이나 Y축으로만 움직이게 하고 싶다면 아래 주석을 해제하세요.
        // newPosition.y = transform.position.y; // Y축 고정 (좌우로만 이동)
        // newPosition.x = transform.position.x; // X축 고정 (상하로만 이동)

        transform.position = newPosition;
    }
}