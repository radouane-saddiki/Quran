using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace Quran.Web.Audio;

public sealed class Reciter
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string NameAr { get; set; } = "";
    /// <summary>Identifiant de la récitation (« read ») dans l'API de minutage MP3Quran.</summary>
    public int ReadId { get; set; }
    /// <summary>URL du fichier audio d'une sourate ; {surah} est remplacé par le numéro sur 3 chiffres (001…114).</summary>
    public string AudioUrl { get; set; } = "";

    public string AudioFor(int surah) => AudioUrl.Replace("{surah}", surah.ToString("000"));
}

public sealed class AudioOptions
{
    /// <summary>{surah} et {read} sont remplacés.</summary>
    public string TimingApi { get; set; } = "https://mp3quran.net/api/v3/ayat_timing?surah={surah}&read={read}";
    /// <summary>Dossier (relatif au projet) où les minutages téléchargés sont conservés.</summary>
    public string CacheDirectory { get; set; } = "App_Data/timings";
    public List<Reciter> Reciters { get; set; } = [];
}

/// <summary>Début et fin d'un verset dans le fichier audio de la sourate, en millisecondes.</summary>
public sealed record AyahTiming(int Ayah, int Start, int End);

/// <summary>
/// Fournit les minutages verset par verset. Ils sont téléchargés une seule fois depuis MP3Quran
/// puis lus depuis le disque (fonctionnement hors ligne ensuite).
/// </summary>
public sealed class TimingService(HttpClient http, IOptions<AudioOptions> options, IWebHostEnvironment env, ILogger<TimingService> log)
{
    private static readonly ConcurrentDictionary<(string, int), IReadOnlyList<AyahTiming>> Memory = new();
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public AudioOptions Options => options.Value;

    public Reciter? Find(string id) =>
        Options.Reciters.FirstOrDefault(r => string.Equals(r.Id, id, StringComparison.OrdinalIgnoreCase));

    private string CachePath(Reciter r, int surah) =>
        Path.Combine(env.ContentRootPath, Options.CacheDirectory, r.Id, $"{surah:000}.json");

    public async Task<IReadOnlyList<AyahTiming>> GetAsync(Reciter reciter, int surah, CancellationToken ct = default)
    {
        if (surah is < 1 or > 114) throw new ArgumentOutOfRangeException(nameof(surah));
        if (Memory.TryGetValue((reciter.Id, surah), out var cached)) return cached;

        var path = CachePath(reciter, surah);
        IReadOnlyList<AyahTiming> timings;
        if (File.Exists(path))
        {
            await using var file = File.OpenRead(path);
            timings = await JsonSerializer.DeserializeAsync<List<AyahTiming>>(file, Json, ct) ?? [];
        }
        else
        {
            var url = Options.TimingApi
                .Replace("{surah}", surah.ToString())
                .Replace("{read}", reciter.ReadId.ToString());
            var raw = await http.GetFromJsonAsync<List<Mp3QuranTiming>>(url, ct) ?? [];
            var ordered = raw.Where(t => t.Ayah > 0 && t.StartTime is not null).OrderBy(t => t.Ayah).ToList();
            timings = ordered
                .Select((t, i) => new AyahTiming(t.Ayah, t.StartTime!.Value,
                    t.EndTime ?? (i + 1 < ordered.Count ? ordered[i + 1].StartTime!.Value : t.StartTime!.Value)))
                .ToList();

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(timings, Json), ct);
            log.LogInformation("Minutages téléchargés : {Reciter} sourate {Surah} ({Count} versets)", reciter.Id, surah, timings.Count);
        }

        Memory[(reciter.Id, surah)] = timings;
        return timings;
    }

    private sealed class Mp3QuranTiming
    {
        [JsonPropertyName("ayah")] public int Ayah { get; set; }
        [JsonPropertyName("start_time")] public int? StartTime { get; set; }
        [JsonPropertyName("end_time")] public int? EndTime { get; set; }
    }
}
