using UnityEngine;

namespace Slainte.Bartending
{
    [DisallowMultipleComponent]
    public sealed class BartendingLayoutBindings : MonoBehaviour
    {
        [SerializeField, Tooltip("Clickable shelf area inside GameArea16x9, excluding art padding.")]
        private RectTransform shelfReturnArea;
        [SerializeField, Tooltip("Fixed hose attachment point in the full-view layout.")]
        private RectTransform hoseAnchor;
        [SerializeField, Tooltip("Resting handle height. The handle hangs directly under the hose anchor.")]
        private RectTransform dispenserDock;

        internal RectTransform ShelfReturnArea => isActiveAndEnabled ? shelfReturnArea : null;

        internal bool ContainsShelfReturnPoint(Vector2 screenPosition) =>
            RectTransformUtility.RectangleContainsScreenPoint(shelfReturnArea, screenPosition,
                BartendingViewport.GetCanvasCamera(shelfReturnArea));

        internal Vector2 GetDockPosition(RectTransform gameArea, Vector2 fallback) =>
            ReadPosition(gameArea, dispenserDock, fallback);

        internal Vector2 GetHoseAnchor(RectTransform gameArea, Vector2 fallback) =>
            ReadPosition(gameArea, hoseAnchor, fallback);

        private Vector2 ReadPosition(RectTransform gameArea, RectTransform point, Vector2 fallback)
        {
            if (!isActiveAndEnabled || gameArea == null || point == null) return fallback;
            Vector2 local = gameArea.InverseTransformPoint(point.position);
            return Rect.PointToNormalized(gameArea.rect, local);
        }
    }
}
