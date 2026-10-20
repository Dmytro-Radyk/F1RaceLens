using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
namespace F1RaceLens.Api.Storage;
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<RaceLensDbContext>
{
    public RaceLensDbContext CreateDbContext(string[] args)
    {
        var connection = Environment.GetEnvironmentVariable("ConnectionStrings__RaceLens")
            ?? "Host=127.0.0.1;Port=55432;Database=f1racelens;Username=f1racelens";
        return new RaceLensDbContext(new DbContextOptionsBuilder<RaceLensDbContext>().UseNpgsql(connection).Options);
    }
}
