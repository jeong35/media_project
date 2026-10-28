namespace Game.Api.Errors;

public sealed class GameRuleException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

public sealed class SessionNotFoundException : Exception;
