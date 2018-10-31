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
using System.Collections.Concurrent;
using System.Threading.Tasks;

namespace SharpGLCudafy
{
    public partial class SharpGLForm : Form
    {
        [DllImport("user32.dll")]
        public static extern int GetKeyboardState(byte[] keystate);

        public bool IsDrawing;
        ComputeImage2D CLImage;
        ComputeCommandQueue queue;
        ComputeKernel kernel;
        List<ComputeMemory> CLLockObjects;
        ComputeProgram program;
        ComputeContext context;

        ChunkHashTable hashTable;
        ComputeBuffer<ChunkData> chunkHashBuffer;

        ComputeBuffer<Camera> cameraBuffer;
        Camera[] camera = new Camera[3];

        ComputeBuffer<float> textureBuffer;
        uint mainTexture;

        float cyclic = 0f;

        int mouseX;
        int mouseY;

        float gravityVelocity = 0.0f;
        bool jumpFrame = false;

        byte[] keyState = new byte[256];

        public SharpGLForm()
        {
            Directory.CreateDirectory(Environment.CurrentDirectory + "\\chunkdata");

            hashTable = new ChunkHashTable(0.7f);

            InitializeComponent();
            this.openGLControl.DrawFPS = false;
            this.Show();

            openGLControl.Width = this.ClientSize.Width;
            openGLControl.Height = this.ClientSize.Height;

            mouseX = openGLControl.ClientSize.Width / 2;
            mouseY = openGLControl.ClientSize.Height / 2;
        }

        unsafe public void DoPhysics()
        {
            int cx = Util.ChunkFromVoxel(camera[0].x);
            int cy = Util.ChunkFromVoxel(camera[0].y + 1); // test player height
            int cz = Util.ChunkFromVoxel(camera[0].z);

            int offsetX = (int)Math.Floor(camera[0].x) & (Util.chunkSize - 1);
            int offsetY = (int)Math.Floor(camera[0].y) & (Util.chunkSize - 1);
            int offsetZ = (int)Math.Floor(camera[0].z) & (Util.chunkSize - 1);

            LoadChunk(cx, cy, cz);
            ChunkData cd = hashTable.Find(cx, cy, cz);

            int vIndex = (offsetX + offsetY * Util.chunkSize + offsetZ * Util.chunkSize * Util.chunkSize);
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

            int checkRange = 3;

            //Parallel.For(-16, 16 + 1, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount - 1 }, dx =>
            {
                for (int dx = -checkRange; dx <= checkRange; dx++)
                {
                    for (int dy = -checkRange; dy <= checkRange; dy++)
                    {
                        for (int dz = -checkRange; dz <= checkRange; dz++)
                        {
                            LoadChunk(cx + dx, cy + dy, cz + dz);
                        }
                    }
                }
            //});            
            }            

            int removeCount = 0;
            int removeRange = 4;

            removeCount = hashTable.RemoveOutsideViewRange(cx, cy, cz, removeRange);

            return removeCount;
        }

        public int LoadChunk(int chunkX, int chunkY, int chunkZ)
        {
            // 1 check cache
            ChunkData cd = hashTable.Find(chunkX, chunkY, chunkZ);

            if (cd.valid == 1)
            {
                return 1;
            }

            // 2 check disk
            cd = Util.LoadChunkFromDisk(chunkX, chunkY, chunkZ);

            if (cd.valid == 1)
            {
                hashTable.Insert(chunkX, chunkY, chunkZ, cd);
                return 2;
            }

            // 3 generate new chunk
            int result = GenerateChunk(chunkX, chunkY, chunkZ);
            return 3;
        }

