using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ObjC;

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

    // Projected Objective-C type into .NET
    class TestObjC : NSObject
    {
        private static readonly Class ClassType;
        private static readonly SEL DoubleIntSelector;
        private static readonly SEL DoubleFloatSelector;
        private static readonly SEL DoubleDoubleSelector;
        private static readonly SEL SetProxySelector;

        unsafe static TestObjC()
        {
            ClassType = Registrar.GetClass(typeof(TestObjC));
            DoubleIntSelector = xm.sel_registerName("doubleInt:");
            DoubleFloatSelector = xm.sel_registerName("doubleFloat:");
            DoubleDoubleSelector = xm.sel_registerName("doubleDouble:");
            SetProxySelector = xm.sel_registerName("setProxy:");
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
    }

    // Implemented dotnet type projected into Objective-C
    class TestDotNet : NSObject
    {
        private static readonly Class ClassType;
        private static readonly SEL MethodIntSelector;
        private static readonly SEL MethodFloatSelector;
        private static readonly SEL MethodDoubleSelector;

        unsafe static TestDotNet()
        {
            // Create the class.
            Class baseClass = Registrar.GetClass(typeof(NSObject));
            ClassType = xm.objc_allocateClassPair(baseClass, nameof(TestDotNet), 0);
            Registrar.RegisterClass(typeof(TestDotNet), ClassType);

            // Register and define the class's methods.
            {
                MethodIntSelector = xm.sel_registerName("doubleInt:");
                var impl = (IMP)(delegate* unmanaged[Cdecl]<id, SEL, int, int>)&DoubleIntProxy;
                xm.class_addMethod(ClassType, MethodIntSelector, impl, "i@:i");
            }

            {
                MethodFloatSelector = xm.sel_registerName("doubleFloat:");
                var impl = (IMP)(delegate* unmanaged[Cdecl]<id, SEL, float, float>)&DoubleFloatProxy;
                xm.class_addMethod(ClassType, MethodFloatSelector, impl, "f@:f");
            }

            {
                MethodDoubleSelector = xm.sel_registerName("doubleDouble:");
                var impl = (IMP)(delegate* unmanaged[Cdecl]<id, SEL, double, double>)&DoubleDoubleProxy;
                xm.class_addMethod(ClassType, MethodDoubleSelector, impl, "d@:d");
            }

            // Register the type with the Objective-C runtime.
            xm.objc_registerClassPair(ClassType);
        }

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        private static int DoubleIntProxy(id self, SEL sel, int a)
        {
            Internals.TryGetObject(self, out object managed);
            Console.WriteLine($"DoubleIntProxy = Self: {self} (Obj: {managed}), SEL: {sel}, a: {a}");
            return ((TestDotNet)managed).DoubleInt(a);
        }

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        private static float DoubleFloatProxy(id self, SEL sel, float a)
        {
            Internals.TryGetObject(self, out object managed);
            Console.WriteLine($"DoubleFloatProxy = Self: {self} (Obj: {managed}), SEL: {sel}, a: {a}");
            return ((TestDotNet)managed).DoubleFloat(a);
        }

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        private static double DoubleDoubleProxy(id self, SEL sel, double a)
        {
            Internals.TryGetObject(self, out object managed);
            Console.WriteLine($"DoubleDoubleProxy = Self: {self} (Obj: {managed}), SEL: {sel}, a: {a}");
            return ((TestDotNet)managed).DoubleDouble(a);
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

            var testDotNet = new TestDotNet();
            testObjC.SetProxy(testDotNet);
            Console.WriteLine($"Proxied-DoubleInt: {testObjC.DoubleInt((int)Math.PI)}");
            Console.WriteLine($"Proxied-DoubleFloat: {testObjC.DoubleFloat((float)Math.PI)}");
            Console.WriteLine($"Proxied-DoubleDouble: {testObjC.DoubleDouble(Math.PI)}");

            testObjC.SetProxy(null);
            Console.WriteLine($"DoubleInt: {testObjC.DoubleInt((int)Math.PI)}");
            Console.WriteLine($"DoubleFloat: {testObjC.DoubleFloat((float)Math.PI)}");
            Console.WriteLine($"DoubleDouble: {testObjC.DoubleDouble(Math.PI)}");
        }
    }
}
