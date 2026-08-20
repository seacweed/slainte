using UnityEngine;

namespace Slainte.TV
{
    /// <summary>
    /// 월드 TV와 화면 공간 방송 UI를 연결한다.
    /// 프리팹 본체는 월드에 남고 패널만 기존 휴식 Canvas 아래에 생성된다.
    /// </summary>
    public sealed class TVSystemController : MonoBehaviour
    {
        [Header("World")]
        public ObjectInteraction interaction;

        [Header("UI")]
        public Canvas uiCanvas;
        public TVUIManager panelPrefab;

        public TVUIManager PanelInstance { get; private set; }

        private void Awake()
        {
            if (interaction == null)
                interaction = GetComponent<ObjectInteraction>();

            Canvas targetCanvas = uiCanvas != null ? uiCanvas : FindScreenCanvas();
            if (targetCanvas == null || panelPrefab == null)
            {
                Debug.LogError(
                    "[TVSystem] 월드 TV에 연결할 Canvas 또는 TVPanel 프리팹이 없습니다.",
                    this);
                if (interaction != null)
                    interaction.enabled = false;
                return;
            }

            PanelInstance = Instantiate(
                panelPrefab,
                targetCanvas.transform,
                worldPositionStays: false);
            PanelInstance.name = "TVPanel";
            PanelInstance.transform.SetAsLastSibling();

            if (interaction != null)
                interaction.targetUIManager = PanelInstance;
        }

        private void OnDestroy()
        {
            if (PanelInstance != null)
                Destroy(PanelInstance.gameObject);
        }

        private static Canvas FindScreenCanvas()
        {
            Canvas fallback = null;
            Canvas[] canvases = FindObjectsByType<Canvas>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);
            for (int i = 0; i < canvases.Length; i++)
            {
                Canvas candidate = canvases[i];
                if (candidate == null || candidate.renderMode == RenderMode.WorldSpace)
                    continue;
                fallback ??= candidate;
                if (candidate.isRootCanvas)
                    return candidate;
            }

            return fallback;
        }
    }
}
