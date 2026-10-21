using System.Text.Json;

namespace F1RaceLens.Api.Catalog;

public sealed class CareerCatalog(IWebHostEnvironment environment)
{
    private readonly Lazy<JsonElement> snapshot = new(() => {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(environment.ContentRootPath, "Catalog", "Data", "catalog.json")));
        return document.RootElement.Clone();
    });

    public JsonElement Read() => snapshot.Value;
}
