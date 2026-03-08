using System;
using System.Collections;
using System.Collections.Generic;
using Mono.Cecil.Cil;
using UnityEngine;
using UnityEngine.UI;

public class CustomerSpawner : MonoBehaviour
{
    [Header("Prefab")]
    [SerializeField] private GameObject customerPrefab;

    [SerializeField] private CustomerOrderDatabase customerDB;
    [SerializeField] private DialogueController dialogue;
    [SerializeField] private OrderTicketManager ticketManager;

    [Header("Slots (Center, Left, Right, Left2, Right2...)")]
    [SerializeField] private List<RectTransform> slots = new();

    [Header("Animation")]
    [SerializeField] private float fadeDuration = 0.35f;
    [SerializeField] private float riseDistance = 50f;
    [SerializeField] private float popHeight = 35f;
    [SerializeField] private float popDuration = 0.12f;

    // [Header("Customer Sprites")]
    // [SerializeField] private List<CustomerEntry> customers = new();

    // private Dictionary<string, Sprite> _map;
    private readonly List<GameObject> _spawned = new();

    // void Awake()
    // {
    //     _map = new Dictionary<string, Sprite>(StringComparer.OrdinalIgnoreCase);
    //     foreach (var c in customers)
    //         if (!string.IsNullOrWhiteSpace(c.key) && c.sprite != null)
    //             _map[c.key] = c.sprite;
    // }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Alpha1)) ShowCustomers(new[] {"yukari"});
    }

    public void Clear()
    {
        foreach (var go in _spawned)
            if (go != null) Destroy(go);
        _spawned.Clear();
    }

    // ✅ N명: 슬롯 기준으로 배치
    public void ShowCustomers(IReadOnlyList<string> keys)
    {
        Clear();

        if (keys == null || keys.Count == 0) return;

        // 슬롯 부족하면 그만큼만 표시
        int count = Mathf.Min(keys.Count, slots.Count);

        for (int i = 0; i < count; i++)
        {
            var data = customerDB.FindByKey(keys[i]);
            if (data == null)
            {
                Debug.LogWarning($"Customer key not found in DB: {keys[i]}");
                continue;
            }

            Sprite sprite = data.sprite;

            RectTransform slot = slots[i];

            // ✅ slot의 자식으로 생성
            GameObject root = Instantiate(customerPrefab, slot);
            _spawned.Add(root);

            // ✅ root를 slot에 가로 stretch로 딱 붙이기
            var rootRT = root.GetComponent<RectTransform>();
            rootRT.anchorMin = new Vector2(0f, 0.5f);
            rootRT.anchorMax = new Vector2(1f, 0.5f);
            rootRT.pivot = new Vector2(0.5f, 0.5f);
            rootRT.offsetMin = new Vector2(0f, rootRT.offsetMin.y);
            rootRT.offsetMax = new Vector2(0f, rootRT.offsetMax.y);
            rootRT.anchoredPosition = Vector2.zero;
            rootRT.localScale = Vector3.one;

            // ✅ Visual 찾기 (프리팹 자식 이름 "Visual" 가정)
            Transform visualT = root.transform.Find("Visual");
            if (visualT == null)
            {
                Debug.LogError("Prefab must have child named 'Visual'.");
                continue;
            }

            var img = visualT.GetComponent<Image>();
            var cg  = visualT.GetComponent<CanvasGroup>();
            var arf = visualT.GetComponent<AspectRatioFitter>();
            var visualRT = visualT.GetComponent<RectTransform>();

            img.sprite = sprite;

            // ✅ 비율 유지 + slot의 가로폭에 맞추기 (WidthControlsHeight)
            if (arf != null && sprite != null)
            {
                arf.aspectMode = AspectRatioFitter.AspectMode.WidthControlsHeight;
                arf.aspectRatio = sprite.rect.width / sprite.rect.height;
            }

            // Visual을 부모 가로폭에 딱 붙이기 (가로 stretch)
            visualRT.anchorMin = new Vector2(0f, 0f);
            visualRT.anchorMax = new Vector2(1f, 0f);
            visualRT.pivot = new Vector2(0.5f, 0f);
            visualRT.offsetMin = new Vector2(0f, visualRT.offsetMin.y);
            visualRT.offsetMax = new Vector2(0f, visualRT.offsetMax.y);
            visualRT.anchoredPosition = new Vector2(0f, 0f);
            visualRT.localScale = Vector3.one;

            StartCoroutine(AppearVisual(visualRT, cg, data));
        }
    }

    private IEnumerator AppearVisual(RectTransform rt, CanvasGroup cg, CustomerOrderData data)
    {
        Vector2 target = rt.anchoredPosition;   // 보통 (0,0)
        Vector2 start  = target - new Vector2(0, riseDistance);

        rt.anchoredPosition = start;
        cg.alpha = 0f;

        float t = 0f;
        while (t < fadeDuration)
        {
            t += Time.deltaTime;
            float a = Mathf.Clamp01(t / fadeDuration);
            cg.alpha = a;
            rt.anchoredPosition = Vector2.Lerp(start, target, a);
            yield return null;
        }

        Vector2 up = target + new Vector2(0, popHeight);

        t = 0f;
        while (t < popDuration)
        {
            t += Time.deltaTime;
            float a = Mathf.Clamp01(t / popDuration);
            rt.anchoredPosition = Vector2.Lerp(target, up, a);
            yield return null;
        }

        t = 0f;
        while (t < popDuration)
        {
            t += Time.deltaTime;
            float a = Mathf.Clamp01(t / popDuration);
            rt.anchoredPosition = Vector2.Lerp(up, target, a);
            yield return null;
        }

        rt.anchoredPosition = target;
        cg.alpha = 1f;

        // ✅ 등장 끝난 후 대사 시작
        if (dialogue != null && data != null && data.lines != null && data.lines.Count > 0){
            ticketManager.Prepare(data.key);
            dialogue.StartDialogue(data.lines);
        }
        else
            dialogue?.HideImmediate();
    }
}
