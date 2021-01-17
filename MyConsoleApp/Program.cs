using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ObjectiveC;
using System.Threading;

using ObjCRuntime;

using CreateObjectFlags = System.Runtime.InteropServices.ObjectiveC.CreateObjectFlags;

using id = System.IntPtr;
using Class = System.IntPtr;
using SEL = System.IntPtr;
using IMP = System.IntPtr;

namespace MyConsoleApp
{
    internal sealed class MyWrappers : Wrappers
    {
        public static readonly Wrappers Instance = new MyWrappers();

        public static readonly Class noclass = new Class(0);
        public static readonly id nil = new id(0);
        public static readonly SEL nosel = new SEL(0);

        protected override IMP GetBlockInvokeAndSignature(Delegate del, CreateBlockFlags flags, out string signature)
        {
            if (del is IntBlock)
            {
                signature = "i?i";
                unsafe
                {
                    return (IMP)(delegate* unmanaged<BlockLiteral*, int, int>)&Trampolines.ABI_IntBlock;
                }
            }

            throw new NotSupportedException();
        }

        protected override ObjectiveCBase CreateObject(id instance, Type typeAssociation, CreateObjectFlags flags)
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

    internal static class Registrar
    {
        public delegate ObjectiveCBase FactoryFunc(id instance, CreateObjectFlags flags);

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
            if (klass == MyWrappers.noclass)
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

    // Generated for supported Block types
    internal static class Trampolines
    {
        public static IntBlock CreateIntBlock(IntPtr block, IntPtr invoker)
        {
            return new IntBlock((int a) =>
            {
                unsafe
                {
                    return ((delegate* unmanaged[Cdecl]<id, int, int>)invoker)(block, a);
                }
            });
        }

        [UnmanagedCallersOnly]
        public unsafe static int ABI_IntBlock(BlockLiteral* b, int a)
            => BlockLiteral.GetDelegate<IntBlock>(b)(a);
    }

    // Base type for all Objective-C types.
    public class NSObject : ObjectiveCBase
    {
        // Call to instantiate a new NSObject from managed code.
        public NSObject()
            : this(RegisterInstanceFlags.None, IntPtr.Zero)
        {
            Debug.Assert(this.instance != ObjectiveCBase.InvalidInstanceValue);
        }

        // Call to instantiate a new NSObject based on an existing Objective-C instance.
        internal NSObject(IntPtr instance)
            : this(RegisterInstanceFlags.None, instance)
        {
        }

        // Call from derived class to participate with instance registration.
        protected NSObject(RegisterInstanceFlags flags, IntPtr instance)
        {
            this.instance = instance;
            if (this.instance == IntPtr.Zero)
            {
                this.instance = SendAllocMessage(this);
            }

            MyWrappers.Instance.RegisterInstanceWithObject(this.instance, typeof(NSObject), this, flags);

            static IntPtr SendAllocMessage(NSObject obj)
            {
                IntPtr klass = Registrar.GetClass(obj.GetType());
                unsafe
                {
                    // [Class alloc]
                    var allocSelector = xm.sel_registerName("alloc");
                    return ((delegate* unmanaged[Cdecl]<IntPtr, IntPtr, IntPtr, IntPtr>)xm.objc_msgSend_Raw)(klass, allocSelector, klass);
                }
            }
        }
    }

    public delegate int IntBlock(int a);

    // Projected Objective-C type into .NET
    public class ObjCObject : NSObject
    {
        private static readonly Class ClassType;
        private static readonly SEL DoubleFloatSelector;
        private static readonly SEL DoubleDoubleSelector;
        private static readonly SEL SayHello1Selector;
        private static readonly SEL SayHello2Selector;
        private static readonly SEL CallHellosSelector;
        private static readonly SEL UsePropertiesSelector;
        private static readonly SEL UseTestDotNetSelector;

        private static readonly SEL GetIntBlockPropSelector;
        private static readonly SEL SetIntBlockPropSelector;
        private static readonly SEL GetIntBlockPropStaticSelector;
        private static readonly SEL SetIntBlockPropStaticSelector;

