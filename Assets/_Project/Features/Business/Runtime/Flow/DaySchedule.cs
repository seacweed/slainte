using System;
using System.Collections.Generic;

namespace Slainte.Business
{
    // 하루 손님 슬롯별로 배정된 인카운터 에피소드 후보를 알려주는 일정 소스.
    // 실제 게임은 EpisodeManager(전체 에피소드 카탈로그)가, 플레이테스트는 메모리 일정표가 구현한다.
    public interface IDayScheduleSource
    {
        // results를 비운 뒤 (챕터, day, slot)에 배정된 후보를 slotPriority 내림차순으로 채운다.
        // 호출자가 버퍼를 넘기는 이유는 슬롯마다 호출되는 조회에서 할당을 없애기 위해서다.
        void CollectSlotCandidates(string chapterId, int day, int slot, List<EpisodeData> results);
    }

    // 에피소드 목록을 (day, slot) 키로 한 번만 색인해 두는 일정표. 각 칸의 후보는 색인 시점에
    // slotPriority 내림차순(같으면 episodeId 순)으로 정렬해 두므로 조회는 필터링만 한다.
    public sealed class DayScheduleIndex : IDayScheduleSource
    {
        private static readonly Comparison<EpisodeData> PriorityOrder = (a, b) =>
        {
            int byPriority = b.slotPriority.CompareTo(a.slotPriority);
            return byPriority != 0
                ? byPriority
                : string.Compare(a.episodeId, b.episodeId, StringComparison.OrdinalIgnoreCase);
        };

        private readonly Dictionary<long, List<EpisodeData>> slots = new();

        public DayScheduleIndex(IEnumerable<EpisodeData> episodes = null)
        {
            Rebuild(episodes);
        }

        public void Rebuild(IEnumerable<EpisodeData> episodes)
        {
            slots.Clear();
            if (episodes == null)
                return;

            foreach (EpisodeData episode in episodes)
                Add(episode);

            foreach (List<EpisodeData> candidates in slots.Values)
                candidates.Sort(PriorityOrder);
        }

        public void CollectSlotCandidates(
            string chapterId,
            int day,
            int slot,
            List<EpisodeData> results)
        {
            if (results == null)
                return;

            results.Clear();
            if (!slots.TryGetValue(ToKey(day, slot), out List<EpisodeData> candidates))
                return;

            for (int i = 0; i < candidates.Count; i++)
            {
                if (IsSameChapter(chapterId, candidates[i].chapterId))
                    results.Add(candidates[i]);
            }
        }

        private void Add(EpisodeData episode)
        {
            if (episode == null
                || !episode.IsScheduled
                || string.IsNullOrWhiteSpace(episode.episodeId))
                return;

            long key = ToKey(episode.scheduledDay, episode.scheduledSlot);
            if (!slots.TryGetValue(key, out List<EpisodeData> candidates))
            {
                candidates = new List<EpisodeData>(1);
                slots.Add(key, candidates);
            }

            candidates.Add(episode);
        }

        // 현재 챕터가 정해지지 않았거나(개발용 진입) 에피소드에 챕터가 비어 있으면 어느 챕터에서든
        // 등장할 수 있게 둔다 — 챕터 데이터가 아직 없는 초기 콘텐츠도 일정만으로 테스트 가능해야 하기 때문.
        private static bool IsSameChapter(string currentChapterId, string episodeChapterId)
        {
            return string.IsNullOrWhiteSpace(currentChapterId)
                || string.IsNullOrWhiteSpace(episodeChapterId)
                || string.Equals(
                    currentChapterId,
                    episodeChapterId,
                    StringComparison.OrdinalIgnoreCase);
        }

        private static long ToKey(int day, int slot)
        {
            return ((long)day << 32) | (uint)slot;
        }
    }

    // 슬롯 차례가 왔을 때 후보 중 실제로 등장할 에피소드 하나를 고른다.
    // 판정을 슬롯 시점으로 미루는 이유: 같은 날 앞 슬롯 에피소드가 세운 flag가 뒤 슬롯의 등장 여부에
    // 곧바로 반영되어야 하기 때문(영업 시작 시 하루치를 미리 확정하면 이 연결이 끊긴다).
    public static class DayScheduleResolver
    {
        public static EpisodeData PickEpisode(
            IReadOnlyList<EpisodeData> candidates,
            GameProgress progress,
            ISet<string> excludedEpisodeIds)
        {
            if (candidates == null || progress == null)
                return null;

            for (int i = 0; i < candidates.Count; i++)
            {
                EpisodeData episode = candidates[i];
                if (episode == null
                    || string.IsNullOrWhiteSpace(episode.episodeId)
                    || progress.IsEpisodeCompleted(episode.episodeId)
                    || (excludedEpisodeIds != null && excludedEpisodeIds.Contains(episode.episodeId))
                    || !ProgressConditionEvaluator.IsMet(episode.triggerCondition, progress))
                    continue;

                return episode;
            }

            return null;
        }
    }
}
