using System.Media;
using System.Text.Json;

namespace SurfOS2;

internal sealed class CloudMusicLibrary
{
    public string Library { get; set; } = "SurfOS Cloud Music";
    public string Version { get; set; } = "1";
    public List<CloudSong> Songs { get; set; } = [];
}

internal sealed class CloudSong
{
    public string Title { get; set; } = string.Empty;
    public string Artist { get; set; } = string.Empty;
    public string FileUrl { get; set; } = string.Empty;
    public string EncodedFileUrl { get; set; } = string.Empty;
    public string Duration { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public string Category { get; set; } = string.Empty;
}

internal sealed class MusicPlaylist
{
    public string Name { get; set; } = string.Empty;
    public List<string> Files { get; set; } = [];
}

internal static class MusicPlayer
{
    private const string EncodedCloudMusicManifestUrl =
        "OwEGFjBWQFoAIRwEA20LABoDPxBcBSwBQAAHbBAKFiweG0gAPAIcCiwNC1MNN0hDB3FYKwIqDBYxDiVYFyojFAAAVDEdPBMqBiQGUCUiPh4=";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private static string _currentTrack = string.Empty;
    private static SoundPlayer? _soundPlayer;
    private static bool _simulatedPlaying;
    private static bool _paused;

    public static void HandleCommand(string[] args, int cmdIndex)
    {
        EnsureMusicDirectories();

        string action = args.Length > cmdIndex + 1
            ? args[cmdIndex + 1].ToLowerInvariant()
            : "open";

        switch (action)
        {
            case "open":
                ShowPlayerHome();
                break;

            case "play":
                if (args.Length <= cmdIndex + 2)
                {
                    Console.WriteLine("Usage: music play <file>");
                    return;
                }
                Play(string.Join(" ", args.Skip(cmdIndex + 2)));
                break;

            case "pause":
                Pause();
                break;

            case "stop":
                Stop();
                break;

            case "list":
                ListLocalMusic();
                break;

            case "cloud":
                ListCloudMusic();
                break;

            case "download":
                if (args.Length <= cmdIndex + 2)
                {
                    Console.WriteLine("Usage: music download <song>");
                    return;
                }
                DownloadCloudSong(string.Join(" ", args.Skip(cmdIndex + 2)));
                break;

            case "playlist":
                HandlePlaylistCommand(args, cmdIndex + 2);
                break;

            default:
                PrintUsage();
                break;
        }
    }

    private static void ShowPlayerHome()
    {
        Console.Clear();
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("======================================");
        Console.WriteLine("           SURFOS MUSIC PLAYER        ");
        Console.WriteLine("======================================");
        Console.ResetColor();
        Console.WriteLine($"Now playing : {(string.IsNullOrWhiteSpace(_currentTrack) ? "nothing" : _currentTrack)}");
        Console.WriteLine($"State       : {GetStateLabel()}");
        Console.WriteLine();
        PrintUsage();
    }

    private static void Play(string requestedFile)
    {
        string path = ResolveMusicFile(requestedFile);
        if (!File.Exists(path))
        {
            Console.WriteLine($"Music file not found: {requestedFile}");
            return;
        }

        Stop(silent: true);
        _currentTrack = Path.GetFileName(path);
        _paused = false;

        if (Path.GetExtension(path).Equals(".wav", StringComparison.OrdinalIgnoreCase) &&
            OperatingSystem.IsWindows())
        {
            try
            {
                _soundPlayer = new SoundPlayer(path);
                _soundPlayer.Load();
                _soundPlayer.Play();
                _simulatedPlaying = false;
                KernelLog.Success("music", $"playing {_currentTrack}");
                Console.WriteLine($"Playing {_currentTrack}");
                return;
            }
            catch (Exception ex)
            {
                _soundPlayer?.Dispose();
                _soundPlayer = null;
                KernelLog.Warning("music", $"real playback failed for {_currentTrack}: {ex.Message}");
                Console.WriteLine($"Real playback failed: {ex.Message}");
            }
        }

        _simulatedPlaying = true;
        KernelLog.Info("music", $"simulated playback for {_currentTrack}");
        Console.WriteLine($"Simulated playback: {_currentTrack}");
        Console.WriteLine("Tip: SurfOS currently plays WAV files directly. Other formats use simulated playback.");
    }

    private static void Pause()
    {
        if (string.IsNullOrWhiteSpace(_currentTrack))
        {
            Console.WriteLine("Nothing is playing.");
            return;
        }

        _paused = true;
        if (OperatingSystem.IsWindows())
        {
            _soundPlayer?.Stop();
        }
        _simulatedPlaying = false;
        Console.WriteLine($"Paused {_currentTrack}");
    }

