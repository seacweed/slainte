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
    [InspectorName("방문 키")]
    public string visitKey;
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
    [InspectorName("재등장 대기 일수")]
    [Min(0)] public int cooldownDays = 1;
    [InspectorName("하루 중복 등장 허용")]
    public bool allowDuplicateInDay;

    [Header("주문 후보")]
    [InspectorName("주문 목록")]
    public List<CustomerVisitOrderOption> orders = new();
}
