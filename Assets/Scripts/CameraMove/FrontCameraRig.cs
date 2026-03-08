using UnityEngine;

public class FrontCameraRig : MonoBehaviour
{
    [Header("Move this (FrontWorld RectTransform)")]
    [SerializeField] RectTransform frontWorld;

    [Header("How much to shift to reveal")]
    [SerializeField] float drawerShiftY = 800f;
    [SerializeField] float shelfShiftX = 2560f;

    [Header("Motion")]
    [SerializeField] float moveTime = 0.35f;
    [SerializeField] AnimationCurve ease = AnimationCurve.EaseInOut(0, 0, 1, 1);

    bool drawerOpen = false;
    bool shelfOpen = false;

    Vector2 fromPos, toPos;
    float t;
    bool animating;

    void Awake()
    {
        if (frontWorld == null) frontWorld = (RectTransform)transform;
        frontWorld.anchoredPosition = Vector2.zero;
    }

    void Update()
    {
        if (!animating)
        {
            if (Input.GetKeyDown(KeyCode.S) && !drawerOpen && !shelfOpen) SetDrawer(true);
            if (Input.GetKeyDown(KeyCode.W) && drawerOpen) SetDrawer(false);
            if (Input.GetKeyDown(KeyCode.D) && !shelfOpen && !drawerOpen) SetShelf(true);
            if (Input.GetKeyDown(KeyCode.A) && shelfOpen) SetShelf(false);
        }

        if (animating)
        {
            t += Time.unscaledDeltaTime / Mathf.Max(0.0001f, moveTime);
            float k = ease.Evaluate(Mathf.Clamp01(t));
            frontWorld.anchoredPosition = Vector2.LerpUnclamped(fromPos, toPos, k);
            if (t >= 1f)
            {
                frontWorld.anchoredPosition = toPos;
                animating = false;
            }
        }
    }

    public void SetDrawer(bool open)
    {
        drawerOpen = open;
        fromPos = frontWorld.anchoredPosition;
        toPos = open ? new Vector2(0, drawerShiftY) : Vector2.zero;
        t = 0f;
        animating = true;
    }

    public void SetShelf(bool open)
    {
        shelfOpen = open;
        fromPos = frontWorld.anchoredPosition;
        toPos = open ? new Vector2(-shelfShiftX, 0) : Vector2.zero;
        t = 0f;
        animating = true;
    }
}
