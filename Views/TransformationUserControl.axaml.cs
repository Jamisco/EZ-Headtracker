using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using EZ_HeadTracker.ViewModels;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using static EZ_HeadTracker.Hardware.HeadTracker;

namespace EZ_HeadTracker.Views
{
    public partial class TransformationUserControl : UserControl
    {
        public Window MainWndow
        {
            get => (Window)this.GetVisualRoot()!;
        }
        public DockPanel TransformationPanel
        {
            get => MainWndow.FindControl<DockPanel>("TransformationPanel")!;
        }

        private Task headTrackerData;

        private bool InvertValue => InvertBox.IsChecked == true;
        private bool ShowPitch => PitchBox.IsChecked == true;
        private bool ShowYaw => YawBox.IsChecked == true;
        private bool ShowRoll => RollBox.IsChecked == true;
        private bool ShowX => XBox.IsChecked == true;
        private bool ShowY => YBox.IsChecked == true;
        private bool ShowZ => ZBox.IsChecked == true;



        public TransformationUserControl()
        {
            InitializeComponent();
            DataContext = new TransformationViewModel();

            if (!Design.IsDesignMode)
            {
                Loaded += TransformationUserControl_Loaded;
                SizeChanged += TransformationUserControl_SizeChanged;
                GraphCanvas.PointerWheelChanged += GraphCanvas_PointerWheelChanged;

                TransformListBox.SelectionChanged += TransformListBox_SelectionChanged;
            }

            //multiplierSlider.ValueChanged += MultiplierSlider_ValueChanged;
            //multiplierTxtBox.LostFocus += MultiplierTxtBox_LostFocus;
            //multiplierTxtBox.TextChanged += MultiplierTxtBox_TextChanged;

            // instead of having different tabs for transforms
            // uise one tab with one graph, will colors circles

        }

        private void TransformListBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            PitchBox.IsChecked  =  !PitchBox.IsChecked;

            if (sender is ListBox listBox && listBox.SelectedItem is ListBoxItem selectedItem)
            {
                string selected = selectedItem.Content.ToString();
                ExpanderTxtBlock.Text = selected;
                Expander.IsExpanded = false;
            }1
        }

        public async Task GetCurrentTransformationData()
        {
            // Start a background task that runs continuously
            //await Task.Run(async () =>
            //{
            //    while (MainWindow.headTracker != null)
            //    {
            //        // Get the current transformation data from head tracker
            //        TransformationData data = MainWindow.headTracker.currentData;
            //        data.Round(2);

            //        // Use InvokeAsync to ensure UI updates are done on the UI thread
            //        await Dispatcher.UIThread.InvokeAsync(() =>
            //        {
            //            // Update the UI controls with the transformation data
            //            Rotation.Text = $"{data.Pitch}, {data.Yaw}, {data.Roll}";
            //            Translation.Text = $"{data.X}, {data.Y}, {data.Z}";

            //            DrawCircleOnGraph(data.X);

            //        });

            //        // Wait for a short period before fetching the data again
            //        await Task.Delay(1); // Adjust the delay as needed
            //    }
            //});
        }

        double zoomLevel = 1f;
        private void GraphCanvas_PointerWheelChanged(object? sender, Avalonia.Input.PointerWheelEventArgs e)
        {
            if (e.Delta.Y > 0)
            {
                zoomLevel += .2f;
            }
            else if (e.Delta.Y < 0)
            {
                zoomLevel -= .2f;
            }

            if(zoomLevel < .5f)
            {
                zoomLevel = .5f;
            }
            else if (zoomLevel > 5)
            {
                zoomLevel = 5f;
            }

            DrawGraph();
        }
        private void TransformationUserControl_SizeChanged(object? sender, SizeChangedEventArgs e)
        {
            DrawGraph();
        }
        private void TransformationUserControl_Loaded(object? sender, RoutedEventArgs e)
        {
            headTrackerData = GetCurrentTransformationData();
        }


        int initSpaceMultiplier = 10;
        int spaceValue = 10;

        private int smallSpacing => (int)(initSpaceMultiplier * zoomLevel);
        private int bigSpacing => smallSpacing * 5;

