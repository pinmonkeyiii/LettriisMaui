using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using LettriisMaui.Models.Session;
using Microsoft.Maui.Storage;

namespace LettriisMaui.Services.Session
{
    public sealed class JsonGameSessionStore : IGameSessionStore
    {
        private readonly string _path;
        private static readonly JsonSerializerOptions _jsonOptions = new()
        {
            WriteIndented = false,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true
        };

        public JsonGameSessionStore()
        {
            _path = Path.Combine(FileSystem.AppDataDirectory, "lettriis_session.json");
        }

        public Task<bool> HasSessionAsync()
            => Task.FromResult(File.Exists(_path));

        public async Task SaveAsync(GameSessionDto dto, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();

            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);

            var json = JsonSerializer.Serialize(dto, _jsonOptions);

            var tmp = _path + ".tmp";
            await File.WriteAllTextAsync(tmp, json, ct).ConfigureAwait(false);

            ct.ThrowIfCancellationRequested();

            try
            {
                if (File.Exists(_path))
                    File.Replace(tmp, _path, null);
                else
                    File.Move(tmp, _path);
            }
            finally
            {
                // In case Replace/Move failed, attempt cleanup
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
            }
        }


        public async Task<GameSessionDto?> LoadAsync(CancellationToken ct = default)
        {
            if (!File.Exists(_path)) return null;

            ct.ThrowIfCancellationRequested();

            try
            {
                var json = await File.ReadAllTextAsync(_path, ct).ConfigureAwait(false);

                ct.ThrowIfCancellationRequested();

                return JsonSerializer.Deserialize<GameSessionDto>(json, _jsonOptions);
            }
            catch
            {
                // Corrupt or incompatible session: treat as no session
                return null;
            }
        }

        public Task ClearAsync(CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();

            if (File.Exists(_path))
            {
                try { File.Delete(_path); } catch { /* ignore */ }
            }
            return Task.CompletedTask;
        }
    }
}