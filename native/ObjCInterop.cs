using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;

namespace System.Runtime.InteropServices.ObjectiveC
{
    public enum CreateInstFlags
    {
        None
    }

    public enum CreateObjectFlags
    {
        None
    }

    /// <summary>
    /// Class used to create wrappers for interoperability with the Objective-C runtime.
    /// </summary>
    public abstract class Wrappers
    {
        /// <summary>
        /// Register the current wrappers instance as the global one for the system.
        /// </summary>
        /// <remarks>
        /// This primarily enables support for <see cref="StartThreadPoolWorkItem"/>
        /// and <see cref="EndThreadPoolWorkItem"/>.
        /// </remarks>
        public void RegisterAsGlobal()
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Type when entering the managed environment from an Objective-C wrapper.
        /// </summary>
        public struct IdDispatch
        {
            public IntPtr Isa;
            public unsafe static T GetInstance<T>(IdDispatch* dispatchPtr) where T : class
            {
                var lifetime = (ManagedObjectWrapperLifetime*)xm.object_getIndexedIvars((nint)dispatchPtr);
                var gcHandle = GCHandle.FromIntPtr(lifetime->GCHandle);
                return (T)gcHandle.Target;
            }
        }

        private struct ManagedObjectWrapperLifetime
        {
            public nint GCHandle;
            public nint RefCount;
        }

        /// <summary>
        /// Get or create a Objective-C wrapper for the supplied managed object.
        /// </summary>
        /// <param name="instance">A managed object to wrap</param>
        /// <param name="flags">Flags for creation</param>
        /// <returns>An Objective-C wrapper</returns>
        /// <see cref="ComputeInstClass(object, CreateInstFlags)"/>
        public IntPtr GetOrCreateInstForObject(object instance, CreateInstFlags flags)
        {
            id native;
            if (Internals.TryGetIdentity(instance, out native))
            {
                return native.value;
            }

            IntPtr klass = this.ComputeInstClass(instance, flags);

            unsafe
            {
                // Add a lifetime size for the GC Handle.
                native = xm.class_createInstance(klass, sizeof(ManagedObjectWrapperLifetime));

                var lifetime = (ManagedObjectWrapperLifetime*)xm.object_getIndexedIvars(native);
                IntPtr gcptr = GCHandle.ToIntPtr(GCHandle.Alloc(instance));
                Trace.WriteLine($"Lifetime: 0x{(nint)lifetime:x}, GCHandle: 0x{gcptr.ToInt64():x}");
                lifetime->GCHandle = gcptr;
                lifetime->RefCount = 1;
            }

            Internals.RegisterIdentity(instance, native);
            return native.value;
        }

        /// <summary>
        /// Called if there is currently no existing Objective-C wrapper.
        /// </summary>
        /// <param name="instance">A managed object to wrap</param>
        /// <param name="flags">Flags for creation</param>
        /// <returns>An Objective-C pointer to a class</returns>
        /// <remarks>
        /// Defer to the implementer for determining the <see cref="https://developer.apple.com/documentation/objectivec/class">Objective-C Class</see>
        /// that should be used to wrap the managed object.
        /// </remarks>
        protected abstract IntPtr ComputeInstClass(object instance, CreateInstFlags flags);

        /// <summary>
        /// Get or create a managed wrapper for the supplied Objective-C object.
        /// </summary>
        /// <param name="instance">An Objective-C object</param>
        /// <param name="flags">Flags for creation</param>
        /// <returns>A managed wrapper</returns>
        /// <see cref="CreateInst(object, CreateInstFlags)"/>
        public object GetOrCreateObjectForInst(IntPtr instance, CreateObjectFlags flags)
        {
            object wrapper;
            if (!Internals.TryGetObject(instance, out wrapper))
            {
                wrapper = this.CreateObject(instance, flags);
                Internals.RegisterIdentity(wrapper, instance);
            }

            return wrapper;
        }

        /// <summary>
        /// Called if there is currently no existing managed wrapper.
        /// </summary>
        /// <param name="instance">An Objective-C object</param>
        /// <param name="flags">Flags for creation</param>
        /// <returns>A managed wrapper</returns>
        protected abstract object CreateObject(IntPtr instance, CreateObjectFlags flags);

