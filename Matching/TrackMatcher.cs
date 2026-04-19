using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.SpotifyLocalSync.Api;
using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.SpotifyLocalSync.Matching;

/// <summary>
/// Pairs a list of SpotifyTrack items against the local Jellyfin audio library.
/// </summary>
public sealed class TrackMatcher
{
    private readonly ILibraryManager _libraryManager;
    private readonly ILogger         _logger;

    public TrackMatcher(ILibraryManager libraryManager, ILogger logger)
    {
        _libraryManager = libraryManager;
        _logger         = logger;
    }

    /// <summary>
    /// Attempts to find a local Audio item for each Spotify track.
    /// Returns matched results ordered by combined score descending.
    /// </summary>
    public List<MatchResult> Match(
        IReadOnlyList<SpotifyTrack> spotifyTracks,
        int  minScore,
        bool requireArtistMatch)
    {
        // Load the entire local audio catalogue once
        var allLocalTracks = _libraryManager.GetItemList(new InternalItemsQuery
        {
            IncludeItemTypes = new[] { BaseItemKind.Audio },
            Recursive        = true
        }).OfType<Audio>().ToList();

        _logger.LogInformation("[SpotifyLocalSync] Loaded {Count} local audio items for matching.",
            allLocalTracks.Count);

        // Pre-normalise local metadata once
        var localNorm = allLocalTracks.Select(t => new LocalNorm(
            Item       : t,
            NormTitle  : FuzzyMatcher.Normalize(t.Name ?? string.Empty),
            NormArtist : FuzzyMatcher.Normalize(
                             string.Join(" ", t.Artists ?? Array.Empty<string>()))
        )).ToList();

        var results = new List<MatchResult>();

        foreach (var spotifyTrack in spotifyTracks)
        {
            var normSpotifyTitle  = FuzzyMatcher.Normalize(spotifyTrack.Name);
            var normSpotifyArtist = FuzzyMatcher.Normalize(spotifyTrack.ArtistsString);

            MatchResult? best = null;

            foreach (var local in localNorm)
            {
                var titleScore = FuzzyMatcher.Score(normSpotifyTitle, local.NormTitle);

                // Quick bail-out
                if (titleScore < minScore - 20) continue;

                var artistScore = string.IsNullOrEmpty(normSpotifyArtist) || string.IsNullOrEmpty(local.NormArtist)
                    ? 100
                    : FuzzyMatcher.Score(normSpotifyArtist, local.NormArtist);

                if (requireArtistMatch && artistScore < minScore) continue;

                // 60% title, 40% artist
                var combined = (int)Math.Round(titleScore * 0.6 + artistScore * 0.4);
                if (combined < minScore) continue;

                if (best is null || combined > best.CombinedScore)
                {
                    best = new MatchResult(
                        SpotifyTrackId : spotifyTrack.Id,
                        SpotifyTitle   : spotifyTrack.Name,
                        SpotifyArtist  : spotifyTrack.ArtistsString,
                        LocalItemId    : local.Item.Id,
                        LocalTitle     : local.Item.Name ?? string.Empty,
                        LocalArtist    : string.Join(", ", local.Item.Artists ?? Array.Empty<string>()),
                        TitleScore     : titleScore,
                        ArtistScore    : artistScore,
                        CombinedScore  : combined
                    );
                }
            }

            if (best is not null)
            {
                results.Add(best);
                _logger.LogDebug(
                    "[SpotifyLocalSync] Match [{SpotifyArtist}] \"{SpotifyTitle}\" -> [{LocalArtist}] \"{LocalTitle}\" (score {Score})",
                    best.SpotifyArtist, best.SpotifyTitle,
                    best.LocalArtist,  best.LocalTitle, best.CombinedScore);
            }
            else
            {
                _logger.LogDebug(
                    "[SpotifyLocalSync] NoMatch [{SpotifyArtist}] \"{SpotifyTitle}\"",
                    spotifyTrack.ArtistsString, spotifyTrack.Name);
            }
        }

        _logger.LogInformation(
            "[SpotifyLocalSync] Matched {Matched}/{Total} Spotify tracks locally.",
            results.Count, spotifyTracks.Count);

        return results.OrderByDescending(r => r.CombinedScore).ToList();
    }

    private record LocalNorm(Audio Item, string NormTitle, string NormArtist);
}
