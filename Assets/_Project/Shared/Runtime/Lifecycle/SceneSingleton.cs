using UnityEngine;

// Scene-scoped singleton: unlike MonoSingleton<T>, does NOT call DontDestroyOnLoad.
// Use for managers that hold serialized references to objects local to a single
// additively-loaded scene (e.g. BusinessScene), so a fresh instance is created
// correctly each time that scene reloads.
public class SceneSingleton<T> : MonoBehaviour where T : MonoBehaviour
{
    private static T _instance;

    public static T Instance
    {
        get
        {
            if (_instance == null)
            {
                _instance = FindFirstObjectByType<T>();
            }
            return _instance;
        }
    }

    protected virtual void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(this.gameObject);
            return;
        }

        _instance = this as T;
    }

    protected virtual void OnDestroy()
    {
        if (_instance == this)
        {
            _instance = null;
        }
    }
}
