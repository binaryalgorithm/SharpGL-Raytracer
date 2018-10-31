using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Serialization.Formatters.Binary;
using System.IO.Compression;
using System.Security.Cryptography;

namespace SharpGLCudafy
{
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct Camera
    {
        public float x;
        public float y;
        public float z;
        public float hRotation;
        public float vRotation;
        public float rightX;
        public float rightY;
        public float rightZ;
        public float upX;
        public float upY;
        public float upZ;
        public float forwardX;
        public float forwardY;
        public float forwardZ;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    [Serializable]
    unsafe public struct ChunkData
    {
        public int valid; // struct is populated
        public int generated; // voxel gen occured
        public int empty; // only air voxels
        public int size; // cubic side length

        public int chunkX; // absolute world coordinates divided by chunk size, in other words chunk coordinates
        public int chunkY;
        public int chunkZ;
        public int hash;

        public fixed byte voxelData[Util.chunkVoxelCount]; // 32^3

        public override string ToString()
        {
            return $"({chunkX}, {chunkY}, {chunkZ}) v={valid} g={generated} e={empty} size={size}";
        }
    }

    public static class Util
    {
        static Random rnd = new Random();
        public const int chunkSize = 32;
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
                return (vPos / chunkSize); // 0-7=0 8-15=1
            }
            else
            {
                return ((vPos + 1) / chunkSize) - 1; // -1 to -8 = -1
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

        public static void SaveChunkToDisk(ChunkData cd)
        {
            string chunkPath = Environment.CurrentDirectory + $"\\chunkdata\\{cd.chunkX}.{cd.chunkY}.{cd.chunkZ}.chunk";
            byte[] chunkRawData = Serialize(cd);
            byte[] finalData = Compress(chunkRawData);
            File.WriteAllBytes(chunkPath, finalData);
        }

        public static ChunkData LoadChunkFromDisk(int chunkX, int chunkY, int chunkZ)
        {
            string chunkPath = Environment.CurrentDirectory + $"\\chunkdata\\{chunkX}.{chunkY}.{chunkZ}.chunk";

            if (File.Exists(chunkPath))
            {
                byte[] chunkRawData = File.ReadAllBytes(chunkPath);
                byte[] finalData = Decompress(chunkRawData);
                ChunkData cd = Deserialize<ChunkData>(finalData);
                return cd;
            }

            // failed to load
            return new ChunkData();
        }

        public static byte[] Serialize<T>(T s) where T : struct
        {
            var size = Marshal.SizeOf(typeof(T));
            var array = new byte[size];
            var ptr = Marshal.AllocHGlobal(size);
            Marshal.StructureToPtr(s, ptr, true);
            Marshal.Copy(ptr, array, 0, size);
            Marshal.FreeHGlobal(ptr);
            return array;
        }

        public static T Deserialize<T>(byte[] array) where T : struct
        {
            var size = Marshal.SizeOf(typeof(T));
            var ptr = Marshal.AllocHGlobal(size);
            Marshal.Copy(array, 0, ptr, size);
            var s = (T)Marshal.PtrToStructure(ptr, typeof(T));
            Marshal.FreeHGlobal(ptr);
            return s;
        }

        public static byte[] Compress(byte[] data)
        {
            using (MemoryStream memory = new MemoryStream())
            {
                using (GZipStream gzip = new GZipStream(memory, CompressionMode.Compress, true))
                {
                    gzip.Write(data, 0, data.Length);
                }

                return memory.ToArray();
            }
        }

        static byte[] Decompress(byte[] data)
        {
            using (GZipStream stream = new GZipStream(new MemoryStream(data), CompressionMode.Decompress))
            {
                const int size = 4096;
                byte[] buffer = new byte[size];
                using (MemoryStream memory = new MemoryStream())
                {
                    int count = 0;
                    do
                    {
                        count = stream.Read(buffer, 0, size);
                        if (count > 0)
                        {
                            memory.Write(buffer, 0, count);
                        }
                    }
                    while (count > 0);
                    return memory.ToArray();
                }
            }
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

    public static class PhaseTimer
    {
        public static List<(string phaseName, double ms)> timeRecords = new List<(string, double)>();

        public static Stopwatch sw = new Stopwatch();

        public static void Start()
        {
            timeRecords.Clear();
            sw.Restart();
        }

        public static void Record(string phaseName)
        {
            double ms = ((double)sw.ElapsedTicks / (double)Stopwatch.Frequency) * 1000.0;
            timeRecords.Add((phaseName, ms));
            sw.Restart();
        }

        public static string Dump()
        {
            string text = "";

            foreach (var item in timeRecords)
            {
                text += item.phaseName + " : " + (int)(item.ms) + " ms;  ";
            }

            return text;
        }
    }

    public class RandomProvider
    {
        static RNGCryptoServiceProvider rng = new RNGCryptoServiceProvider();

        public int Next(int min, int max)
        {
            byte[] buffer = new byte[4];
            rng.GetBytes(buffer);
            uint value = BitConverter.ToUInt32(buffer, 0);
            value = (uint)min + (value % (uint)(max - min));
            return (int)value;
        }
    }
}
