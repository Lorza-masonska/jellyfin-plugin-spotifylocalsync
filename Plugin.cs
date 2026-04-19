using System;
using System.Collections.Generic;
using Jellyfin.Plugin.SpotifyLocalSync.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.SpotifyLocalSync;

/// <summary>
/// SpotifyLocalSync plugin entry point.
/// Registered automatically by Jellyfin's plugin loader.
/// </summary>
public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    /// <summary>
    /// Singleton instance – convenient for services that cannot use DI easily.
    /// </summary>
    public static Plugin? Instance { get; private set; }

    /// <inheritdoc />
    public override string Name => "SpotifyLocalSync";

    /// <inheritdoc />
    public override Guid Id => new("4a7b2c3d-1e5f-4a8b-9c0d-2e3f4a5b6c7d");

    /// <inheritdoc />
    public override string Description =>
        "Synchronizes Spotify public playlists with your local Jellyfin music library using fuzzy track matching.";

    /// <summary>
    /// Initializes a new instance of <see cref="Plugin"/>.
    /// </summary>
    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
    }

    /// <inheritdoc />
    public IEnumerable<PluginPageInfo> GetPages()
    {
        // The embedded HTML resource name must match the EmbeddedResource path in the .csproj
        // with dots replacing path separators: "Jellyfin.Plugin.SpotifyLocalSync.Web.configurationpage.html"
        yield return new PluginPageInfo
        {
            Name            = "SpotifyLocalSync",
            EmbeddedResourcePath = $"{GetType().Namespace}.Web.configurationpage.html",
            EnableInMainMenu = false
        };
    }
}