        /// <summary>
        /// Get or provide a managed object wrapper for the supplied Objective-C object.
        /// </summary>
        /// <param name="instance">An Objective-C object</param>
        /// <param name="flags">Flags for creation</param>
        /// <param name="wrapperMaybe">The managed wrapper to use if one doesn't exist</param>
        /// <returns>A managed wrapper</returns>
        public object GetOrRegisterObjectForInst(IntPtr instance, CreateObjectFlags flags, object wrapperMaybe)
        {
            object wrapper;
            if (!Internals.TryGetObject(instance, out wrapper))
            {
                Internals.RegisterIdentity(wrapperMaybe, instance);
                wrapper = wrapperMaybe;
            }

            return wrapper;
        }

        // http://clang.llvm.org/docs/Block-ABI-Apple.html#high-level
        [StructLayout(LayoutKind.Sequential)]
        private unsafe struct BlockLiteral
        {
            public void* Isa;
            public int Flags;
            public int Reserved;
            public void* Invoke; // delegate* unmanaged[Cdecl]<BlockLiteral* , ...args, ret>
            public void* BlockDescriptor;

            // Extension of ABI to help with .NET lifetime.
            public IntPtr DelegateGCHandle;
        }

        [StructLayout(LayoutKind.Sequential)]
        private unsafe struct DelegateBlockDesc
        {
            public nint Reserved;
            public nint Size;
            public delegate* unmanaged[Cdecl]<void*, void*, void> Copy_helper;
            public delegate* unmanaged[Cdecl]<void*, void> Dispose_helper;
            public void* Signature;

            // Extension of ABI to help with .NET lifetime.
            public uint RefCount;
        }

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        private unsafe static void CopyBlock(void* dst, void* src)
        {
            var blockSrc = (BlockLiteral*)src;
            var blockDescSrc = (DelegateBlockDesc*)blockSrc->BlockDescriptor;
            var blockDst = (BlockLiteral*)dst;

            uint count = Interlocked.Decrement(ref blockDescSrc->RefCount);
            Debug.Assert(count > 1);
            blockDst->BlockDescriptor = blockDescSrc;
            blockDst->DelegateGCHandle = blockSrc->DelegateGCHandle;
        }

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        private unsafe static void DisposeBlock(void* blk)
        {
            var block = (BlockLiteral*)blk;
            var blockDesc = (DelegateBlockDesc*)block->BlockDescriptor;

            // Cleanup the delegate's handle
            GCHandle.FromIntPtr(block->DelegateGCHandle).Free();

            uint count = Interlocked.Decrement(ref blockDesc->RefCount);
            if (count != 0)
            {
                return;
            }

            Marshal.FreeCoTaskMem((IntPtr)blockDesc->Signature);
            Marshal.FreeCoTaskMem((IntPtr)blockDesc);
            block->BlockDescriptor = null;
        }

        /// <summary>
        /// Given a <see cref="Delegate"/>, create an Objective-C block that can be passed
        /// to the Objective-C environment.
        /// </summary>
        /// <param name="delegateInstance">The delegate to wrap</param>
        /// <param name="delegateFunctionPointer">The function pointer to call</param>
        /// <param name="signature">The Objective-C type signature of the function</param>
        /// <returns>An Objective-C block</returns>
        /// <remarks>
        /// See <see href="https://developer.apple.com/library/archive/documentation/Cocoa/Conceptual/ObjCRuntimeGuide/Articles/ocrtTypeEncodings.html">Objective-C Type Encodings</see> for how to specify <paramref name="signature"/>.
        ///
        /// See <see href="http://clang.llvm.org/docs/Block-ABI-Apple.html">Objective-C Block ABI</see> for the supported contract.
        /// </remarks>
        public IntPtr CreateBlock(Delegate delegateInstance, IntPtr delegateFunctionPointer, string signature)
        {
            var delegateHandle = GCHandle.Alloc(delegateInstance);

            unsafe
            {
                var desc = (DelegateBlockDesc*)Marshal.AllocCoTaskMem(sizeof(DelegateBlockDesc));
                desc->Size = sizeof(BlockLiteral);
                desc->Copy_helper = &CopyBlock;
                desc->Dispose_helper = &DisposeBlock;
                desc->Signature = Marshal.StringToCoTaskMemUTF8(signature).ToPointer();
                desc->RefCount = 1;

                var block = (BlockLiteral*)Marshal.AllocCoTaskMem(sizeof(BlockLiteral));
                block->Isa = xm._NSConcreteStackBlock_Raw;
                block->Flags = (1 << 25) | (1 << 30); // Descriptor contains copy/dispose and signature.
                block->Invoke = delegateFunctionPointer.ToPointer();
                block->BlockDescriptor = desc;
                block->DelegateGCHandle = GCHandle.ToIntPtr(delegateHandle);

                return (nint)block;
            }
        }

