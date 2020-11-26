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
        private unsafe static int ABI_IntBlock(BlockLiteral* b, int a)
            => BlockLiteral.GetDelegate<IntBlock>(b)(a);

        protected override IntPtr GetBlockInvokeAndSignature(Delegate del, CreateBlockFlags flags, out string signature)
        {
            if (del is IntBlock)
            {
                signature = "i?i";
                unsafe
                {
                    return (IntPtr)(delegate* unmanaged<BlockLiteral*, int, int>)&ABI_IntBlock;
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
            : this(xm.class_createInstance(klass, extraBytes: 0))
        {
        }
    }

    public delegate int IntBlock(int a);

    internal static class Trampolines
    {
        public static IntBlock CreateIntBlock(BlockDispatch dispatch)
        {
            return new IntBlock((int a) =>
            {
                unsafe
                {
                    return ((delegate* unmanaged[Cdecl]<IntPtr, int, int>)dispatch.Invoker)(dispatch.Block, a);
                }
            });
        }
    }

    // Projected Objective-C type into .NET
    class TestObjC : NSObject
    {

        private static readonly Class ClassType;
        private static readonly SEL DoubleFloatSelector;
        private static readonly SEL DoubleDoubleSelector;
        private static readonly SEL UsePropertiesSelector;
        private static readonly SEL UseTestDotNetSelector;

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
            UseTestDotNetSelector = xm.sel_registerName("useTestDotNet:");

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
                    id block = ((delegate* unmanaged<Class, SEL, id>)xm.objc_msgSend_Raw)(ClassType, GetIntBlockPropStaticSelector);
                    if (block.value == xm.nil)
                    {
                        return null;
                    }

                    return (IntBlock)MyWrappers.Instance.GetOrCreateDelegateForBlock(
                        block.value,
                        CreateDelegateFlags.Unwrap,
                        Trampolines.CreateIntBlock);
                }
            }
            set
            {
                unsafe
                {
                    BlockLiteral block;
                    BlockLiteral* blockRaw = null;
                    if (value != null)
                    {
                        block = MyWrappers.Instance.GetOrCreateBlockForDelegate(value, CreateBlockFlags.None);
                        blockRaw = &block;
                    }

                    ((delegate* unmanaged<Class, SEL, BlockLiteral*, void>)xm.objc_msgSend_Raw)(ClassType, SetIntBlockPropStaticSelector, blockRaw);

                    if (blockRaw != null)
                    {
                        MyWrappers.Instance.ReleaseBlockLiteral(ref *blockRaw);
                    }
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
                    // [TODO] How to handle the autorelease signal from the compiler generated property?
                    id block = ((delegate* unmanaged<id, SEL, id>)xm.objc_msgSend_Raw)(this.instance, GetIntBlockPropSelector);
                    if (block.value == xm.nil)
                    {
                        return null;
                    }

                    return (IntBlock)MyWrappers.Instance.GetOrCreateDelegateForBlock(
                        block.value,
                        CreateDelegateFlags.Unwrap,
                        Trampolines.CreateIntBlock);
                }
            }
            set
            {
                unsafe
                {
                    BlockLiteral block;
                    BlockLiteral* blockRaw = null;
                    if (value != null)
                    {
                        block = MyWrappers.Instance.GetOrCreateBlockForDelegate(value, CreateBlockFlags.None);
                        blockRaw = &block;
                    }

                    ((delegate* unmanaged<id, SEL, BlockLiteral*, void>)xm.objc_msgSend_Raw)(this.instance, SetIntBlockPropSelector, blockRaw);

                    if (blockRaw != null)
                    {
                        MyWrappers.Instance.ReleaseBlockLiteral(ref *blockRaw);
                    }
                }
            }
        }

        public float DoubleFloat(float a)
        {
            unsafe
            {
                return ((delegate* unmanaged<id, SEL, float, float>)xm.objc_msgSend_Raw)(this.instance, DoubleFloatSelector, a);
            }
        }

        public double DoubleDouble(double a)
        {
            unsafe
            {
                return ((delegate* unmanaged<id, SEL, double, double>)xm.objc_msgSend_Raw)(this.instance, DoubleDoubleSelector, a);
            }
        }

        public void UseProperties()
        {
            unsafe
            {
                ((delegate* unmanaged<id, SEL, void>)xm.objc_msgSend_Raw)(this.instance, UsePropertiesSelector);
            }
        }

        public void UseTestDotNet(TestDotNet dn)
        {
            unsafe
            {
                IntPtr id_dn = default;
                if (dn != null)
                {
                    id_dn = MyWrappers.Instance.GetOrCreateInstanceForObject(dn, CreateInstanceFlags.None);
                }

                ((delegate* unmanaged<id, SEL, IntPtr, void>)xm.objc_msgSend_Raw)(this.instance, UseTestDotNetSelector, id_dn);
            }
        }
    }

    // Implemented dotnet type projected into Objective-C
    class TestDotNet : NSObject
    {
        private static readonly Class ClassType;

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
                SEL DoubleFloatSelector = xm.sel_registerName("doubleFloat:");
                var impl = (IMP)(delegate* unmanaged<id, SEL, float, float>)&DoubleFloatProxy;
                xm.class_addMethod(ClassType, DoubleFloatSelector, impl, "f@:f");
            }

            {
                SEL DoubleDoubleSelector = xm.sel_registerName("doubleDouble:");
                var impl = (IMP)(delegate* unmanaged<id, SEL, double, double>)&DoubleDoubleProxy;
                xm.class_addMethod(ClassType, DoubleDoubleSelector, impl, "d@:d");
            }

            {
                SEL GetIntBlockPropSelector = xm.sel_registerName("intBlockProp");
                var getImpl = (IMP)(delegate* unmanaged<id, SEL, IntPtr>)&GetIntBlockPropProxy;
                xm.class_addMethod(ClassType, GetIntBlockPropSelector, getImpl, "?@:");

                SEL SetIntBlockPropSelector = xm.sel_registerName("setIntBlockProp:");
                var setImpl = (IMP)(delegate* unmanaged<id, SEL, IntPtr, void>)&SetIntBlockPropProxy;
                xm.class_addMethod(ClassType, SetIntBlockPropSelector, setImpl, "v@:?");
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


        [UnmanagedCallersOnly]
        private static float DoubleFloatProxy(id self, SEL sel, float a)
        {
            unsafe
            {
                TestDotNet managed = Instance.GetInstance<TestDotNet>((Instance*)self.value);
                Trace.WriteLine($"DoubleFloatProxy = Self: {self} (Obj: {managed}), SEL: {sel}, a: {a}");
                return managed.DoubleFloat(a);
            }
        }

        [UnmanagedCallersOnly]
        private static double DoubleDoubleProxy(id self, SEL sel, double a)
        {
            unsafe
            {
                TestDotNet managed = Instance.GetInstance<TestDotNet>((Instance*)self.value);
                Trace.WriteLine($"DoubleDoubleProxy = Self: {self} (Obj: {managed}), SEL: {sel}, a: {a}");
                return managed.DoubleDouble(a);
            }
        }

        [UnmanagedCallersOnly]
        private static IntPtr GetIntBlockPropProxy(id self, SEL sel)
        {
            unsafe
            {
                TestDotNet managed = Instance.GetInstance<TestDotNet>((Instance*)self.value);
                Trace.WriteLine($"GetIntBlockPropProxy = Self: {self} (Obj: {managed}), SEL: {sel}");

                BlockLiteral block = MyWrappers.Instance.GetOrCreateBlockForDelegate(managed.IntBlockProp, CreateBlockFlags.None);
                void* newBlock = xm.Block_copy(&block);

                return (IntPtr)newBlock;
            }
        }

        [UnmanagedCallersOnly]
        private static void SetIntBlockPropProxy(id self, SEL sel, IntPtr blk)
        {
            unsafe
            {
                TestDotNet managed = Instance.GetInstance<TestDotNet>((Instance*)self.value);
                Trace.WriteLine($"SetIntBlockPropProxy = Self: {self} (Obj: {managed}), SEL: {sel}");

                managed.IntBlockProp = (IntBlock)MyWrappers.Instance.GetOrCreateDelegateForBlock(
                    blk,
                    CreateDelegateFlags.Unwrap,
                    Trampolines.CreateIntBlock);
            }
        }

        public TestDotNet()
        { }

        public IntBlock IntBlockProp
        {
            get;
            set;
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

            // Pass over .NET object and use
            var testDotNet = new TestDotNet();
            testDotNet.IntBlockProp = (int a) => { return a * 2; };
            testObjC.UseTestDotNet(testDotNet);

            // Clean up
            testObjC.IntBlockProp = null;
            TestObjC.IntBlockPropStatic = null;
        }
    }
}
