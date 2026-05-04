using System.Collections.Concurrent;
using WorkTimeTracker.Shared.Dtos;

namespace WorkTimeTracker.Agent.Services;

public interface IEventQueue
{
    void Enqueue(ActivityEventDto evt);
    IReadOnlyList<ActivityEventDto> DrainAll(int maxItems = 500);
    int Count { get; }
}

public class EventQueue : IEventQueue
{
    private readonly ConcurrentQueue<ActivityEventDto> _queue = new();

    public int Count => _queue.Count;

    public void Enqueue(ActivityEventDto evt) => _queue.Enqueue(evt);

    public IReadOnlyList<ActivityEventDto> DrainAll(int maxItems = 500)
    {
        var taken = new List<ActivityEventDto>(Math.Min(maxItems, _queue.Count));
        while (taken.Count < maxItems && _queue.TryDequeue(out var evt))
        {
            taken.Add(evt);
        }
        return taken;
    }
}
