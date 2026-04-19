const pluginUniqueId = '4a7b2c3d-1e5f-4a8b-9c0d-2e3f4a5b6c7d';

function showStatus(message, isError) {
    const el = document.getElementById('syncStatus');
    el.textContent = message;
    el.style.display = 'block';
    el.style.background = isError ? 'rgba(220,53,69,0.15)' : 'rgba(40,167,69,0.15)';
    el.style.border = '1px solid ' + (isError ? '#dc3545' : '#28a745');
    el.style.color = isError ? '#dc3545' : '#28a745';
}

export default function (view) {
    view.dispatchEvent(new CustomEvent('create'));

    view.addEventListener('viewshow', function () {
        Dashboard.showLoadingMsg();

        ApiClient.getPluginConfiguration(pluginUniqueId).then(function (config) {
            document.getElementById('SpotifyClientId').value          = config.SpotifyClientId         || '';
            document.getElementById('SpotifyClientSecret').value      = config.SpotifyClientSecret     || '';
            document.getElementById('SpotifyPlaylistUrls').value      = config.SpotifyPlaylistUrls     || '';
            document.getElementById('MinMatchScore').value            = config.MinMatchScore           != null ? config.MinMatchScore : 75;
            document.getElementById('RequireArtistMatch').checked     = config.RequireArtistMatch      !== false;
            document.getElementById('PlaylistNamePrefix').value       = config.PlaylistNamePrefix      || '[Spotify] ';
            document.getElementById('PlaylistOwnerUserId').value      = config.PlaylistOwnerUserId     || '';
            document.getElementById('UpdateExistingPlaylist').checked = config.UpdateExistingPlaylist  !== false;
            document.getElementById('AutoSyncIntervalHours').value    = config.AutoSyncIntervalHours   != null ? config.AutoSyncIntervalHours : 24;

            Dashboard.hideLoadingMsg();
        }).catch(function (err) {
            Dashboard.hideLoadingMsg();
            showStatus('❌ Błąd wczytywania: ' + (err.message || err), true);
        });
    });

    document.getElementById('SpotifyLocalSyncForm').addEventListener('submit', function (e) {
        e.preventDefault();
        Dashboard.showLoadingMsg();

        ApiClient.getPluginConfiguration(pluginUniqueId).then(function (config) {
            config.SpotifyClientId        = document.getElementById('SpotifyClientId').value.trim();
            config.SpotifyClientSecret    = document.getElementById('SpotifyClientSecret').value.trim();
            config.SpotifyPlaylistUrls    = document.getElementById('SpotifyPlaylistUrls').value.trim();
            config.MinMatchScore          = parseInt(document.getElementById('MinMatchScore').value, 10) || 75;
            config.RequireArtistMatch     = document.getElementById('RequireArtistMatch').checked;
            config.PlaylistNamePrefix     = document.getElementById('PlaylistNamePrefix').value;
            config.PlaylistOwnerUserId    = document.getElementById('PlaylistOwnerUserId').value.trim();
            config.UpdateExistingPlaylist = document.getElementById('UpdateExistingPlaylist').checked;
            config.AutoSyncIntervalHours  = parseInt(document.getElementById('AutoSyncIntervalHours').value, 10) || 0;

            return ApiClient.updatePluginConfiguration(pluginUniqueId, config);
        }).then(function (result) {
            Dashboard.hideLoadingMsg();
            Dashboard.processPluginConfigurationUpdateResult(result);
        }).catch(function (err) {
            Dashboard.hideLoadingMsg();
            showStatus('❌ Błąd zapisu: ' + (err.message || err), true);
        });

        return false;
    });

    document.getElementById('btnSyncNow').addEventListener('click', function () {
        const btn = document.getElementById('btnSyncNow');
        btn.setAttribute('disabled', 'disabled');
        showStatus('⏳ Uruchamianie synchronizacji…', false);

        ApiClient.getScheduledTasks().then(function (tasks) {
            const task = tasks.find(function (t) { return t.Key === 'SpotifyLocalSync'; });
            if (!task) throw new Error('Nie znaleziono zadania SpotifyLocalSync. Zrestartuj Jellyfin.');
            return ApiClient.startScheduledTask(task.Id);
        }).then(function () {
            showStatus('✅ Synchronizacja uruchomiona. Sprawdź Zaplanowane zadania.', false);
            btn.removeAttribute('disabled');
        }).catch(function (err) {
            showStatus('❌ ' + (err.message || err), true);
            btn.removeAttribute('disabled');
        });
    });
}
