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

    // ── Dynamic Drain 參數 ───────────────────────────────
    /// <summary>正常每 Tick 最多處理數量</summary>
    private const int NormalBatchSize = 10;
    /// <summary>Queue 超過此數量時啟動 Drain 模式</summary>
    private const int DrainThreshold = 20;
    /// <summary>Drain 模式單次最多處理上限（防 UI thread 被反壓）</summary>
    private const int MaxDrainBatchSize = 40;
    /// <summary>連續幾個 Tick queue 仍超過閾值才記 Warning</summary>
    private const int HighQueueWarningTicks = 3;

    private int _highQueueTickCount = 0;

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
        int queueLength = _queue.Count;

        // Dynamic Drain：queue 積壓時自動提高單次處理量
        int batchSize = queueLength > DrainThreshold
            ? Math.Min(queueLength, MaxDrainBatchSize)
            : NormalBatchSize;

        // 長時間高 queue 警告（不丟棄事件，僅記錄）
        if (queueLength > DrainThreshold)
        {
            _highQueueTickCount++;
            if (_highQueueTickCount >= HighQueueWarningTicks)
            {
                AppLogger.Error($"KeyEventQueue 持續積壓：queue={queueLength}，已連續 {_highQueueTickCount} Tick 超過閾值");
                _highQueueTickCount = 0; // 重置，避免重複 spam
            }
        }
        else
        {
            _highQueueTickCount = 0;
        }

        int processed = 0;
        while (processed < batchSize && _queue.TryDequeue(out var data))
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
