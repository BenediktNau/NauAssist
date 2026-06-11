using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace NauAssist.Backend.Tests.Helpers;

public class TestAppFactory : WebApplicationFactory<Program>
{
    private readonly string _dbPath;

    public TestAppFactory()
    {
        _dbPath = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"nauassist-integration-{Guid.NewGuid():N}.db");
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration(cfg =>
        {
            cfg.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Persistence:DatabasePath"] = _dbPath,
            });
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        try
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(_dbPath)) File.Delete(_dbPath);
        }
        catch { /* OS-Aufräumung übernimmt im Notfall */ }
    }
}
