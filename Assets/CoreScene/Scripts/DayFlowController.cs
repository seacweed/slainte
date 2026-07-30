using UnityEngine;

// 하루 진행 순서(필수 에피소드 큐, 영업, 정산)를 전담하는 컨트롤러.
// EpisodeRunner/EpisodeBoardManager/영업 스텁은 GameManager를 직접 호출하지 않고 이 클래스를 거친다.
public class DayFlowController : MonoSingleton<DayFlowController>
{
    private enum PendingStep
    {
        GoToBusiness,
        GoToSettlement
    }

    private PendingStep _afterEpisode = PendingStep.GoToSettlement;

    // Rest에서 "영업 시작"을 눌렀을 때 호출.
    public void StartBusinessDay()
    {
        GameProgress.Instance?.AdvanceDay();

        EpisodeData mandatory = EpisodeManager.Instance?.GetNextMandatoryEpisode();
        if (mandatory != null && mandatory.mandatorySlot == MandatorySlot.BeforeBusiness)
        {
            _afterEpisode = PendingStep.GoToBusiness;
            EpisodeManager.Instance.StartEpisode(mandatory.episodeId);
        }
        else
        {
            EnterBusiness();
        }
    }

    // Rest 보드에서 기본 에피소드를 선택했을 때 호출.
    public void StartDefaultEpisode(string episodeId)
    {
        GameProgress.Instance?.AdvanceDay();

        _afterEpisode = PendingStep.GoToSettlement;
        EpisodeManager.Instance?.StartEpisode(episodeId);
    }

    // EpisodeRunner.EndEncounter()에서 호출 (Default/Mandatory 공통).
    public void OnEpisodeCompleted()
    {
        PendingStep step = _afterEpisode;
        _afterEpisode = PendingStep.GoToSettlement;

        if (step == PendingStep.GoToBusiness)
            EnterBusiness();
        else
            GoToSettlement();
    }

    // 영업(스텁 또는 추후 실제 구현)이 끝났을 때 호출.
    public void OnBusinessCompleted()
    {
        EpisodeData mandatory = EpisodeManager.Instance?.GetNextMandatoryEpisode();
        if (mandatory != null && mandatory.mandatorySlot == MandatorySlot.AfterBusiness)
        {
            _afterEpisode = PendingStep.GoToSettlement;
            EpisodeManager.Instance.StartEpisode(mandatory.episodeId);
        }
        else
        {
            GoToSettlement();
        }
    }

    private void EnterBusiness()
    {
        GameManager.Instance?.ChangeState(GameState.Business);
    }

    public void GoToSettlement()
    {
        GameManager.Instance?.ChangeState(GameState.Settlement);
    }
}
