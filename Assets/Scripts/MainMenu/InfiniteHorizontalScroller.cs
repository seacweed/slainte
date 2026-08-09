using UnityEngine;

public class InfiniteHorizontalScroller : MonoBehaviour
{
    [SerializeField] private RectTransform[] tiles;
    [SerializeField] private float scrollSpeed = 20f;

    private float tileWidth;
    private float totalWidth;
    private float offset;

    private void Awake()
    {
        if (tiles == null || tiles.Length == 0)
        {
            enabled = false;
            return;
        }

        tileWidth = tiles[0].rect.width;
        totalWidth = tileWidth * tiles.Length;
    }

    private void Update()
    {
        offset -= scrollSpeed * Time.deltaTime;
        offset = Mod(offset, totalWidth);

        for (int i = 0; i < tiles.Length; i++)
        {
            float x = Mod(i * tileWidth + offset, totalWidth);
            if (x > totalWidth * 0.5f)
                x -= totalWidth;

            Vector2 pos = tiles[i].anchoredPosition;
            pos.x = x;
            tiles[i].anchoredPosition = pos;
        }
    }

    // 타일을 totalWidth 범위 안에서 순환시켜 이가 빠지지 않게 함
    private static float Mod(float a, float b)
    {
        return a - b * Mathf.Floor(a / b);
    }
}
