using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace EZ_HeadTracker.Hardware
{
    public class HeadTracker
    {
        private VideoCapture capture;
        private Thread trackingThread;

        private Mat Frame = new Mat();
        private Mat displayFrame = new Mat();

        public const int FRAMEWIDTH = 640;
        public const int FRAMEHEIGHT = 480;
        public const int CaptureFps = 30;

        public bool IsTracking;
        private bool SHOWGRAY = false;
        public Task InitiatingTracking;

        public event EventHandler<HeadPoseEventArgs> HeadPoseUpdated;
        public event EventHandler<HeadPoseEventArgs> HeadPoseLost;
        

        /// <summary>
        /// All the transformations that can be done to the head pose.
        /// It is important to note that the order of the transformations matters for various purposes throughout the entire program 
        /// </summary>
        public enum TransformationType { Pitch, Yaw, Roll, X, Y, Z };

        public TransformationData currentData;
        public struct TransformationData
        {
            public float Pitch;
            public float Yaw;
            public float Roll;

            public float X;
            public float Y;
            public float Z;

            public float[] DataArray => new float[] { Pitch, Yaw, Roll, X, Y, Z };

            public TransformationData(Point3f rotation, Point3f translation)
            {
                Pitch = rotation.X;
                Yaw = rotation.Y;
                Roll = rotation.Z;

                X = translation.X;
                Y = translation.Y;
                Z = translation.Z;
            }

            public void Round(int d)
            {
                Pitch = (float)Math.Round(Pitch, d);
                Yaw = (float)Math.Round(Yaw, d);
                Roll = (float)Math.Round(Roll, d);
                X = (float)Math.Round(X, d);
                Y = (float)Math.Round(Y, d);
                Z = (float)Math.Round(Z, d);
            }
        }

        public HeadTracker(int cameraIndex = 0)
        {
            //Thread.Sleep(1000); // Wait for the camera to warm up

            // Initialize the camera
            InitializeCameraAsync(cameraIndex);
        }

        private async void InitializeCameraAsync(int cameraIndex)
        {
            InitiatingTracking = Task.Run(() =>
            {
                // use videocapture any to select best api for platform, might take a while thought
                capture = new VideoCapture(cameraIndex, VideoCaptureAPIs.DSHOW);
                capture.Set(VideoCaptureProperties.Fps, CaptureFps);
                capture.FrameHeight = FRAMEHEIGHT;
                capture.FrameWidth = FRAMEWIDTH;

                StartTracking();
            });

        }
        public Trapezoid prevShape { get; private set; }
        public Trapezoid curShape { get; private set; }

        public bool HasCenter { get; set; } = false;

        public Point2f printStartPos
        {
            get
            {
                Point2f p = new Point2f(10, 20);

                p = p + printNewLine * printLine;
                printLine += 5;

                return p;
            }
        }

        public Point2f printNewLine = new Point2f(0, 20);
        int printLine = 1;

        public void StartTracking()
        {
            IsTracking = true;

            currentData = new TransformationData();
            trackingThread = new Thread(TrackingLoop);

            trackingThread.Start();
        }

        public int frameCount = 0;
        public void CenterFrame()
        {
            PoseTransformation.ResetOffsets();
        }

        string frameName = "Tracking Frame";
        private void TrackingLoop()
        {
            Mat prevFrame = new Mat();
            // calibration frame completed, now we simply need to show the calibration points as we are calibrating or just skip straight to calculation
            while (IsTracking)
            {
                capture.Read(Frame);

                var ledPoints = ExtractLedPoints(Frame);

                displayFrame = Frame.EmptyClone();
                DrawCenterGraph();

                Trapezoid trap = new Trapezoid(ledPoints);

                if (trap.IsValid)
                {
                    if (curShape == null)
                    {
                        prevShape = curShape;
                        curShape = trap;
                    }
                    else if (curShape != null && curShape.IsValid)
                    {
                        float distance = (float)Point2f.Distance(curShape.Centroid, trap.Centroid);

                        prevShape = curShape;
                        curShape = trap;
                    }
                }
                

                if(curShape == null)
                {
                    continue;
                }

                curShape.ShowCurrentShape(displayFrame, printStartPos);
                printLine = 0;

                if (HasCenter)
                {
                    ShowCenterTriangle(displayFrame);
                }

                ShowHeadPose(displayFrame);

                if(SHOWGRAY)
                {
                    ShowFrameCounter(displayFrame);
                    Cv2.NamedWindow(frameName);
                    Cv2.SetWindowProperty(frameName, WindowPropertyFlags.AspectRatio, 5);
                    Cv2.ImShow(frameName, displayFrame);
                }

                Cv2.WaitKey(1);

                frameCount++;
                prevFrame = Frame.Clone();
            }
        }

        private double threshold = 30;
        private double step = 30;

        private List<Point2f> ExtractLedPoints(Mat frame)
        {
            try
            {
                Point[][] curContours;
                Mat grayFrame = new Mat();

                // this is to reduce all the glare that might occur from the leds
                int co = 100;

                Cv2.InRange(frame, new Scalar(co, co, co), new Scalar(255, 255, 255), grayFrame);

                HierarchyIndex[] hierarchy;

                // apply gausasain filter
                //Cv2.GaussianBlur(grayFrame, grayFrame, new Size(5, 5), 0);

                //Cv2.Threshold(grayFrame, grayFrame, threshold, 255, ThresholdTypes.Binary);

                Cv2.FindContours(grayFrame, out curContours, out hierarchy, RetrievalModes.External, ContourApproximationModes.ApproxNone);

                // last valid frame, 
                // if the contours are greater than 4, we will try to find the contours again by increasing the threshold if we filter drops below 4, then use last frame where it was 4

                // if contours are less than 4, we will try to find the contours again by decreasing the threshold, if we filter and we get 5, then use last frame where it was 4
                int contourCount = curContours.Count();
                int initCount = contourCount;

                (Mat, Point[][]) lastFrame = (grayFrame, curContours);

                bool dontStop = true;

                step = (contourCount > 4) ? step * -1 : step;

                if (contourCount != 4)
                {
                    Mat kernel = Cv2.GetStructuringElement(MorphShapes.Ellipse, new Size(1, 1));

                    if (contourCount < 4)
                    {
                        kernel = Cv2.GetStructuringElement(MorphShapes.Ellipse, new Size(5, 5));
                        Cv2.MorphologyEx(grayFrame, grayFrame, MorphTypes.Dilate, kernel);

                    }
                    else
                    {
                        Cv2.MorphologyEx(grayFrame, grayFrame, MorphTypes.Erode, kernel);
                    }

                    Cv2.FindContours(grayFrame, out curContours, out hierarchy, RetrievalModes.External, ContourApproximationModes.ApproxNone);
                }

                var led = PointFromContours(lastFrame.Item1, lastFrame.Item2);

                if (SHOWGRAY)
                {
                    Cv2.ImShow("OG Image", frame);

                    Cv2.ImShow("Gray Frame", grayFrame);
                    //Cv2.ImShow("GrayF", grayF);

                }

                return led;
            }
            catch (Exception ex)
            {
                return null;
            }
        }
        private List<Point2f> PointFromContours(Mat frame, Point[][] contours)
        {
            // Step 1: Calculate areas for all contours
            List<Tuple<Point[], double>> contourAreas = new List<Tuple<Point[], double>>();

            List<Point2f> pulses = new List<Point2f>();

            Mat testFrame = frame.EmptyClone();
            Cv2.CvtColor(testFrame, testFrame, ColorConversionCodes.GRAY2BGR); // Or use COLOR_GRAY2BGR if working with grayscale

            int initCount = contours.Length;

            Cv2.PutText(frame, "Init Contour Count: " + contours.Length, new Point(0, 100), HersheyFonts.HersheyPlain, 1, Scalar.White);

            // take the largest contours
            if (contours.Length > 4 && prevShape != null)
            {
                List<(float, Point[])> distance = new List<(float, Point[])>();

                foreach (Point2f prevPoint in prevShape.Points)
                {
                    float minD = float.MaxValue;
                    Point[] minContour = null;

                    Point2f[] contourCenters = contours.Select(contour =>
                    {
                        Cv2.MinEnclosingCircle(contour, out Point2f center, out float _);
                        return center;
                    }).ToArray();

                    foreach (var (center, contour) in contourCenters.Zip(contours, (c, cnt) => (c, cnt)))
                    {
                        float d = (float)prevPoint.DistanceTo(center);
                        if (d < minD)
                        {
                            minD = d;
                            minContour = contour;
                        }
                    }

                    distance.Add((minD, minContour));
                    contours = contours.Where(c => c != minContour).ToArray();
                }

                distance.Sort((a, b) => a.Item1.CompareTo(b.Item1));

                distance = distance.Take(4).ToList();

                contours = distance.Select(d => d.Item2).ToArray();

            }


            if (contours.Length != 4)
            {
                Scalar c;

                if (contours.Length < 4)
                {
                    c = Scalar.Yellow;
                }
                else
                {
                    c = Scalar.Red;
                }

                foreach (var contour in contours)
                {
                    Cv2.MinEnclosingCircle(contour, out Point2f center, out float _);

                    testFrame.Circle((Point)center, 6, c, -1);
                    frame.Circle((Point)center, 6, c, -1);
                }
            }

            foreach (var contour in contours)
            {
                Point2f center;

                Cv2.MinEnclosingCircle(contour, out center, out float _);

                pulses.Add(center);
            }

            Cv2.PutText(frame, "Pulse Count: " + pulses.Count, new Point(0, 150), HersheyFonts.HersheyPlain, 1, Scalar.White);

            foreach (var item in pulses)
            {
                Cv2.Circle(testFrame, item.R2P(), 10, Scalar.Green, 2);
                Cv2.Circle(frame, item.R2P(), 10, Scalar.White, 2);
            }

            // the below code will mirror the points such that, when your head looks/rolls left, on the screen its will also look as such

            if (true)
            {
                for (int i = 0; i < pulses.Count; i++)
                {
                    Point2f p = pulses[i];

                    // Mirror across the center of the frame
                    p.X = FRAMEWIDTH - p.X;  // This flips relative to frame width

                    pulses[i] = p;
                }
            }

            //Cv2.ImShow("Test Frame", testFrame);
            //Cv2.WaitKey(1);
            return pulses;
        }
        private void ShowCenterTriangle(Mat displayFrame)
        {
            Mat f = Frame.EmptyClone();

            //if (HasCenter)
            //{
            //    centerTShape.DrawShape(displayFrame, Scalar.White, true);
            //    centerTShape.DrawCentriod(displayFrame);

            //    centerTShape.PrintData(displayFrame, printStartPos);
            //}

            if (HasCenter)
            {
                curShape.DrawShape(displayFrame, Scalar.White, true);
                curShape.DrawCentriod(displayFrame);

                curShape.PrintData(displayFrame, printStartPos);
            }
        }
        private void ShowFrameCounter(Mat displayFrame)
        {
            Point bottom = new Point(0, 580);

            Cv2.PutText(displayFrame, "Frame Count: " + frameCount, bottom, HersheyFonts.HersheyPlain, 1, Scalar.White);
        }

        private void DrawCenterGraph()
        {
            Point2f topMid, botMid;
            Point2f leftMid, rightMid;

            Size frameSize = displayFrame.Size();

            float height = frameSize.Height;
            float width = frameSize.Width;

            topMid = new Point2f(width / 2, 0);
            botMid = new Point2f(width / 2, height);

            Point2f yAdjust = new Point2f(0, 15);

            leftMid = new Point2f(0, height / 2) - yAdjust;
            rightMid = new Point2f(width, height / 2) - yAdjust;

            Scalar col = Scalar.Teal;
            int s = 1;

            Cv2.Line(displayFrame, topMid.R2P(), botMid.R2P(), col, s);
            Cv2.Line(displayFrame, leftMid.R2P(), rightMid.R2P(), col, s);
        }
        private void ShowHeadPose(Mat displayFrame)
        {
            Point3d r, t;

            Point start = new Point(0, 300);
            Point step = new Point(0, 20);
            int count = 0;

            Point2f[] points;

            points = curShape.Points;

            try
            {
                PoseTransformation.EstimateTransformation6(displayFrame, curShape, out Point3f r2, out Point3f t2);

                count += 2;

                Cv2.PutText(displayFrame, "Rotation: " + r2.R2P(2),
                    start + step * count++, HersheyFonts.HersheyPlain, 1, Scalar.White);

                Cv2.PutText(displayFrame, "Translation: " + t2.R2P(),
                    start + step * count++, HersheyFonts.HersheyPlain, 1, Scalar.White);

                currentData = new TransformationData(r2, t2);

                // Raise event
                HeadPoseUpdated?.Invoke(this, new HeadPoseEventArgs(currentData));

                //DataBridge.SendData2OpenTrack(r2, t2);
            }
            catch (Exception ex)
            {

                return;
            }
        }

        public void StopTracking()
        {
            IsTracking = false;
            if (trackingThread != null && trackingThread.IsAlive)
            {
                trackingThread.Join(3000);  // Wait for the thread to finish
            }
        }
        public void ReleaseResources()
        {
            StopTracking();
            Frame.Release();
            capture.Release();
        }

        public class HeadPoseEventArgs : EventArgs
        {
            public TransformationData Data { get; }

            public HeadPoseEventArgs(TransformationData data)
            {
                Data = data;
            }
        }
    }
}