using System.Text.Json;

namespace F1RaceLens.Api.Tests;

public sealed class CatalogTests
{
    [Fact]
    public void PublishedSnapshotContainsCompleteUniqueCareerRecordsAndAuditableTitles()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "backend", "F1RaceLens.Api", "Catalog", "Data", "catalog.json"))) directory = directory.Parent;
        Assert.NotNull(directory);
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory.FullName, "backend", "F1RaceLens.Api", "Catalog", "Data", "catalog.json")));
        var root = document.RootElement;
        Assert.Equal("F1DB", root.GetProperty("source").GetString());
        Assert.Equal("v2026.17.0", root.GetProperty("version").GetString());
        foreach (var name in new[] { "drivers", "constructors" })
        {
            var rows = root.GetProperty(name).EnumerateArray().ToArray();
            Assert.True(rows.Length >= (name == "drivers" ? 800 : 150));
            Assert.Equal(rows.Length, rows.Select(row => row.GetProperty("id").GetString()).Distinct().Count());
            foreach (var row in rows)
            {
                Assert.NotEmpty(row.GetProperty("seasons").EnumerateArray());
                Assert.Equal(row.GetProperty("titles").GetInt32(), row.GetProperty("titleYears").GetArrayLength());
                Assert.True(row.GetProperty("starts").GetInt32() <= row.GetProperty("entries").GetInt32());
                Assert.True(row.GetProperty("wins").GetInt32() <= row.GetProperty("starts").GetInt32());
            }
        }
        var michael = root.GetProperty("drivers").EnumerateArray().Single(row => row.GetProperty("id").GetString() == "michael-schumacher");
        Assert.Equal(7, michael.GetProperty("titles").GetInt32());
        Assert.Equal(91, michael.GetProperty("wins").GetInt32());
        var ferrari = root.GetProperty("constructors").EnumerateArray().Single(row => row.GetProperty("id").GetString() == "ferrari");
        Assert.Equal(16, ferrari.GetProperty("titles").GetInt32());
    }
}
