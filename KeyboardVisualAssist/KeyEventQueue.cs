using System.Collections.Concurrent;
using System.Windows.Threading;
using KeyboardVisualAssist.InputCapture;
using KeyboardVisualAssist.Logging;

namespace KeyboardVisualAssist;

/// <summary>
/// 緩衝 Hook 事件，避免 Hook callback 阻塞
/// 使用 DispatcherTimer 在 UI thread 定期消化 queue
/// </summary>
public class KeyEventQueue : IDisposable
{
    private readonly ConcurrentQueue<KeyEventData> _queue = new();
    private readonly DispatcherTimer _timer;
    private readonly Action<KeyEventData> _processor;
    private const int MaxProcessPerTick = 10; // 每次最多處理 10 個事件，防止積壓

    public event Action<KeyEventData>? EventProcessed;

    public KeyEventQueue(Action<KeyEventData> processor, int intervalMs = 16)
    {
        _processor = processor;

        _timer = new DispatcherTimer(DispatcherPriority.Input)
        {
            Interval = TimeSpan.FromMilliseconds(intervalMs)
        };
        _timer.Tick += OnTimerTick;
        _timer.Start();
    }

    /// <summary>由 Hook callback 呼叫，立即返回不阻塞</summary>
    public void Enqueue(KeyEventData data)
    {
        _queue.Enqueue(data);
    }

    private void OnTimerTick(object? sender, EventArgs e)
    {
        int processed = 0;
        while (processed < MaxProcessPerTick && _queue.TryDequeue(out var data))
        {
            try
            {
                _processor(data);
                EventProcessed?.Invoke(data);
            }
            catch (Exception ex)
            {
                AppLogger.Error("處理按鍵事件失敗", ex);
            }
            processed++;
        }
    }

    public void Dispose()
    {
        _timer.Stop();
    }
}
