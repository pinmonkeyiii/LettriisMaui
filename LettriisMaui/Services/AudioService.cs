using System.Collections.Concurrent;
using Plugin.Maui.Audio;

namespace LettriisMaui.Services;

public sealed class AudioService
{
    private readonly IAudioManager _audio;

    // Cache loaded raw audio bytes (fast to create players from)
    private readonly ConcurrentDictionary<string, byte[]> _sfxData = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, long> _lastPlayMs = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, byte[]> _musicData = new(StringComparer.OrdinalIgnoreCase);

    // Music ducking
    private readonly SemaphoreSlim _duckGate = new(1, 1);
    private double _duckMultiplier = 1.0;

    // Music fading (pause/resume polish)
    private CancellationTokenSource? _musicFadeCts;

    private double _musicFadeBaseVolume => Clamp01(MusicVolume * _duckMultiplier);
    private MemoryStream? _musicStream;


    // Music (single looping player)
    private IAudioPlayer? _musicPlayer;
    private string? _currentMusicKey;

    // User settings hooks (set from SettingsService toggles)
    public bool SfxEnabled { get; set; } = true;
    public bool MusicEnabled { get; set; } = true;

    public double SfxVolume { get; set; } = 1.0;   // 0..1
    public double MusicVolume { get; set; } = 0.6; // 0..1

