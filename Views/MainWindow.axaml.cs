using Avalonia.Controls;
using Avalonia.Threading;
using EZ_HeadTracker.Hardware;
using System;
using static EZ_HeadTracker.Hardware.HeadTracker;
using System.Threading.Tasks;
using Avalonia.Media;
using static EZ_HeadTracker.Views.TransformationUserControl;
using System.Runtime.InteropServices;
using System.Threading;

namespace EZ_HeadTracker.Views
{
    public partial class MainWindow : Window
    {
        public static HeadTracker headTracker;
        public static OpenTrackLauncher openTrackLauncher;
        
        public TransformationData RawData
        {
            get
            {
                if(headTracker != null && headTracker.IsTracking)
                {
                    return headTracker.currentData;
                }
                else
                {
                    return new TransformationData();
                }
            }
        }

 
        public MainWindow()
        {
            InitializeComponent();

            // need this because opencv will take a long time to load without it if u use videocaptureAPI.any or msfm
            //Environment.SetEnvironmentVariable("OPENCV_VIDEOIO_MSMF_ENABLE_HW_TRANSFORMS", "0");

            if (!Design.IsDesignMode)
            {
                headTracker = new HeadTracker();
                headTracker.HeadPoseUpdated += HeadTracker_HeadPoseUpdated;

                Loaded += MainWindow_Loaded;
                CenterHeadBtn.Click += CenterHeadBtn_Click;
                PositionChanged += MainWindow_PositionChanged;
                SizeChanged += MainWindow_SizeChanged;

                openTrackBtn.Click += UdpBtn_ClickAsync;
                OpenTrackLauncher.Begin();
            }

            // on close event

            this.Closing += MainWindow_Closing;
        }

        private void MainWindow_SizeChanged(object? sender, SizeChangedEventArgs e)
        {
            StackWithOpenTrack();
        }

        private async void UdpBtn_ClickAsync(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            // Create and show the UdpSettings window
            var udpSettings = new OpenTrackSettings();
            await udpSettings.ShowDialog(this);

            // After dialog is closed, access the values from the dialog instance
            if (!string.IsNullOrWhiteSpace(udpSettings.IPAddress) && udpSettings.Port > 0)
            {
                DataBridge.SetUDPSettings(udpSettings.Port, udpSettings.IPAddress);
                OpenTrackLauncher.openTrackDir = udpSettings.OpenTrackFolderPath;
            }
        }

        private void MainWindow_PositionChanged(object? sender, PixelPointEventArgs e)
        {
            StackWithOpenTrack();
        }

        private const int SWP_NOZORDER = 0x0004;
        private const int SWP_NOACTIVATE = 0x0010;
        private const int SWP_NOSIZE = 0x0001;

        [DllImport("user32.dll")]
        static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
            int X, int Y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll")]
        static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left, Top, Right, Bottom;
        }

        private void StackWithOpenTrack()
        {
            var otProcess = OpenTrackLauncher.OpenTrackProcess;

            if (otProcess == null) return;

            IntPtr openTrackHwnd = otProcess.MainWindowHandle;

            IntPtr myAppHwnd = GetWindowHandle(this);

            RestoreOpenTrackIfMinimized(openTrackHwnd);

            // Get your app's position on screen
            GetWindowRect(myAppHwnd, out var myRect);
            GetWindowRect(openTrackHwnd, out var otRect);

            int otWidth = otRect.Right - otRect.Left;
            int otHeight = otRect.Bottom - otRect.Top;
            int offsetX = 10; // Optional spacing between windows

            // Move OpenTrack to the right of your app, keeping its size
            SetWindowPos(openTrackHwnd, IntPtr.Zero,
                myRect.Right + offsetX, myRect.Top, 0, 0,
                SWP_NOZORDER | SWP_NOACTIVATE | SWP_NOSIZE);

            // Keep it docked on movement
            this.PositionChanged += (_, __) =>
            {
                GetWindowRect(myAppHwnd, out myRect);

                SetWindowPos(openTrackHwnd, IntPtr.Zero,
                    myRect.Right + offsetX, myRect.Top, 0, 0,
                    SWP_NOZORDER | SWP_NOACTIVATE | SWP_NOSIZE);
            };
        }

        const int SW_RESTORE = 9;

        [DllImport("user32.dll")]
        static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        void RestoreOpenTrackIfMinimized(IntPtr openTrackHwnd)
        {
            ShowWindow(openTrackHwnd, SW_RESTORE);
        }

        private IntPtr GetWindowHandle(Window window)
        {
            if (OperatingSystem.IsWindows())
            {
                var platformHandle = window.TryGetPlatformHandle();
                return platformHandle?.Handle ?? IntPtr.Zero;
            }
            return IntPtr.Zero;
        }


        private void CenterHeadBtn_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            if(headTracker != null && headTracker.IsTracking)
            {
                headTracker.CenterFrame();
            }
        }

        private void HeadTracker_HeadPoseUpdated(object? sender, HeadPoseEventArgs e)
        {
            // Use Dispatcher.UIThread.Invoke instead of Dispatcher.Invoke
            Dispatcher.UIThread.Invoke(() =>
            {
                TransformationData data = e.Data;

                data.Round(2);

                //rotationLbl.Content = $"{data.Pitch}, {data.Yaw}, {data.Roll}";
                //translationLbl.Content = $"{data.X}, {data.Y}, {data.Z}";

                string pose = $"Rot: {data.Pitch}, {data.Yaw}, {data.Roll}" + "\n"
                            + "Trans: " + $"{data.X}, {data.Y}, {data.Z}";

                HeadPosLabel.Content = pose;


                //double sData = e.Data.DataArray[TransUserControl.SelectedIndex];
                TransUserControl.AddShapesToDraw(data);
                TransUserControl.DrawShapes();

                DataBridge.SendData2OpenTrack(data);
            });
        }

        private void MainWindow_Loaded(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {

        }

        private void MainWindow_Closing(object? sender, WindowClosingEventArgs e)
        {
            headTracker?.StopTracking();
            headTracker?.ReleaseResources();
        }
    }
}