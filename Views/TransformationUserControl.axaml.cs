using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using EZ_HeadTracker.ViewModels;
using OpenCvSharp.LineDescriptor;
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

        public List<Point> GraphCurvePoints = new List<Point>();



        public TransformationType SelectedType => (TransformationType)TransformListBox.SelectedIndex;
        public TSettings SelectedSettings => tsd.CurTSettings[SelectedType];
        public GraphAxis SelectedAxis => DrawAxis(SelectedType);
        public int SelectedIndex => TransformListBox.SelectedIndex;

        TransformationSaveData tsd = new TransformationSaveData();
        private bool Instantiated = false;
        public TransformationUserControl()
        {
            InitializeComponent();
            DataContext = new TransformationViewModel();

            graphData = new GraphCanvasData(GraphCanvas);

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

                GraphCanvas.PointerPressed += GraphCanvas_PointerPressed;
                GraphCanvas.PointerMoved += GraphCanvas_PointerMoved;
            }

            topSlider.txtLabel.Content = "Multiplier";
            botSlider.txtLabel.Content = "Deadzone";
            Instantiated = true;
        }

        private Point draggedPointInDeg = default;
        private void GraphCanvas_PointerMoved(object? sender, Avalonia.Input.PointerEventArgs e)
        {
            Point spacePoint = e.GetCurrentPoint(GraphCanvas).Position;

            spacePoint = spacePoint.QuickRound();

            if (e.GetCurrentPoint(GraphCanvas).Properties.IsLeftButtonPressed)
            {
                if (graphData.IsPointerOnPoint(spacePoint, out Point pog) || draggedPointInDeg != default)
                {
                    GraphAxis axis = DrawAxis(SelectedType);
                    TSettings settings = tsd.CurTSettings[SelectedType];

                    // the selected point in degrees
                    Point curDegPoint = graphData.SpaceToDegree(pog);


                    Point destDegPoint = graphData.SpaceToDegree(spacePoint);

                    if (!graphData.WithinActiveCurveZone(spacePoint, axis))
                    {
                        // we are within the active curve zone, so we can update the point
                        // we do this to prevent the user from dragging the point outside of the active curve zone
                        return;
                    }

                    // the reason we do this is because when u update the point, the points new position is the same as the point you updated it to, however this updated point will not be immediately refeflected, thus when u use is pointer over point, it returns false
                    if (draggedPointInDeg != default)
                    {
                        settings.UpdateCurvePoint(draggedPointInDeg, destDegPoint);
                        draggedPointInDeg = destDegPoint;
                    }
                    else
                    {
                        settings.UpdateCurvePoint(curDegPoint, destDegPoint);
                        draggedPointInDeg = destDegPoint;
                    }

                    graphData.SetCurves(settings, axis);
                    graphData.DrawCurves();

                    tsd.SaveSettings();
                }
            }
            else
            {
                draggedPointInDeg = default;
            }
        }

        private void GraphCanvas_PointerPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e)
        {
            Point point = e.GetCurrentPoint(GraphCanvas).Position;
            point = point.QuickRound();

            TSettings settings = tsd.CurTSettings[SelectedType];
            GraphAxis axis = DrawAxis(SelectedType);

            if (e.GetCurrentPoint(GraphCanvas).Properties.IsRightButtonPressed)
            {
                if (graphData.IsPointerOnPoint(point, out Point pog))
                {
                    Point degreePoint = graphData.SpaceToDegree(pog);

                    settings.RemoveCurve(degreePoint);
                    graphData.SetCurves(settings, axis);
                    graphData.DrawCurves();
                    tsd.SaveSettings();

                    return;
                }

                return;
            }

            bool success = graphData.WithinActiveCurveZone(point, axis);
            bool onPoint = graphData.IsPointerOnPoint(point, out Point p);

            if (!success || onPoint)
            {
                return;
            }

            Point deg = graphData.SpaceToDegree(point);
            success = settings.AddCurve(deg);

            if (success)
            {
                graphData.SetCurves(settings, axis);
                graphData.DrawCurves();
                tsd.SaveSettings();

            }
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
                    if (!Instantiated)
                    {
                        // the reason we do this is because the control is not yet instantiated, so any changed to the control will trigger this function. Causing it to save the current default state not the actual settings
                        return;
                    }
                    _debounceTimer.Stop();


                    TransformationType cur = SelectedType;
                    TSettings curSettings = tsd.CurTSettings[cur];

                    UpdateControlSettings(curSettings);
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
            if (sender is ListBox listBox && listBox.SelectedItem is ListBoxItem selectedItem)
            {
                string selected = selectedItem.Content.ToString();
                ExpanderTxtBlock.Text = selected;
                Expander.IsExpanded = false;

                if (tsd.CurTSettings != null)
                {
                    TSettings settings;

                    if (!tsd.CurTSettings.TryGetValue(SelectedType, out settings))
                    {
                        // if we dont have the settings for the selected type, we load the default settings
                        settings = new TSettings();

                        tsd.AddSettings(SelectedType, settings);
                    }

                    topSlider.slider.Value = settings.Multiplier;
                    botSlider.slider.Value = settings.Deadzone;
                    InvertBox.IsChecked = settings.Invert;

                    GraphAxis axis = DrawAxis(SelectedType);

                    graphData.SetCurves(settings, axis);
                    graphData.DrawCurves();
                }
            }
        }

        private GraphCanvasData graphData;

        private void GraphCanvas_PointerWheelChanged(object? sender, Avalonia.Input.PointerWheelEventArgs e)
        {
            if (e.Delta.Y > 0)
            {
                graphData.zoomLevel += .2f;
            }
            else if (e.Delta.Y < 0)
            {
                graphData.zoomLevel -= .2f;
            }

            if (graphData.zoomLevel < .5f)
            {
                graphData.zoomLevel = .5f;
            }
            else if (graphData.zoomLevel > 5)
            {
                graphData.zoomLevel = 5f;
            }

            graphData.DrawGraph();
            graphData.SetCurves(SelectedSettings, SelectedAxis);
            graphData.DrawCurves();
        }
        private void TransformationUserControl_SizeChanged(object? sender, SizeChangedEventArgs e)
        {
            graphData.DrawGraph();
        }
        private void TransformationUserControl_Loaded(object? sender, RoutedEventArgs e)
        {
            tsd.LoadSettings();
            InitLoadSettings();
        }

        private void InitLoadSettings()
        {
            int i = 0;

            foreach (CheckBox c in CheckboxPanel.Children.Where(x => x is CheckBox))
            {
                c.IsChecked = tsd.Shapes2Show[i++];
            }

            // we load Pitch because Pitch will always be displayed FIRST!
            TSettings settings = tsd.CurTSettings[TransformationType.Pitch];

            if (tsd.CurTSettings != null)
            {
                topSlider.slider.Value = settings.Multiplier;
                botSlider.slider.Value = settings.Deadzone;
                InvertBox.IsChecked = settings.Invert;
            }

            GraphAxis axis = DrawAxis(TransformationType.Pitch);

            graphData.SetCurves(settings, axis);
            graphData.DrawCurves();
        }


        public enum GraphAxis { XAxis, YAxis, AutoSelect }

        /// <summary>
        /// Adds a shape to the graph canvas to be drawn. If the shape already exists, it will be replaced.
        /// </summary>
        /// <param name="type"></param>
        /// <param name="degree"></param>
        public void AddShapesToDraw(TransformationData data)
        {
            for (int i = 0; i < 6; i++)
            {
                TransformationType type = (TransformationType)i;
                ShapeStorage shapeStorage = new ShapeStorage();

                TSettings settings = tsd.CurTSettings[type];

                double degree = data.DataArray[i];

                degree = settings.ApplyCalculation(degree);

                Point curvePoint = settings.RemapToCurve(degree, DrawAxis(type));
                curvePoint = graphData.DegreeToSpace(curvePoint);

                shapeStorage.rawShape = CreateAngleShape(type, degree, GraphAxis.AutoSelect);
                shapeStorage.deadZoneShape = CreateDeadzoneShape(type, settings.Deadzone, GraphAxis.AutoSelect);

                shapeStorage.curveShape = CreateCurveShape(type, curvePoint);

                if (ShapesToDraw.ContainsKey(type))
                {
                    ShapeStorage stored = ShapesToDraw[type];

                    GraphCanvas.Children.Remove(stored.rawShape);
                    GraphCanvas.Children.Remove(stored.deadZoneShape);
                    GraphCanvas.Children.Remove(stored.curveShape);


                    ShapesToDraw[type] = shapeStorage;
                }
                else
                {
                    ShapesToDraw.Add(type, shapeStorage);
                }
            }
        }

        // since we can draw multiple transformation types at once, we store them in a dictionary
        // Shape Order is the rawData, Deadzone
        private Dictionary<TransformationType, ShapeStorage> ShapesToDraw = new();
        public void DrawShapes()
        {
            foreach (var shape in ShapesToDraw)
            {
                bool canShow = CanShowShape[(int)shape.Key];

                if (canShow)
                {
                    ShapeStorage stored = shape.Value;

                    if(stored.rawShape != null)
                    {
                        GraphCanvas.Children.Add(stored.rawShape);
                    }

                    if (stored.deadZoneShape != null)
                    {
                        GraphCanvas.Children.Add(stored.deadZoneShape);
                    }

                    if (stored.curveShape != null)
                    {
                        GraphCanvas.Children.Add(stored.curveShape);
                    }
                }
            }
        }

        double shapeRadius = 15;

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

            double pos = graphData.DegreeToSpace(degree);

            if (!graphData.SpaceWithinGraph(pos))
            {
                return null;
            }

            if (axis == GraphAxis.AutoSelect)
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

        private Shape CreateDeadzoneShape(TransformationType type, double deadZone, GraphAxis axis = GraphAxis.AutoSelect)
        {
            Shape shape;

            Color color = TransformColors[(int)type];

            double d2p = graphData.DegreeToSpace(deadZone) * 2;

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

            double pos = graphData.DegreeToSpace(deadZone);

            if (!graphData.SpaceWithinGraph(pos))
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

        private Shape CreateCurveShape(TransformationType type, Point curvePointInSpace)
        {
            Shape shape;

            Color color = Colors.Yellow;

            double shapeRadius = 10;

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

            Canvas.SetLeft(shape, curvePointInSpace.X -shapeRadius / 2);
            Canvas.SetTop(shape, curvePointInSpace.Y -shapeRadius / 2);

            return shape;

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
                settings = new TSettings();

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

        /// <summary>
        /// Will update the control settings with the current values of the controls
        /// Does not affect/modify the graph curves
        /// </summary>
        /// <param name="settings"></param>
        /// <returns></returns>
        private TSettings UpdateControlSettings(TSettings settings)
        {
            settings.Invert = InvertValue;
            settings.Multiplier = topSlider.slider.Value;
            settings.Deadzone = botSlider.slider.Value;

            return settings;
        }
        public class TransformationSaveData
        {
            public static string saveDir = System.IO.Path.Combine(AppContext.BaseDirectory, "SavedData");
            public Dictionary<TransformationType, TSettings> CurTSettings { get; private set; }
            public List<bool> Shapes2Show { get; set; }

            [JsonConstructor]
            public TransformationSaveData(Dictionary<TransformationType, TSettings> CurTSettings, List<bool> Shapes2Show)
            {
                this.CurTSettings = CurTSettings ?? new Dictionary<TransformationType, TSettings>();
                this.Shapes2Show = Shapes2Show ?? new List<bool>() { true, false, false, false, false, false };
            }

            private JsonSerializerOptions jsonOptions = new JsonSerializerOptions();
            public TransformationSaveData()
            {
                CurTSettings = new Dictionary<TransformationType, TSettings>();
                Shapes2Show = new List<bool>() { true, false, false, false, false, false };

                for (int i = 0; i < Shapes2Show.Count; i++)
                {
                    CurTSettings.Add((TransformationType)i, new TSettings());
                }

                jsonOptions.Converters.Add(new PointConverter());
                jsonOptions.WriteIndented = true;
            }
            public void AddSettings(TransformationType type, TSettings settings)
            {
                if (CurTSettings == null)
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

                string json = System.Text.Json.JsonSerializer.Serialize(this, jsonOptions);
                System.IO.File.WriteAllText(filePath, json);
            }

            public void LoadSettings()
            {
                string filePath = System.IO.Path.Combine(saveDir, "TransformSettings.json");

                try
                {
                    if (System.IO.File.Exists(filePath))
                    {
                        string json = System.IO.File.ReadAllText(filePath);

                        TransformationSaveData settings = System.Text.Json.JsonSerializer.Deserialize<TransformationSaveData>(json, jsonOptions);

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

                    SaveSettings();
                }

            }
        }

        /// <summary>
        /// This is a custom converter for the Point struct to be used with System.Text.Json serialization.
        /// </summary>
        public class PointConverter : JsonConverter<Point>
        {
            public override Point Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            {
                var json = JsonDocument.ParseValue(ref reader);
                var x = json.RootElement.GetProperty("X").GetDouble();
                var y = json.RootElement.GetProperty("Y").GetDouble();
                return new Point(x, y);
            }

            public override void Write(Utf8JsonWriter writer, Point value, JsonSerializerOptions options)
            {
                writer.WriteStartObject();
                writer.WriteNumber("X", value.X);
                writer.WriteNumber("Y", value.Y);
                writer.WriteEndObject();
            }
        }


        public class TSettings
        {
            public bool Invert { get; set; } = false;
            public double Multiplier { get; set; } = 1;
            public double Deadzone { get; set; } = 0;

            [JsonInclude]
            public List<Point> CurvesPointInDegrees { get; set; } = new List<Point>();

            // these are the start, and end points(upper and lower), these points will be present always and not modifiable.
            // every time we draw the curve, the graph will calculate and set these points for us.
            public List<Point> EdgeCurvesInDegress  = new List<Point>();


            public static double CurvePointRadius = 15;

            // you have to be sure not to add degrees that are not within this settings zone.
            // for example, a pitch curve values are only from if spacePoint.X > 0
            public bool AddCurve(Point degreePoint)
            {
                if (CurvesPointInDegrees == null)
                {
                    CurvesPointInDegrees = new List<Point>();
                }

                // So as long as the degrees arent thesame, we dont care how close there are, it is up to the graph data to determine how close they want the degrees to be
                if (!CurvesPointInDegrees.Contains(degreePoint))
                {
                    CurvesPointInDegrees.Add(degreePoint);

                    return true;
                }

                return false;
            }

            public bool RemoveCurve(Point degreePoint)
            {
                return CurvesPointInDegrees.Remove(degreePoint);
            }

            public bool UpdateCurvePoint(Point curDegree, Point newDegree)
            {
                if (RemoveCurve(curDegree))
                {
                    // possible error here if duplicates exists, but this shouldnt happen
                    AddCurve(newDegree);
                    return true;
                }

                return false;
            }

            public double ApplyCalculation(double degrees)
            {
                if (Invert)
                {
                    degrees = -degrees;
                }

                degrees = degrees * Multiplier;

                // apply the deadzone
                if (Math.Abs(degrees) < Deadzone)
                {
                    degrees = 0;
                }

                return degrees;
            }

            public Point RemapToCurve(double degree, GraphAxis axis)
            {
                Point upper = new Point();
                Point lower = new Point();
                Point mid = new Point();

                // there will always be a start points at 0,0 and an end point at the end of the graph
                List<Point> points = new List<Point>();

                points.AddRange(EdgeCurvesInDegress);
                points.AddRange(CurvesPointInDegrees);

                // we add the start point to the list of points, 
                // start points is not includedd in the edge curves nor is it an actual point in a settings curve because we do want it to be modified/moved
                points.Add(new Point(0, 0));

                if (axis == GraphAxis.XAxis)
                {
                    points = points.OrderBy(x => x.X).ToList();
                    upper = points.FirstOrDefault(x => x.X > degree);
                    lower = points.LastOrDefault(x => x.X < degree);

                    double t = (degree - lower.X) / (upper.X - lower.X);
                    double remappedY = lower.Y + t * (upper.Y - lower.Y);
                    mid = new Point(degree, remappedY);
                }
                else
                {
                    points = points.OrderBy(x => x.Y).ToList();
                    upper = points.FirstOrDefault(x => x.Y > degree);
                    lower = points.LastOrDefault(x => x.Y < degree);

                    double t = (degree - lower.Y) / (upper.Y - lower.Y);
                    double remappedX = lower.X + t * (upper.X - lower.X);
                    mid = new Point(remappedX, degree);
                }

                return mid;
            }

            private void CalculateEdgePoints()
            {
                Point start = new Point();


            }
        }

        public struct ShapeStorage
        {
            public Shape rawShape;
            public Shape deadZoneShape;
            public Shape curveShape;
            public Shape multiplierShape;
        }

        public struct GraphCanvasData
        {
            public double zoomLevel { get; set; } = 1f;

            int initSpaceMultiplier = 10;
            int spaceValue = 10;

            private int smallSpacing => (int)(initSpaceMultiplier * zoomLevel);
            private int bigSpacing => smallSpacing * 5;

            public bool[] Checked;

            public Canvas GraphCanvas;

            public double GraphWidth => GraphCanvas.Bounds.Width / 2;
            public double GraphHeight => GraphCanvas.Bounds.Height / 2;
            private Point GraphCenterPoint
            {
                get
                {
                    double curWidth = GraphCanvas.Bounds.Width;
                    double curHeight = GraphCanvas.Bounds.Height;

                    double offsetX = curWidth / 2;
                    double offsetY = curHeight / 2;

                    return new Point(offsetX, offsetY);
                }
            }
            private Point StartPointCurve => new Point(0, 0);
            private Point BasicEndPointCurve
            {
                get
                {
                    // check width and height, which ever is smaller convert said space to degree and return

                    double curWidth = GraphCanvas.Bounds.Width / 2;
                    double curHeight = GraphCanvas.Bounds.Height / 2;

                    double min = Math.Min(curWidth, curHeight);

                    min = SpaceToDegree(min);

                    return new Point(min, -min);
                }
            }
            private List<Point> curvePointsInDegrees { get; set; }
            private List<(Shape, Point)> drawnPoints;
            private Polyline upperDrawnLine;
            private Polyline lowerDrawnLine;



            // Whenever any modification is done to the graph, THE ENTIRE GRAPH IS REDRAWN
            // DRAWING THE GRAPH is not a PERFORMANCE INTENSIVE OPERATION

            public GraphCanvasData(Canvas GraphCanvas)
            {
                this.GraphCanvas = GraphCanvas;
                curvePointsInDegrees = new List<Point>();
                drawnPoints = new List<(Shape, Point)>();
            }

            public void DrawGraph()
            {
                double curWidth = GraphCanvas.Bounds.Width;
                double curHeight = GraphCanvas.Bounds.Height;

                Point offset = GraphCenterPoint;

                #region Graph Stuff

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

                for (int i = 0; i < curWidth / 2; i += smallSpacing)
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


                #endregion

                GraphCanvas.Children.Clear();

                GraphCanvas.Children.Add(ellipse);
                GraphCanvas.Children.Add(backgroundRect);
                GraphCanvas.Children.Add(verticalLine);
                GraphCanvas.Children.Add(horizontalLine);
                GraphCanvas.Children.AddRange(xMarkers);
                GraphCanvas.Children.AddRange(yMarkers);
                GraphCanvas.Children.AddRange(labels);
            }

            List<Shape> lines = new List<Shape>();
            public void DrawCurves()
            {
                if (curvePointsInDegrees == null)
                {
                    return;
                }

                GraphCanvasData local = this;

                // these points are used to draw lines that connect the points


                foreach (var dps in drawnPoints)
                {
                    GraphCanvas.Children.Remove(dps.Item1);
                }

                if (upperDrawnLine != null)
                {
                    GraphCanvas.Children.Remove(upperDrawnLine);
                    upperDrawnLine = null;
                }

                if (lowerDrawnLine != null)
                {
                    GraphCanvas.Children.Remove(lowerDrawnLine);
                    lowerDrawnLine = null;
                }

                foreach (Shape line in lines)
                {
                    GraphCanvas.Children.Remove(line);
                }

                drawnPoints.Clear();
                lines.Clear();

                int zeroIndex = curvePointsInDegrees.IndexOf(new Point(0, 0));

                // we divide the curve into two parts, the upper and lower part
                // and draw them seperately, such that each part is drawn from the center (0,0) till the end
                List<Point> upper = curvePointsInDegrees.Take(zeroIndex).ToList();
                upper.Add(new Point(0, 0)); // add the zero point to the upper list
                upper.Reverse();

                List<Point> lower = curvePointsInDegrees.Skip(zeroIndex + 1).ToList();
                lower.Insert(0, new Point(0, 0)); // add the zero point to the lower list
                
                DrawLines(upper, out var upperPoints);
                DrawLines(lower, out var lowPoints);

                Polyline upperCurveLine = new Polyline
                {
                    Stroke = new SolidColorBrush(Colors.White),
                    StrokeThickness = 2,
                    Points = upperPoints
                };

                Polyline lowerCurveLine = new Polyline
                {
                    Stroke = new SolidColorBrush(Colors.White),
                    StrokeThickness = 2,
                    Points = lowPoints
                };

                upperDrawnLine = upperCurveLine;
                lowerDrawnLine = lowerCurveLine;

                GraphCanvas.Children.Add(upperCurveLine);
                GraphCanvas.Children.Add(lowerCurveLine);

                GraphCanvas.Children.AddRange(drawnPoints.Select(x => x.Item1));


                void DrawLines(List<Point> points, out List<Point> linePoints)
                {
                    Point prevSpace = new Point(-2313213, -232323);
                    linePoints = new List<Point>();

                    for (int i = 0; i < points.Count; i++)
                    {
                        Point curDeg = points[i];
                        Point curSpace = local.DegreeToSpace(curDeg);

                        if (local.SpaceWithinGraph(curSpace))
                        {
                            if (i >= points.Count - 1)
                            {
                                break;
                            }

                            Point nextDeg = points[i + 1];
                            Point nextSpace = local.DegreeToSpace(nextDeg);

                            if (local.SpaceWithinGraph(nextSpace))
                            {
                                local.drawnPoints.Add((CreatePoint(curSpace), curSpace));
                                local.drawnPoints.Add((CreatePoint(nextSpace), nextSpace));

                                // since a line requires two points, 
                                if (curSpace != prevSpace)
                                {
                                    linePoints.Add(curSpace);
                                    linePoints.Add(nextSpace);
                                }
                                else
                                {
                                    linePoints.Add(nextSpace);
                                }
                            }
                            else
                            {
                                Line newLine = new Line
                                {
                                    StartPoint = curSpace,
                                    EndPoint = nextSpace,
                                    Stroke = new SolidColorBrush(Colors.White),
                                    StrokeThickness = 2
                                };

                                newLine.Clip = new RectangleGeometry
                                {
                                    Rect = new Rect(-local.GraphWidth, -local.GraphHeight, 
                                                     local.GraphWidth * 2, local.GraphHeight * 2)
                                };

                                local.drawnPoints.Add((CreatePoint(curSpace), curSpace));

                                local.lines.Add(newLine);
                                local.GraphCanvas.Children.Add(newLine);

                                break;
                            }

                            prevSpace = curSpace;
                        }
                    }

                    linePoints = linePoints.Distinct().ToList();
                }

                Shape CreatePoint(Point curSpace)
                {
                    Shape newShape = new Ellipse
                    {
                        Width = TSettings.CurvePointRadius,
                        Height = TSettings.CurvePointRadius,
                        Fill = new SolidColorBrush(Colors.Red),
                        Stroke = new SolidColorBrush(Colors.White),
                        StrokeThickness = 2
                    };

                    Canvas.SetLeft(newShape, curSpace.X - TSettings.CurvePointRadius / 2);
                    Canvas.SetTop(newShape, curSpace.Y - TSettings.CurvePointRadius / 2);

                    return newShape;
                }
            }

            public void SetCurves(TSettings settings, GraphAxis axis)
            {
                // zero ourselves for center, then sort the points
                // this way we can draw the graph in a single pass

                //foreach (var d in pointInDegrees)
                //{
                //    Point dp = DegreeToSpace(d);

                //    curvePointsInDegrees.Add(dp);
                //}
                curvePointsInDegrees.Clear();

                curvePointsInDegrees.AddRange(settings.CurvesPointInDegrees);

                List<Point> endCurves = new List<Point>();

                curvePointsInDegrees.Add(StartPointCurve);
                endCurves.AddRange(GetEndCurves(curvePointsInDegrees, axis));

                curvePointsInDegrees.AddRange(endCurves);

                settings.EdgeCurvesInDegress = endCurves;

                if (axis == GraphAxis.XAxis)
                {
                    curvePointsInDegrees = curvePointsInDegrees.OrderBy(p => p.X).ToList();
                }
                else
                {
                    curvePointsInDegrees = curvePointsInDegrees.OrderBy(p => p.Y).ToList();
                }
            }

            private List<Point> GetEndCurves(List<Point> cid, GraphAxis axis)
            {
                Point upper = default;
                Point lower = default;
                Point basic = BasicEndPointCurve;

                List<Point> endCurves = new List<Point>();

                // what this statement is doing is that it is saying if a particular half of the graph doesnt have any points, set the edge to the top corner, such that the is a 1:1 ratio between the degrees. so if pitch is 10 degrees, its curve will also be 10 degrees.
                // if it does have points, simply make the end point go straight respective of axis
                if (axis == GraphAxis.XAxis)
                {
                    upper = cid.Where(point => point.X > 0)
                               .OrderByDescending(point => point.X)
                               .FirstOrDefault();

                    lower = cid.Where(point => point.X < 0)
                                 .OrderBy(point => point.X)
                                 .FirstOrDefault();

                    if (upper == default)
                    {
                        endCurves.Add(basic);
                    }
                    else
                    {
                        double xDeg = SpaceToDegree(GraphWidth);
                        double yDeg = upper.Y;
                        endCurves.Add(new Point(xDeg, yDeg));
                    }

                    if (lower == default)
                    {
                        basic = new Point(-basic.X, basic.Y);

                        endCurves.Add(basic);
                    }
                    else
                    {
                        double xDeg = SpaceToDegree(-GraphWidth);
                        double yDeg = lower.Y;
                        endCurves.Add(new Point(xDeg, yDeg));
                    }
                }
                else
                {
                    // for y, the direction is flippd negative is up, positive down
                    upper = cid.Where(point => point.Y > 0)
                               .OrderByDescending(point => point.Y)
                               .FirstOrDefault();

                    lower = cid.Where(point => point.Y < 0)
                               .OrderBy(point => point.Y)
                               .FirstOrDefault();

                    if (lower == default)
                    {
                        endCurves.Add(basic);
                    }
                    else
                    {
                        double yDeg = SpaceToDegree(-GraphHeight);
                        double xDeg = lower.X;
                        endCurves.Add(new Point(xDeg, yDeg));
                    }

                    if (upper == default)
                    {
                        basic = new Point(basic.X, -basic.Y);
                        endCurves.Add(basic);
                    }
                    else
                    {
                        double yDeg = SpaceToDegree(GraphHeight);
                        double xDeg = upper.X;

                        endCurves.Add(new Point(xDeg, yDeg));
                    }
                }

                return endCurves;
            }

            public bool IsPointerOnPoint(Point spacePoint, out Point pog)
            {
                foreach (var dps in drawnPoints)
                {
                    if (dps.Item1.IsPointerOver)
                    {
                        pog = dps.Item2;
                        return true;
                    }
                }
                pog = default;

                return false;
            }


            public double SpaceToDegree(double space)
            {
                // given a canvas position, convert it to a space value

                return ((space / bigSpacing) * spaceValue).QuickRound();
            }
            public double DegreeToSpace(double degree)
            {
                // given a canvas position, convert it to a space value

                return ((degree / spaceValue) * bigSpacing).QuickRound();
            }

            public Point SpaceToDegree(Point canvasPos)
            {
                // given a canvas position, convert it to a space value
                return new Point(SpaceToDegree(canvasPos.X), SpaceToDegree(canvasPos.Y));
            }

            public Point DegreeToSpace(Point canvasPos)
            {
                // given a canvas position, convert it to a space value
                return new Point(DegreeToSpace(canvasPos.X), DegreeToSpace(canvasPos.Y));
            }

            public bool SpaceWithinGraph(double graphSpace)
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

            public bool SpaceWithinGraph(Point point)
            {
                double curWidth = GraphCanvas.Bounds.Width;
                double curHeight = GraphCanvas.Bounds.Height;

                double x = point.X;
                double y = point.Y;

                if (x > curWidth / 2 || x < -curWidth / 2)
                {
                    return false;
                }

                if (y > curHeight / 2 || y < -curHeight / 2)
                {
                    return false;
                }



                return true;


            }

            public bool WithinActiveCurveZone(Point spacePoint, GraphAxis axis)
            {
                // the reason we limit the curves to 95% of the graph is because we want to prevent the user from putting a point on/near the edge because if that happens, then the user will be unable to remove said point since it will most likely be intersecting with the end points which are not removeable

                double x = GraphWidth * .95;
                double y = GraphHeight * .95;

                if (axis == GraphAxis.XAxis)
                {
                    if (spacePoint.Y < 0 && Math.Abs(spacePoint.X) < x)
                    {
                        return true;
                    }

                    return false;
                }
                else
                {
                    if (spacePoint.X > 0 &&  Math.Abs(spacePoint.Y) < y )
                    {
                        return true;
                    }

                    return false;
                }
            }

            bool ContainsApprox(List<double> list, double value, double tolerance = 0.001)
            {
                return list.Any(x => Math.Abs(x - value) < tolerance);
            }
        }
    }
}


