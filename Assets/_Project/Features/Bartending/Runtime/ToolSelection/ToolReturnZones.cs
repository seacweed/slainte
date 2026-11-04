using System.Collections.Generic;
using UnityEngine;

namespace Slainte.Bartending
{
    // 들고 있는 도구·잔을 놓았을 때 "도구 공간 반납"으로 처리할 화면 영역.
    // 도구 컨트롤러들이 도구 공간 UI 클래스를 직접 알지 않도록 BottleReturnZones와 같은 방식으로 분리한다.
    public interface IToolReturnZone
    {
        bool ContainsReturnPoint(Vector2 screenPosition);
        // 반납을 시도한다. 영역 안에서 처리된 입력이면(반납이 거부돼도) true를 돌려 일반 놓기로 새지 않게 한다.
        bool TryReturn(IBartendingItem item);
    }

    // 활성 도구 반납 영역 등록소. 반납이 일어난 프레임을 기록해, 같은 누름의 슬롯 클릭이
    // 방금 반납한 도구를 다시 꺼내는 것을 막는다.
    public static class ToolReturnZones
    {
        private static readonly List<IToolReturnZone> zones = new List<IToolReturnZone>();
        private static int lastReturnFrame = -1;

        public static int LastReturnFrame => lastReturnFrame;

        public static void Register(IToolReturnZone zone)
        {
            if (zone != null && !zones.Contains(zone))
                zones.Add(zone);
        }

        public static void Unregister(IToolReturnZone zone)
        {
            zones.Remove(zone);
        }

        public static bool TryReturnHeldItem(IBartendingItem item, Vector2 screenPosition)
        {
            if (item == null)
                return false;

            IToolReturnZone zone = FindZone(screenPosition);
            if (zone == null)
                return false;

            if (!zone.TryReturn(item))
                return false;

            lastReturnFrame = Time.frameCount;
            return true;
        }

        private static IToolReturnZone FindZone(Vector2 screenPosition)
        {
            for (int i = zones.Count - 1; i >= 0; i--)
            {
                IToolReturnZone zone = zones[i];
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
