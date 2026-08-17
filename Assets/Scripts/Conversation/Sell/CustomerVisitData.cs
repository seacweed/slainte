using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public sealed class CustomerVisitMember
{
    [InspectorName("캐릭터 키")]
    public string characterKey;
    [InspectorName("배치 슬롯 (-1은 자동)")]
    public int slotIndex = -1;
    [InspectorName("기본 표정 키")]
    public string expressionKeyMid = "mid";
    [InspectorName("좋음 표정 키")]
    public string expressionKeyGood = "good";
    [InspectorName("나쁨 표정 키")]
    public string expressionKeyBad = "bad";
}

[Serializable]
public sealed class CustomerVisitOrderOption
{
    [InspectorName("주문 데이터")]
    public CustomerOrderData order;
    [InspectorName("선택 가중치")]
    [Min(0f)] public float weight = 1f;
    [InspectorName("등장 조건")]
    public EpisodeTriggerCondition condition = new();
}

[CreateAssetMenu(menuName = "Slainte/손님 방문 데이터", fileName = "CustomerVisit_")]
public sealed class CustomerVisitData : ScriptableObject
{
    [Header("식별 정보")]
    [InspectorName("원본 손님 ID")]
    public string sourceCustomerId;
    [InspectorName("방문 키")]
    public string visitKey;
    [InspectorName("손님 속성 키")]
    public string customerAttributeKey;
    [InspectorName("말투 속성 키")]
    public string speechStyleKey;
    [InspectorName("태그")]
    public List<string> tags = new();

    [Header("등장 구성")]
    [InspectorName("구성원")]
    public List<CustomerVisitMember> members = new();

    [Header("추첨 규칙")]
    [InspectorName("선택 가중치")]
    [Min(0f)] public float weight = 1f;
    [InspectorName("등장 조건")]
    public EpisodeTriggerCondition condition = new();
    [InspectorName("최대 등장 날짜")]
    [Tooltip("0이면 최대 날짜 제한이 없습니다.")]
    [Min(0)] public int maxDay;
    [InspectorName("재등장 대기 시간(초)")]
    [Tooltip("주문 결과 처리가 끝난 뒤 이 손님이 일반 손님 풀에 다시 들어오기까지의 유효 영업시간입니다.")]
    [Min(0f)] public float cooldownSeconds = 100f;
    [InspectorName("쿨다운 공유 키")]
    [Tooltip("같은 인물의 여러 방문형이 공유할 키입니다. 비어 있으면 방문 키를 사용합니다.")]
    public string cooldownGroupKey;

    [Header("주문 후보")]
    [InspectorName("기획 원본 주문명")]
    [Tooltip("CSV 원문 보존용입니다. 연결되지 않은 레시피도 이 목록에는 남습니다.")]
    public List<string> plannedOrderNames = new();
    [InspectorName("주문 목록")]
    public List<CustomerVisitOrderOption> orders = new();

    public string GetCooldownKey()
    {
        return !string.IsNullOrWhiteSpace(cooldownGroupKey)
            ? cooldownGroupKey.Trim()
            : visitKey?.Trim() ?? string.Empty;
    }
}