        /// <summary>
        /// Type used to represent the dispatching function and associated context.
        /// </summary>
        public record BlockProxy
        {
            /// <summary>
            /// Block instance.
            /// </summary>
            public IntPtr Block { get; init; }

            /// <summary>
            /// The function pointer to call for dispatch.
            /// </summary>
            /// <remarks>
            /// The C# function pointer syntax is dependent on the signature of the
            /// block, but does takes the <see cref="Block"/> as the first argument.
            /// For example:
            /// <code>
            /// delegate* unmanaged[Cdecl]&lt;IntPtr [, arg]*, ret&gt;
            /// </code>
            /// </remarks>
            public IntPtr FunctionPointer { get; init; }

            ~BlockProxy()
            {
                xm.Block_release(this.Block);
            }
        }

        /// <summary>
        /// Create a <see cref="BlockProxy"/> for <paramref name="block"/>.
        /// </summary>
        /// <param name="block">Objective-C block</param>
        /// <returns>Proxy to use for dispatch</returns>
        /// <remarks>
        /// See <see href="http://clang.llvm.org/docs/Block-ABI-Apple.html">Objective-C Block ABI</see> for the supported contract.
        /// </remarks>
        public BlockProxy CreateBlockProxy(IntPtr block)
        {
            // Ownership has been transferred at this point (i.e. someone else has called [block copy]).
            unsafe
            {
                BlockLiteral* blockLiteral = (BlockLiteral*)block;
                return new BlockProxy() { Block = block, FunctionPointer = (IntPtr)blockLiteral->Invoke };
            }
        }

        /// <summary>
        /// Create a <see cref="Wrappers"/> instance.
        /// </summary>
        protected Wrappers()
        {
        }

        /// <summary>
        /// Allows the implementer of the Objective-C wrapper class
        /// to provide overrides to the 'objc_msgSend*' APIs for the
        /// Objective-C runtime.
        /// </summary>
        /// <param name="objc_msgSend"></param>
        /// <param name="objc_msgSend_fpret"></param>
        /// <param name="objc_msgSend_stret"></param>
        /// <param name="objc_msgSendSuper"></param>
        /// <param name="objc_msgSendSuper_stret"></param>
        /// <remarks>
        /// Providing these overrides can enable support for Objective-C
        /// exception propagation as well as variadic argument support.
        /// </remarks>
        protected void SetMessageSendCallbacks(
            IntPtr objc_msgSend,
            IntPtr objc_msgSend_fpret,
            IntPtr objc_msgSend_stret,
            IntPtr objc_msgSendSuper,
            IntPtr objc_msgSendSuper_stret)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Get the retain and release implementations that should be added
        /// to all managed type definitions that are projected into the
        /// Objective-C environment.
        /// </summary>
        /// <param name="retainImpl">Retain implementation</param>
        /// <param name="releaseImpl">Release implementation</param>
        /// <remarks>
        /// See <see href="https://developer.apple.com/documentation/objectivec/1418956-nsobject/1571946-retain">retain</see>.
        /// See <see href="https://developer.apple.com/documentation/objectivec/1418956-nsobject/1571957-release">release</see>.
        /// </remarks>
        public static void GetRetainReleaseMethods(
            out IntPtr retainImpl,
            out IntPtr releaseImpl)
        {
            unsafe
            {
                retainImpl = (nint)xm.clr_retain_Raw;
                releaseImpl = (nint)xm.clr_release_Raw;
            }
        }

        /// <summary>
        /// Provides a start callout for the ThreadPool API when a new
        /// work item is to be schedule.
        /// </summary>
        /// <remarks>
        /// Addresses https://github.com/dotnet/runtime/issues/44213
        /// </remarks>
        protected abstract void StartThreadPoolWorkItem();

