using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace Slainte.Bartending
{
    // 배송원이 바테이블 뒤에서 올라왔다가 다시 내려가며 사라지는 연출. 씬에 배치한 위치가 "올라온"
    // 위치이고, riseDistance만큼 아래가 숨은 위치다. 바테이블 배경보다 먼저 그려지는 형제 순서에
    // 둬야 테이블 뒤에서 솟아오르는 것처럼 보인다.
    // 오브젝트는 항상 켜 두고 Image 표시만 토글한다 — 부모가 꺼지는 도중 SetActive를 바꾸면 Unity가
    // 거부하고, 꺼진 오브젝트에서는 코루틴을 시작할 수 없기 때문.
    public sealed class DeliveryRisePresenter : MonoBehaviour
    {
        [SerializeField] private RectTransform target;
        [SerializeField] private Image image;
        [SerializeField] private Sprite deliverySprite;

        [Header("Motion")]
        [SerializeField] private float riseDistance = 600f;
        [SerializeField, Min(0.01f)] private float riseDuration = 0.35f;
        [SerializeField, Min(0f)] private float holdDuration = 0.6f;
        [SerializeField, Min(0.01f)] private float sinkDuration = 0.3f;

        private Vector2 visiblePosition;
        private bool initialized;
        private Coroutine playRoutine;
        private Action pendingComplete;

        public bool IsPlaying => playRoutine != null;

        private void Awake()
        {
            EnsureInitialized();
        }

        // 연출 도중 부모가 꺼지면 코루틴이 끊기므로, 완료 콜백(재고 지급)을 여기서 대신 호출해
        // 결제만 되고 병이 들어오지 않는 상황을 막는다.
        private void OnDisable()
        {
            if (playRoutine == null)
                return;

            playRoutine = null;
            ResetVisual();
            InvokePendingComplete();
        }

        private void EnsureInitialized()
        {
            if (initialized)
                return;

            initialized = true;
            if (target == null)
                target = transform as RectTransform;
            if (image == null && target != null)
                image = target.GetComponent<Image>();
            if (image != null)
            {
                image.raycastTarget = false;
                if (deliverySprite != null)
                    image.sprite = deliverySprite;
            }

            visiblePosition = target != null ? target.anchoredPosition : Vector2.zero;
            ResetVisual();
        }

        // 이미 재생 중이면 false를 돌려준다 — 호출자가 결제 전에 확인해 중복 배송을 막는다.
        public bool Play(Action onComplete)
        {
            EnsureInitialized();
            if (IsPlaying)
                return false;

            pendingComplete = onComplete;
            if (target == null || image == null || !isActiveAndEnabled)
            {
                // 연출할 수 없는 상태면 지급만 바로 한다.
                InvokePendingComplete();
                return true;
            }

            playRoutine = StartCoroutine(PlayRoutine());
            return true;
        }

        private IEnumerator PlayRoutine()
        {
            Vector2 hidden = visiblePosition + Vector2.down * riseDistance;
            image.enabled = true;
            yield return Move(hidden, visiblePosition, riseDuration);
            if (holdDuration > 0f)
                yield return new WaitForSecondsRealtime(holdDuration);
            yield return Move(visiblePosition, hidden, sinkDuration);

            playRoutine = null;
            ResetVisual();
            InvokePendingComplete();
        }

        private IEnumerator Move(Vector2 from, Vector2 to, float duration)
        {
            float elapsed = 0f;
            target.anchoredPosition = from;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                float eased = 1f - Mathf.Pow(1f - t, 3f);
                target.anchoredPosition = Vector2.LerpUnclamped(from, to, eased);
                yield return null;
            }

            target.anchoredPosition = to;
        }

        private void ResetVisual()
        {
            if (target != null)
                target.anchoredPosition = visiblePosition;
            if (image != null)
                image.enabled = false;
        }

        private void InvokePendingComplete()
        {
            Action complete = pendingComplete;
            pendingComplete = null;
            complete?.Invoke();
        }
    }
}
