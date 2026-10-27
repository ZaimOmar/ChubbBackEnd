using System.Collections.Concurrent;
using Serilog.Core;
using Serilog.Events;

namespace Chubb.TestSupport;

public sealed class CapturingLogSink : ILogEventSink
{
    private readonly ConcurrentQueue<string> _entries = new();

    public IReadOnlyCollection<string> Entries => _entries.ToArray();

    public void Emit(LogEvent logEvent)
    {
        var properties = string.Join(' ', logEvent.Properties.Select(pair => $"{pair.Key}={pair.Value}"));
        _entries.Enqueue($"{logEvent.RenderMessage()} {properties} {logEvent.Exception}");
    }
}
