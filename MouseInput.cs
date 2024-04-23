using System;
using System.Reactive;
using System.Windows;
using System.Linq;
using System.Diagnostics;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading;
using JuliusSweetland.OptiKey.Contracts;
using System.Runtime.InteropServices;
using System.IO;
using JuliusSweetland.OptiKey.Static;

namespace MouseInput
{

    public class MouseInput : IPointService, IDisposable
    {
        #region Fields
        private event EventHandler<Timestamped<Point>> pointEvent;
        
        // Separate thread for polling the mouse
        private BackgroundWorker pollWorker;

        private Random random = new Random();
        private int noiseScale;

        #endregion

        #region Ctor

        public MouseInput()
        {
            noiseScale = (int)(0.1 * Graphics.PrimaryScreenHeightInPixels);

            pollWorker = new BackgroundWorker();
            pollWorker.DoWork += pollMouse;
            pollWorker.WorkerSupportsCancellation = true;            
        }

        public void Dispose()
        {
            pollWorker.CancelAsync();
            pollWorker.Dispose();
        }

        [DllImport("user32.dll")]
        public static extern bool GetCursorPos(out POINT lpPoint);

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int X;
            public int Y;
        }

        #endregion

        #region Events

        public event EventHandler<Exception> Error;

        public event EventHandler<Timestamped<Point>> Point
        {
            add
            {
                if (pointEvent == null)
                {
                    // Start polling the mouse
                    pollWorker.RunWorkerAsync();
                }

                pointEvent += value;
            }
            remove
            {
                pointEvent -= value;

                if (pointEvent == null)
                {
                    pollWorker.CancelAsync();
                }
            }
        }

        #endregion

        #region Private methods

        private static Mutex mutex = new Mutex();

        [DllImport("Kernel32.dll", CallingConvention = CallingConvention.Winapi)]
        private static extern void GetSystemTimePreciseAsFileTime(out long filetime);

        public static DateTime HighResolutionUtcNow
        {
            get
            {
                try
                {
                    long filetime;
                    GetSystemTimePreciseAsFileTime(out filetime);
                    return DateTime.FromFileTimeUtc(filetime);
                }
                catch (EntryPointNotFoundException)
                {
                    // GetSystemTimePreciseAsFileTime is available from Windows 8+
                    // Fall back to lower resolution alternative
                    return DateTime.UtcNow;
                }
            }
        }

        private void pollMouse(object sender, DoWorkEventArgs e)
        {
            while (!pollWorker.CancellationPending)
            {
                lock (this)
                {
                    // Get latest mouse position
                    var timeStamp = HighResolutionUtcNow.ToUniversalTime();

                    // Gets the absolute mouse position, relative to screen
                    POINT cursorPos;
                    GetCursorPos(out cursorPos);

                    // Report true position + noise
                    pointEvent(this, new Timestamped<Point>(
                        new Point(cursorPos.X + random.Next(-noiseScale, noiseScale),
                                  cursorPos.Y + random.Next(-noiseScale, noiseScale)),
                                   timeStamp));

                    // Sleep thread to avoid hot loop                    
                    int delay = 100; // ms
                    Thread.Sleep(delay);
                }
            }
        }
        #endregion

        #region Publish Error

        private void PublishError(object sender, Exception ex)
        {
            if (Error != null)
            {
                Error(sender, ex);
            }
        }

        #endregion
    }
}
