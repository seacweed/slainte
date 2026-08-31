using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(CanvasGroup))]
public abstract class BaseUIManager : MonoBehaviour
{
    // RestScene의 BaseUIManager 팝업들은 서로 배타적으로 열림 — 하나가 열리면 나머지는 자동으로 닫힘
    private static readonly HashSet<BaseUIManager> _openInstances = new();

    // 인트로 로딩 연출처럼, 진행 중에 다른 팝업으로 전환되면 안 되는 구간에서 켬(OpenUI 자체를 막음).
    // CloseUI()는 막지 않으므로 잠긴 상태에서도 자기 자신은 닫을 수 있음.
    private static bool _transitionLocked = false;

    protected static void LockTransitions()   => _transitionLocked = true;
    protected static void UnlockTransitions() => _transitionLocked = false;

    // 내부 변수
    protected CanvasGroup _canvasGroup;
    protected Coroutine _activeCoroutine;
    protected bool _isOpening = false; // OpenUI 호출 중인지 여부 추적용 플래그

    protected virtual void Awake()
    {
        _canvasGroup = GetComponent<CanvasGroup>();
        
        // 초기화: 터치 방지, 투명하게, 비활성화
        if (_canvasGroup != null)
        {
            _canvasGroup.alpha = 0; // 기본은 안 보임
            _canvasGroup.interactable = false;
            _canvasGroup.blocksRaycasts = false;
        }
        
        // OpenUI() 진행 중에 Awake가 호출된 경우엔 비활성화하지 않음
        if (gameObject.activeSelf && !_isOpening) gameObject.SetActive(false);
    }

    // --- [공통기능] 열기 / 닫기 신호 ---

    public void OpenUI()
    {
        if (_transitionLocked || !CanOpen()) return;

        CloseOtherPanels();

        _isOpening = true;
        gameObject.SetActive(true);
        OnOpen(); // 자식별 초기화 실행

        if (_activeCoroutine != null) StopCoroutine(_activeCoroutine);
        _activeCoroutine = StartCoroutine(AnimateOpen()); // 자식의 애니메이션 실행

        _openInstances.Add(this);
    }

    public void CloseUI()
    {
        _isOpening = false;
        _openInstances.Remove(this);

        if (_activeCoroutine != null) StopCoroutine(_activeCoroutine);
        _activeCoroutine = StartCoroutine(AnimateClose()); // 자식의 애니메이션 실행
        OnClose(); // 자식별 종료 실행
    }

    // 지금 열려 있는 다른 BaseUIManager 팝업을 전부 닫음(자기 자신은 제외)
    private void CloseOtherPanels()
    {
        if (_openInstances.Count == 0) return;

        // 순회 중 CloseUI()가 컬렉션을 변경하므로 스냅샷을 떠서 순회
        var others = new List<BaseUIManager>(_openInstances);
        foreach (var other in others)
        {
            if (other != null && other != this) other.CloseUI();
        }
    }

    protected virtual void OnDestroy()
    {
        _openInstances.Remove(this);
    }

    // --- [추상 메서드] 자식들이 각자 구현해야 함! ---
    protected abstract IEnumerator AnimateOpen();
    protected abstract IEnumerator AnimateClose();

    // (선택) 오버라이드용 빈 함수
    protected virtual bool CanOpen() => true;
    protected virtual void OnOpen() { }
    protected virtual void OnClose() { }
}
