using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace F1RaceLens.Api.Assistant;

public sealed record WebEvidence(IReadOnlyList<ChatSource> Sources, string Status);

// Fixed Wikimedia endpoints only. No arbitrary fetching, generated URLs, chat history or account data.
public sealed class ExternalKnowledge(IHttpClientFactory clients, TimeProvider clock)
{
    public static string SearchTerms(ChatRequest request)
    {
        var question = request.Messages[^1].Content;
        // Do not transmit pasted URLs, addresses, identifiers or likely secrets to the search provider.
        question = Regex.Replace(question, @"https?://\S+|\S+@\S+|\b[\w-]{24,}\b|\b\d{5,}\b", " ", RegexOptions.IgnoreCase);
        var normalized = AssistantKnowledge.Normalize(question);
        var expanded = AssistantKnowledge.Query(new(request.Language, [new("user", question)]));
        var aliases = expanded[normalized.Length..].Trim();
        if (aliases.Length > 0) return string.Join(' ', aliases.Split(' ', StringSplitOptions.RemoveEmptyEntries).Distinct().Take(6));
        var stop = "що таке як коли чому скільки розкажи поясни мені будь ласка про який яка які його її це та чи для має титулів перемог what who when why how many tell explain about please is are the a an of in and his her titles wins was does has which que quien qué cómo cuantos cuántos de la el le les des du est qui combien explique titres victoires was wie viele wer der die das und über erkläre titel siege cosa chi come quanti della delle di il lo gli una un e titoli vittorie qué cuantos dime sobre los las del y en títulos victorias".Split(' ').ToHashSet(StringComparer.OrdinalIgnoreCase);
        var words = Regex.Matches(question, @"[\p{L}\p{N}][\p{L}\p{N}-]{1,30}")
            .Select(m => m.Value).Where(w => !stop.Contains(w)).Distinct(StringComparer.OrdinalIgnoreCase).Take(8);
        var terms = string.Join(' ', words);
        return terms.Length == 0 ? "Formula 1" : terms;
    }

    public async Task<WebEvidence> SearchAsync(ChatRequest request, CancellationToken token)
    {
        if (!request.WebSearch) return new([], "off");
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(token);
        budget.CancelAfter(TimeSpan.FromSeconds(8));
        try
        {
            var language = request.Language is "uk" or "en" or "de" or "it" or "es" or "fr" ? request.Language : "en";
            var terms = SearchTerms(request);
            // Transliteration aliases retrieve the canonical English article; the model translates evidence.
            if (Regex.IsMatch(terms, @"\A[a-zA-Z0-9 -]+\z")) language = "en";
            var client = clients.CreateClient("AssistantWeb");
            var url = $"https://{language}.wikipedia.org/w/api.php?action=query&generator=search&gsrnamespace=0&gsrlimit=3&gsrsearch={Uri.EscapeDataString(terms)}&prop=extracts&exintro=1&explaintext=1&exchars=700&exlimit=3&format=json&formatversion=2";
            using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, budget.Token);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength > 128 * 1024) return new([], "unavailable");
            await using var stream = await response.Content.ReadAsStreamAsync(budget.Token);
            using var buffer = new MemoryStream();
            var chunk = new byte[4096];
            int read;
            while ((read = await stream.ReadAsync(chunk, budget.Token)) > 0)
            {
                if (buffer.Length + read > 128 * 1024) return new([], "unavailable");
                buffer.Write(chunk, 0, read);
            }
            using var json = JsonDocument.Parse(buffer.ToArray());
            var sources = Parse(json.RootElement, language, clock.GetUtcNow());
            return new(sources, sources.Count > 0 ? "ok" : "empty");
        }
        catch (Exception error) when (error is HttpRequestException or JsonException or IOException
            || error is OperationCanceledException && !token.IsCancellationRequested)
        {
            // Outgoing URLs and user queries must not be logged, even on provider failure.
            return new([], "unavailable");
        }
    }

    public static IReadOnlyList<ChatSource> Parse(JsonElement root, string language, DateTimeOffset fetchedAt)
    {
        if (language is not ("uk" or "en" or "de" or "it" or "es" or "fr")) return [];
        if (!root.TryGetProperty("query", out var query) || !query.TryGetProperty("pages", out var pages)) return [];
        var result = new List<ChatSource>();
        foreach (var page in pages.EnumerateArray().OrderBy(p => p.TryGetProperty("index", out var i) ? i.GetInt32() : 99).Take(3))
        {
            if (!page.TryGetProperty("title", out var title) || !page.TryGetProperty("extract", out var extract)) continue;
            var text = WebUtility.HtmlDecode(Regex.Replace(extract.GetString() ?? "", "<[^>]*>", ""));
            if (string.IsNullOrWhiteSpace(text)) continue;
            // Search is scoped to motorsport: irrelevant encyclopedia hits are not evidence.
            if (!Regex.IsMatch(text + " " + title.GetString(), @"formula|formule|formel|fórmula|формул|grand prix|гран-прі|motorsport|автоспорт|racing|перегон", RegexOptions.IgnoreCase)) continue;
            var heading = title.GetString() ?? "Wikipedia";
            result.Add(new(result.Count + 1, $"Wikipedia · {heading} · {fetchedAt:yyyy-MM-dd} UTC (retrieved)",
                $"https://{language}.wikipedia.org/wiki/{Uri.EscapeDataString(heading.Replace(' ', '_'))}",
                "External encyclopedia excerpt; retrieval date is not the date of the underlying facts. " + text[..Math.Min(text.Length, 700)]));
        }
        return result;
    }
}
