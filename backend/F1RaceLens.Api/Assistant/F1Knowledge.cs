namespace F1RaceLens.Api.Assistant;

public sealed record F1Fact(string Terms, string Title, string Text, string Url, string? Ukrainian = null);

public static class F1Knowledge
{
    public static ChatReply? Reference(ChatRequest request) => Reference(request,
        Facts.Where(f => f.Title == "F1 · Undercut / overcut").Select((f, i) => new ChatSource(i + 1, f.Title, f.Url, f.Text)).ToArray());

    public static ChatReply? Reference(ChatRequest request, IReadOnlyList<ChatSource> sources)
    {
        var question = request.Messages[^1].Content;
        var terms = string.Join(' ', request.Messages.Where(m => m.Role == "user").TakeLast(2).Select(m => m.Content));
        if (question.Length > 220 || !System.Text.RegularExpressions.Regex.IsMatch(terms, "андеркат|оверкат|undercut|overcut", System.Text.RegularExpressions.RegexOptions.IgnoreCase)
            || !System.Text.RegularExpressions.Regex.IsMatch(question, "андеркат|оверкат|undercut|overcut|різниц|differ|unterschied|difer", System.Text.RegularExpressions.RegexOptions.IgnoreCase)) return null;
        var source = sources.FirstOrDefault(s => s.Title == "F1 · Undercut / overcut");
        if (source is null) return null;
        var text = request.Language switch {
            "uk" => "Андеркат — раніший піт-стоп за суперника, щоб виграти час на свіжих шинах. Оверкат — довше залишатися на трасі й зупинитися пізніше; обидві стратегії спрямовані на виграш позиції, без гарантії успіху.",
            "de" => "Beim Undercut stoppt man früher als der Gegner und nutzt frische Reifen. Beim Overcut bleibt man länger draußen; beide Strategien sollen Positionen gewinnen, garantieren es aber nicht.",
            "it" => "L’undercut anticipa la sosta del rivale per sfruttare gomme fresche. L’overcut prolunga lo stint: entrambe le strategie cercano di guadagnare posizione, senza garanzie.",
            "es" => "El undercut adelanta la parada respecto al rival para aprovechar neumáticos frescos. El overcut retrasa la parada: ambas estrategias buscan ganar posición, sin garantizarlo.",
            "fr" => "L’undercut consiste à s’arrêter avant le rival pour profiter de pneus frais. L’overcut prolonge le relais : les deux stratégies visent à gagner une position, sans garantie.",
            _ => "An undercut pits before a rival to gain time on fresh tyres. An overcut stays out longer; both aim to gain a position after the stops, without guaranteeing success."
        };
        return new(text, [source with { Text = "" }], "F1 reference", "reference");
    }

    // Short editorial summaries, not copies of source articles. Review when regulations change.
    public static readonly F1Fact[] Facts = [
        new("андеркат undercut оверкат overcut стратег strategy strategie strategia estrategia stratégie", "F1 · Undercut / overcut",
            "An undercut is pitting before a rival to gain time on fresh tyres and emerge ahead after the rival stops. An overcut keeps a driver out longer to gain time before stopping. Traffic, tyre warm-up and degradation affect whether either works; neither guarantees an overtake.",
            "https://www.formula1.com/en/latest/article/jolyon-palmers-analysis-singapore-and-the-art-of-undercutting.1NgVyVsZnHTDEA9wi0s5lW",
            "Андеркат: пілот заїжджає на піт-стоп раніше за суперника й на свіжих шинах намагається виграти час, щоб опинитися попереду після піт-стопу суперника. Оверкат: пілот залишається на трасі довше. Результат залежить від трафіку, прогріву й зношування шин; перевага не гарантована."),
        new("1950 істор histor geschichte storia histoire historia почат чемпіон", "F1 · History",
            "The first World Championship race was at Silverstone on 13 May 1950. Nino Farina won the first drivers championship for Alfa Romeo.",
            "https://www.formula1.com/en/latest/article/why-is-it-called-formula-1-and-12-other-questions-about-the-championships.1GHeel6u4jga6hMpX2eFs1"),
        new("1958 1957 конструктор constructor конструкторів істор histor geschichte storia histoire historia", "F1 · Constructors championship",
            "The constructors championship was introduced in 1958; Vanwall was its first winner. There is no constructors championship ranking for 1950–1957.",
            "https://www.formula1.com/en/latest/article/the-beginners-guide-to-the-f1-constructors-championship.66nTfWSqrUYv3bnbosPkHV"),
        new("шини шин tyre tire pneumatic neumát pneu reifen деградац stint стінт", "F1 · Tyres and stints",
            "A stint is the stretch of a race between tyre changes (or start/finish). Tyre degradation means loss of performance with use. Age alone does not measure remaining grip: compound, temperature, loading and track conditions matter. This app has no live tyre-wear measurements.",
            "https://www.formula1.com/en/page/f1-glossary"),
        new("прогноз predict forecast prognos previs pronostic модель lightgbm скрипт", "F1 Race Lens · Forecast methodology",
            "This app has two separate estimates: a statistical simulation script and a saved LightGBM v4 tabular model. Neither is a language model. Winning probabilities are frequencies across simulations, not guarantees; stable superiority to a simple baseline has not been established. Script scenarios can vary weather, hypothetical team development and replacements; saved LightGBM v4 does not include them. There is no live race probability model.",
            "/predictions"),
    ];
}
