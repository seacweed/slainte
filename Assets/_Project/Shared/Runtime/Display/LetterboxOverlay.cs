using UnityEngine;
using UnityEngine.UI;

namespace Slainte.Shared.Display
{
    // 16:9 게임 영역 바깥을 검은 띠로 덮는 최상단 오버레이. 씬에 배치하지 않고 첫 씬 로드 직후 스스로
    // 생성되어 DontDestroyOnLoad로 상주한다 — 개발용 씬(샌드박스 등)이나 CoreScene 없이 단독 실행한
    // 씬에서도 똑같이 동작하게 하려는 것. 해상도 변경 감지와 GameAspect.ScreenSizeChanged 발행도 여기서 담당.
    [DisallowMultipleComponent]
    public sealed class LetterboxOverlay : MonoBehaviour
    {
        // 컷씬·페이드 등 다른 모든 Overlay 캔버스보다 위에 그려야 하므로 허용 최댓값을 쓴다.
        private const int SortingOrder = short.MaxValue;

        private static LetterboxOverlay s_instance;

        private RectTransform _left;
        private RectTransform _right;
        private RectTransform _bottom;
        private RectTransform _top;
        private int _lastWidth;
        private int _lastHeight;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void CreateOnStartup()
        {
            if (s_instance != null)
                return;

            var go = new GameObject(nameof(LetterboxOverlay));
            DontDestroyOnLoad(go);
            go.AddComponent<LetterboxOverlay>();
        }

        private void Awake()
        {
            if (s_instance != null && s_instance != this)
            {
                Destroy(gameObject);
                return;
            }

            s_instance = this;
            BuildCanvas();
            Apply();
        }

        private void OnDestroy()
        {
            if (s_instance == this)
                s_instance = null;
        }

        // 해상도 변경은 별도 콜백이 없어 매 프레임 정수 두 개만 비교한다(변경이 없으면 그 외 비용 없음).
        private void Update()
        {
            if (Screen.width == _lastWidth && Screen.height == _lastHeight)
                return;

            Apply();
            GameAspect.NotifyScreenSizeChanged();
        }

        private void BuildCanvas()
        {
            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = SortingOrder;
            // 띠 위의 클릭이 아래 게임 UI로 새지 않도록 레이캐스트를 받는다.
            gameObject.AddComponent<GraphicRaycaster>();

            _left   = CreateBar("Left");
            _right  = CreateBar("Right");
            _bottom = CreateBar("Bottom");
            _top    = CreateBar("Top");
        }

        private RectTransform CreateBar(string barName)
        {
            var go = new GameObject(barName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(transform, false);
            go.GetComponent<Image>().color = Color.black;

            var rect = (RectTransform)go.transform;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            return rect;
        }

        // 띠 위치를 정규화 앵커로만 지정해 캔버스 스케일과 무관하게 화면 비율만으로 배치한다.
        private void Apply()
        {
            _lastWidth = Screen.width;
            _lastHeight = Screen.height;
            if (_lastWidth <= 0 || _lastHeight <= 0)
                return;

            Rect area = GameAspect.GetGameScreenRect(_lastWidth, _lastHeight);
            float x0 = area.xMin / _lastWidth;
            float x1 = area.xMax / _lastWidth;
            float y0 = area.yMin / _lastHeight;
            float y1 = area.yMax / _lastHeight;

            SetBar(_left,   new Vector2(0f, 0f),  new Vector2(x0, 1f));
            SetBar(_right,  new Vector2(x1, 0f),  new Vector2(1f, 1f));
            SetBar(_bottom, new Vector2(x0, 0f),  new Vector2(x1, y0));
            SetBar(_top,    new Vector2(x0, y1),  new Vector2(x1, 1f));
        }

        // 크기가 0인 띠는 꺼서 정확히 16:9 화면에서는 드로우콜·레이캐스트 대상이 남지 않게 한다.
        private static void SetBar(RectTransform bar, Vector2 anchorMin, Vector2 anchorMax)
        {
            bar.anchorMin = anchorMin;
            bar.anchorMax = anchorMax;
            bool visible = anchorMax.x - anchorMin.x > 0.0001f && anchorMax.y - anchorMin.y > 0.0001f;
            if (bar.gameObject.activeSelf != visible)
                bar.gameObject.SetActive(visible);
        }
    }
}
