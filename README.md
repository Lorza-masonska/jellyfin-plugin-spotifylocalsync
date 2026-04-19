# 🎵 SpotifyLocalSync – Jellyfin Plugin

Wtyczka do Jellyfin synchronizująca publiczne playlisty Spotify z lokalną biblioteką muzyczną.
Używa **fuzzy matchingu** (Levenshtein + token-set ratio) do parowania utworów, radząc sobie
z różnicami w interpunkcji, akcentach, tagach "feat.", przyrostkach "Remastered" itp.

---

## Wymagania

| Wymaganie          | Wersja            |
|--------------------|-------------------|
| Jellyfin Server    | 10.9.0 lub nowszy |
| .NET               | 8.0               |
| Konto Spotify      | Wymagane tylko do założenia aplikacji deweloperskiej |

---

## Instalacja

### Opcja A – własne repozytorium wtyczek (zalecane)

1. Skompiluj projekt lub pobierz gotowy `.zip` z zakładki **Releases**.
2. Opublikuj `manifest.json` pod publicznym adresem URL
   (np. na GitHub Pages lub jako raw gist).
3. W Jellyfin → **Pulpit → Wtyczki → Repozytoria** → **+** dodaj URL do `manifest.json`.
4. Przejdź do **Wykaz → SpotifyLocalSync** i kliknij **Zainstaluj**.
5. Zrestartuj serwer.

### Opcja B – instalacja ręczna

1. Skompiluj: `dotnet publish -c Release -o ./publish`
2. Skopiuj `Jellyfin.Plugin.SpotifyLocalSync.dll` do katalogu `plugins/SpotifyLocalSync/`
   w katalogu danych Jellyfin (np. `C:\ProgramData\Jellyfin\Server\plugins\SpotifyLocalSync\`).
3. Zrestartuj serwer.

---

## Konfiguracja

Przejdź do **Pulpit → Wtyczki → SpotifyLocalSync → Ustawienia**.

### 1. Utwórz aplikację Spotify

1. Wejdź na [developer.spotify.com/dashboard](https://developer.spotify.com/dashboard).
2. Kliknij **Create App**.
3. Wypełnij nazwę i opis (dowolne).
4. W polu **Redirect URI** wpisz np. `http://localhost` (nie jest używane, ale wymagane).
5. Skopiuj **Client ID** i **Client Secret** do ustawień wtyczki.

> ⚠️ Client Secret powinien być traktowany jak hasło. Nie udostępniaj go publicznie.

### 2. Dodaj playlisty

Wklej linki do publicznych playlist Spotify, po jednym w linii:

```
https://open.spotify.com/playlist/37i9dQZF1DXcBWIGoYBM5M
https://open.spotify.com/playlist/5FJXhjdILmRA2z5bvz4nzf
```

### 3. Dostosuj opcje dopasowania

| Opcja                      | Domyślnie | Opis |
|----------------------------|-----------|------|
| Minimalny wynik dopasowania| 75        | 0–100; wyżej = mniej fałszywych trafień |
| Wymagaj dopasowania artysty| ✅ Tak    | Wyłącz dla składanek bez tagów artysty  |

### 4. Synchronizacja

- Kliknij **Synchronizuj teraz** na stronie konfiguracji, lub
- Poczekaj na automatyczne wykonanie wg harmonogramu (domyślnie co 24h), lub
- Uruchom ręcznie w **Pulpit → Zaplanowane zadania → Spotify Local Sync**.

---

## Jak działa fuzzy matching?

Algorytm parowania składa się z dwóch etapów:

1. **Normalizacja** – obie strony (Spotify i lokalna) są przetwarzane:
   - zamiana na małe litery
   - usunięcie znaków diakrytycznych (ą→a, ę→e, …)
   - usunięcie szumu w nawiasach: `(feat. X)`, `(Remastered 2011)`, `(Radio Edit)`, …
   - zastąpienie `&` → `and`, `+` → `and`
   - usunięcie interpunkcji, zwinięcie spacji

2. **Scoring** (wynik 0–100):
   - **Levenshtein ratio** – odległość edycji znaków (waga 40%)
   - **Token-set ratio** – przecięcie zbiorów słów, odporne na kolejność
     (np. "Beyoncé feat. Jay-Z" vs "Jay-Z & Beyoncé") (waga 60%)
   - Wynik końcowy: `(titleScore × 0.6 + artistScore × 0.4)`

---

## Struktura projektu

```
SpotifyLocalSync/
├── .github/workflows/release.yml    # CI/CD – build i release
├── Api/
│   └── SpotifyApiClient.cs          # HTTP klient Spotify + parsowanie odpowiedzi
├── Configuration/
│   └── PluginConfiguration.cs       # Model konfiguracji (serializowany do XML)
├── Matching/
│   ├── FuzzyMatcher.cs              # Algorytm Levenshtein + token-set
│   └── TrackMatcher.cs              # Łączy Spotify tracks z lokalnymi Audio items
├── Tasks/
│   └── ScheduledSyncTask.cs         # Zaplanowane zadanie Jellyfin
├── Web/
│   └── configurationpage.html       # Strona konfiguracji (osadzona jako zasób)
├── Plugin.cs                        # Punkt wejścia wtyczki
├── ServiceRegistrator.cs            # Rejestracja DI
├── SpotifyLocalSync.csproj
└── manifest.json                    # Metadane repozytorium wtyczek
```

---

## Rozwiązywanie problemów

| Problem | Rozwiązanie |
|---------|-------------|
| "0 matched tracks" | Sprawdź czy biblioteka muzyczna Jellyfin jest zindeksowana. Obniż `MinMatchScore` do 60. |
| HTTP 401 od Spotify | Sprawdź Client ID i Client Secret. Upewnij się że aplikacja jest aktywna. |
| Playlista nie pojawia się | Upewnij się że użytkownik-właściciel ma dostęp do biblioteki muzycznej. |
| Task nie pojawia się | Zrestartuj serwer po instalacji wtyczki. |

---

## Licencja

MIT
