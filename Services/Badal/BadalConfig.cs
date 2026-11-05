using System.Text.Json;

namespace RowadUmrahSystem.Web.Services.Badal;

/// <summary>
/// Back-office settings of «عمرة البدل». The PRICE lives here, never on the phone: the app only
/// displays what this returns, and hides the price pill entirely while it has nothing to show.
/// </summary>
public sealed class BadalConfig
{
    /// <summary>Price of one omra, all included (certificate and videos).</summary>
    public decimal Price { get; set; }

    public string CurrencyIso { get; set; } = "KWD";

    /// <summary>Days allowed after the pilgrim is assigned; past that, full refund.</summary>
    public int SlaDays { get; set; } = BadalRules.SlaDays;

    /// <summary>Shown on the presentation screen, e.g. "3 – 7 أيام".</summary>
    public string DelayLabel { get; set; } = "3 – 7 أيام";

    /// <summary>Off until the agency has settled its licence; the service then stays hidden.</summary>
    public bool Enabled { get; set; }

    /// <summary>Licence reference of the provider, for the Saudi regulation.</summary>
    public string? LicenceReference { get; set; }
}

/// <summary>
/// Same pattern as BookingConfigStore: badal-config.json holds the defaults (in git) and the
/// back-office writes badal-config.local.json (not in git), which wins. No database row, so no
/// migration is needed to change a price.
/// </summary>
public sealed class BadalConfigStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly string _defaultPath;
    private readonly string _localPath;

    public BadalConfigStore(IWebHostEnvironment environment)
    {
        _defaultPath = Path.Combine(environment.ContentRootPath, "App_Data", "badal-config.json");
        _localPath = Path.Combine(environment.ContentRootPath, "App_Data", "badal-config.local.json");
    }

    public async Task<BadalConfig> ReadAsync()
    {
        var config = await ReadFileAsync(_localPath) ?? await ReadFileAsync(_defaultPath);
        return config ?? new BadalConfig();
    }

    public async Task WriteAsync(BadalConfig config)
    {
        var temporary = _localPath + ".tmp";
        var options = new JsonSerializerOptions(JsonOptions)
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };
        await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(config, options));
        if (File.Exists(_localPath))
        {
            File.Copy(_localPath, Path.Combine(Path.GetDirectoryName(_localPath)!, "badal-config.backup.json"), overwrite: true);
        }
        File.Move(temporary, _localPath, overwrite: true);
    }

    private static async Task<BadalConfig?> ReadFileAsync(string path)
    {
        if (!File.Exists(path)) return null;
        try
        {
            return JsonSerializer.Deserialize<BadalConfig>(await File.ReadAllTextAsync(path), JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
