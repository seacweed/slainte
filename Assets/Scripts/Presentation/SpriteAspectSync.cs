using UnityEngine;
using UnityEngine.UI;

[ExecuteAlways]
[RequireComponent(typeof(Image), typeof(AspectRatioFitter))]
public class SpriteAspectSync : MonoBehaviour
{
#if UNITY_EDITOR
    void OnValidate()
    {
        var img = GetComponent<Image>();
        var arf = GetComponent<AspectRatioFitter>();
        if (img != null && arf != null && img.sprite != null)
        {
            arf.aspectMode  = AspectRatioFitter.AspectMode.WidthControlsHeight;
            arf.aspectRatio = img.sprite.rect.width / img.sprite.rect.height;
        }
    }
#endif
}