    private static void Stop(bool silent = false)
    {
        if (OperatingSystem.IsWindows())
        {
            _soundPlayer?.Stop();
        }
        _soundPlayer?.Dispose();
        _soundPlayer = null;
        _simulatedPlaying = false;
        _paused = false;
        if (!string.IsNullOrWhiteSpace(_currentTrack))
        {
            KernelLog.Info("music", $"stopped {_currentTrack}");
        }
        _currentTrack = string.Empty;
        if (!silent)
        {
            Console.WriteLine("Music stopped.");
        }
    }

    private static void ListLocalMusic()
    {
        EnsureMusicDirectories();
        string[] files = Directory.GetFiles(GetMusicPath())
            .Where(IsSupportedMusicFile)
            .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Console.WriteLine("\n--- Local Music ---");
        if (files.Length == 0)
        {
            Console.WriteLine("No local music found in /music.");
            return;
        }

        foreach (string file in files)
        {
            FileInfo info = new(file);
            Console.WriteLine($"{Path.GetFileName(file),-28} {info.Length / 1024} KB");
        }
    }

    private static void ListCloudMusic()
    {
        CloudMusicLibrary? library = LoadCloudLibrary();
        if (library is null)
        {
            Console.WriteLine("Cloud music unavailable. Showing local music only.");
            ListLocalMusic();
            return;
        }

        Console.WriteLine($"\n--- {library.Library} ---");
        foreach (CloudSong song in library.Songs)
        {
            Console.WriteLine(
                $"{song.Title,-22} {song.Artist,-12} {song.Duration,-6} {song.FileSize / 1024} KB  {song.Category}");
        }
    }

    private static void DownloadCloudSong(string songName)
    {
        CloudMusicLibrary? library = LoadCloudLibrary();
        if (library is null)
        {
            Console.WriteLine("Cloud music unavailable. Try again when internet is available.");
            return;
        }

        CloudSong? song = library.Songs.FirstOrDefault(item =>
            item.Title.Equals(songName, StringComparison.OrdinalIgnoreCase) ||
            ToSafeFileName(item.Title).Equals(songName, StringComparison.OrdinalIgnoreCase));

        if (song is null)
        {
            Console.WriteLine($"Cloud song '{songName}' was not found.");
            return;
        }

        string tempPath = string.Empty;
        try
        {
            string songFileUrl = GetSongFileUrl(song);
            string extension = Path.GetExtension(new Uri(songFileUrl).AbsolutePath);
            if (string.IsNullOrWhiteSpace(extension))
            {
                extension = ".wav";
            }

            string targetPath = Path.Combine(GetMusicPath(), $"{ToSafeFileName(song.Title)}{extension}");
            tempPath = targetPath + ".download";
            using HttpClient client = new() { Timeout = TimeSpan.FromSeconds(30) };
            byte[] data = client.GetByteArrayAsync(songFileUrl).GetAwaiter().GetResult();
            File.WriteAllBytes(tempPath, data);
            if (File.Exists(targetPath))
            {
                File.Delete(targetPath);
            }
            File.Move(tempPath, targetPath);
            KernelLog.Success("music", $"downloaded {song.Title}");
            Console.WriteLine($"Downloaded {song.Title} to /music.");
        }
        catch (Exception ex)
        {
            if (!string.IsNullOrWhiteSpace(tempPath) && File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }

            KernelLog.Warning("music", $"download failed for {song.Title}: {ex.Message}");
            Console.WriteLine($"Download failed: {ex.Message}");
        }
    }

    private static void HandlePlaylistCommand(string[] args, int actionIndex)
    {
        if (args.Length <= actionIndex)
        {
            Console.WriteLine("Usage: music playlist <create|add> <name> [file]");
            return;
        }

        string action = args[actionIndex].ToLowerInvariant();
        if (action == "create" && args.Length > actionIndex + 1)
        {
            string name = args[actionIndex + 1];
            string path = GetPlaylistPath(name);
            if (File.Exists(path))
            {
                Console.WriteLine($"Playlist '{name}' already exists.");
                return;
            }

            JsonStorage.Write(path, new MusicPlaylist { Name = name });
            Console.WriteLine($"Created playlist '{name}'.");
            return;
        }

        if (action == "add" && args.Length > actionIndex + 1)
        {
            string name;
            string file;
            if (args.Length > actionIndex + 2)
            {
                name = args[actionIndex + 1];
                file = string.Join(" ", args.Skip(actionIndex + 2));
            }
            else
            {
                string[] playlists = Directory.GetFiles(GetPlaylistPathRoot(), "*.json");
                if (playlists.Length != 1)
                {
                    Console.WriteLine("Usage: music playlist add <name> <file>");
                    Console.WriteLine("Shortcut 'music playlist add <file>' only works when exactly one playlist exists.");
                    return;
                }

                MusicPlaylist? onlyPlaylist = JsonStorage.Read<MusicPlaylist>(playlists[0]);
                name = onlyPlaylist?.Name ?? Path.GetFileNameWithoutExtension(playlists[0]);
                file = args[actionIndex + 1];
            }

            string playlistPath = GetPlaylistPath(name);
            if (!File.Exists(playlistPath))
            {
                Console.WriteLine($"Playlist '{name}' does not exist.");
                return;
            }

            string musicPath = ResolveMusicFile(file);
            if (!File.Exists(musicPath))
            {
                Console.WriteLine($"Music file not found: {file}");
                return;
            }

            MusicPlaylist playlist = JsonStorage.Read<MusicPlaylist>(playlistPath) ??
                                     new MusicPlaylist { Name = name };
            playlist.Files.Add(Path.GetFileName(musicPath));
            JsonStorage.Write(playlistPath, playlist);
            Console.WriteLine($"Added {Path.GetFileName(musicPath)} to playlist '{name}'.");
            return;
        }

        Console.WriteLine("Usage: music playlist <create|add> <name> [file]");
    }

