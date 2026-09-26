using System.Diagnostics;
using Microsoft.Extensions.Options;

namespace Quran.Web.Audio;

/// <summary>
/// Copie locale des fichiers audio : wwwroot/audio/{récitateur}/{001..114}.mp3.
/// Quand un fichier local existe, le site le lit à la place du serveur distant.
/// </summary>
public sealed class LocalAudio(IWebHostEnvironment env)
{
    public string Folder(Reciter r) => Path.Combine(env.WebRootPath, "audio", r.Id);

    public string FilePath(Reciter r, int surah) => Path.Combine(Folder(r), $"{surah:000}.mp3");

    public bool Exists(Reciter r, int surah) => File.Exists(FilePath(r, surah));

    /// <summary>URL à donner au navigateur : fichier local s'il existe, sinon serveur du récitateur.</summary>
    public string UrlFor(Reciter r, int surah) =>
        Exists(r, surah) ? $"/audio/{r.Id}/{surah:000}.mp3" : r.AudioFor(surah);
}

/// <summary>Téléchargement de toutes les sourates (reprise possible : les fichiers déjà présents sont ignorés).</summary>
public sealed class AudioDownloader(IHttpClientFactory factory, IOptions<AudioOptions> options, LocalAudio local)
{
    public async Task<int> RunAsync(IReadOnlyCollection<string> only, CancellationToken ct = default)
    {
        var http = factory.CreateClient("audio-download");
        var reciters = options.Value.Reciters
            .Where(r => only.Count == 0 || only.Contains(r.Id, StringComparer.OrdinalIgnoreCase))
            .ToList();
        if (reciters.Count == 0)
        {
            Console.WriteLine($"Aucun récitateur ne correspond. Disponibles : {string.Join(", ", options.Value.Reciters.Select(r => r.Id))}");
            return 1;
        }

        var failures = 0;
        foreach (var r in reciters)
        {
            if (!r.AudioUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine($"{r.Name} : AudioUrl n'est pas une adresse Internet ({r.AudioUrl}), ignoré.");
                continue;
            }

            Directory.CreateDirectory(local.Folder(r));
            Console.WriteLine($"\n{r.Name} → {local.Folder(r)}");
            long total = 0;
            var sw = Stopwatch.StartNew();

            for (var s = 1; s <= 114; s++)
            {
                var dest = local.FilePath(r, s);
                if (File.Exists(dest))
                {
                    total += new FileInfo(dest).Length;
                    continue;
                }

                var ok = false;
                for (var attempt = 1; attempt <= 3 && !ok; attempt++)
                {
                    var part = dest + ".part";
                    try
                    {
                        using var resp = await http.GetAsync(r.AudioFor(s), HttpCompletionOption.ResponseHeadersRead, ct);
                        resp.EnsureSuccessStatusCode();
                        await using (var src = await resp.Content.ReadAsStreamAsync(ct))
                        await using (var fs = File.Create(part))
                        {
                            await src.CopyToAsync(fs, ct);
                        }
                        File.Move(part, dest, overwrite: true);
                        var size = new FileInfo(dest).Length;
                        total += size;
                        ok = true;
                        Console.WriteLine($"  {s:000}/114  {size / 1048576.0,7:0.0} Mo   (total {total / 1048576.0:0} Mo, {sw.Elapsed:hh\\:mm\\:ss})");
                    }
                    catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException && !ct.IsCancellationRequested)
                    {
                        if (File.Exists(part)) File.Delete(part);
                        Console.WriteLine($"  {s:000}/114  échec (essai {attempt}/3) : {ex.Message}");
                        if (attempt < 3) await Task.Delay(TimeSpan.FromSeconds(3 * attempt), ct);
                    }
                }
                if (!ok) failures++;
            }

            var present = Enumerable.Range(1, 114).Count(s => local.Exists(r, s));
            Console.WriteLine($"{r.Name} : {present}/114 sourates sur le disque ({total / 1048576.0:0} Mo).");
        }

        if (failures > 0)
            Console.WriteLine($"\n{failures} fichier(s) non téléchargé(s). Relancez la même commande pour reprendre.");
        return failures == 0 ? 0 : 2;
    }
}
