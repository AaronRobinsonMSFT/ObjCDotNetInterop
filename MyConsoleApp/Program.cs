using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ObjectiveC;

namespace MyConsoleApp
{
    // Base type for all Objective-C types.
    class NSObject
    {
        private static readonly Class ClassType;
        unsafe static NSObject()
        {
            ClassType = Registrar.GetClass(typeof(NSObject));
        }

        protected readonly id instance;

        public NSObject()
        {
            this.instance = xm.class_createInstance(ClassType, 0);
            Internals.RegisterIdentity(this, this.instance);
        }

        protected NSObject(Class klass)
        {
            this.instance = xm.class_createInstance(klass, 0);
            Internals.RegisterIdentity(this, this.instance);
        }

        ~NSObject()
        {
            xm.objc_destructInstance(this.instance);
        }
    }

    public delegate double DoubleDoubleBlock(double a);

    // Projected Objective-C type into .NET
    class TestObjC : NSObject
    {
        private static readonly Class ClassType;
        private static readonly SEL DoubleIntSelector;
        private static readonly SEL DoubleFloatSelector;
        private static readonly SEL DoubleDoubleSelector;
        private static readonly SEL GetDoubleDoubleBlockSelector;
        private static readonly SEL SetProxySelector;
        private static readonly SEL CallDoubleDoubleBlockThroughProxySelector;

        unsafe static TestObjC()
        {
            ClassType = Registrar.GetClass(typeof(TestObjC));
            DoubleIntSelector = xm.sel_registerName("doubleInt:");
            DoubleFloatSelector = xm.sel_registerName("doubleFloat:");
            DoubleDoubleSelector = xm.sel_registerName("doubleDouble:");
            GetDoubleDoubleBlockSelector = xm.sel_registerName("getDoubleDoubleBlock");
            SetProxySelector = xm.sel_registerName("setProxy:");
            CallDoubleDoubleBlockThroughProxySelector = xm.sel_registerName("callDoubleDoubleBlockThroughProxy:");
        }

        public TestObjC() : base(ClassType) { }

        protected TestObjC(Class klass) : base(klass) { }

        public int DoubleInt(int a)
        {
            unsafe
            {
                return ((delegate* unmanaged[Cdecl]<id, SEL, int, int>)xm.objc_msgSend_Raw)(this.instance, DoubleIntSelector, a);
            }
        }

        public float DoubleFloat(float a)
        {
            unsafe
            {
                return ((delegate* unmanaged[Cdecl]<id, SEL, float, float>)xm.objc_msgSend_Raw)(this.instance, DoubleFloatSelector, a);
            }
        }

        public double DoubleDouble(double a)
        {
            unsafe
            {
                return ((delegate* unmanaged[Cdecl]<id, SEL, double, double>)xm.objc_msgSend_Raw)(this.instance, DoubleDoubleSelector, a);
            }
        }

        public DoubleDoubleBlock GetDoubleDoubleBlock()
        {
            unsafe
            {
                id block = ((delegate* unmanaged[Cdecl]<id, SEL, id>)xm.objc_msgSend_Raw)(this.instance, GetDoubleDoubleBlockSelector);
                if (block == xm.nil)
                {
                    return null;
                }

                BlockMarshaller.BlockProxy proxy = BlockMarshaller.BlockToProxy(block);
                return new DoubleDoubleBlock(
                    (double d) =>
                    {
                        double ret = ((delegate* unmanaged[Cdecl]<id, double, double>)proxy.FunctionPointer.ToPointer())(proxy.Block, d);
                        return ret;
                    });
            }
        }

        public void SetProxy(TestDotNet a)
        {
            unsafe
            {
                id native;
                if (a == null)
                {
                    native = xm.nil;
                }
                else if (!Internals.TryGetIdentity(a, out native))
                {
                    throw new Exception("No native mapping");
                }

                ((delegate* unmanaged[Cdecl]<id, SEL, id, void>)xm.objc_msgSend_Raw)(this.instance, SetProxySelector, native);
            }
        }

        public double CallDoubleDoubleBlockThroughProxy(double a)
        {
            unsafe
            {
                return ((delegate* unmanaged[Cdecl]<id, SEL, double, double>)xm.objc_msgSend_Raw)(this.instance, CallDoubleDoubleBlockThroughProxySelector, a);
            }
        }
    }

    // Implemented dotnet type projected into Objective-C
    class TestDotNet : NSObject
    {
        private static readonly Class ClassType;
        private static readonly SEL DoubleIntSelector;
        private static readonly SEL DoubleFloatSelector;
        private static readonly SEL DoubleDoubleSelector;
        private static readonly SEL GetDoubleDoubleBlockSelector;

