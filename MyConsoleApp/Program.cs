using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ObjectiveC;
using System.Threading;

using ObjCRuntime;

using CreateObjectFlags = System.Runtime.InteropServices.ObjectiveC.CreateObjectFlags;

namespace MyConsoleApp
{
    public sealed class MyWrappers : Wrappers
    {
        public static readonly Wrappers Instance = new MyWrappers();

        protected override IntPtr ComputeInstClass(object instance, CreateInstFlags flags)
            => Registrar.GetClass(instance.GetType()).value;

        protected override object CreateObject(IntPtr instance, CreateObjectFlags flags)
        {
            string className = xm.object_getClassName(instance);

            var factory = Registrar.GetFactory(className);
            return factory(instance, flags);
        }

        protected override void EndThreadPoolWorkItem()
            => throw new NotImplementedException();

        protected override void StartThreadPoolWorkItem()
            => throw new NotImplementedException();
    }

    public static class Registrar
    {
        public delegate object FactoryFunc(id instance, CreateObjectFlags flags);

        public static void Initialize((Type mt, string nt, FactoryFunc factory)[] typeMapping)
        {
            foreach (var (mt, nt, factory) in typeMapping)
            {
                Class nClass = xm.objc_getClass(nt);
                RegisterClass(mt, nClass, factory);
            }
        }

        private static readonly ReaderWriterLockSlim RegisteredClassesLock = new ReaderWriterLockSlim();
        private static readonly Dictionary<Type, Class> RegisteredClasses = new Dictionary<Type, Class>();
        private static readonly Dictionary<string, FactoryFunc> RegisteredFactories = new Dictionary<string, FactoryFunc>();
        public static Class GetClass(Type type)
        {
            RegisteredClassesLock.EnterReadLock();

            try
            {
                if (!RegisteredClasses.TryGetValue(type, out Class klass))
                {
                    throw new Exception("Unknown class");
                }

                return klass;
            }
            finally
            {
                RegisteredClassesLock.ExitReadLock();
            }
        }

        public static FactoryFunc GetFactory(string className)
        {
            RegisteredClassesLock.EnterReadLock();

            try
            {
                if (!RegisteredFactories.TryGetValue(className, out FactoryFunc factory))
                {
                    throw new Exception("Unknown class");
                }

                return factory;
            }
            finally
            {
                RegisteredClassesLock.ExitReadLock();
            }
        }

        public static void RegisterClass(Type type, Class klass, FactoryFunc factory)
        {
            if (klass == xm.noclass)
            {
                throw new Exception("Invalid native class");
            }

            RegisteredClassesLock.EnterWriteLock();

            try
            {
                string className = xm.class_getName(klass);

                RegisteredClasses.Add(type, klass);
                RegisteredFactories.Add(className, factory);
            }
            finally
            {
                RegisteredClassesLock.ExitWriteLock();
            }
        }
    }

    // Base type for all Objective-C types.
    class NSObject
    {
        protected readonly id instance;

        public NSObject()
        {
            this.instance = MyWrappers.Instance.GetOrCreateInstForObject(this, CreateInstFlags.None);
        }

        internal NSObject(id instance)
        {
            MyWrappers.Instance.GetOrRegisterObjectForInst(instance.value, CreateObjectFlags.None, this);
            this.instance = instance;
        }

        protected NSObject(Class klass)
        {
            id instance = xm.class_createInstance(klass, 0);
            MyWrappers.Instance.GetOrRegisterObjectForInst(instance.value, CreateObjectFlags.None, this);
            this.instance = instance;
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

        public TestObjC()
            : base(ClassType)
        { }

        internal TestObjC(id instance)
            : base(instance)
        { }

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

                Wrappers.BlockProxy proxy = MyWrappers.Instance.CreateBlockProxy(block.value);
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
                else
                {
                    native = MyWrappers.Instance.GetOrCreateInstForObject(a, CreateInstFlags.None);
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
            Registrar.RegisterClass(
                typeof(TestDotNet),
                ClassType,
                (id inst, CreateObjectFlags flags) => throw new NotImplementedException());

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

            // Override the retain/release methods for memory management.
            {
                SEL retainSelector = xm.sel_registerName("retain");
                SEL releaseSelector = xm.sel_registerName("release");
                Wrappers.GetRetainReleaseMethods(out IntPtr retainImpl, out IntPtr releaseImpl);
                xm.class_addMethod(ClassType, retainSelector, new IMP(retainImpl), ":@:");
                xm.class_addMethod(ClassType, releaseSelector, new IMP(releaseImpl), "v@:");
            }

            // Register the type with the Objective-C runtime.
            xm.objc_registerClassPair(ClassType);
        }

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        private static int DoubleIntProxy(id self, SEL sel, int a)
        {
            unsafe
            {
                TestDotNet managed = Wrappers.IdDispatch.GetInstance<TestDotNet>((Wrappers.IdDispatch*)self.value);
                Trace.WriteLine($"DoubleIntProxy = Self: {self} (Obj: {managed}), SEL: {sel}, a: {a}");
                return managed.DoubleInt(a);
            }
        }

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        private static float DoubleFloatProxy(id self, SEL sel, float a)
        {
            unsafe
            {
                TestDotNet managed = Wrappers.IdDispatch.GetInstance<TestDotNet>((Wrappers.IdDispatch*)self.value);
                Trace.WriteLine($"DoubleFloatProxy = Self: {self} (Obj: {managed}), SEL: {sel}, a: {a}");
                return managed.DoubleFloat(a);
            }
        }

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        private static double DoubleDoubleProxy(id self, SEL sel, double a)
        {
            unsafe
            {
                TestDotNet managed = Wrappers.IdDispatch.GetInstance<TestDotNet>((Wrappers.IdDispatch*)self.value);
                Trace.WriteLine($"DoubleDoubleProxy = Self: {self} (Obj: {managed}), SEL: {sel}, a: {a}");
                return managed.DoubleDouble(a);
            }
        }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate double DoubleDoubleBlockProxy(id blk, double a);

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        // Should be returning `id` but can't because of non-primitive return.
        // See https://github.com/dotnet/runtime/issues/35928
        private static nint GetDoubleDoubleBlockProxy(id self, SEL sel)
        {
            unsafe
            {
                TestDotNet managed = Wrappers.IdDispatch.GetInstance<TestDotNet>((Wrappers.IdDispatch*)self.value);
                Trace.WriteLine($"GetDoubleDoubleBlockProxy = Self: {self} (Obj: {managed}), SEL: {sel}");

                DoubleDoubleBlock block = managed.GetDoubleDoubleBlock();
                DoubleDoubleBlockProxy proxy = (id blk, double a) =>
                {
                    Trace.WriteLine($"DoubleDoubleBlockProxy: id: {blk} a: {a}");
                    return block(a);
                };

                IntPtr fptr = Marshal.GetFunctionPointerForDelegate(proxy);
                return MyWrappers.Instance.CreateBlock(proxy, fptr, "d?d");
            }
        }

        public TestDotNet()
        { }

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

            Registrar.Initialize(new (Type, string, Registrar.FactoryFunc)[]
            {
                (typeof(NSObject), nameof(NSObject), (id i, CreateObjectFlags f) => new NSObject(i)),
                (typeof(TestObjC), nameof(TestObjC), (id i, CreateObjectFlags f) => new TestObjC(i)),
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