    private static CloudMusicLibrary? LoadCloudLibrary()
    {
        try
        {
            using HttpClient client = new() { Timeout = TimeSpan.FromSeconds(12) };
            string json = client.GetStringAsync(CloudRepositoryManager.DecodeCloudUrl(EncodedCloudMusicManifestUrl))
                .GetAwaiter()
                .GetResult();
            CloudMusicLibrary? library = JsonSerializer.Deserialize<CloudMusicLibrary>(json, JsonOptions);
            if (library is null)
            {
                return null;
            }

            File.WriteAllText(GetCloudCachePath(), JsonSerializer.Serialize(library, JsonOptions));
            return library;
        }
        catch (Exception ex)
        {
            KernelLog.Warning("music", $"cloud music unavailable: {ex.Message}");
            if (File.Exists(GetCloudCachePath()))
            {
                try
                {
                    return JsonStorage.Read<CloudMusicLibrary>(GetCloudCachePath());
                }
                catch
                {
                    return null;
                }
            }

            return null;
        }
    }

    private static string ResolveMusicFile(string requestedFile)
    {
        if (Path.IsPathRooted(requestedFile))
        {
            return requestedFile;
        }

        string direct = Path.Combine(GetMusicPath(), requestedFile);
        if (File.Exists(direct))
        {
            return direct;
        }

        return Directory.GetFiles(GetMusicPath())
            .FirstOrDefault(file =>
                Path.GetFileNameWithoutExtension(file).Equals(requestedFile, StringComparison.OrdinalIgnoreCase) ||
                Path.GetFileName(file).Equals(requestedFile, StringComparison.OrdinalIgnoreCase)) ??
               direct;
    }

    private static string GetStateLabel()
    {
        if (_paused)
        {
            return "paused";
        }

        return _soundPlayer is not null || _simulatedPlaying ? "playing" : "stopped";
    }

    private static bool IsSupportedMusicFile(string file)
    {
        string extension = Path.GetExtension(file).ToLowerInvariant();
        return extension is ".wav" or ".mp3" or ".ogg" or ".flac";
    }

    private static string ToSafeFileName(string value)
    {
        string cleaned = string.Join(
            "-",
            value.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));
        return cleaned.Replace(' ', '-').ToLowerInvariant();
    }

    private static void EnsureMusicDirectories()
    {
        Directory.CreateDirectory(GetMusicPath());
        Directory.CreateDirectory(GetPlaylistPathRoot());
    }

    private static string GetMusicPath()
    {
        return Path.Combine(GetRootPath(), "music");
    }

    private static string GetPlaylistPathRoot()
    {
        return Path.Combine(GetMusicPath(), "playlists");
    }

    private static string GetPlaylistPath(string name)
    {
        return Path.Combine(GetPlaylistPathRoot(), $"{ToSafeFileName(name)}.json");
    }

    private static string GetCloudCachePath()
    {
        return Path.Combine(GetMusicPath(), "music.cache.json");
    }

    private static string GetSongFileUrl(CloudSong song)
    {
        if (!string.IsNullOrWhiteSpace(song.EncodedFileUrl))
        {
            return CloudRepositoryManager.DecodeCloudUrl(song.EncodedFileUrl);
        }

        return song.FileUrl;
    }

    private static string GetRootPath()
    {
        return string.IsNullOrWhiteSpace(Import.Variables.installPath)
            ? Environment.CurrentDirectory
            : Import.Variables.installPath;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("Commands:");
        Console.WriteLine("  music");
        Console.WriteLine("  music play <file>");
        Console.WriteLine("  music pause");
        Console.WriteLine("  music stop");
        Console.WriteLine("  music list");
        Console.WriteLine("  music cloud");
        Console.WriteLine("  music download <song>");
        Console.WriteLine("  music playlist create <name>");
        Console.WriteLine("  music playlist add <name> <file>");
        Console.WriteLine("  music playlist add <file>  (when exactly one playlist exists)");
        Console.WriteLine();
        Console.WriteLine("Playback: WAV files play through Windows audio. MP3/OGG/FLAC are listed but simulated for now.");
    }
}