        unsafe public int GenerateChunk(int chunkX, int chunkY, int chunkZ)
        {
            ChunkData cd = new ChunkData();

            int size = Util.chunkSize;

            // do init
            cd.valid = 1;
            cd.size = size;
            cd.empty = 1;
            cd.generated = 1;

            cd.chunkX = chunkX;
            cd.chunkY = chunkY;
            cd.chunkZ = chunkZ;

            RandomProvider rnd = new RandomProvider();

            for (int dx = 0; dx < size; dx++)
            {
                for (int dy = 0; dy < size; dy++)
                {
                    for (int dz = 0; dz < size; dz++)
                    {
                        int vIndex = (dx + dy * size + dz * size * size);

                        cd.voxelData[vIndex] = 0;

                        if (chunkY > 0)
                        {
                            byte b = (byte)rnd.Next(1, 64 + 1);
                            cd.voxelData[vIndex] = b;
                            cd.empty = 0;
                        }
                    }
                }
            }

            Util.SaveChunkToDisk(cd);

            hashTable.Insert(chunkX, chunkY, chunkZ, cd);

            return 1;
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

            // pick first platform
            ComputePlatform platform = ComputePlatform.Platforms[0];

            // Create CL context properties, add WGL context & handle to DC
            IntPtr openGLContextHandle = GL.RenderContextProvider.RenderContextHandle;
            IntPtr deviceContextHandle = GL.RenderContextProvider.DeviceContextHandle;

            // Create the context property list and populate it.
            ComputeContextProperty p1 = new ComputeContextProperty(ComputeContextPropertyName.Platform, platform.Handle.Value);
            ComputeContextProperty p2 = new ComputeContextProperty(ComputeContextPropertyName.CL_GL_CONTEXT_KHR, openGLContextHandle);
            ComputeContextProperty p3 = new ComputeContextProperty(ComputeContextPropertyName.CL_WGL_HDC_KHR, deviceContextHandle);
            ComputeContextPropertyList cpl = new ComputeContextPropertyList(new ComputeContextProperty[] { p1, p2, p3 });

            // Create the context. Usually, you'll want this on a GPU but other options might be available as well.
            context = new ComputeContext(ComputeDeviceTypes.Gpu, cpl, null, IntPtr.Zero);

            // create a command queue with first gpu found
            queue = new ComputeCommandQueue(context, context.Devices[0], ComputeCommandQueueFlags.None);

            string clSource = File.ReadAllText("RayTracer.cl");

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

            kernel = program.CreateKernel("RayTraceMain");

            CLImage = ComputeImage2D.CreateFromGLTexture2D(queue.Context, ComputeMemoryFlags.WriteOnly, (int)OpenGL.GL_TEXTURE_2D, 0, (int)textures[0]);

            CLLockObjects = new List<ComputeMemory>() { CLImage };

            cameraBuffer = new ComputeBuffer<Camera>(context, ComputeMemoryFlags.ReadWrite | ComputeMemoryFlags.CopyHostPointer, camera);
            chunkHashBuffer = new ComputeBuffer<ChunkData>(context, ComputeMemoryFlags.ReadOnly | ComputeMemoryFlags.CopyHostPointer, hashTable.values);

            Image img = Image.FromFile("texturepacked.png");
            Bitmap bmp = new Bitmap(img);

            List<float> imgData = new List<float>();

            BitmapData bmpData = bmp.LockBits(new Rectangle(0, 0, img.Width, img.Height), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
            var byteLen = bmpData.Stride * bmpData.Height;
            byte[] bytes = new byte[byteLen];
            Marshal.Copy(bmpData.Scan0, bytes, 0, byteLen);
            bmp.UnlockBits(bmpData);

            for (int y = 0; y < img.Height; y++)
            {
                for (int x = 0; x < img.Width; x++)
                {
                    int pos = ((y * img.Width) * 3) + (x * 3);

                    imgData.Add(bytes[pos + 0] / 256f);
                    imgData.Add(bytes[pos + 1] / 256f);
                    imgData.Add(bytes[pos + 2] / 256f);
                }
            }

            textureBuffer = new ComputeBuffer<float>(context, ComputeMemoryFlags.ReadOnly | ComputeMemoryFlags.CopyHostPointer, imgData.ToArray());

            RunOpenCLKernel(); // initialize CL
        }

        public void RunOpenCLKernel()
        {
            OpenGL GL = openGLControl.OpenGL;

            int w = openGLControl.ClientSize.Width;
            int h = openGLControl.ClientSize.Height;

            long[] globalWorkOffset = new long[] { 0, 0, 0 };
            long[] globalWorkSize = new long[] { w, h };
            long[] localWorkSize = new long[] { 16, 16 };

            queue.AcquireGLObjects(CLLockObjects, null);

            // sync data to GPU
            queue.WriteToBuffer(camera, cameraBuffer, true, null);

            if (hashTable.GPUSizeUpdateRequired == true)
            {
                chunkHashBuffer.Dispose(); // discard old buffer
                chunkHashBuffer = new ComputeBuffer<ChunkData>(context, ComputeMemoryFlags.ReadOnly | ComputeMemoryFlags.CopyHostPointer, hashTable.values); // realloc size on GPU
                hashTable.GPUSizeUpdateRequired = false;
            }
            else
            {
                queue.WriteToBuffer(hashTable.values, chunkHashBuffer, true, null); // update chunk hash data to GPU
            }

            PhaseTimer.Record("WriteToBuffer()");

            kernel.SetMemoryArgument(0, CLImage);
            kernel.SetMemoryArgument(1, cameraBuffer);
            kernel.SetMemoryArgument(2, textureBuffer);
            kernel.SetMemoryArgument(3, chunkHashBuffer);
            kernel.SetValueArgument(4, hashTable.maxOffset);
            kernel.SetValueArgument(5, hashTable.currentArraySize);
            kernel.SetValueArgument(6, (float)mouseX / (float)openGLControl.ClientSize.Width);
            kernel.SetValueArgument(7, (float)mouseY / (float)openGLControl.ClientSize.Height);

            queue.Execute(kernel, globalWorkOffset, globalWorkSize, localWorkSize, null);

            PhaseTimer.Record("Execute() kernel");

            CheckViewChunks(); // CPU algo can run while kernel is running, as long as it's not a longer run time (which would delay the loop)

            PhaseTimer.Record("CheckViewChunks()");

            queue.ReleaseGLObjects(CLLockObjects, null);

            queue.Finish(); // required to render the buffer

            queue.ReadFromBuffer(cameraBuffer, ref camera, true, null);

            camera[1] = camera[2]; // update

            DoPhysics(); // update cam[0]

            PhaseTimer.Record("Finish()");

            string text = PhaseTimer.Dump();
            text += " Chunks: " + hashTable.recordCount + " / " + hashTable.values.Length + "; GPU bytes xfer: " + (hashTable.values.Length * Marshal.SizeOf(typeof(ChunkData)) / 1024) + "KB";

            if (cyclic % 5 == 0)
            {
                this.Text = text;
            }
        }

        // frame drawing code, it has its own draw timer
        private void openGLControl_OpenGLDraw(object sender, RenderEventArgs e)
        {
            IsDrawing = true;

            PhaseTimer.Start();

            int w = openGLControl.ClientSize.Width;
            int h = openGLControl.ClientSize.Height;

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
            // GL.Finish(); // not really needed for CL sync?

            PhaseTimer.Record("GL DrawQuad/Flush");

            IsDrawing = false;

            cyclic++; // frame counter

            RunOpenCLKernel();
            CheckKeyboardInput();
        }

        private void openGLControl_Resized(object sender, EventArgs e)
        {
            OpenGL GL = openGLControl.OpenGL;

            // only an issue if its smaller than the original control size, not clear why

            openGLControl.Width = this.ClientSize.Width;
            openGLControl.Height = this.ClientSize.Height;

            int w = openGLControl.ClientSize.Width;
            int h = openGLControl.ClientSize.Height;

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

        private void MainWindow_FormClosing(object sender, FormClosingEventArgs e)
        {
            // cleanup CL
            CLImage.Dispose();
            kernel.Dispose();
            program.Dispose();
            queue.Dispose();
            context.Dispose();

            // buffers here
            chunkHashBuffer.Dispose();
            textureBuffer.Dispose();
            cameraBuffer.Dispose();

            // save chunk state
            hashTable.SaveAllChunksToDisk();
        }

        private void openGLControl_MouseDown(object sender, MouseEventArgs e)
        {
            //Simulation.lastClickX = e.X;
            //Simulation.lastClickY = e.Y;
        }

        public bool CheckKeyDown(Keys key)
        {
            if ((keyState[(int)key] & 128) == 128)
            {
                return true;
            }

            return false;
        }

        public bool CheckKeyUp(Keys key)
        {
            if ((keyState[(int)key] & 128) == 0)
            {
                return true;
            }

            return false;
        }

        public void CheckKeyboardInput()
        {
            GetKeyboardState(keyState);

            if (CheckKeyDown(Keys.Escape)) { Close(); return; }

            float speed = 0.45f;

            if (CheckKeyDown(Keys.W)) { camera[0].vRotation -= 4f; }
            if (CheckKeyDown(Keys.S)) { camera[0].vRotation += 4f; }
            if (CheckKeyDown(Keys.A)) { camera[0].hRotation += 4f; }
            if (CheckKeyDown(Keys.D)) { camera[0].hRotation -= 4f; }

            if (CheckKeyDown(Keys.Up))
            {
                camera[0].x -= camera[0].forwardX * speed;
                camera[0].y -= 0; // camera[0].forwardY * speed;
                camera[0].z -= camera[0].forwardZ * speed;
            }

            if (CheckKeyDown(Keys.Down))
            {
                camera[0].x += camera[0].forwardX * speed;
                camera[0].y += 0; // camera[0].forwardY * speed;
                camera[0].z += camera[0].forwardZ * speed;
            }

            if (CheckKeyDown(Keys.Left))
            {
                camera[0].x -= camera[0].rightX * speed;
                camera[0].y -= 0; // camera[0].rightY * speed;
                camera[0].z -= camera[0].rightZ * speed;
            }

            if (CheckKeyDown(Keys.Right))
            {
                camera[0].x += camera[0].rightX * speed;
                camera[0].y += 0; // camera[0].rightY * speed;
                camera[0].z += camera[0].rightZ * speed;
            }

            if (CheckKeyDown(Keys.Space))
            {
                if (Math.Abs(gravityVelocity) < 0.01f)
                {
                    jumpFrame = true;
                }
            }
        }

        private void openGLControl_MouseMove(object sender, MouseEventArgs e)
        {
            mouseX = e.X;
            mouseY = e.Y;
        }
    }
}
