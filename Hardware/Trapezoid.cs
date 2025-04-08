using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

using static EZ_HeadTracker.Hardware.ExtensionMethods;

namespace EZ_HeadTracker.Hardware
{
    public class Trapezoid
    {
        public Point2f[] Points;
        public Point2f Centroid
        {
            get
            {
                float x = 0;
                float y = 0;

                foreach (var point in Points)
                {
                    x += point.X;
                    y += point.Y;
                }

                return new Point2f(x / Points.Length, y / Points.Length);

            }
        }
        public Point2f BaseCentroid
        {
            get
            {
                return MidPoint2f(Points[0], Points[3]);
            }
        }
        public Point2f TopCentroid
        {
            get
            {
                return MidPoint2f(Points[1], Points[2]);
            }
        }
        public float Height
        {
            get
            {
                return (float)Point2f.Distance(TopCentroid, BaseCentroid);
            }
        }
        public float Width
        {
            get
            {
                return (float)Point2f.Distance(Points[0], Points[3]);
            }
        }
        public bool IsValid
        {
            get
            {
                return IsValidPoints(Points);
            }
        }
        public static bool IsValidPoints(Point2f[] points)
        {
            if (points.Length != 4)
            {
                return false;
            }

            foreach (var point in points)
            {
                if (point.X < 0 || point.X >= HeadTracker.FRAMEWIDTH || point.Y < 0 || point.Y >= HeadTracker.FRAMEHEIGHT)
                {
                    return false;
                }
            }

            return true;
        }

        public Trapezoid(List<Point2f> leds)
        {
            if (leds == null || leds.Count < 4)
            {
                Points = new Point2f[0];
                return;
            }

            // Sort points from left to right
            leds.Sort((a, b) => a.X.CompareTo(b.X));

            // Identify the top and bottom points for the left and right sides
            Point2f[] leftPoints = leds.Take(2).OrderBy(p => p.Y).ToArray();
            Point2f[] rightPoints = leds.Skip(2).Take(2).OrderBy(p => p.Y).ToArray();

            // Assign points in the correct order: bottom-left, top-left, top-right, bottom-right
            Points = new Point2f[]
            {
                leftPoints[0], // bottom-left
                leftPoints[1], // top-left
                rightPoints[1], // top-right
                rightPoints[0]  // bottom-right
            };
        }

        public Trapezoid(Point2f[] points)
        {
            Points = points;
        }
        public void DrawShape(Mat frame, Scalar sl, bool showLengths = true)
        {
            Point2f px = new Point2f(20, 0);
            Point2f xp = new Point2f(70, 0);
            Point2f py = new Point2f(0, 30);
            Point2f xp2 = new Point2f(20, 0);

            int n = Points.Length;
            //Scalar sl = Scalar.White;

            foreach (var point in Points)
            {
                Cv2.Circle(frame, point.R2P(), 2, sl, 2);
            }


            int ls = 2;

            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;

                Cv2.Line(frame, Points[i].R2P(), Points[j].R2P(), sl, ls);
            }

            Point2f mid = MidPoint2f(Points[0], Points[2]);
            Point2f mid2 = new Point2f(Points[1].X, mid.Y);

            string height = Height.ToString("0.00");
            string width = Width.ToString("0.00");

            float ts = 1.3f;

            if (showLengths == false)
            {
                return;
            }

            Cv2.PutText(frame, width, (mid + py).R2P(), HersheyFonts.HersheyPlain, ts, sl);

            Cv2.PutText(frame, height, (Points[1] + px).R2P(), HersheyFonts.HersheyPlain, ts, sl);
        }
        public void DrawCentriod(Mat frame)
        {
            Cv2.Circle(frame, Centroid.R2P(), 2, Scalar.White, 2);

            Cv2.Circle(frame, TopCentroid.R2P(), 2, Scalar.Yellow, 2);


        }
        public void PrintData(Mat frame, Point2f pos)
        {
            Point2f py = new Point2f(0, 20);
            int c = 1;
            Scalar sl = Scalar.White;

            Cv2.PutText(frame, "Centroid: " + Centroid.R2P().ToString2(), pos.R2P(), HersheyFonts.HersheyPlain, 1, sl);
        }
        public void ShowCurrentShape(Mat displayFrame, Point2f printPos)
        {
            try
            {
                DrawShape(displayFrame, Scalar.Red);
                DrawCentriod(displayFrame);

                PrintData(displayFrame, printPos);
            }
            catch (Exception)
            {

            }
        }
    }
}
