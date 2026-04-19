using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.SpotifyLocalSync.Api;

// ═══════════════════════════════════════════════════════════════════
//  DTOs  (minimal surface – only fields we actually use)
// ═══════════════════════════════════════════════════════════════════

public record SpotifyTrack(
    [property: JsonPropertyName("id")]    string Id,
    [property: JsonPropertyName("name")]  string Name,
    [property: JsonPropertyName("artists")] List<SpotifyArtist> Artists,
    [property: JsonPropertyName("duration_ms")] int DurationMs
)
{
    /// <summary>All artist names joined by " and ".</summary>
    public string ArtistsString => string.Join(" & ", Artists.ConvertAll(a => a.Name));
}

public record SpotifyArtist(
    [property: JsonPropertyName("name")] string Name
);

public record SpotifyPlaylistItem(
    [property: JsonPropertyName("track")] SpotifyTrack? Track
);

// ═══════════════════════════════════════════════════════════════════
//  Response wrappers
// ═══════════════════════════════════════════════════════════════════

file record TokenResponse(
    [property: JsonPropertyName("access_token")] string AccessToken,
    [property: JsonPropertyName("expires_in")]   int ExpiresIn
);

file record PlaylistTracksPage(
    [property: JsonPropertyName("items")]  List<SpotifyPlaylistItem> Items,
    [property: JsonPropertyName("next")]   string? Next,
    [property: JsonPropertyName("total")]  int Total
);

file record PlaylistMeta(
    [property: JsonPropertyName("name")]   string Name,
    [property: JsonPropertyName("tracks")] PlaylistTracksPage Tracks
);

// ═══════════════════════════════════════════════════════════════════
//  Client
// ═══════════════════════════════════════════════════════════════════

/// <summary>
/// Thin wrapper around the Spotify Web API using Client Credentials flow.
/// No user login required – works for any public playlist.
/// </summary>
public sealed class SpotifyApiClient
{
    private static readonly JsonSerializerOptions _json = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger            _logger;

    private string _accessToken  = string.Empty;
    private DateTime _tokenExpiry = DateTime.MinValue;

    public SpotifyApiClient(IHttpClientFactory httpFactory, ILogger logger)
    {
        _httpFactory = httpFactory;
        _logger      = logger;
    }

    // ── Auth ────────────────────────────────────────────────────────

    private async Task EnsureTokenAsync(string clientId, string clientSecret, CancellationToken ct)
    {
        if (DateTime.UtcNow < _tokenExpiry) return;

        _logger.LogDebug("[SpotifyLocalSync] Requesting new Spotify access token…");

        var http    = _httpFactory.CreateClient("SpotifyLocalSync");
        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{clientId}:{clientSecret}"));

        var req = new HttpRequestMessage(HttpMethod.Post, "https://accounts.spotify.com/api/token")
        {
            Headers = { Authorization = new AuthenticationHeaderValue("Basic", encoded) },
            Content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string,string>("grant_type", "client_credentials")
            })
        };

        var resp = await http.SendAsync(req, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();

        var token = JsonSerializer.Deserialize<TokenResponse>(
            await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false), _json)!;

        _accessToken  = token.AccessToken;
        _tokenExpiry  = DateTime.UtcNow.AddSeconds(token.ExpiresIn - 60); // 60s safety margin
    }

    // ── Public API ──────────────────────────────────────────────────

    /// <summary>
    /// Extracts the playlist ID from a URL or raw ID string.
    /// Accepts:
    ///   https://open.spotify.com/playlist/37i9dQZF1DXcBWIGoYBM5M
    ///   spotify:playlist:37i9dQZF1DXcBWIGoYBM5M
    ///   37i9dQZF1DXcBWIGoYBM5M
    /// </summary>
    public static string? ParsePlaylistId(string input)
    {
        input = input.Trim();

        // URL form
        var urlMatch = Regex.Match(input, @"spotify\.com/playlist/([A-Za-z0-9]+)");
        if (urlMatch.Success) return urlMatch.Groups[1].Value;

        // URI form
        var uriMatch = Regex.Match(input, @"spotify:playlist:([A-Za-z0-9]+)");
        if (uriMatch.Success) return uriMatch.Groups[1].Value;

        // Raw ID (22 alphanumeric chars typical but not enforced)
        if (Regex.IsMatch(input, @"^[A-Za-z0-9]{10,}$")) return input;

        return null;
    }

    /// <summary>
    /// Fetches the display name of a Spotify playlist.
    /// </summary>
    public async Task<string> GetPlaylistNameAsync(
        string clientId, string clientSecret, string playlistId, CancellationToken ct)
    {
        await EnsureTokenAsync(clientId, clientSecret, ct).ConfigureAwait(false);

        var http = _httpFactory.CreateClient("SpotifyLocalSync");
        http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", _accessToken);

        var url  = $"https://api.spotify.com/v1/playlists/{playlistId}?fields=name";
        var json = await http.GetStringAsync(url, ct).ConfigureAwait(false);
        var meta = JsonSerializer.Deserialize<PlaylistMeta>(json, _json)!;
        return meta.Name;
    }

    /// <summary>
    /// Fetches ALL tracks from a Spotify playlist (handles pagination automatically).
    /// </summary>
    public async Task<List<SpotifyTrack>> GetPlaylistTracksAsync(
        string clientId, string clientSecret, string playlistId, CancellationToken ct)
    {
        await EnsureTokenAsync(clientId, clientSecret, ct).ConfigureAwait(false);

        var http = _httpFactory.CreateClient("SpotifyLocalSync");
        http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", _accessToken);

        var tracks = new List<SpotifyTrack>();
        string? nextUrl = $"https://api.spotify.com/v1/playlists/{playlistId}/tracks" +
                          "?fields=items(track(id,name,artists,duration_ms)),next,total&limit=50";

        while (nextUrl != null)
        {
            ct.ThrowIfCancellationRequested();

            var json = await http.GetStringAsync(nextUrl, ct).ConfigureAwait(false);
            var page = JsonSerializer.Deserialize<PlaylistTracksPage>(json, _json)!;

            foreach (var item in page.Items)
            {
                if (item.Track is not null)
                    tracks.Add(item.Track);
            }

            nextUrl = page.Next;
            _logger.LogDebug("[SpotifyLocalSync] Fetched {Count}/{Total} tracks…",
                tracks.Count, page.Total);
        }

        return tracks;
    }
}
