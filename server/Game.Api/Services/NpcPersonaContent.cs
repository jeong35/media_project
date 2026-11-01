using System.Text.Json;
using System.Text.Encodings.Web;

namespace Game.Api.Services;

public sealed record NpcPersonaContent(
    string Id,
    string Name,
    string Role,
    string Archetype,
    IReadOnlyDictionary<string, double> Personality,
    IReadOnlyList<string> CoreTraits,
    IReadOnlyDictionary<string, string> SpeechStyle,
    IReadOnlyList<string> Values,
    IReadOnlyList<string> Likes,
    IReadOnlyList<string> Dislikes,
    string InitialAttitude,
    IReadOnlyList<string> BehaviorRules,
    string DisclosurePolicy);

public sealed class NpcPersonaCatalog(IReadOnlyDictionary<string, NpcPersonaContent> personas)
{
    public NpcPersonaContent Get(string npcId) => personas.TryGetValue(npcId, out var persona)
        ? persona
        : throw new InvalidOperationException($"NPC persona was not found: {npcId}");

    public static NpcPersonaCatalog Load()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "data", "npc");
        if (!Directory.Exists(directory))
            throw new InvalidOperationException($"NPC persona directory was not found: {directory}");

        var personas = Directory.GetFiles(directory, "*.json")
            .Select(path => JsonSerializer.Deserialize<NpcPersonaContent>(File.ReadAllText(path), PersonaJson.Options)
                ?? throw new InvalidOperationException($"NPC persona could not be parsed: {path}"))
            .ToDictionary(persona => persona.Id, StringComparer.Ordinal);
        if (personas.Count == 0) throw new InvalidOperationException("No NPC personas were found.");
        return new NpcPersonaCatalog(personas);
    }
}

public static class NpcPersonaPromptBuilder
{
    public static string Build(NpcPersonaContent persona) => JsonSerializer.Serialize(new
    {
        persona.Id,
        persona.Name,
        persona.Role,
        persona.Archetype,
        persona.Personality,
        persona.CoreTraits,
        persona.SpeechStyle,
        persona.Values,
        persona.Likes,
        persona.Dislikes,
        persona.InitialAttitude,
        persona.BehaviorRules
    }, PersonaJson.Options);
}

internal static class PersonaJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };
}
