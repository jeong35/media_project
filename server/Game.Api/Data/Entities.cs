using System.ComponentModel.DataAnnotations;

namespace Game.Api.Data;

public sealed class GameSession
{
    [Key] public Guid Id { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public string Status { get; set; } = "Active";
    public int CurrentAct { get; set; } = 1;
    public int CurrentTurn { get; set; }
    public string Mode { get; set; } = "Mock";
    public string Phase { get; set; } = "AwaitingDialogue";
    public int Military { get; set; }
    public int Treasury { get; set; }
    public int Legitimacy { get; set; }
    public int Ward { get; set; }
    public string? SelectedChoiceId { get; set; }
    public string? EndingJson { get; set; }
}

public sealed class GameEvent
{
    [Key] public Guid Id { get; set; }
    public Guid SessionId { get; set; }
    public int Sequence { get; set; }
    public string EventType { get; set; } = string.Empty;
    public int Act { get; set; }
    public string PayloadJson { get; set; } = "{}";
    public DateTimeOffset CreatedAt { get; set; }
    public string? IdempotencyKey { get; set; }
}

public sealed class NpcState
{
    public Guid SessionId { get; set; }
    public string NpcId { get; set; } = string.Empty;
    public int Trust { get; set; }
    public int Fear { get; set; }
    public int Suspicion { get; set; }
    public string Status { get; set; } = "active";
    public string KnownFactsJson { get; set; } = "[]";
    public string RevealedSecretsJson { get; set; } = "[]";
    public string ConversationSummary { get; set; } = string.Empty;
}

public sealed class TraitScore
{
    public Guid SessionId { get; set; }
    public string TraitId { get; set; } = string.Empty;
    public int Score { get; set; }
}

public sealed class CareerAffinity
{
    public Guid SessionId { get; set; }
    public string AffinityId { get; set; } = string.Empty;
    public int Score { get; set; }
    public int LastChangedAct { get; set; }
}
