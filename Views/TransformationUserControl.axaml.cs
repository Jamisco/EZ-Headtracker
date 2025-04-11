using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using EZ_HeadTracker.ViewModels;
using OpenCvSharp.XPhoto;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
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

        public Dictionary<TransformationType, TransformationSaveData> TransformationSettings = new();
        private bool InvertValue => InvertBox.IsChecked == true;
        private bool ShowPitch => PitchBox.IsChecked == true;
        private bool ShowYaw => YawBox.IsChecked == true;
        private bool ShowRoll => RollBox.IsChecked == true;
        private bool ShowX => XBox.IsChecked == true;
        private bool ShowY => YBox.IsChecked == true;
        private bool ShowZ => ZBox.IsChecked == true;

        /// <summary>
        /// Convienient array to check which shapes to show
        /// </summary>
        private bool[] CanShowShape =>
        [
            ShowPitch,
            ShowYaw,
            ShowRoll,
            ShowX,
            ShowY,
            ShowZ
        ];

        private Color[] TransformColors { get; } = new Color[]
        {
           Colors.Red,
           Colors.Green,
           Colors.Blue,
           Colors.Red,
           Colors.Green,
           Colors.Blue,
        };


        public TransformationType SelectedType => (TransformationType)TransformListBox.SelectedIndex;
        public int SelectedIndex => TransformListBox.SelectedIndex;

        TransformationSaveData tsd = new TransformationSaveData();

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
                TransformListBox.SelectedIndex = 0; // Set default selection to the first item

                topSlider.slider.ValueChanged += Control_Changed;
                botSlider.slider.ValueChanged += Control_Changed;
                InvertBox.IsCheckedChanged += Control_Changed;

                foreach (Control c in CheckboxPanel.Children.Where(e => e is Control))
                {
                    if (c is CheckBox)
                    {
                        (c as CheckBox).IsCheckedChanged += Control_Changed;
                    }
                    else if (c is Slider)
                    {
                        (c as Slider).ValueChanged += Control_Changed;
                    }
                }
            }

            topSlider.txtLabel.Content = "Multiplier";
            botSlider.txtLabel.Content = "Deadzone";
        }

        private DispatcherTimer _debounceTimer;
        private void Control_Changed(object? sender, RoutedEventArgs e)
        {
            // essentially this prevents repeated calls to save the settings if the user is still changing the settings... for example while the user is moving the slider
            if (_debounceTimer == null)
            {
                _debounceTimer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(50) // adjust as needed
                };

                _debounceTimer.Tick += (s, args) =>
                {
                    _debounceTimer.Stop();


                    TransformationType cur = SelectedType;
                    tsd.AddSettings(cur, GetCurrentSettings());
                    tsd.UpdateShapes2Show(CanShowShape);
                    tsd.SaveSettings();
                };
            }

            // Restart timer every time a change comes in
            _debounceTimer.Stop();
            _debounceTimer.Start();
        }

        private void TransformListBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            PitchBox.IsChecked  =  !PitchBox.IsChecked;

            if (sender is ListBox listBox && listBox.SelectedItem is ListBoxItem selectedItem)
            {
                string selected = selectedItem.Content.ToString();
                ExpanderTxtBlock.Text = selected;
                Expander.IsExpanded = false;

                if(tsd.CurTSettings != null)
                {
                    TSettings settings;

                    if(!tsd.CurTSettings.TryGetValue(SelectedType, out settings))
                    {
                        // if we dont have the settings for the selected type, we load the default settings
                        settings = TSettings.DefaultSettings();

                        tsd.AddSettings(SelectedType, settings);
                    }

                    topSlider.slider.Value = settings.Multiplier;
                    botSlider.slider.Value = settings.Deadzone;
                    InvertBox.IsChecked = settings.Invert;
                }
            }
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
            tsd.LoadSettings();
            InitLoadSettings();
        }

        private void InitLoadSettings()
        {
            int i = 0;

            foreach(CheckBox c in CheckboxPanel.Children.Where(x => x is CheckBox))
            {
                c.IsChecked = tsd.Shapes2Show[i++];
            }

            // we load Pitch because Pitch will always be displayed FIRST!
            if (tsd.CurTSettings != null)
            {
                TSettings settings = tsd.CurTSettings[TransformationType.Pitch];

                topSlider.slider.Value = settings.Multiplier;
                botSlider.slider.Value = settings.Deadzone;
                InvertBox.IsChecked = settings.Invert;
            }

        }


        int initSpaceMultiplier = 10;
        int spaceValue = 10;

        private int smallSpacing => (int)(initSpaceMultiplier * zoomLevel);
        private int bigSpacing => smallSpacing * 5;

        private Point GraphCenterPoint
        {
            get
            {
                double curWidth = GraphCanvas.Bounds.Width;
                double curHeight = GraphCanvas.Bounds.Height;

                double cch = canvasParent.Bounds.Height;

                double offsetX = curWidth / 2;
                double offsetY = curHeight / 2;

                return new Point(offsetX, offsetY);
            }
        }

        public void DrawGraph()
        {
            double curWidth = GraphCanvas.Bounds.Width;
            double curHeight = GraphCanvas.Bounds.Height;

            Point offset = GraphCenterPoint;

            // Set the origin of the canvas to the center
            GraphCanvas.RenderTransform = new TranslateTransform(offset.X, offset.Y);

            // Create a background rectangle to offset
            Rectangle backgroundRect = new Rectangle
            {
                Width = curWidth,
                Height = curHeight,
                Fill = new SolidColorBrush(Colors.Black)
            };

            Canvas.SetLeft(backgroundRect, -offset.X);
            Canvas.SetTop(backgroundRect, -offset.Y);

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

        public enum GraphAxis { XAxis, YAxis, AutoSelect }

        /// <summary>
        /// Adds a shape to the graph canvas to be drawn. If the shape already exists, it will be replaced.
        /// </summary>
        /// <param name="type"></param>
        /// <param name="degree"></param>
        public void AddShapesToDraw(TransformationData data)
        {
            for(int i = 0; i < 6; i++)
            {
                TransformationType type = (TransformationType)i;
                double degree = data.DataArray[i];
                double deadzone = tsd.CurTSettings[type].Deadzone;

                degree = ApplyCalculation(type, degree);

                Shape newDegShape = CreateAngleShape(type, degree, GraphAxis.AutoSelect);
                Shape newDeadShape = CreateDeadzoneShape(type, deadzone, GraphAxis.AutoSelect);


                if (ShapesToDraw.ContainsKey(type))
                {
                    GraphCanvas.Children.Remove(ShapesToDraw[type].Item1);
                    GraphCanvas.Children.Remove(ShapesToDraw[type].Item2);

                    ShapesToDraw[type] = (newDegShape, newDeadShape);
                }
                else
                {
                    ShapesToDraw.Add(type, (newDegShape, newDeadShape));
                }
            }
        }

        private Dictionary<TransformationType, (Shape, Shape)> ShapesToDraw = new();
        public void DrawShapes()
        {
            foreach (var shape in ShapesToDraw)
            {
                bool canShow = CanShowShape[(int)shape.Key];

                if(canShow)
                {
                    if (shape.Value.Item1 != null)
                    {
                        GraphCanvas.Children.Add(shape.Value.Item1);
                    }
                    if (shape.Value.Item2 != null)
                    {
                        GraphCanvas.Children.Add(shape.Value.Item2);
                    }

                }
            }
        }

        double shapeRadius = 20;

        private Shape CreateAngleShape(TransformationType type, double degree, GraphAxis axis = GraphAxis.AutoSelect)
        {
            Shape shape;

            Color color = TransformColors[(int)type];

            switch (type)
            {
                case TransformationType.Pitch:
                case TransformationType.Yaw:
                case TransformationType.Roll:
                    shape = new Ellipse
                    {
                        Width = shapeRadius,
                        Height = shapeRadius,
                        Fill = new SolidColorBrush(color),
                        Stroke = new SolidColorBrush(Colors.White),
                        StrokeThickness = 2
                    };
                    break;
                case TransformationType.X:
                case TransformationType.Y:
                case TransformationType.Z:
                    shape = new Rectangle
                    {
                        Width = shapeRadius,
                        Height = shapeRadius,
                        Fill = new SolidColorBrush(color),
                        Stroke = new SolidColorBrush(Colors.White),
                        StrokeThickness = 2
                    };
                    break;
                default:
                    return null;// this should never run
            }

            double pos = DegreeToSpace(degree);

            if(!WithinGraph(pos))
            {
                return null;
            }

            if(axis == GraphAxis.AutoSelect)
            {
                axis = DrawAxis(type);
            }

            if (axis == GraphAxis.XAxis)
            {
                Canvas.SetLeft(shape, pos - shapeRadius / 2);
                Canvas.SetTop(shape, -shapeRadius / 2);
            }
            else
            {
                Canvas.SetTop(shape, pos - shapeRadius / 2);
                Canvas.SetLeft(shape, -shapeRadius / 2);
            }

            return shape;

        }

        private Shape CreateDeadzoneShape(TransformationType type, double degree, GraphAxis axis = GraphAxis.AutoSelect)
        {
            Shape shape;

            Color color = TransformColors[(int)type];

            double d2p = DegreeToSpace(degree) * 2;

            double shapeRadius = d2p;


            switch (type)
            {
                case TransformationType.Pitch:
                case TransformationType.Yaw:
                case TransformationType.Roll:
                    shape = new Ellipse
                    {
                        Width = shapeRadius,
                        Height = shapeRadius,
                        Fill = new SolidColorBrush(color),
                        Stroke = new SolidColorBrush(Colors.White),
                        StrokeThickness = 2
                    };
                    break;
                case TransformationType.X:
                case TransformationType.Y:
                case TransformationType.Z:
                    shape = new Rectangle
                    {
                        Width = shapeRadius,
                        Height = shapeRadius,
                        Fill = new SolidColorBrush(color),
                        Stroke = new SolidColorBrush(Colors.White),
                        StrokeThickness = 2
                    };
                    break;
                default:
                    return null;// this should never run
            }

            double pos = DegreeToSpace(degree);

            if (!WithinGraph(pos))
            {
                return null;
            }

            if (axis == GraphAxis.AutoSelect)
            {
                axis = DrawAxis(type);
            }

            if (axis == GraphAxis.XAxis)
            {
                shape.Width = d2p;
                shape.Height = shapeRadius;

                Canvas.SetLeft(shape, -shapeRadius / 2);
                Canvas.SetTop(shape, -shapeRadius / 2);
            }
            else
            {
                shape.Width = shapeRadius;
                shape.Height = d2p;

                Canvas.SetTop(shape, -shapeRadius / 2);
                Canvas.SetLeft(shape, -shapeRadius / 2);
            }

            return shape;

        }

        private bool WithinGraph(double graphSpace)
        {
            double curWidth = GraphCanvas.Bounds.Width;
            double curHeight = GraphCanvas.Bounds.Height;
            if (graphSpace > curWidth / 2 || graphSpace < -curWidth / 2)
            {
                return false;
            }
            if (graphSpace > curHeight / 2 || graphSpace < -curHeight / 2)
            {
                return false;
            }
            return true;
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

        public GraphAxis DrawAxis(TransformationType type)
        {
            switch (type)
            {
                case TransformationType.Pitch:
                case TransformationType.Y:
                case TransformationType.Z:
                    return GraphAxis.YAxis;
                case TransformationType.Roll:
                case TransformationType.X:
                case TransformationType.Yaw:
                    return GraphAxis.XAxis;
                default:
                    return GraphAxis.XAxis;
            }
        }
   
        public double ApplyCalculation(TransformationType type, double degrees)
        {
            TSettings settings;

            if (!tsd.CurTSettings.TryGetValue(type, out settings))
            {
                // if we dont have the settings for the selected type, we load the default settings
                settings = TSettings.DefaultSettings();

                tsd.AddSettings(SelectedType, settings);
            }

            double multiplier = settings.Multiplier;
            double deadzone = settings.Deadzone;

            if (settings.Invert)
            {
                degrees = -degrees;
            }

            degrees = degrees * multiplier;

            // apply the deadzone
            if (Math.Abs(degrees) < deadzone)
            {
                degrees = 0;
            }

            return degrees;
        }
        private TSettings GetCurrentSettings()
        {
            TSettings settings = new TSettings
            {
                Invert = InvertValue,
                Multiplier = topSlider.slider.Value,
                Deadzone = botSlider.slider.Value
            };

            return settings;
        }
        public struct TSettings
        {
            public bool Invert { get; set; }
            public double Multiplier { get; set; }
            public double Deadzone { get; set; }

            public override int GetHashCode()
            {
                return HashCode.Combine(Multiplier, Deadzone, Invert);
            }

            public static TSettings DefaultSettings()
            {
                return new TSettings
                {
                    Invert = false,
                    Multiplier = 1,
                    Deadzone = 0
                };
            }
        }

        public class TransformationSaveData
        {
            public static string saveDir = System.IO.Path.Combine(AppContext.BaseDirectory, "SavedData");
            public Dictionary<TransformationType, TSettings> CurTSettings { get; private set; }
            public List<bool> Shapes2Show { get; private set; }

            [JsonConstructor]
            public TransformationSaveData(Dictionary<TransformationType, TSettings> CurTSettings, List<bool> Shapes2Show)
            {
                this.CurTSettings = CurTSettings ?? new Dictionary<TransformationType, TSettings>();
                this.Shapes2Show = Shapes2Show ?? new List<bool>() { true, false, false, false, false, false };
            }

            public TransformationSaveData()
            {
                CurTSettings = new Dictionary<TransformationType, TSettings>();
                Shapes2Show = new List<bool>() { true, false, false, false, false, false };
            }

            public void AddSettings(TransformationType type, TSettings settings)
            {
                if(CurTSettings == null)
                {
                    CurTSettings = new Dictionary<TransformationType, TSettings>();
                }

                if (CurTSettings.ContainsKey(type))
                {
                    CurTSettings[type] = settings;
                }
                else
                {
                    CurTSettings.Add(type, settings);
                }
            }

            public void UpdateShapes2Show(bool[] showShapes)
            {
                this.Shapes2Show = showShapes.ToList();
            }

            public void SaveSettings()
            {
                string filePath = System.IO.Path.Combine(saveDir, "TransformSettings.json");

                if (!System.IO.Directory.Exists(saveDir))
                {
                    System.IO.Directory.CreateDirectory(saveDir);
                }

                string json = System.Text.Json.JsonSerializer.Serialize(this);
                System.IO.File.WriteAllText(filePath, json);
            }

            public void  LoadSettings()
            {
                string filePath = System.IO.Path.Combine(saveDir, "TransformSettings.json");

                try
                {
                    if (System.IO.File.Exists(filePath))
                    {
                        string json = System.IO.File.ReadAllText(filePath);

                        TransformationSaveData settings = System.Text.Json.JsonSerializer.Deserialize<TransformationSaveData>(json);

                        this.CurTSettings = settings.CurTSettings;
                        this.Shapes2Show = settings.Shapes2Show;
                    }
                }
                catch (Exception ex)
                {
                    System.IO.File.WriteAllText(filePath, "");

                    TransformationSaveData settings = new TransformationSaveData();

                    this.CurTSettings = settings.CurTSettings;
                    this.Shapes2Show = settings.Shapes2Show;

                    for(int i = 0; i < Shapes2Show.Count; i++)
                    {
                        CurTSettings.Add((TransformationType)i, TSettings.DefaultSettings());
                    }

                    SaveSettings();
                }

            }
        }
    }
}