        /// <summary>
        /// Provides an end callout for the ThreadPool API when a
        /// work item is completed.
        /// </summary>
        /// <remarks>
        /// Addresses https://github.com/dotnet/runtime/issues/44213
        /// </remarks>
        protected abstract void EndThreadPoolWorkItem();

        private static class Internals
        {
            private static readonly ReaderWriterLockSlim RegisteredIdentityLock = new ReaderWriterLockSlim();
            private static readonly Dictionary<object, id> ObjectToIdentity = new Dictionary<object, id>();
            private static readonly Dictionary<id, object> IdentityToObject = new Dictionary<id, object>();
            public static void RegisterIdentity(object managed, id native)
            {
                RegisteredIdentityLock.EnterWriteLock();

                try
                {
                    ObjectToIdentity.Add(managed, native);
                    IdentityToObject.Add(native, managed);
                }
                finally
                {
                    RegisteredIdentityLock.ExitWriteLock();
                }
            }
            public static bool TryGetIdentity(object managed, out id native)
            {
                RegisteredIdentityLock.EnterReadLock();

                try
                {
                    return ObjectToIdentity.TryGetValue(managed, out native);
                }
                finally
                {
                    RegisteredIdentityLock.ExitReadLock();
                }
            }
            public static bool TryGetObject(id native, out object managed)
            {
                RegisteredIdentityLock.EnterReadLock();

                try
                {
                    return IdentityToObject.TryGetValue(native, out managed);
                }
                finally
                {
                    RegisteredIdentityLock.ExitReadLock();
                }
            }
        }
    }

    // Objective-C runtime types
    [StructLayout(LayoutKind.Sequential)]
    public readonly struct id
    {
        public id(nint c) => this.value = c;
        public readonly nint value;
        public override bool Equals(object obj)
        {
            if (obj is id id_obj) return this == id_obj;
            return base.Equals(obj);
        }
        public override int GetHashCode() => this.value.GetHashCode();
        public override string ToString() => "0x" + this.value.ToString("x");
        public unsafe static explicit operator id(void* ptr) => new id((nint)ptr);
        public unsafe static implicit operator id(IntPtr i) => new id(i);
        public static bool operator ==(id l, id r) => l.value == r.value;
        public static bool operator !=(id l, id r) => !(l == r);
    };
    [StructLayout(LayoutKind.Sequential)]
    public readonly struct SEL
    {
        public SEL(nint c) => this.value = c;
        public readonly nint value;
        public override bool Equals(object obj)
        {
            if (obj is SEL sel_obj) return this == sel_obj;
            return base.Equals(obj);
        }
        public override int GetHashCode() => this.value.GetHashCode();
        public override string ToString() => "0x" + this.value.ToString("x");
        public unsafe static explicit operator SEL(void* ptr) => new SEL((nint)ptr);
        public unsafe static implicit operator SEL(IntPtr i) => new SEL(i);
        public static bool operator ==(SEL l, SEL r) => l.value == r.value;
        public static bool operator !=(SEL l, SEL r) => !(l == r);
    };
    [StructLayout(LayoutKind.Sequential)]
    public readonly struct IMP
    {
        public IMP(nint c) => this.value = c;
        public readonly nint value;
        public override bool Equals(object obj)
        {
            if (obj is IMP imp_obj) return this == imp_obj;
            return base.Equals(obj);
        }
        public override int GetHashCode() => this.value.GetHashCode();
        public override string ToString() => "0x" + this.value.ToString("x");
        public unsafe static explicit operator IMP(void* ptr) => new IMP((nint)ptr);
        public unsafe static implicit operator IMP(IntPtr i) => new IMP(i);
        public static bool operator ==(IMP l, IMP r) => l.value == r.value;
        public static bool operator !=(IMP l, IMP r) => !(l == r);
    };
    [StructLayout(LayoutKind.Sequential)]
    public readonly struct Class
    {
        public Class(nint c) => this.value = c;
        public readonly nint value;
        public override bool Equals(object obj)
        {
            if (obj is Class class_obj) return this == class_obj;
            return base.Equals(obj);
        }
        public override int GetHashCode() => this.value.GetHashCode();
        public override string ToString() => "0x" + this.value.ToString("x");
        public unsafe static explicit operator Class(void* ptr) => new Class((nint)ptr);
        public unsafe static implicit operator Class(IntPtr i) => new Class(i);
        public static bool operator ==(Class l, Class r) => l.value == r.value;
        public static bool operator !=(Class l, Class r) => !(l == r);
    };

