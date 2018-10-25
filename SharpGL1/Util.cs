using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace SharpGLCudafy
{
    public static class Util
    {
        static Random rnd = new Random();
        public const int chunkSize = 8;
        public const int chunkVoxelCount = chunkSize * chunkSize * chunkSize;

        public static int mod(int a, int b)
        {
            int r = a % b;
            return r < 0 ? r + b : r;
        }

        public static int ChunkFromVoxel(float vPos)
        {
            return ChunkFromVoxel((int)Math.Floor(vPos));
        }

        public static int ChunkFromVoxel(int vPos)
        {
            if (vPos >= 0)
            {
                return (vPos / 8); // 0-7=0 8-15=1
            }
            else
            {
                return ((vPos + 1) / 8) - 1; // -1 to -8 = -1
            }
        }

        public static double Deg2Rad(double angle)
        {
            return angle * (Math.PI / 180);
        }

        public static double DegCos(double angle)
        {
            return Math.Cos(angle * (Math.PI / 180));
        }

        public static double DegSin(double angle)
        {
            return Math.Sin(angle * (Math.PI / 180));
        }

        public static double Dot(double x1, double y1, double x2, double y2)
        {
            return (x1 * x2) + (y1 * y2);
        }

        public static double Satlin(double value)
        {
            if (value <= 0.0f) { return 0.0f; }
            else if (value >= 1.0f) { return 1.0f; }
            else { return value; }
        }

        public static double Distance(double x1, double y1, double x2, double y2)
        {
            return (((x2 - x1).Pow(2) + (y2 - y1).Pow(2))).Pow(0.5);
        }

        public static double GetRandomRange(double min, double max)
        {
            return min + ((max - min) * rnd.NextDouble());
        }

        public static float GetRandomRangeF(double min, double max)
        {
            return (min + ((max - min) * rnd.NextDouble())).ToFloat();
        }

        // exponent extension
        public static double Pow(this double value, double power)
        {
            return Math.Pow(value, power);
        }

        public static float ToFloat(this double d)
        {
            return (float)d;
        }
    }
    

    public static class MultiTimer
    {
        public class TimerData
        {
            public long startTick;
            public long endTick;
            public long lastEllapsedTicks;
            public long totalEllapsedTicks;
        }

        public static string DebugString(bool frameSum = false)
        {
            string text = "";

            foreach (var kvp in timers)
            {
                if (frameSum == true)
                {
                    text += kvp.Key + " : " + ((float)kvp.Value.totalEllapsedTicks / (float)Stopwatch.Frequency).ToString("N3") + "  " + Environment.NewLine;
                }
                else
                {
                    text += kvp.Key + " : " + ((float)kvp.Value.lastEllapsedTicks / (float)Stopwatch.Frequency * 1000f).ToString("N1") + " ms; ";
                }
            }

            return text;
        }

        static Dictionary<string, TimerData> timers = new Dictionary<string, TimerData>();

        public static void StartTimer(string name)
        {
            if (!timers.ContainsKey(name))
            {
                timers[name] = new TimerData();
            }

            timers[name].startTick = Stopwatch.GetTimestamp();
        }

        public static double StopTimer(string name)
        {
            if (!timers.ContainsKey(name))
            {
                return 0;
            }

            timers[name].endTick = Stopwatch.GetTimestamp();
            timers[name].lastEllapsedTicks = timers[name].endTick - timers[name].startTick;
            timers[name].totalEllapsedTicks += timers[name].lastEllapsedTicks;

            return ((double)timers[name].lastEllapsedTicks / (double)Stopwatch.Frequency); // seconds conversion
        }

        public static double GetTotalEllapsed(string name)
        {
            if (!timers.ContainsKey(name))
            {
                return 0;
            }

            return ((double)timers[name].totalEllapsedTicks / (double)Stopwatch.Frequency); // seconds conversion
        }
    }

}
