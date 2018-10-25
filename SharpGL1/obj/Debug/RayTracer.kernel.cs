//------------------------------------------------------------------------------
// <auto-generated>
//     This code was generated by a tool.
//     Runtime Version:4.0.30319.42000
//
//     Changes to this file may cause incorrect behavior and will be lost if
//     the code is regenerated.
// </auto-generated>
//------------------------------------------------------------------------------

namespace RayTracer {
    using System.IO;
    using OpenCL.Net;
    using OpenCL.Net.Extensions;
    
    
    public class helloWorld : OpenCL.Net.Extensions.KernelWrapperBase {
        
        public helloWorld(OpenCL.Net.Context context) : 
                base(context) {
        }
        
        protected override string KernelPath {
            get {
                return System.IO.Path.Combine(System.AppDomain.CurrentDomain.BaseDirectory, "RayTracer.cl");
            }
        }
        
        protected override string OriginalKernelPath {
            get {
                return "C:\\Users\\cwinn\\Desktop\\Programming\\SharpGL Raytracer\\SharpGL1\\RayTracer.cl";
            }
        }
        
        protected override string KernelSource {
            get {
                return System.IO.File.ReadAllText(System.IO.Path.Combine(System.AppDomain.CurrentDomain.BaseDirectory, "RayTracer.cl"));
            }
        }
        
        protected override string KernelName {
            get {
                return "helloWorld";
            }
        }
        
        private OpenCL.Net.Event run(OpenCL.Net.CommandQueue commandQueue, OpenCL.Net.IMem bmp, uint globalWorkSize0, uint globalWorkSize1 = 0, uint globalWorkSize2 = 0, uint localWorkSize0 = 0, uint localWorkSize1 = 0, uint localWorkSize2 = 0, params OpenCL.Net.Event[] waitFor) {
            OpenCL.Net.Cl.SetKernelArg(this.Kernel, 0, bmp);
            OpenCL.Net.Event ev;
            OpenCL.Net.ErrorCode err;
            err = OpenCL.Net.Cl.EnqueueNDRangeKernel(commandQueue, this.Kernel, base.GetWorkDimension(globalWorkSize0, globalWorkSize1, globalWorkSize2), null, base.GetWorkSizes(globalWorkSize0, globalWorkSize1, globalWorkSize2), base.GetWorkSizes(localWorkSize0, localWorkSize1, localWorkSize2), ((uint)(waitFor.Length)), waitFor.Length == 0 ? null : waitFor, out ev);
            OpenCL.Net.Cl.Check(err);
            return ev;
        }
        
        public void Run(OpenCL.Net.CommandQueue commandQueue, OpenCL.Net.IMem bmp, uint globalWorkSize, uint localWorkSize = 0, params OpenCL.Net.Event[] waitFor) {
            OpenCL.Net.Event ev = this.run(commandQueue, bmp, globalWorkSize0: globalWorkSize, localWorkSize0: localWorkSize, waitFor: waitFor);
            ev.Wait();
        }
        
        public OpenCL.Net.Event EnqueueRun(OpenCL.Net.CommandQueue commandQueue, OpenCL.Net.IMem bmp, uint globalWorkSize, uint localWorkSize = 0, params OpenCL.Net.Event[] waitFor) {
            return this.run(commandQueue, bmp, globalWorkSize0: globalWorkSize, localWorkSize0: localWorkSize, waitFor: waitFor);
        }
        
        public void Run(OpenCL.Net.CommandQueue commandQueue, OpenCL.Net.IMem bmp, uint globalWorkSize0, uint globalWorkSize1, uint localWorkSize0 = 0, uint localWorkSize1 = 0, params OpenCL.Net.Event[] waitFor) {
            OpenCL.Net.Event ev = this.run(commandQueue, bmp, globalWorkSize0: globalWorkSize0, globalWorkSize1: globalWorkSize1, localWorkSize0: localWorkSize0, localWorkSize1: localWorkSize1, waitFor: waitFor);
            ev.Wait();
        }
        
        public OpenCL.Net.Event EnqueueRun(OpenCL.Net.CommandQueue commandQueue, OpenCL.Net.IMem bmp, uint globalWorkSize0, uint globalWorkSize1, uint localWorkSize0 = 0, uint localWorkSize1 = 0, params OpenCL.Net.Event[] waitFor) {
            return this.run(commandQueue, bmp, globalWorkSize0: globalWorkSize0, globalWorkSize1: globalWorkSize1, localWorkSize0: localWorkSize0, localWorkSize1: localWorkSize1, waitFor: waitFor);
        }
        
        public void Run(OpenCL.Net.CommandQueue commandQueue, OpenCL.Net.IMem bmp, uint globalWorkSize0, uint globalWorkSize1, uint globalWorkSize2, uint localWorkSize0 = 0, uint localWorkSize1 = 0, uint localWorkSize2 = 0, params OpenCL.Net.Event[] waitFor) {
            OpenCL.Net.Event ev = this.run(commandQueue, bmp, globalWorkSize0: globalWorkSize0, globalWorkSize1: globalWorkSize1, globalWorkSize2: globalWorkSize2, localWorkSize0: localWorkSize0, localWorkSize1: localWorkSize1, localWorkSize2: localWorkSize2, waitFor: waitFor);
            ev.Wait();
        }
        
        public OpenCL.Net.Event EnqueueRun(OpenCL.Net.CommandQueue commandQueue, OpenCL.Net.IMem bmp, uint globalWorkSize0, uint globalWorkSize1, uint globalWorkSize2, uint localWorkSize0 = 0, uint localWorkSize1 = 0, uint localWorkSize2 = 0, params OpenCL.Net.Event[] waitFor) {
            return this.run(commandQueue, bmp, globalWorkSize0: globalWorkSize0, globalWorkSize1: globalWorkSize1, globalWorkSize2: globalWorkSize2, localWorkSize0: localWorkSize0, localWorkSize1: localWorkSize1, localWorkSize2: localWorkSize2, waitFor: waitFor);
        }
    }
}