        unsafe static TestDotNet()
        {
            // Create the class.
            Class baseClass = Registrar.GetClass(typeof(NSObject));
            ClassType = xm.objc_allocateClassPair(baseClass, nameof(TestDotNet), 0);
            Registrar.RegisterClass(typeof(TestDotNet), ClassType);

            // Register and define the class's methods.
            {
                DoubleIntSelector = xm.sel_registerName("doubleInt:");
                var impl = (IMP)(delegate* unmanaged[Cdecl]<id, SEL, int, int>)&DoubleIntProxy;
                xm.class_addMethod(ClassType, DoubleIntSelector, impl, "i@:i");
            }

            {
                DoubleFloatSelector = xm.sel_registerName("doubleFloat:");
                var impl = (IMP)(delegate* unmanaged[Cdecl]<id, SEL, float, float>)&DoubleFloatProxy;
                xm.class_addMethod(ClassType, DoubleFloatSelector, impl, "f@:f");
            }

            {
                DoubleDoubleSelector = xm.sel_registerName("doubleDouble:");
                var impl = (IMP)(delegate* unmanaged[Cdecl]<id, SEL, double, double>)&DoubleDoubleProxy;
                xm.class_addMethod(ClassType, DoubleDoubleSelector, impl, "d@:d");
            }

            {
                GetDoubleDoubleBlockSelector = xm.sel_registerName("getDoubleDoubleBlock");
                var impl = (IMP)(delegate* unmanaged[Cdecl]<id, SEL, nint>)&GetDoubleDoubleBlockProxy;
                xm.class_addMethod(ClassType, GetDoubleDoubleBlockSelector, impl, "?@:");
            }

            // Register the type with the Objective-C runtime.
            xm.objc_registerClassPair(ClassType);
        }

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        private static int DoubleIntProxy(id self, SEL sel, int a)
        {
            Internals.TryGetObject(self, out object managed);
            Trace.WriteLine($"DoubleIntProxy = Self: {self} (Obj: {managed}), SEL: {sel}, a: {a}");
            return ((TestDotNet)managed).DoubleInt(a);
        }

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        private static float DoubleFloatProxy(id self, SEL sel, float a)
        {
            Internals.TryGetObject(self, out object managed);
            Trace.WriteLine($"DoubleFloatProxy = Self: {self} (Obj: {managed}), SEL: {sel}, a: {a}");
            return ((TestDotNet)managed).DoubleFloat(a);
        }

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        private static double DoubleDoubleProxy(id self, SEL sel, double a)
        {
            Internals.TryGetObject(self, out object managed);
            Trace.WriteLine($"DoubleDoubleProxy = Self: {self} (Obj: {managed}), SEL: {sel}, a: {a}");
            return ((TestDotNet)managed).DoubleDouble(a);
        }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate double DoubleDoubleBlockProxy(id blk, double a);

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        // Should be returning `id` but can't because of non-primitive return.
        // See https://github.com/dotnet/runtime/issues/35928
        private static nint GetDoubleDoubleBlockProxy(id self, SEL sel)
        {
            Internals.TryGetObject(self, out object managed);
            Trace.WriteLine($"GetDoubleDoubleBlockProxy = Self: {self} (Obj: {managed}), SEL: {sel}");

            DoubleDoubleBlock block = ((TestDotNet)managed).GetDoubleDoubleBlock();
            DoubleDoubleBlockProxy proxy = (id blk, double a) =>
            {
                Trace.WriteLine($"DoubleDoubleBlockProxy: id: {blk} a: {a}");
                return block(a);
            };

            IntPtr fptr = Marshal.GetFunctionPointerForDelegate(proxy);
            var handle = GCHandle.Alloc(proxy);
            return BlockMarshaller.CreateBlock(ref handle, fptr, "d?d").value;
        }

        public TestDotNet()
            : base(ClassType)
        {
        }

        public int DoubleInt(int a)
        {
            return a * 2;
        }

        public float DoubleFloat(float a)
        {
            return a * 2;
        }

        public double DoubleDouble(double a)
        {
            return a * 2;
        }

        public DoubleDoubleBlock GetDoubleDoubleBlock()
        {
            return (a) => this.DoubleDouble(a);
        }
    }

    unsafe class Program
    {
        static void Main(string[] args)
        {
            xm.Initialize();

            Registrar.Initialize(new[]
            {
                (typeof(NSObject), nameof(NSObject)),
                (typeof(TestObjC), nameof(TestObjC)),
            });

            var testObjC = new TestObjC();
            Console.WriteLine($"DoubleInt: {testObjC.DoubleInt((int)Math.PI)}");
            Console.WriteLine($"DoubleFloat: {testObjC.DoubleFloat((float)Math.PI)}");
            Console.WriteLine($"DoubleDouble: {testObjC.DoubleDouble(Math.PI)}");

            // Get delegate early
            var doubleDoubleDel = testObjC.GetDoubleDoubleBlock();

            var testDotNet = new TestDotNet();
            testObjC.SetProxy(testDotNet);
            Console.WriteLine($"Proxied-DoubleInt: {testObjC.DoubleInt((int)Math.PI)}");
            Console.WriteLine($"Proxied-DoubleFloat: {testObjC.DoubleFloat((float)Math.PI)}");
            Console.WriteLine($"Proxied-DoubleDouble: {testObjC.DoubleDouble(Math.PI)}");
            Console.WriteLine($"Proxied-DoubleDouble Block: {testObjC.CallDoubleDoubleBlockThroughProxy(Math.PI)}");

            testObjC.SetProxy(null);
            Console.WriteLine($"DoubleInt: {testObjC.DoubleInt((int)Math.PI)}");
            Console.WriteLine($"DoubleFloat: {testObjC.DoubleFloat((float)Math.PI)}");
            Console.WriteLine($"DoubleDouble: {testObjC.DoubleDouble(Math.PI)}");

            Console.WriteLine($"DoubleDouble Block: {doubleDoubleDel(Math.PI)}");
        }
    }
}
