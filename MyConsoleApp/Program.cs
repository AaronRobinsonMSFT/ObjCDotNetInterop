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

        protected override IntPtr ComputeInstanceClass(object instance, CreateInstanceFlags flags)
            => Registrar.GetClass(instance.GetType()).value;

        [UnmanagedCallersOnly]
        private unsafe static int ABI_IntBlock(BlockDispatch* b, int a)
        {
            return BlockDispatch.GetInstance<IntBlock>(b)(a);
        }

        protected override IntPtr GetBlockInvokeAndSignature(Delegate del, CreateInstanceFlags flags, out string signature)
        {
            if (del is IntBlock)
            {
                signature = "i?i";
                unsafe
                {
                    return (IntPtr)(delegate* unmanaged<BlockDispatch*, int, int>)&ABI_IntBlock;
                }
            }

            throw new NotSupportedException();
        }

        protected override object CreateObject(IntPtr instance, CreateObjectFlags flags)
        {
            string className = xm.object_getClassName(instance);

            var factory = Registrar.GetFactory(className);
            return factory(instance, flags);
        }

        public override void GetMessageSendCallbacks(
            out IntPtr objc_msgSend,
            out IntPtr objc_msgSend_fpret,
            out IntPtr objc_msgSend_stret,
            out IntPtr objc_msgSendSuper,
            out IntPtr objc_msgSendSuper_stret)
        {
            objc_msgSend = IntPtr.Zero;
            objc_msgSend_fpret = IntPtr.Zero;
            objc_msgSend_stret = IntPtr.Zero;
            objc_msgSendSuper = IntPtr.Zero;
            objc_msgSendSuper_stret = IntPtr.Zero;
        }
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

        /// <summary>
        /// Called for .NET types projected into Objective-C.
        /// </summary>
        public NSObject()
        {
            this.instance = MyWrappers.Instance.GetOrCreateInstanceForObject(this, CreateInstanceFlags.None);
        }

        /// <summary>
        /// Called for existing Objective-C types entering .NET.
        /// </summary>
        /// <param name="instance"></param>
        public NSObject(id instance)
        {
            MyWrappers.Instance.GetOrRegisterObjectForInstance(instance.value, CreateObjectFlags.None, this);
            this.instance = instance;
        }

        /// <summary>
        /// Called for instantiating Objective-C types in .NET.
        /// </summary>
        /// <param name="klass"></param>
        protected NSObject(Class klass)
            : this(xm.class_createInstance(klass, 0))
        {
        }
    }

    public delegate int IntBlock(int a);

    // Projected Objective-C type into .NET
    class TestObjC : NSObject
    {

        private static readonly Class ClassType;
        private static readonly SEL DoubleFloatSelector;
        private static readonly SEL DoubleDoubleSelector;
        private static readonly SEL UsePropertiesSelector;

        private static readonly SEL GetIntBlockPropSelector;
        private static readonly SEL SetIntBlockPropSelector;
        private static readonly SEL GetIntBlockPropStaticSelector;
        private static readonly SEL SetIntBlockPropStaticSelector;

        unsafe static TestObjC()
        {
            ClassType = Registrar.GetClass(typeof(TestObjC));
            DoubleFloatSelector = xm.sel_registerName("doubleFloat:");
            DoubleDoubleSelector = xm.sel_registerName("doubleDouble:");
            UsePropertiesSelector = xm.sel_registerName("useProperties");

            GetIntBlockPropSelector = xm.sel_registerName("intBlockProp");
            SetIntBlockPropSelector = xm.sel_registerName("setIntBlockProp:");
            GetIntBlockPropStaticSelector = xm.sel_registerName("intBlockPropStatic");
            SetIntBlockPropStaticSelector = xm.sel_registerName("setIntBlockPropStatic:");
        }

        public static IntBlock IntBlockPropStatic
        {
            get
            {
                unsafe
                {
                    id block = ((delegate* unmanaged[Cdecl]<Class, SEL, id>)xm.objc_msgSend_Raw)(ClassType, GetIntBlockPropStaticSelector);
                    if (block.value == xm.nil)
                    {
                        return null;
                    }

                    return (IntBlock)MyWrappers.Instance.GetOrCreateObjectForInstance(block.value, CreateObjectFlags.Block);
                }
            }
            set
            {
                unsafe
                {
                    id block = xm.nil;
                    if (value != null)
                    {
                        block = MyWrappers.Instance.GetOrCreateInstanceForObject(value, CreateInstanceFlags.Block);
                    }

                    ((delegate* unmanaged[Cdecl]<Class, SEL, id, void>)xm.objc_msgSend_Raw)(ClassType, SetIntBlockPropStaticSelector, block);
                }
            }
        }

        public TestObjC()
            : base(ClassType)
        { }

        internal TestObjC(id instance)
            : base(instance)
        { }

        public IntBlock IntBlockProp
        {
            get
            {
                unsafe
                {
                    id block = ((delegate* unmanaged[Cdecl]<id, SEL, id>)xm.objc_msgSend_Raw)(this.instance, GetIntBlockPropSelector);
                    if (block.value == xm.nil)
                    {
                        return null;
                    }

                    return (IntBlock)MyWrappers.Instance.GetOrCreateObjectForInstance(block.value, CreateObjectFlags.Block);
                }
            }
            set
            {
                unsafe
                {
                    id block = xm.nil;
                    if (value != null)
                    {
                        block = MyWrappers.Instance.GetOrCreateInstanceForObject(value, CreateInstanceFlags.Block);
                    }

                    ((delegate* unmanaged[Cdecl]<id, SEL, id, void>)xm.objc_msgSend_Raw)(this.instance, SetIntBlockPropSelector, block);
                }
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

        public void UseProperties()
        {
            unsafe
            {
                ((delegate* unmanaged[Cdecl]<id, SEL, void>)xm.objc_msgSend_Raw)(this.instance, UsePropertiesSelector);
            }
        }
    }

    // Implemented dotnet type projected into Objective-C
    class TestDotNet : NSObject
    {
        //private static readonly Class ClassType;

        //unsafe static TestDotNet()
        //{
        //    // Create the class.
        //    Class baseClass = Registrar.GetClass(typeof(NSObject));
        //    ClassType = xm.objc_allocateClassPair(baseClass, nameof(TestDotNet), 0);
        //    Registrar.RegisterClass(
        //        typeof(TestDotNet),
        //        ClassType,
        //        (id inst, CreateObjectFlags flags) => throw new NotImplementedException());

        //    // Register and define the class's methods.
        //    {
        //        SEL DoubleIntSelector = xm.sel_registerName("doubleInt:");
        //        var impl = (IMP)(delegate* unmanaged[Cdecl]<id, SEL, int, int>)&DoubleIntProxy;
        //        xm.class_addMethod(ClassType, DoubleIntSelector, impl, "i@:i");
        //    }

        //    {
        //        SEL DoubleFloatSelector = xm.sel_registerName("doubleFloat:");
        //        var impl = (IMP)(delegate* unmanaged[Cdecl]<id, SEL, float, float>)&DoubleFloatProxy;
        //        xm.class_addMethod(ClassType, DoubleFloatSelector, impl, "f@:f");
        //    }

        //    {
        //        SEL DoubleDoubleSelector = xm.sel_registerName("doubleDouble:");
        //        var impl = (IMP)(delegate* unmanaged[Cdecl]<id, SEL, double, double>)&DoubleDoubleProxy;
        //        xm.class_addMethod(ClassType, DoubleDoubleSelector, impl, "d@:d");
        //    }

        //    {
        //        SEL GetDoubleDoubleBlockPropSelector = xm.sel_registerName("doubleDoubleBlockProp");
        //        var getImpl = (IMP)(delegate* unmanaged[Cdecl]<id, SEL, nint>)&GetDoubleDoubleBlockPropProxy;
        //        xm.class_addMethod(ClassType, GetDoubleDoubleBlockPropSelector, getImpl, "?@:");

        //        SEL SetDoubleDoubleBlockPropSelector = xm.sel_registerName("setDoubleDoubleBlockProp:");
        //        var setImpl = (IMP)(delegate* unmanaged[Cdecl]<id, SEL, id, void>)&SetDoubleDoubleBlockPropProxy;
        //        xm.class_addMethod(ClassType, SetDoubleDoubleBlockPropSelector, setImpl, "v@:?");
        //    }

        //    //GetDoubleDoubleBlockPropStaticSelector = xm.sel_registerName("doubleDoubleBlockPropStatic");
        //    //SetDoubleDoubleBlockPropStaticSelector = xm.sel_registerName("setDoubleDoubleBlockPropStatic:");

        //    // Override the retain/release methods for memory management.
        //    {
        //        SEL retainSelector = xm.sel_registerName("retain");
        //        SEL releaseSelector = xm.sel_registerName("release");
        //        Wrappers.GetRetainReleaseMethods(out IntPtr retainImpl, out IntPtr releaseImpl);
        //        xm.class_addMethod(ClassType, retainSelector, new IMP(retainImpl), ":@:");
        //        xm.class_addMethod(ClassType, releaseSelector, new IMP(releaseImpl), "v@:");
        //    }

        //    // Register the type with the Objective-C runtime.
        //    xm.objc_registerClassPair(ClassType);
        //}

        //[UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        //private static int DoubleIntProxy(id self, SEL sel, int a)
        //{
        //    unsafe
        //    {
        //        TestDotNet managed = Wrappers.IdDispatch.GetInstance<TestDotNet>((Wrappers.IdDispatch*)self.value);
        //        Trace.WriteLine($"DoubleIntProxy = Self: {self} (Obj: {managed}), SEL: {sel}, a: {a}");
        //        return managed.DoubleInt(a);
        //    }
        //}

        //[UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        //private static float DoubleFloatProxy(id self, SEL sel, float a)
        //{
        //    unsafe
        //    {
        //        TestDotNet managed = Wrappers.IdDispatch.GetInstance<TestDotNet>((Wrappers.IdDispatch*)self.value);
        //        Trace.WriteLine($"DoubleFloatProxy = Self: {self} (Obj: {managed}), SEL: {sel}, a: {a}");
        //        return managed.DoubleFloat(a);
        //    }
        //}

        //[UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        //private static double DoubleDoubleProxy(id self, SEL sel, double a)
        //{
        //    unsafe
        //    {
        //        TestDotNet managed = Wrappers.IdDispatch.GetInstance<TestDotNet>((Wrappers.IdDispatch*)self.value);
        //        Trace.WriteLine($"DoubleDoubleProxy = Self: {self} (Obj: {managed}), SEL: {sel}, a: {a}");
        //        return managed.DoubleDouble(a);
        //    }
        //}

        //[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        //private delegate double DoubleDoubleBlockProxy(id blk, double a);

        //[UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        //// Should be returning `id` but can't because of non-primitive return.
        //// See https://github.com/dotnet/runtime/issues/35928
        //private static nint GetDoubleDoubleBlockPropProxy(id self, SEL sel)
        //{
        //    unsafe
        //    {
        //        TestDotNet managed = Wrappers.IdDispatch.GetInstance<TestDotNet>((Wrappers.IdDispatch*)self.value);
        //        Trace.WriteLine($"GetDoubleDoubleBlockPropProxy = Self: {self} (Obj: {managed}), SEL: {sel}");

        //        DoubleDoubleBlock block = managed.DoubleDoubleBlockProp;
        //        DoubleDoubleBlockProxy proxy = (id blk, double a) =>
        //        {
        //            Trace.WriteLine($"DoubleDoubleBlockProxy: id: {blk} a: {a}");
        //            return block(a);
        //        };

        //        IntPtr fptr = Marshal.GetFunctionPointerForDelegate(proxy);
        //        return MyWrappers.Instance.CreateBlock(proxy, fptr, "d?d");
        //    }
        //}

        //[UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        //private static void SetDoubleDoubleBlockPropProxy(id self, SEL sel, id blk)
        //{
        //    unsafe
        //    {
        //        TestDotNet managed = Wrappers.IdDispatch.GetInstance<TestDotNet>((Wrappers.IdDispatch*)self.value);
        //        Trace.WriteLine($"GetDoubleDoubleBlockPropProxy = Self: {self} (Obj: {managed}), SEL: {sel}");

        //        //managed.DoubleDoubleBlockProp
        //        throw new NotImplementedException();
        //    }
        //}

        //public static DoubleDoubleBlock DoubleDoubleBlockPropStatic
        //{
        //    get;
        //    set;
        //}

        //public TestDotNet()
        //{ }

        //public DoubleDoubleBlock DoubleDoubleBlockProp
        //{
        //    get;
        //    set;
        //}

        //public int DoubleInt(int a)
        //{
        //    return a * 2;
        //}

        //public float DoubleFloat(float a)
        //{
        //    return a * 2;
        //}

        //public double DoubleDouble(double a)
        //{
        //    return a * 2;
        //}
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

            // Call Objective-C methods
            Console.WriteLine($"DoubleFloat: {testObjC.DoubleFloat((float)Math.PI)}");
            Console.WriteLine($"DoubleDouble: {testObjC.DoubleDouble(Math.PI)}");

            // Roundtrip Delegate <=> Block
            testObjC.IntBlockProp = (int a) => { return a * 2; };
            Console.WriteLine($"IntBlockProp: {testObjC.IntBlockProp((int)Math.PI)}");

            // Roundtrip Delegate <=> Block (static)
            TestObjC.IntBlockPropStatic = (int a) => { return a * 3; };
            Console.WriteLine($"IntBlockPropStatic: {TestObjC.IntBlockPropStatic((int)Math.PI)}");

            // Use delegates in Objective-C
            testObjC.UseProperties();

            // Clean up
            testObjC.IntBlockProp = null;
            TestObjC.IntBlockPropStatic = null;
        }
    }
}
