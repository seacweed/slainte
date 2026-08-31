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

    [SerializeField] private string endingEpisodeId; // '지구로' 에피소드의 episodeId. Inspector에서 설정.

    private PendingStep _afterEpisode = PendingStep.GoToSettlement;
    private string _pendingSettlementCutsceneId;

    // Rest에서 "영업 시작"을 눌렀을 때 호출.
    public void StartBusinessDay()
    {
        GameProgress.Instance?.AdvanceDay();
        _pendingSettlementCutsceneId = null;
        BeginMandatoryOrBusiness();
    }

    // MainMenu "게임 시작"에서 최초 1회 호출. Day 1은 이미 1이므로 AdvanceDay를 호출하지 않고,
    // StartBusinessDay와 동일한 필수 에피소드 큐 로직을 그대로 태운다.
    public void StartFirstDay()
    {
        _pendingSettlementCutsceneId = CutsceneIds.FathersNote;
        BeginMandatoryOrBusiness();
    }

    private void BeginMandatoryOrBusiness()
    {
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
        _pendingSettlementCutsceneId =
            (!string.IsNullOrEmpty(endingEpisodeId) && episodeId == endingEpisodeId)
                ? CutsceneIds.Ending
                : null;
        EpisodeManager.Instance?.StartEpisode(episodeId);
    }

    // 정산 종료 시 SettlementManager가 재생할 컷씬 id를 가져가면서 비운다. 없으면 null.
    public string ConsumePendingSettlementCutscene()
    {
        string id = _pendingSettlementCutsceneId;
        _pendingSettlementCutsceneId = null;
        return id;
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
