using System;
using System.Windows.Threading;

namespace WebWeaver.Services
{
    /// <summary>Автосохранение по времени и по числу изменений.
    /// Пока Autosave.Enabled = false — ничего не делает.</summary>
    public static class AutosaveManager
    {
        private static DispatcherTimer? _timer;
        private static int _changes;

        /// <summary>MainWindow подпишет сюда сохранение карты.</summary>
        public static event Action? SaveRequested;

        public static void Start()
        {
            Stop();
            var a = SettingsManager.Settings.Autosave;
            if (!a.Enabled || a.IntervalSeconds <= 0) return;

            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(a.IntervalSeconds) };
            _timer.Tick += (_, _) => SaveRequested?.Invoke();
            _timer.Start();
        }

        public static void Stop() { _timer?.Stop(); _timer = null; }

        /// <summary>Вызывать после каждого изменения карты (из PushHistory).</summary>
        public static void RegisterChange()
        {
            var a = SettingsManager.Settings.Autosave;
            if (!a.Enabled) return;
            if (++_changes >= a.ChangesCount) { _changes = 0; SaveRequested?.Invoke(); }
        }
    }
}