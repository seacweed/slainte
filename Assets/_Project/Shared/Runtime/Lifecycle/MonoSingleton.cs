using UnityEngine;

internal static class MonoSingletonLifecycle
{
    public static bool IsQuitting { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset()
    {
        IsQuitting = false;
    }

    public static void MarkQuitting()
    {
        IsQuitting = true;
    }
}

public class MonoSingleton<T> : MonoBehaviour where T : MonoBehaviour
{
    private static T _instance;

    public static T Instance
    {
        get
        {
            if (MonoSingletonLifecycle.IsQuitting)
                return null;

            if (_instance == null)
            {
                // 씬에 이미 존재하는지 확인
                _instance = FindFirstObjectByType<T>();

                // 씬에 없다면 새로 생성 (보통 Core_Scene에 미리 배치하므로 실행될 일은 적음)
                if (_instance == null)
                {
                    GameObject singletonObject = new GameObject(typeof(T).Name);
                    _instance = singletonObject.AddComponent<T>();
                }
            }
            return _instance;
        }
    }

    protected virtual void Awake()
    {
        // 인스턴스가 이미 존재하는데, 자기 자신이 아니라면 파괴 (중복 방지)
        if (_instance != null && _instance != this)
        {
            Destroy(this.gameObject);
            return;
        }

        // 인스턴스가 자기 자신이라면 유지
        _instance = this as T;
        DontDestroyOnLoad(this.gameObject);
    }

    protected virtual void OnApplicationQuit()
    {
        MonoSingletonLifecycle.MarkQuitting();
    }

    protected virtual void OnDestroy()
    {
        if (_instance == this)
            _instance = null;
    }
}
