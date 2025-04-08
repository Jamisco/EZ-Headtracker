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

        private bool isTracking;
        private bool SHOWGRAY = false;
        Task initTask;

        public TransformationData currentData;
        public struct TransformationData
        {
            public float Pitch;
            public float Yaw;
            public float Roll;

            public float X;
            public float Y;
            public float Z;

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
            initTask = Task.Run(() =>
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
            isTracking = true;

            currentData = new TransformationData();
            trackingThread = new Thread(TrackingLoop);

            trackingThread.Start();
        }

        public int frameCount = 0;
        public void CenterFrame()
        {
            PoseTransformation.ClearOffsets();
        }

        string frameName = "Tracking Frame";
        private void TrackingLoop()
        {
            Mat prevFrame = new Mat();
            // calibration frame completed, now we simply need to show the calibration points as we are calibrating or just skip straight to calculation
            while (isTracking)
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

                curShape.ShowCurrentShape(displayFrame, printStartPos);
                printLine = 0;

                if (HasCenter)
                {
                    ShowCenterTriangle(displayFrame);
                }

                ShowHeadPose(displayFrame);
                ShowFrameCounter(displayFrame);

                if(SHOWGRAY)
                {
                    Cv2.NamedWindow(frameName);
                    Cv2.SetWindowProperty(frameName, WindowPropertyFlags.AspectRatio, 5);
                    Cv2.ImShow(frameName, displayFrame);
                }

                Cv2.WaitKey(1);

                frameCount++;
                prevFrame = Frame.Clone();
            }
        }
        private List<Point2f> ExtractLedPoints(Mat frame)
        {
            try
            {
                Point[][] curContours;
                Mat grayFrame = new Mat();

                Cv2.CvtColor(frame, grayFrame, ColorConversionCodes.BGR2GRAY);

                HierarchyIndex[] hierarchy;

                // apply gausasain filter
                //Cv2.GaussianBlur(grayFrame, grayFrame, new Size(5, 5), 0);

                Cv2.Threshold(grayFrame, grayFrame, 50, 255, ThresholdTypes.Binary);


                // pass in the previous points and modify the method so that it favors points that are closer to the previous points.
                // also make it so that points that are within the blob of said closer points are thesame as the previous points.
                Cv2.FindContours(grayFrame, out curContours, out hierarchy, RetrievalModes.External, ContourApproximationModes.ApproxSimple);

                var led = PointFromContours(grayFrame, curContours);

                if (SHOWGRAY)
                {
                    Cv2.ImShow("OG Image", frame);
                    Cv2.ImShow("Gray Frame", grayFrame);
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

            List<(Point2f, double)> pulses = new List<(Point2f, double)>();

            Cv2.PutText(frame, "Countours Found: " + contours.Length, new Point(0, 100), HersheyFonts.HersheyPlain, 1, Scalar.White);

            foreach (var contour in contours)
            {
                Moments moments = Cv2.Moments(contour);

                Point2f center = contour[0];

                Cv2.Circle(frame, center.R2P(), 10, Scalar.White, 2);
            }

            if (contours.Length > 4 && prevShape != null)
            {
                List<(float, Point[])> distance = new List<(float, Point[])>();

                foreach (var contour in contours)
                {
                    Moments moments = Cv2.Moments(contour);

                    Point2f con = new Point2f((int)(moments.M10 / moments.M00), (int)(moments.M01 / moments.M00));

                    float minD = float.MaxValue;

                    foreach (Point2f c in prevShape.Points)
                    {
                        float d = (float)con.DistanceTo(c);

                        if (d < minD)
                        {
                            minD = d;
                        }
                    }

                    distance.Add((minD, contour));
                }

                distance.Sort((a, b) => a.Item1.CompareTo(b.Item1));

                distance = distance.Take(4).ToList();

                contours = distance.Select(d => d.Item2).ToArray();

            }

            foreach (var contour in contours)
            {
                double area = Cv2.ContourArea(contour);
                contourAreas.Add(Tuple.Create(contour, area));
            }

            // Step 2: Sort the contours by area (descending order)
            contourAreas.Sort((a, b) => b.Item2.CompareTo(a.Item2));

            // Step 3: Take the largest contours 

            for (int i = 0; i < contourAreas.Count; i++)
            {
                double area = contourAreas[i].Item2;

                // there might be a situation where the light so dim such that the area is 0, even tho the light has been seen, in such a case, we will just use the position of the first contour because if the area is zero, that means whatever contours were found all have thesame position.
                if (area == 0)
                {
                    if (contourAreas[i].Item1.Length > 0)
                    {
                        pulses.Add((contourAreas[i].Item1[0], area));
                    }
                }
                else
                {
                    Moments moments = Cv2.Moments(contourAreas[i].Item1);

                    Point2f center = new Point2f((int)(moments.M10 / moments.M00), (int)(moments.M01 / moments.M00));

                    pulses.Add((center, area));
                }
            }

            // if 2 pulses are too close together remove the one with the smaller area

            float minDistance = 10;

            if (pulses.Count > 4)
            {
                int sds = 2;
            }

            for (int i = 0; i < pulses.Count; i++)
            {
                for (int j = i + 1; j < pulses.Count; j++)
                {
                    if (pulses[i].Item1.DistanceTo(pulses[j].Item1) < minDistance)
                    {
                        if (pulses[i].Item2 > pulses[j].Item2)
                        {
                            pulses.RemoveAt(j);
                        }
                        else
                        {
                            pulses.RemoveAt(i);
                        }
                    }
                }
            }

            for (int i = 0; i < pulses.Count; i++)
            {
                Point2f p = pulses[i].Item1;
                double area = pulses[i].Item2;

                // Mirror across the center of the frame
                p.X = FRAMEWIDTH - p.X;  // This flips relative to frame width

                pulses[i] = (p, area);
            }

            return pulses.Select(p => p.Item1).ToList();
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
                DataBridge.SendData2OpenTrack(r2, t2);
            }
            catch (Exception ex)
            {

                return;
            }
        }

        public void StopTracking()
        {
            isTracking = false;
            if (trackingThread != null && trackingThread.IsAlive)
            {
                trackingThread.Join();  // Wait for the thread to finish
            }
        }
        public void ReleaseResources()
        {
            StopTracking();
            Frame.Release();
            capture.Release();
        }
    }
}