    // Keys -> filenames in Resources/Raw
    private static readonly IReadOnlyDictionary<string, string> SfxMap =
    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["rotate"] = "rotate.wav",
        ["clear_word"] = "clear_word.wav",
        ["level_up"] = "level_up.wav",
    };

    private static readonly IReadOnlyDictionary<string, string> MusicMap =
     new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
     {
         ["theme"] = "theme.mp3",
         ["lv_1_5"] = "lv_1_5.mp3",
         ["lv_6_10"] = "lv_6_10.mp3",
         ["lv_11_15"] = "lv_11_15.mp3",
         ["lv_16_20"] = "lv_16_20.mp3",
         ["lv_20_plus"] = "lv_20_plus.mp3",
     };

    public AudioService(IAudioManager audioManager)
    {
        _audio = audioManager;
    }

    public static string GetMusicKeyForLevel(int level)
    {
        if (level <= 5) return "lv_1_5";
        if (level <= 10) return "lv_6_10";
        if (level <= 15) return "lv_11_15";
        if (level <= 20) return "lv_16_20";
        return "lv_20_plus";
    }

    public void SetMusicVolume(double volume01)
    {
        MusicVolume = Clamp01(volume01);
        if (_musicPlayer is not null)
            _musicPlayer.Volume = Clamp01(MusicVolume * _duckMultiplier);
    }

    public void SetSfxVolume(double volume01)
    {
        SfxVolume = Clamp01(volume01);
    }

    /// <summary>
    /// Temporarily lowers music volume (e.g., for level-up / big clears).
    /// Safe to call repeatedly; newest duck wins.
    /// </summary>
    public async Task DuckMusicAsync(double multiplier, int downMs = 40, int holdMs = 140, int upMs = 160)
    {
        if (!MusicEnabled || _musicPlayer is null)
            return;

        multiplier = Clamp01(multiplier);

        await _duckGate.WaitAsync().ConfigureAwait(false);
        try
        {
            // Apply duck
            _duckMultiplier = multiplier;
            ApplyMusicVolumeInternal();

            // Simple timed duck (no fancy tweening needed for now)
            await Task.Delay(Math.Max(0, downMs + holdMs)).ConfigureAwait(false);

            // Restore
            _duckMultiplier = 1.0;
            ApplyMusicVolumeInternal();
        }
        catch
        {
            // ignore
        }
        finally
        {
            _duckGate.Release();
        }
    }

    private void ApplyMusicVolumeInternal()
    {
        try
        {
            if (_musicPlayer is not null)
                _musicPlayer.Volume = Clamp01(MusicVolume * _duckMultiplier);
        }
        catch { /* ignore */ }
    }

    private void CancelMusicFade()
    {
        try { _musicFadeCts?.Cancel(); } catch { }
        try { _musicFadeCts?.Dispose(); } catch { }
        _musicFadeCts = null;
    }

    private async Task FadeMusicToAsync(double targetVolume01, int ms, CancellationToken ct)
    {
        if (_musicPlayer is null)
            return;

        ms = Math.Max(0, ms);
        targetVolume01 = Clamp01(targetVolume01);

        // If no duration, jump
        if (ms == 0)
        {
            _musicPlayer.Volume = targetVolume01;
            return;
        }

        double start = _musicPlayer.Volume;
        const int stepMs = 16; // ~60fps
        int steps = Math.Max(1, ms / stepMs);

        for (int i = 1; i <= steps; i++)
        {
            ct.ThrowIfCancellationRequested();

            double t = (double)i / steps;
            double v = start + (targetVolume01 - start) * t;

            try { _musicPlayer.Volume = Clamp01(v); } catch { /* ignore */ }
            await Task.Delay(stepMs, ct).ConfigureAwait(false);
        }

        try { _musicPlayer.Volume = targetVolume01; } catch { /* ignore */ }
    }



    /// <summary>
    /// Preload the SFX that will be used, to avoid latency on first play.
    /// </summary>
    public async Task PreloadAsync(IEnumerable<string>? sfxKeys = null, CancellationToken ct = default)
    {
        var keys = sfxKeys?.ToArray() ?? SfxMap.Keys.ToArray();

        foreach (var key in keys)
        {
            ct.ThrowIfCancellationRequested();
            await LoadSfxBytesAsync(key, ct);
        }
    }

    public async Task PlaySfxAsync(string key, double? volume = null, int? throttleMs = null)
    {
        if (!SfxEnabled)
            return;

        // Throttle spammy SFX (rotate, move, etc.)
        // You can tune per key below.
        int gate = throttleMs ?? key.ToLowerInvariant() switch
        {
            "rotate" => 45,
            "lock" => 30,
            "drop" => 60,
            _ => 0
        };

        if (gate > 0 && IsThrottled(key, gate))
            return;

        var bytes = await LoadSfxBytesAsync(key, CancellationToken.None);
        if (bytes is null || bytes.Length == 0)
            return;

        // Create a short-lived player from memory, play, then dispose later.
        // This allows overlapping playback of the same sound (important for juice).
        var ms = new MemoryStream(bytes);      // DO NOT using
        var player = _audio.CreatePlayer(ms);
        player.Volume = Clamp01((volume ?? 1.0) * SfxVolume);
        player.Play();

        var delayMs = (int)(player.Duration > 0 ? player.Duration * 1000.0 + 50 : 800);
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(delayMs);
                player.Dispose();
                ms.Dispose();                  // dispose stream when done
            }
            catch { }
        });
    }

    private async Task<byte[]?> LoadMusicBytesAsync(string key, CancellationToken ct)
    {
        if (_musicData.TryGetValue(key, out var cached))
            return cached;

        if (!MusicMap.TryGetValue(key, out var filename))
            return null;

        try
        {
            await using var stream = await FileSystem.OpenAppPackageFileAsync(filename);
            using var ms = new MemoryStream();
            await stream.CopyToAsync(ms, ct);
            var bytes = ms.ToArray();
            _musicData[key] = bytes;
            return bytes;
        }
        catch
        {
            return null;
        }
    }

    public async Task PlayMusicAsync(string key, bool restartIfSame = false)
    {
        if (!MusicEnabled) return;
        if (!MusicMap.ContainsKey(key)) return;

        if (_musicPlayer is not null && _currentMusicKey == key && !restartIfSame)
        {
            if (!_musicPlayer.IsPlaying) _musicPlayer.Play();
            return;
        }

        StopMusic();

        var bytes = await LoadMusicBytesAsync(key, CancellationToken.None);
        if (bytes is null || bytes.Length == 0) return;

        _musicStream = new MemoryStream(bytes);              // keep alive
        _musicPlayer = _audio.CreatePlayer(_musicStream);
        _musicPlayer.Loop = true;
        _duckMultiplier = 1.0;
        _musicPlayer.Volume = 0;
        _currentMusicKey = key;

        _musicPlayer.Play();
        ResumeMusic(fadeInMs: 260);
    }

    public void StopMusic()
    {
        CancelMusicFade();

        try
        {
            _musicPlayer?.Stop();
            _musicPlayer?.Dispose();
        }
        catch { }
        finally
        {
            _musicPlayer = null;
            _currentMusicKey = null;

            try { _musicStream?.Dispose(); } catch { }
            _musicStream = null;
        }
    }

    public void PauseMusic(int fadeOutMs = 180)
    {
        try
        {
            if (_musicPlayer is null)
                return;

            CancelMusicFade();
            _musicFadeCts = new CancellationTokenSource();
            var ct = _musicFadeCts.Token;

            // Fire-and-forget fade
            _ = Task.Run(async () =>
            {
                try
                {
                    if (_musicPlayer is null) return;

                    // Fade to 0, then pause
                    await FadeMusicToAsync(0, fadeOutMs, ct).ConfigureAwait(false);

                    if (_musicPlayer?.IsPlaying == true)
                        _musicPlayer.Pause();
                }
                catch (OperationCanceledException) { }
                catch { /* ignore */ }
            }, ct);
        }
        catch { /* ignore */ }
    }

    public void ResumeMusic(int fadeInMs = 220)
    {
        if (!MusicEnabled) return;

        try
        {
            if (_musicPlayer is null)
                return;

            CancelMusicFade();
            _musicFadeCts = new CancellationTokenSource();
            var ct = _musicFadeCts.Token;

            _ = Task.Run(async () =>
            {
                try
                {
                    if (_musicPlayer is null) return;

                    // Ensure playing, start from 0 volume, then fade up
                    if (!_musicPlayer.IsPlaying)
                        _musicPlayer.Play();

                    try { _musicPlayer.Volume = 0; } catch { /* ignore */ }

                    // Fade to "intended" volume (respects MusicVolume + ducking)
                    await FadeMusicToAsync(_musicFadeBaseVolume, fadeInMs, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException) { }
                catch { /* ignore */ }
            }, ct);
        }
        catch { /* ignore */ }
    }

    public void ApplySettings(bool sfxEnabled, bool musicEnabled)
    {
        SfxEnabled = sfxEnabled;
        MusicEnabled = musicEnabled;

        if (!musicEnabled)
            StopMusic();
    }

    private bool IsThrottled(string key, int gateMs)
    {
        var now = Environment.TickCount64;
        var last = _lastPlayMs.GetOrAdd(key, _ => 0);

        if (now - last < gateMs)
            return true;

        _lastPlayMs[key] = now;
        return false;
    }

    private async Task<byte[]?> LoadSfxBytesAsync(string key, CancellationToken ct)
    {
        if (_sfxData.TryGetValue(key, out var cached))
            return cached;

        if (!SfxMap.TryGetValue(key, out var filename))
            return null;

        try
        {
            await using var stream = await FileSystem.OpenAppPackageFileAsync(filename);
            using var ms = new MemoryStream();
            await stream.CopyToAsync(ms, ct);
            var bytes = ms.ToArray();
            _sfxData[key] = bytes;
            return bytes;
        }
        catch
        {
            // Missing file or load failure: just no-op safely.
            return null;
        }
    }

    private static double Clamp01(double v) => v < 0 ? 0 : (v > 1 ? 1 : v);
}