using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace CanTestSqlite
{
    public class MultimediaTimer : IDisposable
    {
        [DllImport("winmm.dll", SetLastError = true)]
        private static extern uint timeSetEvent(int msDelay, int msResolution,
            TimerCallback callback, IntPtr userCtx, int eventType);

        [DllImport("winmm.dll", SetLastError = true)]
        private static extern uint timeKillEvent(uint timerId);

        private delegate void TimerCallback(uint uTimerID, uint uMsg, UIntPtr dwUser, UIntPtr dw1, UIntPtr dw2);

        private uint timerId = 0;
        private readonly TimerCallback _callback;
        private bool _disposed = false;

        public event EventHandler? Tick;

        public int Period { get; set; } = 1; // ms

        public MultimediaTimer()
        {
            _callback = TimerProc;
        }

        public void Start()
        {
            if (timerId != 0) return;
            timerId = timeSetEvent(Period, 0, _callback, IntPtr.Zero, 1); // 1 = TIME_PERIODIC
        }

        public void Stop()
        {
            if (timerId != 0)
            {
                timeKillEvent(timerId);
                timerId = 0;
            }
        }

        private void TimerProc(uint uTimerID, uint uMsg, UIntPtr dwUser, UIntPtr dw1, UIntPtr dw2)
        {
            Tick?.Invoke(this, EventArgs.Empty);
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                Stop();
                _disposed = true;
            }
        }
    }
}
