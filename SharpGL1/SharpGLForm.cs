using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading;
using System.Windows.Forms;
using SharpGL;
using Cloo;

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

        public fixed byte voxelData[Util.chunkVoxelCount]; // 8^3

        public override string ToString()
        {
            return $"({chunkX}, {chunkY}, {chunkZ}) v={valid} g={generated} e={empty} size={size}";
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

    public partial class SharpGLForm : Form
    {
        string newTitle = "";

        Random rnd = new Random();
        Thread simThread;

        uint mainTexture;

        public bool IsDrawing;
        ComputeImage2D CLImage;
        ComputeCommandQueue queue;
        ComputeKernel kernel;
        List<ComputeMemory> CLLockObjects;
        ComputeProgram program;
        ComputeContext context;

        ComputeBuffer<ChunkHashKey> chunkHashKeyBuffer;
        ComputeBuffer<ChunkData> chunkDataBuffer;
        int chunkDataBufferSize = 0;

        ComputeBuffer<ChunkData> chunkHashBuffer;

        ComputeBuffer<Camera> cameraBuffer;
        Camera[] camera = new Camera[3];

        ComputeBuffer<float> textureBuffer;

        bool chunkInit = false;
        ChunkHashTable hashTable;

        ChunkData[] chunkData = new ChunkData[33 * 33 * 33];

        float cyclic = 0f;

        double frameTimeOpenGL;
        double frameTimeOpenCL;

        List<long> timeCapture = new List<long>();
        List<string> timeCaptureName = new List<string>();

        List<float> imgData = new List<float>();

        int mouseX;
        int mouseY;

        int hashBufferSize;

        float gravityVelocity = 0.0f;
        bool jumpFrame = false;

        public SharpGLForm()
        {
            hashTable = new ChunkHashTable(0.9f);
            hashBufferSize = hashTable.currentArraySize;

            // Image img = Image.FromFile("test.png");
            Image img = Image.FromFile("texturepacked.png");
            Bitmap bmp = new Bitmap(img);

            BitmapData bmpData = bmp.LockBits(new Rectangle(0, 0, img.Width, img.Height), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
            var byteLen = bmpData.Stride * bmpData.Height;
            byte[] bytes = new byte[byteLen];
            Marshal.Copy(bmpData.Scan0, bytes, 0, byteLen);
            bmp.UnlockBits(bmpData);
            
            for (int h = 0; h < img.Height; h++)
            {
                for (int w = 0; w < img.Width; w++)
                {
                    int pos = ((h * img.Width) * 3) + (w * 3);

                    imgData.Add(bytes[pos + 0] / 256f);
                    imgData.Add(bytes[pos + 1] / 256f);
                    imgData.Add(bytes[pos + 2] / 256f);
                }
            }

            //int[] offsetCounts = hashTable.Analyze();

            //int sum = 0;
            //int sumTotal = 0;

            //for (int n = 0; n < offsetCounts.Length; n++)
            //{
            //    sum += offsetCounts[n];
            //    sumTotal += offsetCounts[n] * (n + 1);
            //}

            //int mem = Marshal.SizeOf(typeof(ChunkData)) * hashTable.values.Length;

            //float wAve = (float)sumTotal / (float)sum;

            int uu = 0;

            MultiTimer.StartTimer("[ALL]");

            InitializeComponent();
            //this.openGLControl.DrawFPS = false;
            this.Show();
            openGLControl.Width = this.ClientSize.Width;
            openGLControl.Height = this.ClientSize.Height;

            mouseX = openGLControl.ClientSize.Width / 2;
            mouseY = openGLControl.ClientSize.Height / 2;

            //simThread = new Thread(() => Simulation.SimMain(this));
            //simThread.Start();
        }

        unsafe public void DoPhysics()
        {
            int cx = Util.ChunkFromVoxel(camera[0].x);
            int cy = Util.ChunkFromVoxel(camera[0].y + 1); // test player height
            int cz = Util.ChunkFromVoxel(camera[0].z);

            int offsetX = (int)Math.Floor(camera[0].x) & 7;
            int offsetY = (int)Math.Floor(camera[0].y) & 7;
            int offsetZ = (int)Math.Floor(camera[0].z) & 7;

            ChunkData cd = GenerateChunk(cx, cy, cz); // get chunk data (should exist)
            int vIndex = (offsetX + offsetY * 8 + offsetZ * 64);
            byte vType = cd.voxelData[vIndex];

            if (vType == 0) // air, we can fall
            {
                camera[0].y += gravityVelocity;
                gravityVelocity += 0.001f;

                if (gravityVelocity > 0.25f)
                {
                    gravityVelocity = 0.25f;
                }
            }
            else
            {
                gravityVelocity = 0.0f;
                camera[0].y = (float)Math.Floor(camera[0].y);

                if (jumpFrame == true)
                {
                    gravityVelocity -= 0.06f;
                    camera[0].y += gravityVelocity;
                    jumpFrame = false;
                }
            }
        }

        public int CheckViewChunks()
        {
            int cx = Util.ChunkFromVoxel(camera[0].x);
            int cy = Util.ChunkFromVoxel(camera[0].y);
            int cz = Util.ChunkFromVoxel(camera[0].z);

            int checkRange = 8;

            //Parallel.For(-16, 16 + 1, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount - 1 }, dx =>
            {
                for (int dx = -checkRange; dx <= checkRange; dx++)
                {
                    for (int dy = -checkRange; dy <= checkRange; dy++)
                    {
                        for (int dz = -checkRange; dz <= checkRange; dz++)
                        {
                            //int index = (16 + (chunkX - camChunkX)) + (16 + (chunkY - camChunkY)) * 33 + (16 + (chunkZ - camChunkZ)) * 33 * 33;

                            int index = (dx + 16) + ((dy + 16) * 33) + ((dz + 16) * 33 * 33);
                            chunkData[index] = GenerateChunk(cx + dx, cy + dy, cz + dz);

                            // GenerateChunk(cx + dx, cy + dy, cz + dz);
                        }
                    }
                }
            //});            
            }

            int removeCount = 0;
            int removeRange = 12;

            for (int n = 0; n < hashTable.values.Length; n++)
            {
                if (hashTable.values[n].valid == 1)
                {
                    ChunkData cd = hashTable.values[n];

                    if (Math.Abs(cd.chunkX - cx) > removeRange || Math.Abs(cd.chunkY - cy) > removeRange || Math.Abs(cd.chunkZ - cz) > removeRange)
                    {
                        // remove 'far away' chunks
                        hashTable.values[n] = new ChunkData();
                        hashTable.keys[n] = new ChunkHashKey();
                        hashTable.keys[n].hash = -1; // clear code
                        hashTable.recordCount--;
                        removeCount++;
                        continue;
                    }
                }
            }

            return removeCount;
        }

        unsafe public ChunkData GenerateChunk(int chunkX, int chunkY, int chunkZ)
        {
            ChunkData cd = hashTable.Find(chunkX, chunkY, chunkZ);

            //bool checkResult = chunkMap.TryGetValue((chunkX, chunkY, chunkZ), out ChunkData cd);

            if (cd.valid == 1)
            {
                return cd;
            }

            // init chunk
            {
                // do init
                cd.valid = 1;
                cd.size = 8;
                cd.empty = 1;
                cd.generated = 1;

                cd.chunkX = chunkX;
                cd.chunkY = chunkY;
                cd.chunkZ = chunkZ;
            }

            RandomProvider rnd = new RandomProvider();

            for (int dx = 0; dx < 8; dx++)
            {
                for (int dy = 0; dy < 8; dy++)
                {
                    for (int dz = 0; dz < 8; dz++)
                    {
                        int vIndex = (int)(dx + dy * 8 + dz * 64);

                        cd.voxelData[vIndex] = 0;

                        if (chunkY > 0)
                        {
                            byte b = (byte)rnd.Next(1, 16 + 1);
                            cd.voxelData[vIndex] = b;
                            cd.empty = 0;
                        }
                    }
                }
            }

            hashTable.Insert(chunkX, chunkY, chunkZ, cd);

            //bool addResult = chunkMap.TryAdd((chunkX, chunkY, chunkZ), cd);

            //if (addResult == false)
            //{
            //    Debugger.Break();
            //}

            return cd;
        }

        private void openGLControl_OpenGLInitialized(object sender, EventArgs e)
        {
            OpenGL GL = openGLControl.OpenGL;
            GL.ClearColor(0.0f, 0.0f, 0.0f, 1.0f);

            uint[] textures = new uint[1];

            GL.GenTextures(1, textures);
            GL.BindTexture(OpenGL.GL_TEXTURE_2D, textures[0]);
            mainTexture = textures[0];

            GL.TexParameter(OpenGL.GL_TEXTURE_2D, OpenGL.GL_TEXTURE_WRAP_S, OpenGL.GL_CLAMP_TO_EDGE);
            GL.TexParameter(OpenGL.GL_TEXTURE_2D, OpenGL.GL_TEXTURE_WRAP_T, OpenGL.GL_CLAMP_TO_EDGE);
            GL.TexParameter(OpenGL.GL_TEXTURE_2D, OpenGL.GL_TEXTURE_MAG_FILTER, OpenGL.GL_NEAREST);
            GL.TexParameter(OpenGL.GL_TEXTURE_2D, OpenGL.GL_TEXTURE_MIN_FILTER, OpenGL.GL_NEAREST);

            int w = openGLControl.ClientSize.Width; // this.ClientRectangle.Width;
            int h = openGLControl.ClientSize.Height; // this.ClientRectangle.Height;

            GL.TexImage2D(OpenGL.GL_TEXTURE_2D, 0, OpenGL.GL_RGBA, w, h, 0, OpenGL.GL_RGBA, OpenGL.GL_FLOAT, null);

            // try GL init here
            GL.Disable(OpenGL.GL_DEPTH_TEST);
            GL.Clear(OpenGL.GL_COLOR_BUFFER_BIT | OpenGL.GL_DEPTH_BUFFER_BIT);
            GL.MatrixMode(OpenGL.GL_PROJECTION);
            GL.LoadIdentity();
            GL.Ortho2D(0.0, w, h, 0.0); // 2D mode, pixel mode

            GL.LineWidth(1.0f);
            GL.PointSize(1.0f);

            GL.Enable(OpenGL.GL_TEXTURE_2D);
            GL.BindTexture(OpenGL.GL_TEXTURE_2D, mainTexture); // Bind the texture we changed in CL kernel
            GL.Color(1.0f, 1.0f, 1.0f, 0.1f); // needed for control to render anything            

            //int[] pixelData = new int[4 * w * h];
            //GL.GetTexImage(OpenGL.GL_TEXTURE_2D, 0, OpenGL.GL_RGBA, OpenGL.GL_BYTE, pixelData);

            // pick first platform
            ComputePlatform platform = ComputePlatform.Platforms[0];

            // Create CL context properties, add WGL context & handle to DC
            IntPtr openGLContextHandle = GL.RenderContextProvider.RenderContextHandle;
            IntPtr deviceContextHandle = GL.RenderContextProvider.DeviceContextHandle;

            // Select a platform which is capable of OpenCL/OpenGL interop.
            //ComputePlatform platform = ComputePlatform.GetByName(name);

            // Create the context property list and populate it.
            ComputeContextProperty p1 = new ComputeContextProperty(ComputeContextPropertyName.Platform, platform.Handle.Value);
            ComputeContextProperty p2 = new ComputeContextProperty(ComputeContextPropertyName.CL_GL_CONTEXT_KHR, openGLContextHandle);
            ComputeContextProperty p3 = new ComputeContextProperty(ComputeContextPropertyName.CL_WGL_HDC_KHR, deviceContextHandle);
            ComputeContextPropertyList cpl = new ComputeContextPropertyList(new ComputeContextProperty[] { p1, p2, p3 });

            // Create the context. Usually, you'll want this on a GPU but other options might be available as well.
            context = new ComputeContext(ComputeDeviceTypes.Gpu, cpl, null, IntPtr.Zero);

            // create a command queue with first gpu found
            queue = new ComputeCommandQueue(context, context.Devices[0], ComputeCommandQueueFlags.None);

            // load opencl source
            string clSource = @"            

            __kernel void helloWorld(__write_only image2d_t bmp, float green)
            {
               int x = get_global_id(0);
               int y = get_global_id(1);
               int w = get_global_size(0) - 1;
               int h = get_global_size(1) - 1;
   
               if(x > w || y > h) { return; }

               int2 coords = (int2)(x,y);
   
               float red = (float)x/(float)w;
               float blue = (float)y/(float)h;

               float4 val = (float4)(red, green, blue, 1.0f);

               // float4 val = (float4)(1.0f, 0.0f, 1.0f, 1.0f);

               write_imagef(bmp, coords, val);  
            }
            ";

            //clSource = File.ReadAllText("OpenCLTest.cl", Encoding.ASCII);
            clSource = File.ReadAllText("RayTracer.cl");

            // create program with opencl source
            program = new ComputeProgram(context, clSource);

            // compile opencl source
            try
            {
                program.Build(null, null, null, IntPtr.Zero);
            }
            catch (Exception ex)
            {
                Debug.WriteLine(program.GetBuildLog(context.Devices[0]));
                Debugger.Break();
            }

            // load chosen kernel from program
            //kernel = program.CreateKernel("OpenCLTest");
            kernel = program.CreateKernel("RayTraceMain");

            CLImage = ComputeImage2D.CreateFromGLTexture2D(queue.Context, ComputeMemoryFlags.WriteOnly, (int)OpenGL.GL_TEXTURE_2D, 0, (int)textures[0]);

            //GL.frame
            //var CLBuffer = ComputeImage2D.CreateFromGLRenderbuffer(queue.Context, ComputeMemoryFlags.WriteOnly, 1);

            CLLockObjects = new List<ComputeMemory>() { CLImage };

            cameraBuffer = new ComputeBuffer<Camera>(context, ComputeMemoryFlags.ReadWrite | ComputeMemoryFlags.CopyHostPointer, camera);

            // chunkHashKeyBuffer = new ComputeBuffer<ChunkHashKey>(context, ComputeMemoryFlags.ReadOnly | ComputeMemoryFlags.CopyHostPointer, hashTable.keys);
            chunkDataBuffer = new ComputeBuffer<ChunkData>(context, ComputeMemoryFlags.ReadOnly | ComputeMemoryFlags.CopyHostPointer, chunkData);

            textureBuffer = new ComputeBuffer<float>(context, ComputeMemoryFlags.ReadOnly | ComputeMemoryFlags.CopyHostPointer, imgData.ToArray());

            chunkHashBuffer = new ComputeBuffer<ChunkData>(context, ComputeMemoryFlags.ReadOnly | ComputeMemoryFlags.CopyHostPointer, hashTable.values);
            //chunkHashBuffer = new ComputeBuffer<ChunkData>(context, ComputeMemoryFlags.ReadOnly | ComputeMemoryFlags.CopyHostPointer, chunkData);

            RunOpenCLKernel();
        }

        unsafe public void RunOpenCLKernel(float greenValue = 0.0f)
        {
            OpenGL GL = openGLControl.OpenGL;

            int w = openGLControl.ClientSize.Width;
            int h = openGLControl.ClientSize.Height;

            MultiTimer.StartTimer("RunOpenCLKernel");

            long[] globalWorkOffset = new long[] { 0, 0, 0 };
            long[] globalWorkSize = new long[] { w, h };
            long[] localWorkSize = new long[] { 16, 16 };

            timeCapture.Add(Stopwatch.GetTimestamp());

            queue.AcquireGLObjects(CLLockObjects, null);

            // sync to GPU
            queue.WriteToBuffer(camera, cameraBuffer, true, null);

            if (hashBufferSize != (int)hashTable.currentArraySize)
            {
                chunkHashBuffer.Dispose(); // discard old buffer

                // realloc size on GPU
                chunkHashBuffer = new ComputeBuffer<ChunkData>(context, ComputeMemoryFlags.ReadOnly | ComputeMemoryFlags.CopyHostPointer, hashTable.values);

                hashBufferSize = (int)hashTable.currentArraySize;
            }
            else
            {
                queue.WriteToBuffer(hashTable.values, chunkHashBuffer, true, null);
            }

            // queue.WriteToBuffer(chunkData, chunkDataBuffer, true, null);

            PhaseTimer.Record("WriteToBuffer()");
            timeCapture.Add(Stopwatch.GetTimestamp());

            kernel.SetMemoryArgument(0, CLImage);
            kernel.SetMemoryArgument(1, cameraBuffer);
            kernel.SetMemoryArgument(2, textureBuffer);
            kernel.SetMemoryArgument(3, chunkHashBuffer);
            kernel.SetValueArgument(4, hashTable.maxOffset);
            kernel.SetValueArgument(5, hashTable.currentArraySize);
            kernel.SetValueArgument(6, (float)mouseX / (float)openGLControl.ClientSize.Width);
            kernel.SetValueArgument(7, (float)mouseY / (float)openGLControl.ClientSize.Height);
            kernel.SetMemoryArgument(8, chunkDataBuffer); // 'jitters' when using fixed array method, using chunk hash currently

            //kernel.SetMemoryArgument(0, CLImage);
            //kernel.SetValueArgument(1, 0.0f);

            timeCapture.Add(Stopwatch.GetTimestamp());

            queue.Execute(kernel, globalWorkOffset, globalWorkSize, localWorkSize, null);

            PhaseTimer.Record("Execute() kernel");
            timeCapture.Add(Stopwatch.GetTimestamp());

            CheckViewChunks();

            PhaseTimer.Record("CheckViewChunks()");

            queue.ReleaseGLObjects(CLLockObjects, null);

            queue.Finish(); // required to render the buffer

            queue.ReadFromBuffer(cameraBuffer, ref camera, true, null);

            camera[1] = camera[2]; // update

            DoPhysics(); // update cam[0]

            PhaseTimer.Record("Finish()");

            timeCapture.Add(Stopwatch.GetTimestamp()); // last

            double TM = MultiTimer.StopTimer("RunOpenCLKernel");

            //this.Text = TM.ToString("N3");

            long baseTime = timeCapture[0];

            string text = "";
            foreach (long t in timeCapture)
            {
                text += ((double)(t - baseTime) / (double)Stopwatch.Frequency * 1000.0).ToString("N3") + " ; ";
            }

            //text = "";
            //text = $"x {camera[0].x}, y {camera[0].y}, z {camera[0].z}";
            // text = $"x {camera[1].x}, y {camera[1].y}, z {camera[1].z}";
            //text = $"x {camera[2].x}, y {camera[2].y}, z {camera[2].z}";

            //text = $"x {mouseX}, y {mouseY}";

            //text = camera[2].forwardX.ToString();

            text = PhaseTimer.Dump();

            text += " Chunks: " + hashTable.recordCount + " / " + hashTable.values.Length + "; GPU bytes xfer: " + (hashTable.values.Length * Marshal.SizeOf(typeof(ChunkData)) / 1024) + "KB";

            if (cyclic % 3 == 0)
            {
                this.Text = text;
            }

            timeCapture.Clear();

            frameTimeOpenCL = TM;
        }

        // frame drawing code
        private void openGLControl_OpenGLDraw(object sender, RenderEventArgs e)
        {
            IsDrawing = true;

            PhaseTimer.Start();

            MultiTimer.StopTimer("[ALL]");
            MultiTimer.StartTimer("[ALL]");

            MultiTimer.StartTimer("OpenGLDraw");

            int w = openGLControl.ClientSize.Width; // this.ClientRectangle.Width;
            int h = openGLControl.ClientSize.Height; // this.ClientRectangle.Height;

            OpenGL GL = openGLControl.OpenGL;

            //GL.Clear(OpenGL.GL_COLOR_BUFFER_BIT);
            //GL.Clear(OpenGL.GL_COLOR_BUFFER_BIT | OpenGL.GL_DEPTH_BUFFER_BIT);
            //GL.MatrixMode(OpenGL.GL_PROJECTION);
            //GL.LoadIdentity();
            //GL.Ortho2D(0.0, w, h, 0.0); // 2D mode, pixel mode

            //GL.LineWidth(1.0f);
            //GL.PointSize(1.0f);

            //GL.Enable(OpenGL.GL_TEXTURE_2D);
            //GL.BindTexture(OpenGL.GL_TEXTURE_2D, mainTexture); // Bind the texture we changed in CL kernel
            //GL.Color(1.0f, 1.0f, 1.0f, 0.1f); // needed for control to render anything

            GL.Begin(OpenGL.GL_QUADS);
            GL.TexCoord(0, 0); GL.Vertex(0, 0);
            GL.TexCoord(0, 1); GL.Vertex(0, h);
            GL.TexCoord(1, 1); GL.Vertex(w, h);
            GL.TexCoord(1, 0); GL.Vertex(w, 0);
            GL.End();

            //GL.Disable(OpenGL.GL_TEXTURE_2D);

            GL.Flush();
            // GL.Finish(); // not having this causes CL portion to take longer anyway

            PhaseTimer.Record("GL DrawQuad/Flush");

            IsDrawing = false;

            double TM = MultiTimer.StopTimer("OpenGLDraw");

            frameTimeOpenGL = TM;

            if (cyclic % 10 == 0)
            {
                // this.Text = (frameTimeOpenGL * 1000).ToString("N1") + " , " + (frameTimeOpenCL * 1000).ToString("N1");
            }

            // this.Text = MultiTimer.DebugString(true);

            float greenValue = Math.Abs(Util.DegSin(cyclic).ToFloat());
            cyclic++;

            RunOpenCLKernel(greenValue);
        }

        private void openGLControl_Resized(object sender, EventArgs e)
        {
            OpenGL GL = openGLControl.OpenGL;

            // only an issue if its smaller than the original control size, not clear why

            openGLControl.Width = this.ClientSize.Width;
            openGLControl.Height = this.ClientSize.Height;

            int w = openGLControl.ClientSize.Width; // this.ClientRectangle.Width;
            int h = openGLControl.ClientSize.Height; // this.ClientRectangle.Height;

            GL.Disable(OpenGL.GL_DEPTH_TEST);
            GL.Clear(OpenGL.GL_COLOR_BUFFER_BIT | OpenGL.GL_DEPTH_BUFFER_BIT);
            GL.MatrixMode(OpenGL.GL_PROJECTION);
            GL.LoadIdentity();
            GL.Ortho2D(0.0, w, h, 0.0); // 2D mode, pixel mode

            GL.LineWidth(1.0f);
            GL.PointSize(1.0f);

            GL.Enable(OpenGL.GL_TEXTURE_2D);
            GL.BindTexture(OpenGL.GL_TEXTURE_2D, mainTexture); // Bind the texture we changed in CL kernel
            GL.Color(1.0f, 1.0f, 1.0f, 0.1f); // needed for control to render anything
        }

        private void openGLControl_Load(object sender, EventArgs e)
        {
            //
        }

        private void openGLControl_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape)
            {
                Close(); // end program
            }

            //switch (e.KeyCode)
            //{
            //    case Keys.W:
            //        camera[0].vRotation -= 4f;
            //        break;
            //    case Keys.S:
            //        camera[0].vRotation += 4f;
            //        break;
            //    case Keys.A:
            //        camera[0].hRotation += 4f;
            //        break;
            //    case Keys.D:
            //        camera[0].hRotation -= 4f;
            //        break;

            //    case Keys.Up:
            //        camera[0].x += camera[0].forwardX * 0.3f;
            //        camera[0].y += camera[0].forwardY * 0.3f;
            //        camera[0].z += camera[0].forwardZ * 0.3f;
            //        break;
            //    case Keys.Down:
            //        camera[0].vRotation += 4f;
            //        break;
            //    case Keys.Left:
            //        camera[0].hRotation += 4f;
            //        break;
            //    case Keys.Right:
            //        camera[0].hRotation -= 4f;
            //        break;
            //}


        }

        private void MainWindow_FormClosing(object sender, FormClosingEventArgs e)
        {
            // cleanup CL
            CLImage.Dispose();
            kernel.Dispose();
            program.Dispose();
            queue.Dispose();
            context.Dispose();

            chunkHashBuffer.Dispose();
            //chunkDataBuffer.Dispose();
            textureBuffer.Dispose();
            cameraBuffer.Dispose();
        }

        private void openGLControl_MouseDown(object sender, MouseEventArgs e)
        {
            //Simulation.lastClickX = e.X;
            //Simulation.lastClickY = e.Y;
        }

        private void SharpGLForm_Load(object sender, EventArgs e)
        {
            //
        }

        private void SharpGLForm_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape)
            {
                Close(); // end program
            }

            float speed = 0.25f;

            switch (e.KeyCode)
            {
                case Keys.W:
                    camera[0].vRotation -= 4f;
                    break;
                case Keys.S:
                    camera[0].vRotation += 4f;
                    break;
                case Keys.A:
                    camera[0].hRotation += 4f;
                    break;
                case Keys.D:
                    camera[0].hRotation -= 4f;
                    break;

                case Keys.Up:
                    camera[0].x -= camera[0].forwardX * speed;
                    camera[0].y -= 0; // camera[0].forwardY * speed;
                    camera[0].z -= camera[0].forwardZ * speed;
                    break;
                case Keys.Down:
                    camera[0].x += camera[0].forwardX * speed;
                    camera[0].y += 0; // camera[0].forwardY * speed;
                    camera[0].z += camera[0].forwardZ * speed;
                    break;
                case Keys.Left:
                    camera[0].x -= camera[0].rightX * speed;
                    camera[0].y -= 0; // camera[0].rightY * speed;
                    camera[0].z -= camera[0].rightZ * speed;
                    break;
                case Keys.Right:
                    camera[0].x += camera[0].rightX * speed;
                    camera[0].y += 0; // camera[0].rightY * speed;
                    camera[0].z += camera[0].rightZ * speed;
                    break;
                case Keys.Space:
                    if (Math.Abs(gravityVelocity) < 0.01f)
                    {
                        jumpFrame = true;
                    }
                    break;
            }

        }

        private void SharpGLForm_PreviewKeyDown(object sender, PreviewKeyDownEventArgs e)
        {
            e.IsInputKey = true;
        }

        private void openGLControl_MouseMove(object sender, MouseEventArgs e)
        {
            mouseX = e.X;
            mouseY = e.Y;
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

}