    public unsafe class xm
    {
        [DllImport(nameof(xm))]
        public extern static void Initialize();

        [DllImport(nameof(xm))]
        public extern static void dummy(nint ptr);

        public static readonly void* clr_retain_Raw = Get_clr_retain();

        [DllImport(nameof(xm))]
        private extern static void* Get_clr_retain();

        public static readonly void* clr_release_Raw = Get_clr_release();

        [DllImport(nameof(xm))]
        private extern static void* Get_clr_release();

        public static readonly void* objc_msgSend_Raw = Get_objc_msgSend();

        [DllImport(nameof(xm))]
        private extern static void* Get_objc_msgSend();

        public static readonly void* _NSConcreteStackBlock_Raw = Get_NSConcreteStackBlock();

        [DllImport(nameof(xm))]
        private extern static void* Get_NSConcreteStackBlock();

        [DllImport(nameof(xm), EntryPoint = "objc_getMetaClass_proxy")]
        public extern static Class objc_getMetaClass(
            [MarshalAs(UnmanagedType.LPStr)] string name);

        [DllImport(nameof(xm), EntryPoint = "objc_getClass_proxy")]
        public extern static Class objc_getClass(
            [MarshalAs(UnmanagedType.LPStr)] string name);

        [DllImport(nameof(xm), EntryPoint = "object_getClassName_proxy")]
        [return: MarshalAs(UnmanagedType.LPStr)]
        public extern static string object_getClassName(id obj);

        [DllImport(nameof(xm), EntryPoint = "objc_allocateClassPair_proxy")]
        public extern static Class objc_allocateClassPair(
            Class superclass,
            [MarshalAs(UnmanagedType.LPStr)] string name,
            nint extraBytes);

        [DllImport(nameof(xm), EntryPoint = "sel_registerName_proxy")]
        public extern static SEL sel_registerName([MarshalAs(UnmanagedType.LPStr)] string str);

        // types string - https://developer.apple.com/library/archive/documentation/Cocoa/Conceptual/ObjCRuntimeGuide/Articles/ocrtTypeEncodings.html#//apple_ref/doc/uid/TP40008048-CH100
        [DllImport(nameof(xm), EntryPoint = "class_addMethod_proxy")]
        [return: MarshalAs(UnmanagedType.U1)]
        public extern static bool class_addMethod(
            Class cls,
            SEL name,
            IMP imp,
            [MarshalAs(UnmanagedType.LPStr)] string types);

        [DllImport(nameof(xm), EntryPoint = "objc_registerClassPair_proxy")]
        public extern static void objc_registerClassPair(Class cls);

        [DllImport(nameof(xm), EntryPoint = "class_createInstance_proxy")]
        public extern static id class_createInstance(Class cls, nint extraBytes);

        [DllImport(nameof(xm), EntryPoint = "class_getName_proxy")]
        [return: MarshalAs(UnmanagedType.LPStr)]
        public extern static string class_getName(Class cls);

        [DllImport(nameof(xm), EntryPoint = "objc_destructInstance_proxy")]
        public extern static void objc_destructInstance(id obj);

        [DllImport(nameof(xm), EntryPoint = "object_getIndexedIvars_proxy")]
        public extern static void* object_getIndexedIvars(id obj);

        [DllImport(nameof(xm), EntryPoint = "Block_copy_proxy")]
        public extern static id Block_copy(id block);

        [DllImport(nameof(xm), EntryPoint = "Block_release_proxy")]
        public extern static void Block_release(id block);

        public static readonly Class noclass = new Class(0);
        public static readonly id nil = new id(0);
        public static readonly SEL nosel = new SEL(0);

        [DllImport(nameof(xm), EntryPoint = "objc_msgSend_proxy")]
        public extern static void objc_msgSend(id self, SEL sel);
    }
}