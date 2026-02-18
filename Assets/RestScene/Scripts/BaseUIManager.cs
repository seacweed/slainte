using System.Collections;
using UnityEngine;

[RequireComponent(typeof(CanvasGroup))]
public abstract class BaseUIManager : MonoBehaviour
{
    // 내부 변수
    protected CanvasGroup _canvasGroup;
    protected Coroutine _activeCoroutine;

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
        
        if (gameObject.activeSelf) gameObject.SetActive(false);
    }

    // --- [공통기능] 열기 / 닫기 신호 ---

    public void OpenUI()
    {
        gameObject.SetActive(true);
        OnOpen(); // 자식별 초기화 실행

        if (_activeCoroutine != null) StopCoroutine(_activeCoroutine);
        _activeCoroutine = StartCoroutine(AnimateOpen()); // 자식의 애니메이션 실행
    }

    public void CloseUI()
    {
        if (_activeCoroutine != null) StopCoroutine(_activeCoroutine);
        _activeCoroutine = StartCoroutine(AnimateClose()); // 자식의 애니메이션 실행
        OnClose(); // 자식별 종료 실행
    }

    // --- [추상 메서드] 자식들이 각자 구현해야 함! ---
    protected abstract IEnumerator AnimateOpen();
    protected abstract IEnumerator AnimateClose();

    // (선택) 오버라이드용 빈 함수
    protected virtual void OnOpen() { }
    protected virtual void OnClose() { }
}