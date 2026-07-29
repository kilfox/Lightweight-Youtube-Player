using System.Text.Json.Serialization;
using YtMusicTerminal.Configuration;
using YtMusicTerminal.Models;

namespace YtMusicTerminal.Serialization;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true)]
[JsonSerializable(typeof(AppSettings))]
[JsonSerializable(typeof(List<HistoryEntry>))]
[JsonSerializable(typeof(LibraryState))]
internal sealed partial class AppJsonContext : JsonSerializerContext;
