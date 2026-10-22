namespace F1RaceLens.Api.Assistant;

public sealed record ChatMessage(string Role, string Content);
public sealed record ChatRequest(string Language, ChatMessage[] Messages, string? Page = null, bool WebSearch = false);
public sealed record ChatSource(int Id, string Title, string Url, string Text);
public sealed record ChatReply(string Answer, IReadOnlyList<ChatSource> Sources, string Model, string Kind = "model", string WebStatus = "off", IReadOnlyList<ChatSource>? WebSources = null);

public static class ChatValidation
{
    public static bool Valid(ChatRequest? request) => request is { Messages: { Length: > 0 and <= 12 } }
        && new[] { "uk", "en", "de", "it", "es", "fr" }.Contains(request.Language)
        && request.Messages.All(m => m is not null && m.Role is "user" or "assistant"
            && !string.IsNullOrWhiteSpace(m.Content) && m.Content.Length <= 3000)
        && request.Messages.Sum(m => m.Content.Length) <= 6000
        && request.Messages[^1].Role == "user"
        && request.Messages.Select((m, i) => m.Role == (i % 2 == 0 ? "user" : "assistant")).All(x => x)
        && (request.Page is null || request.Page.Length <= 240 && System.Text.RegularExpressions.Regex.IsMatch(
            request.Page, @"\A/(?:home|news|calendar|standings|drivers|teams|compare|insights|predictions|history|search)(?:[/a-zA-Z0-9_?=&-]*)\z"));
}
