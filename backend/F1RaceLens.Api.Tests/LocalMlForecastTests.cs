using System.Security.Cryptography;
using System.Text.Json;
using F1RaceLens.Api.Predictions.Integration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace F1RaceLens.Api.Tests;

public sealed class LocalMlForecastTests
{
    [Fact]
    public async Task Saved_ml_maps_units_preserves_provenance_and_refuses_changed_artifacts_and_past_rounds()
    {
        var root = Path.Combine(Path.GetTempPath(), "f1-ml-test-" + Guid.NewGuid().ToString("N"));
        var now = new DateTimeOffset(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);
        Directory.CreateDirectory(Path.Combine(root, "models/lightgbm-v4")); Directory.CreateDirectory(Path.Combine(root, "reports"));
        try
        {
            var hashes = new Dictionary<string, string>();
            foreach (var name in new[] { "pace.txt", "pace.schema.json", "nonclassification.txt", "nonclassification.schema.json" })
            {
                var bytes = System.Text.Encoding.UTF8.GetBytes(name);
                await File.WriteAllBytesAsync(Path.Combine(root, "models/lightgbm-v4", name), bytes);
                hashes[name] = Convert.ToHexStringLower(SHA256.HashData(bytes));
            }
            var metadata = new { engine = "lightgbm" };
            await File.WriteAllTextAsync(Path.Combine(root, "models/lightgbm-v4/metadata.json"), JsonSerializer.Serialize(metadata));
            object Row(string id) => new { id, name = id, position = id == "a" ? 1 : 2, points = 10, active = true,
                expected_points = 20.5, points_low = 15, points_high = 30, average_position = 1.5, position_low = 1, position_high = 2, title_chance = 50, top3_chance = 100 };
            var report = new {
                model = "lightgbm-race-v4", forecast_version = "lightgbm-season-v1", season = 2026, through_round = 17,
                generated = now.AddMinutes(-1), source_updated = now.AddHours(-1), status = "ready", seed = 123,
                remaining_races = 1, remaining_sprints = 0, simulations = 4000, artifact_sha256 = hashes,
                input_snapshot = new { model_metadata = metadata, history = new[] { new { round = 17 } },
                    standings_calendar = new { season = 2026, through = 17, calendar = new[] {
                        new { round = "18", raceName = "Next", date = "2026-10-25", time = "19:00:00Z", Circuit = new { circuitId = "test" } }
                    } } }, drivers = new[] { Row("a"), Row("b") }, teams = new[] { Row("a"), Row("b") },
                races = new[] { new { round = 18, name = "Next", date = "2026-10-25", drivers = new[] {
                    new { driver = "a", name = "A", expected_points = 20, average_position = 1.5, win_chance = 75, podium_chance = 100, risk = .1 },
                    new { driver = "b", name = "B", expected_points = 15, average_position = 1.7, win_chance = 25, podium_chance = 100, risk = .2 }
                } } }
            };
            await File.WriteAllTextAsync(Path.Combine(root, "reports/season-forecast-lightgbm-v4.json"), JsonSerializer.Serialize(report));
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Predictions:MlRoot"] = root }).Build();
            var clock = new TestClock(now);
            var reader = new LocalMlForecast(new TestEnvironment(), config, clock);
            var response = await reader.ReadAsync(default);
            Assert.Equal("ready", response.Status); Assert.Equal(10, response.Races![0].Drivers[0].NonClassificationChance);
            Assert.Equal(20.5, response.Drivers[0].ExpectedPoints); Assert.Contains("artifact_sha256", response.InputsJson);
            clock.Now = now.AddDays(7);
            var expired = await reader.ReadAsync(default);
            Assert.Equal("synchronizing", expired.Status); Assert.Empty(expired.Races!);
            clock.Now = now;
            await File.WriteAllTextAsync(Path.Combine(root, "models/lightgbm-v4/pace.txt"), "changed");
            await Assert.ThrowsAsync<InvalidDataException>(() => reader.ReadAsync(default));
        }
        finally {
            if (!Path.GetFullPath(root).StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Unexpected test directory.");
            Directory.Delete(root, recursive: true);
        }
    }

    private sealed class TestClock(DateTimeOffset now) : TimeProvider { public DateTimeOffset Now { get; set; } = now; public override DateTimeOffset GetUtcNow() => Now; }
    private sealed class TestEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Test";
        public string ApplicationName { get; set; } = "Test";
        public string ContentRootPath { get; set; } = "";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
