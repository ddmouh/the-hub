using System.Collections.Concurrent;

namespace TheHub.Core.Diagnostics;

public readonly record struct BreadcrumbEntry(DateTime Timestamp, string Message);

public static class Breadcrumbs
{
    private const int MaxEntries = 30;
    private static readonly ConcurrentQueue<BreadcrumbEntry> Queue = new();

    public static void Add(string message)
    {
        Queue.Enqueue(new BreadcrumbEntry(DateTime.UtcNow, message));
        while (Queue.Count > MaxEntries && Queue.TryDequeue(out _)) { }
    }

    public static IReadOnlyList<BreadcrumbEntry> GetRecent(int count = 20)
    {
        return Queue.TakeLast(count).ToList();
    }

    public static void Clear()
    {
        Queue.Clear();
    }
}
