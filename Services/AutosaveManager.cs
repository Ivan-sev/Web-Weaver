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

        /// <summary>Были изменения с последнего сохранения.</summary>
        public static bool IsDirty { get; private set; }

        /// <summary>MainWindow подпишет сюда сохранение карты.</summary>
        public static event Action? SaveRequested;

        public static void Start()
        {
            Stop();
            _changes = 0;

            var a = SettingsManager.Settings.Autosave;
            if (!a.Enabled) return;

            // Таймер создаём ТОЛЬКО если интервал > 0.
            // IntervalSeconds == 0 → ветка «по времени» отключена.
            if (a.IntervalSeconds > 0)
            {
                _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(a.IntervalSeconds) };
                _timer.Tick += (_, _) => SaveRequested?.Invoke();
                _timer.Start();
            }
        }


        public static void Stop() { _timer?.Stop(); _timer = null; }

        public static void RegisterChange()
        {
            IsDirty = true;                    // всегда помним, что карта менялась

            var a = SettingsManager.Settings.Autosave;
            if (!a.Enabled) return;
            if (a.ChangesCount <= 0) return;   // 0 = ветка «по изменениям» отключена

            if (++_changes >= a.ChangesCount)
            {
                _changes = 0;
                SaveRequested?.Invoke();
            }
        }

        /// <summary>После успешного сохранения — сброс.</summary>
        public static void MarkClean() { IsDirty = false; _changes = 0; }
    }
}