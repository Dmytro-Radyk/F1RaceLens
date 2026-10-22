using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using F1RaceLens.Api.Catalog;
using F1RaceLens.Api.News;
using F1RaceLens.Api.Storage;
using Microsoft.EntityFrameworkCore;

namespace F1RaceLens.Api.Assistant;

// Bounded lexical RAG over public records. No generated SQL, arbitrary URLs, or account tables.
public sealed class AssistantKnowledge(CareerCatalog catalog, IServiceScopeFactory scopes, F1News news, TimeProvider clock)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly Dictionary<string, string> Aliases = new() {
        ["льюіс"] = "lewis", ["макс"] = "max", ["міхаел"] = "michael", ["джордж"] = "george",
        ["шарл"] = "charles", ["ландо"] = "lando", ["оскар"] = "oscar", ["фернандо"] = "fernando",
        ["ферстап"] = "verstappen", ["верстап"] = "verstappen", ["гаміл"] = "hamilton", ["хеміл"] = "hamilton",
        ["леклер"] = "leclerc", ["антонел"] = "antonelli", ["рассел"] = "russell", ["норріс"] = "norris",
        ["піастр"] = "piastri", ["алонс"] = "alonso", ["шум ахер"] = "schumacher", ["шумахер"] = "schumacher",
        ["феттел"] = "vettel", ["сенн"] = "senna", ["феррар"] = "ferrari", ["мерсед"] = "mercedes",
        ["маклар"] = "mclaren", ["ред бул"] = "red bull",
        ["албон"] = "albon", ["сайнс"] = "sainz", ["лоусон"] = "lawson", ["хаджар"] = "hadjar",
        ["гасл"] = "gasly", ["газл"] = "gasly", ["колапін"] = "colapinto", ["берман"] = "bearman",
        ["гюлькен"] = "hulkenberg", ["хюлькен"] = "hulkenberg", ["бортолет"] = "bortoleto",
        ["ліндблад"] = "lindblad", ["строл"] = "stroll", ["перес"] = "perez", ["боттас"] = "bottas",
        ["райккон"] = "raikkonen", ["хаккін"] = "hakkinen", ["гаккін"] = "hakkinen", ["лауд"] = "lauda",
        ["фарін"] = "farina", ["фанхіо"] = "fangio", ["фанжіо"] = "fangio", ["кларк"] = "clark",
        ["вільямс"] = "williams", ["альпін"] = "alpine", ["хаас"] = "haas", ["ауді"] = "audi",
        ["каділак"] = "cadillac", ["заубер"] = "sauber", ["лотус"] = "lotus", ["брабем"] = "brabham"
    };
    public static string Normalize(string value) => string.Concat(value.Normalize(NormalizationForm.FormD)
        .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)).ToLowerInvariant();

    public static string Query(ChatRequest request)
    {
        var query = Normalize(string.Join(' ', request.Messages.Where(m => m.Role == "user").TakeLast(3).Select(m => m.Content)));
        var page = request.Page?.Split('?')[0].Split('/') ?? [];
        if (page.Length >= 3 && page[1] is "drivers" or "teams") query += " " + page[^1].Replace('_', ' ').Replace('-', ' ');
        foreach (var alias in Aliases) if (query.Contains(alias.Key, StringComparison.Ordinal)) query += " " + alias.Value;
        return query;
    }

    public async Task<IReadOnlyList<ChatSource>> SearchAsync(ChatRequest request, CancellationToken token)
    {
        var query = Query(request);
        var words = Regex.Matches(query, @"[\p{L}\p{N}_-]{3,}").Select(m => m.Value).Distinct().ToArray();
        int Score(string text) { var normalized = Normalize(text); return words.Count(w => normalized.Contains(w, StringComparison.Ordinal)); }
        var result = new List<ChatSource>();
        void Add(string title, string url, string text)
        {
            if (result.Sum(s => s.Text.Length) + text.Length <= 8000 && result.Count < 10)
                result.Add(new(result.Count + 1, title, url, text));
        }
        foreach (var fact in F1Knowledge.Facts.Where(f => words.Any(w => f.Terms.Split(' ').Any(term => w.StartsWith(Normalize(term), StringComparison.Ordinal)))).Take(2))
            Add(fact.Title, fact.Url, request.Language == "uk" ? fact.Ukrainian ?? fact.Text : fact.Text);
        var data = catalog.Read();
        var people = data.GetProperty("drivers").EnumerateArray().Where(row => Score(row.GetProperty("name").GetString()! + " " + row.GetProperty("id").GetString()) > 0)
            .OrderByDescending(row => Score(row.GetProperty("name").GetString()!)).Take(3).ToArray();
        foreach (var row in people)
            Add($"F1DB · {row.GetProperty("name").GetString()} · cutoff {data.GetProperty("throughSeason")}/{data.GetProperty("throughRound")}",
                "/drivers/career/" + row.GetProperty("id").GetString(), row.GetRawText());
        foreach (var row in data.GetProperty("constructors").EnumerateArray()
            .Where(row => Score(row.GetProperty("name").GetString()!) > 0).OrderByDescending(row => Score(row.GetProperty("name").GetString()!)).Take(2))
            Add($"F1DB · {row.GetProperty("name").GetString()} · cutoff {data.GetProperty("throughSeason")}/{data.GetProperty("throughRound")}",
                "/teams/career/" + row.GetProperty("id").GetString(), row.GetRawText());

        var yearText = Regex.Match(request.Messages[^1].Content, @"\b(?:19|20)\d{2}\b").Value;
        if (yearText.Length == 0) yearText = Regex.Matches(query, @"\b(?:19|20)\d{2}\b").LastOrDefault()?.Value;
        if (string.IsNullOrEmpty(yearText)) yearText = Regex.Match(request.Page ?? "", @"\b(?:19|20)\d{2}\b").Value;
        var year = int.TryParse(yearText, out var chosen) && chosen >= 1950 && chosen <= clock.GetUtcNow().Year + 1 ? chosen : clock.GetUtcNow().Year;
        using var scope = scopes.CreateScope();
        var factory = scope.ServiceProvider.GetService<IDbContextFactory<RaceLensDbContext>>();
        if (factory is not null)
        {
            await using var db = await factory.CreateDbContextAsync(token);
            var prefix = $"/api/seasons/{year}/";
            var keys = new List<string>();
            if (Regex.IsMatch(query, "календар|гонк|гран|race|prix|calendar|коли|наступ|next|nächst|prossim|prochain|próxim")) keys.Add(prefix + "calendar");
            if (Regex.IsMatch(query, "залік|очк|лідер|поточ|сезон|stand|point|leader|season|saison|classific|temporada|класиф")) {
                keys.Add(prefix + "standings/drivers"); keys.Add(prefix + "standings/constructors");
            }
            if (Regex.IsMatch(query, "прогноз|predict|forecast|prognos|pronostic|previs")) keys.Clear();
            if (Regex.IsMatch(query, "новин|нового|news|latest|neuigkeit|notizie|noticia|actualit")) keys.Clear();
            // Page context is a validated public route, never a model-generated request URL.
            var route = request.Page?.Split('?')[0].Split('/') ?? [];
            if (route.Length == 3 && route[1] == "drivers" && route[2] != "career") keys.Insert(0, prefix + "drivers/" + route[2]);
            if (route.Length == 4 && route[1] == "calendar" && int.TryParse(route[3], out var round) && round is > 0 and < 101)
                keys.Insert(0, prefix + $"races/{round}/results/race");
            var snapshots = await db.Snapshots.AsNoTracking().Where(s => keys.Contains(s.ResourceKey))
                .OrderByDescending(s => s.FetchedAt).Take(40).ToListAsync(token);
            foreach (var key in keys)
            {
                var snapshot = snapshots.FirstOrDefault(s => s.ResourceKey == key);
                if (snapshot is null) continue;
                using var payload = JsonDocument.Parse(snapshot.Payload);
                var root = payload.RootElement;
                var link = key.Contains("/drivers/") ? $"/drivers/{route.ElementAtOrDefault(2)}?season={year}"
                    : key.Contains("/results/") ? $"/calendar/{year}/{route.ElementAtOrDefault(3)}?session=race"
                    : key.EndsWith("calendar", StringComparison.Ordinal) ? $"/calendar?season={year}"
                    : $"/standings?season={year}&table={(key.EndsWith("constructors", StringComparison.Ordinal) ? "constructors" : "drivers")}";
                var listKey = root.TryGetProperty("entries", out _) ? "entries" : root.TryGetProperty("races", out _) ? "races" : "";
                var text = root.GetRawText();
                if (listKey.Length > 0)
                {
                    var rows = root.GetProperty(listKey).EnumerateArray().ToArray();
                    // Prefer matching entities but preserve complete small standings; retain metadata and explicit subset marker.
                    var upcoming = key.EndsWith("calendar", StringComparison.Ordinal) && Regex.IsMatch(query, "наступ|next|nächst|prossim|prochain|próxim");
                    if (upcoming) rows = rows.Where(r => r.TryGetProperty("date", out var date) && DateOnly.TryParse(date.GetString(), out var day)
                        && day >= DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime)).ToArray();
                    var resultsQuery = key.Contains("/results/", StringComparison.Ordinal);
                    if (resultsQuery && Regex.IsMatch(query, "зійш|схід|retir|dnf|ausfall|abandon|ritir"))
                        rows = rows.Where(r => r.TryGetProperty("status", out var status) && status.GetString() is { } s
                            && s != "Finished" && !s.StartsWith('+')).ToArray();
                    var ranked = rows.OrderByDescending(r => upcoming ? 0 : Score(r.GetRawText())).Take(listKey == "entries" ? (resultsQuery ? 8 : 24) : 3).ToArray();
                    var fields = resultsQuery ? new[] { "driverName", "position", "points", "status", "grid", "laps", "time", "team" }
                        : new[] { "name", "position", "points", "wins" };
                    object compactRows = listKey == "entries" ? ranked.Select(r => r.EnumerateObject().Where(p => fields.Contains(p.Name))
                        .ToDictionary(p => p.Name, p => p.Value)).ToArray() : ranked;
                    text = JsonSerializer.Serialize(new { season = year, snapshot.FetchedAt, source = "Jolpica", partial = ranked.Length < rows.Length, rows = compactRows }, Json);
                }
                if (text.Length <= 9500) Add($"Jolpica · {key} · {snapshot.FetchedAt:yyyy-MM-dd HH:mm} UTC (saved snapshot)", link, text);
            }
            if (Regex.IsMatch(query, "прогноз|predict|forecast|prognos|pronostic|previs"))
            {
                var runs = await db.ForecastRuns.AsNoTracking().Where(r => r.Season == year).OrderByDescending(r => r.RecordedAt).Take(4).ToListAsync(token);
                foreach (var run in runs.DistinctBy(r => r.Engine).Take(2))
                {
                    using var prediction = JsonDocument.Parse(run.Output);
                    var root = prediction.RootElement;
                    JsonElement[] Rows(string key, int take) => root.TryGetProperty(key, out var list) && list.ValueKind == JsonValueKind.Array
                        ? list.EnumerateArray().OrderByDescending(r => Score(r.GetRawText())).Take(take).ToArray() : [];
                    object Summary(JsonElement row) => new {
                        name = row.GetProperty("name"),
                        expectedPoints = Math.Round(row.GetProperty("expectedPoints").GetDouble(), MidpointRounding.AwayFromZero),
                        titleChancePercent = row.TryGetProperty("titleChance", out var title) ? Math.Round(title.GetDouble() * 100, 1) : (double?)null,
                        winChancePercent = row.TryGetProperty("winChance", out var win) ? Math.Round(win.GetDouble() * 100, 1) : (double?)null,
                    };
                    var compact = JsonSerializer.Serialize(new { run.Season, run.ThroughRound, run.GeneratedAt, run.SourceUpdatedAt, run.Version,
                        warning = "Partial saved forecast. Probabilities are uncalibrated simulation frequencies, not guarantees. Expected points may be fractional; display rounded.",
                        drivers = Rows("drivers", 4).Select(Summary), teams = Rows("teams", 3).Select(Summary), races = Rows("races", 1).Select(r => new {
                            name = r.GetProperty("name"), round = r.GetProperty("round"), date = r.GetProperty("date"),
                            drivers = r.GetProperty("drivers").EnumerateArray().OrderByDescending(x => Score(x.GetRawText())).Take(3).Select(Summary).ToArray()
                        }) }, Json);
                    if (compact.Length <= 9000) Add($"Forecast · {run.Engine} · {run.GeneratedAt:yyyy-MM-dd HH:mm} UTC · uncertain model estimate", "/predictions", compact);
                }
            }
        }
        if (Regex.IsMatch(query, "новин|нового|news|latest|neuigkeit|notizie|noticia|actualit|ferrari|феррар"))
        {
            var feed = await news.ReadAsync(token);
            if (feed is not null)
                foreach (var article in feed.Articles.OrderByDescending(a => Score(a.Title)).ThenByDescending(a => a.PublishedAt).Take(3))
                    Add($"BBC Sport · {article.PublishedAt:yyyy-MM-dd HH:mm} UTC · headline only", article.Url, article.Title);
        }
        var latest = Normalize(request.Messages[^1].Content);
        var wantsNews = Regex.IsMatch(latest, "новин|нового|news|latest|neuigkeit|notizie|noticia|actualit");
        var wantsForecast = Regex.IsMatch(latest, "прогноз|predict|forecast|prognos|pronostic|previs");
        return result.OrderByDescending(s => wantsNews && s.Title.StartsWith("BBC Sport", StringComparison.Ordinal)
                || wantsForecast && s.Title.StartsWith("Forecast ·", StringComparison.Ordinal) ? 100 : Score(s.Title + " " + s.Text))
            .Select((s, i) => s with { Id = i + 1 }).ToArray();
    }
}
