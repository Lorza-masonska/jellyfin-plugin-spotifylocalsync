using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.SpotifyLocalSync.Api;
using Jellyfin.Plugin.SpotifyLocalSync.Matching;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Playlists;
using MediaBrowser.Model.Playlists;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.SpotifyLocalSync.Tasks;

/// <summary>
/// Scheduled task: fetches Spotify playlists and creates/updates native Jellyfin playlists.
/// </summary>
public sealed class ScheduledSyncTask : IScheduledTask
{
    private readonly ILibraryManager           _libraryManager;
    private readonly IPlaylistManager          _playlistManager;
    private readonly IUserManager              _userManager;
    private readonly IHttpClientFactory        _httpClientFactory;
    private readonly ILogger<ScheduledSyncTask> _logger;

    public ScheduledSyncTask(
        ILibraryManager            libraryManager,
        IPlaylistManager           playlistManager,
        IUserManager               userManager,
        IHttpClientFactory         httpClientFactory,
        ILogger<ScheduledSyncTask> logger)
    {
        _libraryManager    = libraryManager;
        _playlistManager   = playlistManager;
        _userManager       = userManager;
        _httpClientFactory = httpClientFactory;
        _logger            = logger;
    }

    public string Name        => "Spotify Local Sync";
    public string Key         => "SpotifyLocalSync";
    public string Description => "Fetches configured Spotify playlists and syncs them with the local Jellyfin music library.";
    public string Category    => "Library";

    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        var hours = Plugin.Instance?.Configuration?.AutoSyncIntervalHours ?? 24;
        if (hours <= 0) return Array.Empty<TaskTriggerInfo>();

