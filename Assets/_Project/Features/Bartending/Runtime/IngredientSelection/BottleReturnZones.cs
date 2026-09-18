using System.Collections.Generic;
using UnityEngine;

namespace Slainte.Bartending
{
    // 들고 있는 병을 놓았을 때 "술장으로 반환"으로 처리할 화면 영역.
    // 술장 UI가 교체돼도 BottleController가 특정 UI 클래스를 알 필요가 없도록 영역을 계약으로 분리한다.
    public interface IBottleReturnZone
    {
        bool ContainsReturnPoint(Vector2 screenPosition);
        void OnBottleReturned();
    }

    // 활성 반환 영역 등록소. 병 놓기 입력과 영역 UI의 클릭 핸들러가 같은 프레임에 모두 반환을
    // 시도할 수 있어, 한 프레임에 한 번만 실제 반환을 수행하고 나머지 호출에는 성공만 알린다.
    public static class BottleReturnZones
    {
        private static readonly List<IBottleReturnZone> zones = new List<IBottleReturnZone>();
        private static int lastReturnFrame = -1;

        // 병 반환은 마우스를 누른 프레임에 일어나고 UI 클릭 이벤트는 뗀 프레임에 오므로, 영역 UI는
        // 이 값으로 "이번 누름이 반환으로 소비됐는지"를 판별해 반환 직후 재료가 스폰되는 것을 막는다.
        public static int LastReturnFrame => lastReturnFrame;

        public static void Register(IBottleReturnZone zone)
        {
            if (zone != null && !zones.Contains(zone))
                zones.Add(zone);
        }

        public static void Unregister(IBottleReturnZone zone)
        {
            zones.Remove(zone);
        }

        public static bool TryReturnHeldBottle(Vector2 screenPosition)
        {
            if (lastReturnFrame == Time.frameCount)
                return true;

            IBottleReturnZone zone = FindZone(screenPosition);
            if (zone == null)
                return false;

            BusinessBartendingBootstrap bartending =
                Object.FindFirstObjectByType<BusinessBartendingBootstrap>();
            if (bartending == null)
                return false;

            if (!bartending.TryReturnHeldBottleToShelf(out string failure))
            {
                // 들고 있는 병이 없으면(failure 비어 있음) 반환 대상이 아니므로 일반 놓기로 넘긴다.
                // 병은 있는데 반환이 거부된 경우는 영역 안에서 놓은 것이므로 입력을 소비한다.
                if (string.IsNullOrWhiteSpace(failure))
                    return false;

                Debug.LogWarning("[BottleReturn] " + failure);
                return true;
            }

            lastReturnFrame = Time.frameCount;
            zone.OnBottleReturned();
            return true;
        }

        private static IBottleReturnZone FindZone(Vector2 screenPosition)
        {
            for (int i = zones.Count - 1; i >= 0; i--)
            {
                IBottleReturnZone zone = zones[i];
                if (zone is Object unityObject && unityObject == null)
                {
                    zones.RemoveAt(i);
                    continue;
                }

                if (zone != null && zone.ContainsReturnPoint(screenPosition))
                    return zone;
            }

            return null;
        }
    }
}
