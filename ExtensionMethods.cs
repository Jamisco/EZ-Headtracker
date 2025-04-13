using Avalonia;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EZ_HeadTracker
{
    public static class ExtensionMethods
    {
        public static int degree = 3;

        public static Point QuickRound(this Point point)
        {
            return new Point(Math.Round(point.X, degree), Math.Round(point.Y, degree));
        }

        public static double QuickRound(this double point)
        {
            return Math.Round(point, degree);
        }
    }
}