        public void DrawGraph()
        {
            double curWidth = GraphCanvas.Bounds.Width;
            double curHeight = GraphCanvas.Bounds.Height;

            double cch = canvasParent.Bounds.Height;

            double offsetX = curWidth / 2;
            double offsetY = curHeight / 2;

            Point offset = new Point(offsetX, offsetY);

            // Set the origin of the canvas to the center
            GraphCanvas.RenderTransform = new TranslateTransform(offsetX, offsetY);

            // Create a background rectangle to offset
            Rectangle backgroundRect = new Rectangle
            {
                Width = curWidth,
                Height = curHeight,
                Fill = new SolidColorBrush(Colors.Black)
            };

            Canvas.SetLeft(backgroundRect, -offsetX);
            Canvas.SetTop(backgroundRect, -offsetY);

            double r = 20;

            Ellipse ellipse = new Ellipse
            {
                Width = r,
                Height = r,
                Fill = new SolidColorBrush(Colors.Red),
                Stroke = new SolidColorBrush(Colors.White),
                StrokeThickness = 2
            };

            Canvas.SetLeft(ellipse, -r / 2); // Center the ellipse
            Canvas.SetTop(ellipse, -r / 2);  // Center the ellipse

            Line verticalLine = new Line
            {
                StartPoint = new Point(0, -(curHeight / 2)),
                EndPoint = new Point(0, curHeight / 2),
                Stroke = new SolidColorBrush(Colors.White),
                StrokeThickness = 5
            };

            Line horizontalLine = new Line
            {
                StartPoint = new Point(-(curWidth / 2), 0),
                EndPoint = new Point(curWidth / 2, 0),
                Stroke = new SolidColorBrush(Colors.White),
                StrokeThickness = 5
            };

            // our goal is to create a zoom in and out effect, as user zooms in the graph max sizes decreases, as he zooms out the graph sizes increases
            List<Line> yMarkers = new List<Line>();
            List<Line> xMarkers = new List<Line>();
            List<TextBlock> labels = new List<TextBlock>();

            double weakStroke = .3;
            double strongStroke = 1;

            for (   int i = 0; i < curWidth / 2; i += smallSpacing)
            {
                // draw a line from the top of the graph to the bottom

                double st = weakStroke;

                if (i % bigSpacing == 0)
                {
                    st = strongStroke;
                }
                else
                {
                    continue;
                }

                Line topMarker = new Line
                {
                    StartPoint = new Point(i, -curHeight / 2),
                    EndPoint = new Point(i, curHeight / 2),
                    Stroke = new SolidColorBrush(Colors.White),
                    StrokeThickness = st
                };

                Line bottomMarker = new Line
                {
                    StartPoint = new Point(-i, -curHeight / 2),
                    EndPoint = new Point(-i, curHeight / 2),
                    Stroke = new SolidColorBrush(Colors.White),
                    StrokeThickness = st
                };

                yMarkers.Add(topMarker);
                yMarkers.Add(bottomMarker);

                if (i % bigSpacing == 0 && i != 0)
                {
                    double value = SpaceToDegree(i);

                    TextBlock label = new TextBlock
                    {
                        Text = (value).ToString(),
                        Foreground = new SolidColorBrush(Colors.White),
                        FontSize = 12
                    };

                    Canvas.SetLeft(label, i);
                    Canvas.SetTop(label, 0);
                    labels.Add(label);

                    TextBlock negLabel = new TextBlock
                    {
                        Text = (-value).ToString(),
                        Foreground = new SolidColorBrush(Colors.White),
                        FontSize = 12
                    };

                    Canvas.SetLeft(negLabel, -i);
                    Canvas.SetTop(negLabel, 0);
                    labels.Add(negLabel);
                }
            }

            for (int i = 0; i < curHeight / 2; i += smallSpacing)
            {
                double st = weakStroke;

                if (i % bigSpacing == 0)
                {
                    st = strongStroke;
                }
                else
                {
                    continue;
                }

                // draw a line from the left of the graph to the right
                Line rightMarker = new Line
                {
                    StartPoint = new Point(-curWidth / 2, i),
                    EndPoint = new Point(curWidth / 2, i),
                    Stroke = new SolidColorBrush(Colors.White),
                    StrokeThickness = st
                };
                Line leftMarker = new Line
                {
                    StartPoint = new Point(-curWidth / 2, -i),
                    EndPoint = new Point(curWidth / 2, -i),
                    Stroke = new SolidColorBrush(Colors.White),
                    StrokeThickness = st
                };

                xMarkers.Add(rightMarker);
                xMarkers.Add(leftMarker);

                if (i % bigSpacing == 0 && i != 0)
                {
                    double value = SpaceToDegree(i);

                    TextBlock negLabel = new TextBlock
                    {
                        Text = (-value).ToString(),
                        Foreground = new SolidColorBrush(Colors.White),
                        FontSize = 12
                    };

                    Canvas.SetLeft(negLabel, 5);
                    Canvas.SetTop(negLabel, i - 0);
                    labels.Add(negLabel);

                    TextBlock posLabel = new TextBlock
                    {
                        Text = (value).ToString(),
                        Foreground = new SolidColorBrush(Colors.White),
                        FontSize = 12
                    };

                    Canvas.SetLeft(posLabel, 5);
                    Canvas.SetTop(posLabel, -i - 0);
                    labels.Add(posLabel);
                }
            }

            GraphCanvas.Children.Clear();

            GraphCanvas.Children.Add(ellipse);
            GraphCanvas.Children.Add(backgroundRect);
            GraphCanvas.Children.Add(verticalLine);
            GraphCanvas.Children.Add(horizontalLine);
            GraphCanvas.Children.AddRange(xMarkers);
            GraphCanvas.Children.AddRange(yMarkers);
            GraphCanvas.Children.AddRange(labels);
        }

        public void DrawCircleOnGraph(double pos)
        {
            double r = 20;

            Ellipse circle = new Ellipse
            {
                Width = r,
                Height = r,
                Fill = new SolidColorBrush(Colors.Red),
                Stroke = new SolidColorBrush(Colors.White),
                StrokeThickness = 2
            };

            double x = DegreeToSpace(pos);

            double y = DegreeToSpace(pos);

            Canvas.SetLeft(circle, x - r / 2);
            Canvas.SetTop(circle, -r / 2);
            // find and remove previous ellipse

            foreach (var child in GraphCanvas.Children)
            {
                if (child is Ellipse && child != circle)
                {
                    GraphCanvas.Children.Remove(child);
                    break;
                }
            }

            GraphCanvas.Children.Add(circle);
        }
        double SpaceToDegree(double canvasPos)
        {
            // given a canvas position, convert it to a space value

            return (canvasPos / bigSpacing) * spaceValue;
        }
        double DegreeToSpace(double canvasPos)
        {
            // given a canvas position, convert it to a space value

            return (canvasPos / spaceValue) * bigSpacing;
        }

        public struct TransformationSaveData
        {
            public static string saveDir = System.IO.Path.Combine(AppContext.BaseDirectory, "SavedData");
            public double Multiplier { get; set; }
            public bool Invert { get; set; }
        }
    }
}