        private static readonly SEL RetainSelector;
        private static readonly SEL ReleaseSelector;

        unsafe static ObjCObject()
        {
            ClassType = Registrar.GetClass(typeof(ObjCObject));
            DoubleFloatSelector = xm.sel_registerName("doubleFloat:");
            DoubleDoubleSelector = xm.sel_registerName("doubleDouble:");
            SayHello1Selector = xm.sel_registerName("sayHello1");
            SayHello2Selector = xm.sel_registerName("sayHello2");
            CallHellosSelector = xm.sel_registerName("callHellos:");
            UsePropertiesSelector = xm.sel_registerName("useProperties");
            UseTestDotNetSelector = xm.sel_registerName("useTestDotNet:");

            GetIntBlockPropSelector = xm.sel_registerName("intBlockProp");
            SetIntBlockPropSelector = xm.sel_registerName("setIntBlockProp:");
            GetIntBlockPropStaticSelector = xm.sel_registerName("intBlockPropStatic");
            SetIntBlockPropStaticSelector = xm.sel_registerName("setIntBlockPropStatic:");

            RetainSelector = xm.sel_registerName("retain");
            ReleaseSelector = xm.sel_registerName("release");
        }

        public static IntBlock IntBlockPropStatic
        {
            get
            {
                unsafe
                {
                    id block = ((delegate* unmanaged<Class, SEL, id>)xm.objc_msgSend_Raw)(ClassType, GetIntBlockPropStaticSelector);
                    if (block == MyWrappers.nil)
                    {
                        return null;
                    }

                    return (IntBlock)MyWrappers.Instance.GetOrCreateDelegateForBlock(
                        block,
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
                        block = MyWrappers.Instance.CreateBlockForDelegate(value, CreateBlockFlags.None);
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

        // Used to handle inheritance
        private id localInstance;
        private unsafe readonly void* msgSendFlavor = xm.objc_msgSend_Raw;

        public ObjCObject()
            : this(RegisterInstanceFlags.None, IntPtr.Zero)
        {
            this.localInstance = this.instance;
        }

        internal ObjCObject(id instance)
            : this(RegisterInstanceFlags.None, instance)
        { }

        protected ObjCObject(RegisterInstanceFlags flags, IntPtr instance)
            : base(flags, instance)
        {
            this.localInstance = this.instance;

            if (flags.HasFlag(RegisterInstanceFlags.ManagedDefinition))
            {
                unsafe
                {
                    // https://developer.apple.com/documentation/objectivec/1456716-objc_msgsendsuper
                    this.msgSendFlavor = xm.objc_msgSendSuper_Raw;

                    // https://developer.apple.com/documentation/objectivec/objc_super
                    // [TODO] Managed allocated memory for objc_super data structure.
                    var super = (IntPtr*)Marshal.AllocCoTaskMem(sizeof(id) + sizeof(Class));
                    super[0] = this.instance;
                    super[1] = ClassType; // During aggregation supply this class as the "super".
                    this.localInstance = (id)super;
                }
            }
        }

        public virtual IntBlock IntBlockProp
        {
            get
            {
                unsafe
                {
                    // [TODO] Handle the autorelease signal from the compiler generated property. At present
                    // this represents an extra 'retain' call that must be released.
                    id block = ((delegate* unmanaged<id, SEL, id>)this.msgSendFlavor)(this.localInstance, GetIntBlockPropSelector);
                    if (block == MyWrappers.nil)
                    {
                        return null;
                    }

                    return (IntBlock)MyWrappers.Instance.GetOrCreateDelegateForBlock(
                        block,
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
                        block = MyWrappers.Instance.CreateBlockForDelegate(value, CreateBlockFlags.None);
                        blockRaw = &block;
                    }

                    ((delegate* unmanaged<id, SEL, BlockLiteral*, void>)this.msgSendFlavor)(this.localInstance, SetIntBlockPropSelector, blockRaw);

                    if (blockRaw != null)
                    {
                        MyWrappers.Instance.ReleaseBlockLiteral(ref *blockRaw);
                    }
                }
            }
        }

        public virtual float DoubleFloat(float a)
        {
            unsafe
            {
                return ((delegate* unmanaged<id, SEL, float, float>)this.msgSendFlavor)(this.localInstance, DoubleFloatSelector, a);
            }
        }

        public virtual double DoubleDouble(double a)
        {
            unsafe
            {
                return ((delegate* unmanaged<id, SEL, double, double>)this.msgSendFlavor)(this.localInstance, DoubleDoubleSelector, a);
            }
        }

        public void UseProperties()
        {
            unsafe
            {
                ((delegate* unmanaged<id, SEL, void>)this.msgSendFlavor)(this.localInstance, UsePropertiesSelector);
            }
        }

        public void UseTestDotNet(DotNetObject dn)
        {
            unsafe
            {
                id id_dn = default;
                if (dn != null)
                {
                    id_dn = MyWrappers.Instance.GetInstanceFromObject(dn);
                }

                ((delegate* unmanaged<id, SEL, id, void>)this.msgSendFlavor)(this.localInstance, UseTestDotNetSelector, id_dn);
            }
        }

        public void CalledDuringDealloc()
        {
            throw new NotSupportedException("Not projected into managed");
        }
    }

    // Implemented dotnet type projected into Objective-C
    public class DotNetObject : ObjCObject
    {
        private static readonly Class ClassType;

        unsafe static DotNetObject()
        {
            // Create the class.
            Class baseClass = Registrar.GetClass(typeof(NSObject));
            ClassType = xm.objc_allocateClassPair(baseClass, nameof(DotNetObject), 0);
            Registrar.RegisterClass(
                typeof(DotNetObject),
                ClassType,
                (id inst, CreateObjectFlags flags) => throw new NotImplementedException());

            // Register and define the class's methods.

            {
                SEL InitSelector = xm.sel_registerName("init");
                var impl = (IMP)(delegate* unmanaged<id, SEL, id>)&InitProxy;
                xm.class_addMethod(ClassType, InitSelector, impl, ":@:");
            }

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
                var getImpl = (IMP)(delegate* unmanaged<id, SEL, id>)&GetIntBlockPropProxy;
                xm.class_addMethod(ClassType, GetIntBlockPropSelector, getImpl, "?@:");

                SEL SetIntBlockPropSelector = xm.sel_registerName("setIntBlockProp:");
                var setImpl = (IMP)(delegate* unmanaged<id, SEL, id, void>)&SetIntBlockPropProxy;
                xm.class_addMethod(ClassType, SetIntBlockPropSelector, setImpl, "v@:?");
            }

            // Override default lifetime/memory management methods.
            {
                Wrappers.GetLifetimeMethods(out IntPtr allocImpl, out IntPtr deallocImpl);

                // Static methods
                id metaClass = xm.object_getClass(ClassType);
                SEL AllocSelector = xm.sel_registerName("alloc");
                xm.class_addMethod(metaClass, AllocSelector, allocImpl, ":@:");

                // Instance methods
                SEL deallocSelector = xm.sel_registerName("dealloc");
                xm.class_addMethod(ClassType, deallocSelector, deallocImpl, "v@:");
            }

            // Register the type with the Objective-C runtime.
            xm.objc_registerClassPair(ClassType);
        }

        [UnmanagedCallersOnly]
        private static id InitProxy(id self, SEL sel)
        {
            // N.B. The registration mapping is performed in the NSObject constructor.
            var dn = new DotNetObject(self);
            return dn.instance;
        }

        [UnmanagedCallersOnly]
        private static float DoubleFloatProxy(id self, SEL sel, float a)
        {
            unsafe
            {
                DotNetObject managed = Instance.GetInstance<DotNetObject>((Instance*)self);
                Trace.WriteLine($"DoubleFloatProxy = Self: {self} (Obj: {managed}), SEL: {sel}, a: {a}");
                return managed.DoubleFloat(a);
            }
        }

        [UnmanagedCallersOnly]
        private static double DoubleDoubleProxy(id self, SEL sel, double a)
        {
            unsafe
            {
                DotNetObject managed = Instance.GetInstance<DotNetObject>((Instance*)self);
                Trace.WriteLine($"DoubleDoubleProxy = Self: {self} (Obj: {managed}), SEL: {sel}, a: {a}");
                return managed.DoubleDouble(a);
            }
        }

        [UnmanagedCallersOnly]
        private static IntPtr GetIntBlockPropProxy(id self, SEL sel)
        {
            unsafe
            {
                DotNetObject managed = Instance.GetInstance<DotNetObject>((Instance*)self);
                Trace.WriteLine($"GetIntBlockPropProxy = Self: {self} (Obj: {managed}), SEL: {sel}");

                var intBlock = managed.IntBlockProp;
                if (intBlock == null)
                {
                    return default(IntPtr);
                }

                BlockLiteral block = MyWrappers.Instance.CreateBlockForDelegate(intBlock, CreateBlockFlags.None);

                // [TODO] Getters typically do a retain followed by an autorelease call. It isn't obvious if
                // a Block_copy() call is appropriate, but it would seem to be given my current understanding.
                void* newBlock = xm.Block_copy(&block);
                MyWrappers.Instance.ReleaseBlockLiteral(ref block);

                return (IntPtr)newBlock;
            }
        }

        [UnmanagedCallersOnly]
        private static void SetIntBlockPropProxy(id self, SEL sel, IntPtr blk)
        {
            unsafe
            {
                DotNetObject managed = Instance.GetInstance<DotNetObject>((Instance*)self);
                Trace.WriteLine($"SetIntBlockPropProxy = Self: {self} (Obj: {managed}), SEL: {sel}");

                managed.IntBlockProp = (IntBlock)MyWrappers.Instance.GetOrCreateDelegateForBlock(
                    blk,
                    CreateDelegateFlags.Unwrap,
                    Trampolines.CreateIntBlock);
            }
        }

        public DotNetObject()
            : this(IntPtr.Zero)
        { }

        // The private contructor represents an Objective-C "init" method.
        private DotNetObject(IntPtr instance)
            : base(RegisterInstanceFlags.ManagedDefinition, instance)
        {
        }

        public override IntBlock IntBlockProp
        {
            get;
            set;
        }

        public override float DoubleFloat(float a)
        {
            return a * 2;
        }

        public override double DoubleDouble(double a)
        {
            return base.DoubleDouble(a);
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
                (typeof(ObjCObject), nameof(ObjCObject), (id i, CreateObjectFlags f) => new ObjCObject(i)),
            });

            var testObjC = new ObjCObject();

            // Call Objective-C methods
            Console.WriteLine($"DoubleFloat: {testObjC.DoubleFloat((float)Math.PI)}");
            Console.WriteLine($"DoubleDouble: {testObjC.DoubleDouble(Math.PI)}");

            // Roundtrip Delegate <=> Block
            testObjC.IntBlockProp = (int a) => { return a * 2; };
            Console.WriteLine($"IntBlockProp: {testObjC.IntBlockProp((int)Math.PI)}");

            // Roundtrip Delegate <=> Block (static)
            ObjCObject.IntBlockPropStatic = (int a) => { return a * 3; };
            Console.WriteLine($"IntBlockPropStatic: {ObjCObject.IntBlockPropStatic((int)Math.PI)}");

            // Use delegates in Objective-C
            testObjC.UseProperties();

            // Pass over .NET object and use properties
            var testDotNet = new DotNetObject();
            testDotNet.IntBlockProp = (int a) => { return a * 2; };
            testObjC.UseTestDotNet(testDotNet);

            // Clean up
            testObjC.IntBlockProp = null;
            ObjCObject.IntBlockPropStatic = null;

            testObjC.UseTestDotNet(null);
        }
    }
}
