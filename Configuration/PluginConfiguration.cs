using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.SpotifyLocalSync.Configuration;

/// <summary>
/// Plugin configuration – stored as XML by Jellyfin's plugin infrastructure.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    // ──────────────────────────────────────────────
    //  Spotify
    // ──────────────────────────────────────────────

    /// <summary>
    /// Spotify Client ID (from developer.spotify.com).
    /// Required for Client Credentials OAuth flow to access the public API.
    /// </summary>
    public string SpotifyClientId { get; set; } = string.Empty;

    /// <summary>
    /// Spotify Client Secret.
    /// </summary>
    public string SpotifyClientSecret { get; set; } = string.Empty;

    /// <summary>
    /// One or more Spotify playlist URLs or IDs, separated by newlines.
    /// Supports formats:
    ///   https://open.spotify.com/playlist/&lt;id&gt;
    ///   spotify:playlist:&lt;id&gt;
    ///   &lt;id&gt; (raw)
    /// </summary>
    public string SpotifyPlaylistUrls { get; set; } = string.Empty;

    // ──────────────────────────────────────────────
    //  Matching
    // ──────────────────────────────────────────────

    /// <summary>
    /// Minimum fuzzy-match score (0–100) required to consider a local track a match.
    /// Default 75 is a reasonable balance between precision and recall.
    /// </summary>
    public int MinMatchScore { get; set; } = 75;

    /// <summary>
    /// When true, a track must match both artist AND title to be included.
    /// When false, a high title-only score can be enough (useful for compilation albums).
    /// </summary>
    public bool RequireArtistMatch { get; set; } = true;

    // ──────────────────────────────────────────────
    //  Playlist
    // ──────────────────────────────────────────────

    /// <summary>
    /// Prefix to add to Jellyfin playlist names, e.g. "[Spotify] My Playlist".
    /// </summary>
    public string PlaylistNamePrefix { get; set; } = "[Spotify] ";

    /// <summary>
    /// Jellyfin User ID that will own the created playlists.
    /// Leave empty to use the first admin user found automatically.
    /// </summary>
    public string PlaylistOwnerUserId { get; set; } = string.Empty;

    /// <summary>
    /// When true, an existing playlist with the same name is updated (tracks replaced).
    /// When false, a new playlist is always created.
    /// </summary>
    public bool UpdateExistingPlaylist { get; set; } = true;

    // ──────────────────────────────────────────────
    //  Scheduler
    // ──────────────────────────────────────────────

    /// <summary>
    /// How often the scheduled task runs automatically (in hours). 0 = manual only.
    /// </summary>
    public int AutoSyncIntervalHours { get; set; } = 24;
}
