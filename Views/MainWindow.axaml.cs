using Avalonia.Controls;
using Avalonia.Threading;
using EZ_HeadTracker.Hardware;
using System;
using static EZ_HeadTracker.Hardware.HeadTracker;
using System.Threading.Tasks;
using Avalonia.Media;
using static EZ_HeadTracker.Views.TransformationUserControl;

namespace EZ_HeadTracker.Views
{
    public partial class MainWindow : Window
    {
        public static HeadTracker headTracker;
        
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
            }

            // on close event

            MultiplierSlider.txtLabel.Content = "Multiplier";
            MultiplierSlider.slider.Value = 1;

            SmootherSlider.txtLabel.Content = "Smoothing";
            SmootherSlider.slider.Value = 1;

            this.Closing += MainWindow_Closing;
        }

        private void TransUserControl_PropertyChanged(object? sender, Avalonia.AvaloniaPropertyChangedEventArgs e)
        {
            TransUserControl.ZBox.IsChecked = !TransUserControl.ZBox.IsChecked;

        }

        private void TransformationPanel_PointerReleased(object? sender, Avalonia.Input.PointerReleasedEventArgs e)
        {
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

                rotationLbl.Content = $"{data.Pitch}, {data.Yaw}, {data.Roll}";
                translationLbl.Content = $"{data.X}, {data.Y}, {data.Z}";

                double mul = MultiplierSlider.slider.Value;
                data.Multiply((float)mul);

                //double sData = e.Data.DataArray[TransUserControl.SelectedIndex];
                TransUserControl.AddShapesToDraw(data);
                TransUserControl.DrawShapes();
            });
        }

        private void MainWindow_Loaded(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            //GetCurrentTransformationData();
        }

        private void MainWindow_Closing(object? sender, WindowClosingEventArgs e)
        {
            headTracker?.StopTracking();
            headTracker?.ReleaseResources();
        }
    }
}