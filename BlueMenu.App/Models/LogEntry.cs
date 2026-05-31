namespace BlueMenu.App.Models;

public sealed class LogEntry
{
    public required DateTime Timestamp { get; init; }

    public required LogLevel Level { get; init; }

    public required string Message { get; init; }

    public string Display => $"[{Timestamp:HH:mm:ss}] {Message}";
}
