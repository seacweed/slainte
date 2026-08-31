using System;
using System.Collections.Generic;
using Slainte.Shared.Lifecycle;
using UnityEngine;

// cutsceneId로 등록된 CutsceneData를 찾아 CutsceneUI에 재생을 위임한다.
public class CutsceneManager : MonoSingleton<CutsceneManager>
{
    [SerializeField] private CutsceneUI cutsceneUI;
    [SerializeField] private List<CutsceneData> cutscenes = new();

    public void Play(string cutsceneId, Action onComplete)
    {
        CutsceneData data = cutscenes.Find(c => c != null && c.cutsceneId == cutsceneId);
        if (data == null || cutsceneUI == null)
        {
            onComplete?.Invoke();
            return;
        }

        cutsceneUI.Play(data, onComplete);
    }

    // 다음 화면이 완전히 화면을 덮은 뒤(SceneTransitionManager.onFadeOutComplete) 호출해서
    // 컷씬 패널을 감춘다. 재생 중이 아니면 안전하게 무시된다.
    public void HideImmediate()
    {
        cutsceneUI?.HideImmediate();
    }
}