        return new[]
        {
            new TaskTriggerInfo
            {
                // "Interval" is the correct string constant in 10.11
                Type          = MediaBrowser.Model.Tasks.TaskTriggerInfoType.IntervalTrigger,
                IntervalTicks = TimeSpan.FromHours(hours).Ticks
            }
        };
    }

    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        var cfg = Plugin.Instance?.Configuration;
        if (cfg is null)
        {
            _logger.LogError("[SpotifyLocalSync] Plugin configuration not available.");
            return;
        }

        if (string.IsNullOrWhiteSpace(cfg.SpotifyClientId) ||
            string.IsNullOrWhiteSpace(cfg.SpotifyClientSecret))
        {
            _logger.LogError("[SpotifyLocalSync] Spotify Client ID / Secret not configured. Aborting.");
            return;
        }

        var playlistLines = cfg.SpotifyPlaylistUrls
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .ToList();

        if (playlistLines.Count == 0)
        {
            _logger.LogWarning("[SpotifyLocalSync] No Spotify playlist URLs configured.");
            return;
        }

        // Resolve owner user ID
        Guid ownerUserId = Guid.Empty;

        if (!string.IsNullOrWhiteSpace(cfg.PlaylistOwnerUserId) &&
            Guid.TryParse(cfg.PlaylistOwnerUserId, out var parsedGuid))
        {
            ownerUserId = parsedGuid;
            _logger.LogInformation("[SpotifyLocalSync] Using configured user ID: {Id}", ownerUserId);
        }

        if (ownerUserId == Guid.Empty)
        {
            // Get first available user (in single-user setups this is always the admin)
            var firstUser = _userManager.Users.FirstOrDefault();

            if (firstUser is null)
            {
                _logger.LogError("[SpotifyLocalSync] No users found. Aborting.");
                return;
            }

            ownerUserId = firstUser.Id;
            _logger.LogInformation("[SpotifyLocalSync] Running sync as user '{User}'.", firstUser.Username);
        }

        var spotifyClient = new SpotifyApiClient(_httpClientFactory, _logger);
        var matcher       = new TrackMatcher(_libraryManager, _logger);
        var total         = playlistLines.Count;

        for (var i = 0; i < total; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress.Report((double)i / total * 100);

            var playlistId = SpotifyApiClient.ParsePlaylistId(playlistLines[i]);
            if (playlistId is null)
            {
                _logger.LogWarning("[SpotifyLocalSync] Cannot parse playlist ID from: {Line}", playlistLines[i]);
                continue;
            }

            await ProcessPlaylistAsync(spotifyClient, matcher, cfg, ownerUserId, playlistId, cancellationToken)
                .ConfigureAwait(false);
        }

        progress.Report(100);
        _logger.LogInformation("[SpotifyLocalSync] Sync complete.");
    }

    private async Task ProcessPlaylistAsync(
        SpotifyApiClient                   spotifyClient,
        TrackMatcher                       matcher,
        Configuration.PluginConfiguration  cfg,
        Guid                               ownerUserId,
        string                             playlistId,
        CancellationToken                  ct)
    {
        _logger.LogInformation("[SpotifyLocalSync] Processing Spotify playlist: {Id}", playlistId);

        string spotifyName;
        List<SpotifyTrack> spotifyTracks;
        try
        {
            spotifyName   = await spotifyClient.GetPlaylistNameAsync(
                                cfg.SpotifyClientId, cfg.SpotifyClientSecret, playlistId, ct)
                            .ConfigureAwait(false);
            spotifyTracks = await spotifyClient.GetPlaylistTracksAsync(
                                cfg.SpotifyClientId, cfg.SpotifyClientSecret, playlistId, ct)
                            .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[SpotifyLocalSync] Failed to fetch data for playlist {Id}.", playlistId);
            return;
        }

        _logger.LogInformation("[SpotifyLocalSync] Playlist \"{Name}\" has {Count} Spotify tracks.",
            spotifyName, spotifyTracks.Count);

        var matches = matcher.Match(spotifyTracks, cfg.MinMatchScore, cfg.RequireArtistMatch);
        if (matches.Count == 0)
        {
            _logger.LogWarning("[SpotifyLocalSync] No local matches for \"{Name}\". Skipping.", spotifyName);
            return;
        }

        var localItemIds = matches.Select(m => m.LocalItemId).ToArray();
        var jellyfinName = cfg.PlaylistNamePrefix + spotifyName;

        await CreateOrUpdatePlaylistAsync(jellyfinName, localItemIds, ownerUserId, cfg.UpdateExistingPlaylist, ct)
            .ConfigureAwait(false);
    }

    private async Task CreateOrUpdatePlaylistAsync(
        string            name,
        Guid[]            itemIds,
        Guid              ownerUserId,
        bool              updateExisting,
        CancellationToken ct)
    {
        if (updateExisting)
        {
            var existing = _libraryManager.GetItemList(new InternalItemsQuery
            {
                IncludeItemTypes = new[] { Jellyfin.Data.Enums.BaseItemKind.Playlist },
                Recursive        = true,
                Name             = name
            }).OfType<MediaBrowser.Controller.Playlists.Playlist>().FirstOrDefault();

            if (existing is not null)
            {
                _logger.LogInformation(
                    "[SpotifyLocalSync] Updating existing playlist \"{Name}\" ({Id})...",
                    name, existing.Id);

                var currentEntryIds = existing
                    .GetLinkedChildren()
                    .Select(c => c.Id.ToString("N", CultureInfo.InvariantCulture))
                    .ToArray();

                if (currentEntryIds.Length > 0)
                {
                    await _playlistManager.RemoveItemFromPlaylistAsync(
                        existing.Id.ToString("N", CultureInfo.InvariantCulture),
                        currentEntryIds)
                        .ConfigureAwait(false);
                }

                await _playlistManager.AddItemToPlaylistAsync(
                    existing.Id,
                    itemIds,
                    ownerUserId)
                    .ConfigureAwait(false);

                _logger.LogInformation(
                    "[SpotifyLocalSync] Playlist \"{Name}\" updated with {Count} tracks.",
                    name, itemIds.Length);
                return;
            }
        }

        _logger.LogInformation("[SpotifyLocalSync] Creating playlist \"{Name}\" with {Count} tracks...",
            name, itemIds.Length);

        var result = await _playlistManager.CreatePlaylist(new PlaylistCreationRequest
        {
            Name       = name,
            ItemIdList = itemIds,
            UserId     = ownerUserId,
            MediaType  = Jellyfin.Data.Enums.MediaType.Audio
        }).ConfigureAwait(false);

        _logger.LogInformation(
            "[SpotifyLocalSync] Playlist \"{Name}\" created (ID: {Id}).",
            name, result.Id);
    }
}
