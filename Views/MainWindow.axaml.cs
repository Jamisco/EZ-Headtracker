using Avalonia.Controls;
using EZ_HeadTracker.Hardware;
using System;

namespace EZ_HeadTracker.Views
{
    public partial class MainWindow : Window
    {
        public static HeadTracker headTracker;
        public MainWindow()
        {
            InitializeComponent();

            // need this because opencv will take a long time to load without it if u use videocaptureAPI.any or msfm
            //Environment.SetEnvironmentVariable("OPENCV_VIDEOIO_MSMF_ENABLE_HW_TRANSFORMS", "0");

            if (!Design.IsDesignMode)
            {
                //headTracker = new HeadTracker();
            }

            // on close event

            this.Closing += MainWindow_Closing;
        }

        private void MainWindow_Closing(object? sender, WindowClosingEventArgs e)
        {
            headTracker?.StopTracking();
            headTracker?.ReleaseResources();
        }

    }
}