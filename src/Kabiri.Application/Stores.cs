using Kabiri.Domain;

namespace Kabiri.Application;

public interface IReadingStore
{
    /// <returns>true if inserted, false if duplicate key already exists (first-wins).</returns>
    bool TryAdd(Reading reading);
    IReadOnlyList<Reading> All();
    int Count { get; }
}

public interface IResultStore
{
    /// <returns>true if inserted, false if same reading key already classified (idempotent).</returns>
    bool TryAdd(Classification classification);
    IReadOnlyList<Classification> All();
    IReadOnlyList<Classification> Acceptable();
}

public interface IAlertStore
{
    bool TryAdd(Alert alert);
    IReadOnlyList<Alert> All();
}

public sealed class InMemoryReadingStore : IReadingStore
{
    private readonly Dictionary<string, Reading> _map = new();
    public bool TryAdd(Reading r) { if (_map.ContainsKey(r.Key)) return false; _map[r.Key] = r; return true; }
    public IReadOnlyList<Reading> All() => _map.Values.ToList();
    public int Count => _map.Count;
}

public sealed class InMemoryResultStore : IResultStore
{
    private readonly Dictionary<string, Classification> _map = new();
    public bool TryAdd(Classification c) { if (_map.ContainsKey(c.Reading.Key)) return false; _map[c.Reading.Key] = c; return true; }
    public IReadOnlyList<Classification> All() => _map.Values.ToList();
    public IReadOnlyList<Classification> Acceptable() => _map.Values.Where(c => c.IsAcceptable).ToList();
}

public sealed class InMemoryAlertStore : IAlertStore
{
    private readonly Dictionary<string, Alert> _map = new();
    public bool TryAdd(Alert a) { if (_map.ContainsKey(a.Key)) return false; _map[a.Key] = a; return true; }
    public IReadOnlyList<Alert> All() => _map.Values.OrderBy(a => a.StartTs).ToList();
}
