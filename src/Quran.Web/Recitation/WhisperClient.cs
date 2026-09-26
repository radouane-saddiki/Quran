using System.Net.Http.Headers;
using Microsoft.Extensions.Options;

namespace Quran.Web.Recitation;

public sealed class RecitationOptions
{
    /// <summary>Adresse du serveur Whisper local (tools/whisper-server).</summary>
    public string WhisperUrl { get; set; } = "http://127.0.0.1:5095";
}

/// <summary>Relais vers le serveur Whisper local : le navigateur n'a ainsi affaire qu'au site.</summary>
public sealed class WhisperClient(HttpClient http, IOptions<RecitationOptions> options)
{
    public const long MaxAudioBytes = 8 * 1024 * 1024; // ~4 min de WAV 16 kHz mono

    private string Base => options.Value.WhisperUrl.TrimEnd('/');

    public async Task<bool> IsAvailableAsync(CancellationToken ct)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(2));
            using var resp = await http.GetAsync($"{Base}/health", cts.Token);
            return resp.IsSuccessStatusCode;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return false;
        }
    }

    public async Task<(int Status, string Json)> TranscribeAsync(Stream wav, CancellationToken ct)
    {
        // Le serveur Python a besoin d'un Content-Length (pas d'envoi « chunked ») : on met l'audio en mémoire.
        using var buffer = new MemoryStream();
        await wav.CopyToAsync(buffer, ct);
        using var content = new ByteArrayContent(buffer.ToArray());
        content.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
        using var resp = await http.PostAsync($"{Base}/transcribe", content, ct);
        return ((int)resp.StatusCode, await resp.Content.ReadAsStringAsync(ct));
    }
}
