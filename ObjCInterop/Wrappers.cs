﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;

using ObjCRuntime;

namespace System.Runtime.InteropServices.ObjectiveC
{
    [Flags]
    public enum RegisterInstanceFlags
    {
        None = 0,

        /// <summary>
        /// The type to register was defined in managed code.
        /// </summary>
        /// <remarks>
        /// Objective-C types defined are in managed code are allocated
        /// with additional bytes to carry a GC Handle used for life time
        /// management. This data structure can be used by the consuming
        /// Objective-C interop implementation. The data structure is an instance
        /// of <see cref="ManagedObjectWrapperLifetime"/>. It can be accessed
        /// on the allocation by through the object_getIndexedIvars Objective-C runtime API.
        /// </remarks>
        ManagedDefinition = 1,
    }

    [Flags]
    public enum CreateObjectFlags
    {
        None = 0,

        /// <summary>
        /// The supplied Objective-C instance should be check if it is a
        /// wrapped managed object and not a pure Objective-C instance.
        ///
        /// If the instance is wrapped return the underlying managed object
        /// instead of creating a new wrapper.
        /// </summary>
        Unwrap = 1,

        /// <summary>
        /// Let the .NET runtime participate in lifetime management.
        /// </summary>
        /// <remarks>
        /// Using this optional is always possible but required if the
        /// created object will contain managed state that must be kept
        /// alive even without a managed reference.
        /// </remarks>
        ManageLifetime = 2,
    }

    [Flags]
    public enum CreateBlockFlags
    {
        None = 0,
    }

    [Flags]
    public enum CreateDelegateFlags
    {
        None = 0,

        /// <summary>
        /// The supplied Objective-C block should be check if it is a
        /// wrapped Delegate and not a pure Objective-C Block.
        ///
        /// If the instance is wrapped return the underlying Delegate
        /// instead of creating a new wrapper.
        /// </summary>
        Unwrap = 1,
    }

    /// <summary>
    /// Data structure for managing object wrapper lifetime.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct ManagedObjectWrapperLifetime
    {
        /// <summary>
        /// Allocated space for Objective-C interop implementation
        /// to use as needed. Will be initialized to nuint.MaxValue.
        /// </summary>
        public nuint Scratch;

        // Internal fields
        internal IntPtr GCHandle;
    }

    /// <summary>
    /// An Objective-C object instance.
    /// </summary>
    public struct Instance
    {
        public IntPtr Isa;

        /// <summary>
        /// Given an instance, return the underlying type.
        /// </summary>
        /// <typeparam name="T">Managed type of the instance.</typeparam>
        /// <param name="instancePtr">Instance pointer</param>
        /// <returns>The managed instance</returns>
        public unsafe static T GetInstance<T>(Instance* instancePtr) where T : class
        {
            var lifetimePtr = (ManagedObjectWrapperLifetime**)xm.object_getIndexedIvars((nint)instancePtr);
            var gcHandle = GCHandle.FromIntPtr((*lifetimePtr)->GCHandle);
            return (T)gcHandle.Target;
        }
    }

    /// <summary>
    /// An Objective-C block instance.
    /// </summary>
    /// <remarks>
    /// See http://clang.llvm.org/docs/Block-ABI-Apple.html#high-level for a
    /// description of the ABI represented by this data structure.
    /// </remarks>
    [StructLayout(LayoutKind.Sequential)]
    public struct BlockLiteral
    {
        internal IntPtr Isa;
        internal int Flags;
        internal int Reserved;
        internal IntPtr Invoke; // delegate* unmanaged[Cdecl]<BlockLiteral* , ...args, ret>
        internal unsafe BlockDescriptor* BlockDescriptor;

        // Extension of ABI to handle .NET lifetime.
        internal unsafe BlockLifetime* Lifetime;

        /// <summary>
        /// Get <typeparamref name="T"/> type from the supplied Block.
        /// </summary>
        /// <typeparam name="T">The delegate type the block is associated with.</typeparam>
        /// <param name="block">The block instance</param>
        /// <returns>A delegate</returns>
        public unsafe static T GetDelegate<T>(BlockLiteral* block) where T : Delegate
        {
            var gcHandle = GCHandle.FromIntPtr(block->Lifetime->GCHandle);
            return (T)gcHandle.Target;
        }
    }

    // Internal Block Descriptor data structure.
    // http://clang.llvm.org/docs/Block-ABI-Apple.html#high-level
    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct BlockDescriptor
    {
        public nint Reserved;
        public nint Size;
        public delegate* unmanaged[Cdecl]<BlockLiteral*, BlockLiteral*, void> Copy_helper;
        public delegate* unmanaged[Cdecl]<BlockLiteral*, void> Dispose_helper;
        public void* Signature;
    }

    // Internal data structure for managing Block lifetime.
    [StructLayout(LayoutKind.Sequential)]
    internal struct BlockLifetime
    {
        public IntPtr GCHandle;
        public int RefCount;
    }

    /// <summary>
    /// Base type for all types participating in Objective-C interop.
    /// </summary>
    public abstract class ObjectiveCBase : IDisposable
    {
        public static readonly IntPtr InvalidInstanceValue = (IntPtr)(-1);
        protected IntPtr instance = ObjectiveCBase.InvalidInstanceValue;
        private bool isDisposed = false;

        internal IntPtr Instance
        {
            get => this.instance;
        }

        /// <summary>
        /// Create a <see cref="ObjectiveCBase"/> instance.
        /// </summary>
        protected ObjectiveCBase()
        {
        }

        ~ObjectiveCBase()
            => this.Dispose(disposing: false);

        /// <inheritdoc />
        public void Dispose()
            => this.Dispose(disposing: true);

        /// <summary>
        /// Method called during Dispose().
        /// </summary>
        /// <param name="disposing">If called from <see cref="Dispose"/></param>
        protected virtual void Dispose(bool disposing)
        {
            if (this.isDisposed)
            {
                return;
            }

            this.isDisposed = true;
        }
    }

    /// <summary>
    /// Class used to create wrappers for interoperability with the Objective-C runtime.
    /// </summary>
    public abstract class Wrappers
    {
        #region Object API
        /// <summary>
        /// Register the associated instances with one another.
        /// </summary>
        /// <param name="instance">The Objective-C instance</param>
        /// <param name="typeAssociation">A strong type mapping to the <paramref name="instance"/>.</param>
        /// <param name="obj">The managed object</param>
        /// <param name="flags">Flags to help with registration</param>
        /// <remarks>
        /// Called when:
        ///   - When an Objective-C projected type is created in managed code (e.g. new NSObject()).
        ///   - When a .NET defined Objective-C type is created in managed code (e.g. new DotNetObject()).
        ///   - When a .NET defined Objective-C type is created in Objective-C code (e.g. [[DotNetObject alloc] init]).
        ///
        /// The supplied <paramref name="typeAssociation"/> is required to inherit from <see cref="ObjectiveCBase"/>.
        /// </remarks>
        public void RegisterInstanceWithObject(
            IntPtr instance,
            Type typeAssociation,
            ObjectiveCBase obj,
            RegisterInstanceFlags flags)
        {
            var origin = RuntimeOrigin.ObjectiveC;
            if (flags.HasFlag(RegisterInstanceFlags.ManagedDefinition))
            {
                origin = RuntimeOrigin.DotNet;
                InitWrapper(instance, obj);
            }

            Internals.RegisterIdentity(obj, instance, origin);
        }

        /// <summary>
        /// Get or create a managed wrapper for the supplied Objective-C object.
        /// </summary>
        /// <param name="instance">An Objective-C object</param>
        /// <param name="typeAssociation">A strong type mapping to the <paramref name="instance"/>.</param>
        /// <param name="flags">Flags for creation</param>
        /// <returns>A managed wrapper</returns>
        /// <remarks>
        /// Called when:
        ///   - An Objective-C instance enters the managed environment.
        ///
        /// The supplied <paramref name="typeAssociation"/> is required to inherit from <see cref="ObjectiveCBase"/>.
        /// </remarks>
        /// <see cref="CreateObject(IntPtr, Type , CreateObjectFlags)"/>
        public object GetOrCreateObjectForInstance(IntPtr instance, Type typeAssociation, CreateObjectFlags flags)
        {
            if (flags.HasFlag(CreateObjectFlags.Unwrap) && xm.clr_isRuntimeAllocated(instance))
            {
                unsafe
                {
                    var instPtr = (Instance*)instance.ToPointer();
                    return Instance.GetInstance<object>(instPtr);
                }
            }

            object wrapper;
            if (Internals.TryGetObject(instance, RuntimeOrigin.ObjectiveC, out wrapper))
            {
                return wrapper;
            }

            wrapper = this.CreateObject(instance, typeAssociation, flags);

            // [TODO] Handle wrapper lifetime
            Internals.RegisterIdentity(wrapper, instance, RuntimeOrigin.ObjectiveC);
            return wrapper;
        }

        /// <summary>
        /// Called by <see cref="GetOrCreateObjectForInstance(IntPtr, Type, CreateObjectFlags)"/>.
        /// </summary>
        /// <param name="instance">An Objective-C instance</param>
        /// <param name="typeAssociation">A strong type mapping to the <paramref name="instance"/>.</param>
        /// <param name="flags">Flags for creation</param>
        /// <returns>A managed wrapper</returns>
        /// <remarks>
        /// Called when:
        ///   - The instance has no currently associated managed object.
        /// </remarks>
        protected abstract ObjectiveCBase CreateObject(IntPtr instance, Type typeAssociation, CreateObjectFlags flags);

        /// <summary>
        /// Get a callback to call when checking for unmanaged references.
        /// </summary>
        /// <param name="isManagedRegistration">Boolean indicating the callback is for a managed registered instance.</param>
        /// <returns>An unmanaged callback</returns>
        /// <remarks>
        /// Overriding this method provides a mechanism to override the default reference check callback.
        ///
        /// The returned callback in C could be defined as below. The argument
        /// is the Objective-C instance.
        /// <code>
        /// int ref_callback(void* id)
        /// {
        ///    return 0; // Return zero for no reference or 1 for reference
        /// }
        /// </code>
        /// </remarks>
        /// <see cref="RegisterInstanceFlags.ManagedDefinition"/>
        protected unsafe virtual delegate* unmanaged[Cdecl]<IntPtr, int> GetReferenceCallback(bool isManagedRegistration) => null;

        /// <summary>
        /// Get the associated Objective-C instance.
        /// </summary>
        /// <param name="obj">Managed wrapper base</param>
        /// <returns>The Objective-C instance</returns>
        /// <remarks>
        /// Called when:
        ///   - Passing an object to the Objective-C runtime when created by an unknown <see cref="Wrappers"/> implementation.
        /// </remarks>
        public IntPtr GetInstanceFromObject(ObjectiveCBase obj)
        {
            return obj.Instance;
        }

        /// <summary>
        /// Separate the object wrapper from the underlying Objective-C instance.
        /// </summary>
        /// <param name="wrapper">Managed wrapper</param>
        /// <remarks>
        /// Called when:
        ///   - The managed object should be separated from its Objective-C instance.
        /// </remarks>
        public void SeparateObjectFromInstance(ObjectiveCBase wrapper)
        {
            Internals.UnregisterObjectWrapper(wrapper);
        }

        /// <summary>
        /// Register the current wrappers instance as the global one for the system.
        /// </summary>
        /// <remarks>
        /// Registering as the global instance will call this implementation's
        /// <see cref="GetMessageSendCallbacks(out IntPtr, out IntPtr, out IntPtr, out IntPtr, out IntPtr)"/> and
        /// pass the pointers to the runtime to override the resolved pointers.
        /// </remarks>
        public void RegisterAsGlobal()
        {
            this.GetMessageSendCallbacks(
                out IntPtr objc_msgSend,
                out IntPtr objc_msgSend_fpret,
                out IntPtr objc_msgSend_stret,
                out IntPtr objc_msgSendSuper,
                out IntPtr objc_msgSendSuper_stret);

            xm.clr_SetGlobalMessageSendCallbacks(
                objc_msgSend,
                objc_msgSend_fpret,
                objc_msgSend_stret,
                objc_msgSendSuper,
                objc_msgSendSuper_stret);
        }

        /// <summary>
        /// Get function pointers for Objective-C runtime message passing.
        /// </summary>
        /// <remarks>
        /// Providing these overrides can enable support for Objective-C
        /// exception propagation and variadic argument support.
        ///
        /// Allows the implementer of the global Objective-C wrapper class
        /// to provide overrides to the 'objc_msgSend*' APIs for the
        /// Objective-C runtime.
        /// </remarks>
        public abstract void GetMessageSendCallbacks(
            out IntPtr objc_msgSend,
            out IntPtr objc_msgSend_fpret,
            out IntPtr objc_msgSend_stret,
            out IntPtr objc_msgSendSuper,
            out IntPtr objc_msgSendSuper_stret);

        /// <summary>
        /// Get the lifetime and memory management functions for all managed
        /// type definitions that are projected into the Objective-C environment.
        /// </summary>
        /// <param name="allocImpl">Alloc implementation</param>
        /// <param name="deallocImpl">Dealloc implementation</param>
        /// <remarks>
        /// See <see href="https://developer.apple.com/documentation/objectivec/nsobject/1571958-alloc">alloc</see>.
        /// See <see href="https://developer.apple.com/documentation/objectivec/nsobject/1571947-dealloc">dealloc</see>.
        /// </remarks>
        public static void GetLifetimeMethods(
            out IntPtr allocImpl,
            out IntPtr deallocImpl)
        {
            unsafe
            {
                allocImpl = (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, IntPtr, IntPtr>)&AllocProxy;
                deallocImpl = (IntPtr)xm.clr_dealloc_Raw;
            }
        }
        #endregion Object API

        #region Block API
        /// <summary>
        /// Create an Objective-C Block for the supplied Delegate.
        /// </summary>
        /// <param name="instance">A Delegate to wrap</param>
        /// <param name="flags">Flags for creation</param>
        /// <returns>An Objective-C Block</returns>
        /// <see cref="GetBlockInvokeAndSignature(Delegate, CreateBlockFlags, out string)"/>
        public BlockLiteral CreateBlockForDelegate(Delegate instance, CreateBlockFlags flags)
        {
            unsafe
            {
                BlockDetails* details;
                if (Internals.TryGetIdentity(instance, RuntimeOrigin.DotNet, out IntPtr blockDetailsMaybe))
                {
                    details = (BlockDetails*)blockDetailsMaybe;
                }
                else
                {
                    string signature;
                    var invoker = this.GetBlockInvokeAndSignature(instance, flags, out signature);

                    details = CreateBlockDetails(instance, invoker, signature);
                    Internals.RegisterIdentity(instance, (IntPtr)details, RuntimeOrigin.DotNet);
                }

                return CreateBlock(&details->Desc, &details->Lifetime, details->Invoker);
            }
        }

        /// <summary>
        /// Called if there is currently no existing Objective-C wrapper for a Delegate.
        /// </summary>
        /// <param name="del">Delegate for block</param>
        /// <param name="flags">Flags for creation</param>
        /// <param name="signature">Type Encoding for returned block</param>
        /// <returns>A callable function pointer for Block dispatch by the Objective-C runtime</returns>
        /// <remarks>
        /// Defer to the implementer for determining the <see cref="https://developer.apple.com/library/archive/documentation/Cocoa/Conceptual/ObjCRuntimeGuide/Articles/ocrtTypeEncodings.html#//apple_ref/doc/uid/TP40008048-CH100">Block signature</see>
        /// that should be used to project the managed Delegate.
        /// </remarks>
        protected abstract IntPtr GetBlockInvokeAndSignature(Delegate del, CreateBlockFlags flags, out string signature);

        /// <summary>
        /// Delegate describing a factory function for creation of a .NET Delegate wrapper for an Objective-C Block.
        /// </summary>
        /// <param name="block">The Objective-C block instance</param>
        /// <param name="invoker">The raw pointer to cast to the appropriate function pointer type and invoke</param>
        /// <returns>A Delegate</returns>
        /// <remarks>
        /// The C# function pointer syntax is dependent on the signature of the
        /// Block, but does takes the block argument as the first argument.
        /// For example:
        /// <code>
        /// ((delegate* unmanaged[Cdecl]&lt;IntPtr [, arg]*, ret&gt)invoker)(block, ...);
        /// </code>
        /// </remarks>
        public delegate Delegate CreateDelegate(IntPtr block, IntPtr invoker);

        /// <summary>
        /// Get or create a Delegate to represent the supplied Objective-C Block.
        /// </summary>
        /// <param name="block">Objective-C Block instance.</param>
        /// <param name="flags">Flags for creation</param>
        /// <param name="createDelegate">Delegate to call if one doesn't exist.</param>
        /// <returns>A Delegate</returns>
        public Delegate GetOrCreateDelegateForBlock(IntPtr block, CreateDelegateFlags flags, CreateDelegate createDelegate)
        {
            if (flags.HasFlag(CreateDelegateFlags.Unwrap))
            {
                // Check if block is a wrapper for an actual delegate.
                if (TryInspectInstanceAsDelegateWrapper(block, out Delegate registeredDelegate))
                {
                    return registeredDelegate;
                }
            }

            // Check if a delegate already exists.
            if (Internals.TryGetObject(block, RuntimeOrigin.ObjectiveC, out object managed))
            {
                return (Delegate)managed;
            }

            // Decompose the pointer to the Objective-C Block ABI and retrieve
            // the needed values.
            IntPtr invoker;
            unsafe
            {
                // [TODO] Handle cleanup if the below factory function throws
                // an exception.
                block = (IntPtr)xm.Block_copy((void*)block);

                var blockLiteral = (BlockLiteral*)block;
                invoker = blockLiteral->Invoke;
            }

            // Call the supplied create delegate with the values needed to
            // invoke the Block.
            Delegate wrappedBlock = createDelegate(block: block, invoker: invoker);

            // [TODO] Register for block release (i.e. Block_release). Since the Delegate
            // is extending the lifetime of the Block, how does it know when and how to
            // release the Block (i.e. on UI thread).
            Internals.RegisterIdentity(wrappedBlock, block, RuntimeOrigin.ObjectiveC);
            return wrappedBlock;
        }

        /// <summary>
        /// Release the supplied block.
        /// </summary>
        /// <param name="block">The block to release</param>
        public void ReleaseBlockLiteral(ref BlockLiteral block)
        {
            ReleaseBlock(ref block);
        }
        #endregion Block API

        #region Threading API
        /// <summary>
        /// Provides a way to indicate to the runtime that the .NET ThreadPool should
        /// add a NSAutoreleasePool to a thread and handle draining.
        /// </summary>
        /// <remarks>
        /// Work items executed on threadpool worker threads are wrapped with an NSAutoreleasePool
        /// that drains when the work item completes.
        /// See https://developer.apple.com/documentation/foundation/nsautoreleasepool
        /// </remarks>
        /// Addresses https://github.com/dotnet/runtime/issues/44213
        public static void EnableAutoReleasePoolsForThreadPool()
        {
            throw new NotImplementedException();
        }
        #endregion Threading API

        /// <summary>
        /// Create a <see cref="Wrappers"/> instance.
        /// </summary>
        protected Wrappers()
        {
        }

        #region Implementation Details
        private unsafe static IntPtr AllocateObjCInstance(IntPtr klass)
        {
            // Allocate additional memory for lifetime pointer.
            return xm.class_createInstance(klass, sizeof(ManagedObjectWrapperLifetime*));
        }

        private unsafe static void InitWrapper(IntPtr wrapper, object instance)
        {
            var lifetime = (ManagedObjectWrapperLifetime*)Marshal.AllocCoTaskMem(sizeof(ManagedObjectWrapperLifetime));
            IntPtr gcptr = GCHandle.ToIntPtr(GCHandle.Alloc(instance));
            Trace.WriteLine($"Object: Lifetime: 0x{(nint)lifetime:x}, GCHandle: 0x{gcptr.ToInt64():x}");

            // N.B. see details in xm.c regarding the ManagedObjectWrapperLifetime type.
            lifetime->GCHandle = gcptr;
            lifetime->Scratch = nuint.MaxValue;

            var lifetimePtr = (ManagedObjectWrapperLifetime**)xm.object_getIndexedIvars(wrapper);
            *lifetimePtr = lifetime;
        }

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        private unsafe static IntPtr AllocProxy(IntPtr cls, IntPtr sel)
        {
            IntPtr new_id = AllocateObjCInstance(cls);
            return new_id;
        }

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        private unsafe static void CopyBlock(BlockLiteral* blockDst, BlockLiteral* blockSrc)
        {
            Trace.WriteLine($"CopyBlock: dst: {(long)blockDst:x} src: {(long)blockSrc:x}");

            Debug.Assert(blockSrc->Lifetime != null);
            Debug.Assert(blockSrc->BlockDescriptor != null);

            var blockDescSrc = blockSrc->BlockDescriptor;

            // Extend the lifetime of the delegate
            {
                int count = Interlocked.Increment(ref blockSrc->Lifetime->RefCount);
                Debug.Assert(count != 1);

                Console.WriteLine($"** Block copy: {(long)blockDescSrc:x}, Count: {count}");
            }

            blockDst->BlockDescriptor = blockDescSrc;
            blockDst->Lifetime = blockSrc->Lifetime;
        }

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        private unsafe static void DisposeBlock(BlockLiteral* block)
        {
            Trace.WriteLine($"DisposeBlock: blk: {(long)block:x}");

            ReleaseBlock(ref *block);
        }

        private unsafe static void ReleaseBlock(ref BlockLiteral block)
        {
            Debug.Assert(block.Lifetime != null);
            Debug.Assert(block.BlockDescriptor != null);

            BlockDescriptor* blockDesc = block.BlockDescriptor;

            // Decrement the ref count on the delegate
            {
                int count = Interlocked.Decrement(ref block.Lifetime->RefCount);
                Debug.Assert(count != -1);

                Console.WriteLine($"** Block dispose: {(long)blockDesc:x}, Count: {count}");
                if (count == 0)
                {
                    Console.WriteLine($"** Weak reference: {(long)blockDesc:x}");
                }

                block.Lifetime = null;
            }

            // Remove the associated block description.
            block.BlockDescriptor = null;
        }

        private struct BlockDetails
        {
            public BlockDescriptor Desc;
            public BlockLifetime Lifetime;
            public IntPtr Invoker;
        }

        // See https://developer.apple.com/library/archive/documentation/Cocoa/Conceptual/ObjCRuntimeGuide/Articles/ocrtTypeEncodings.html for signature details
        private unsafe static BlockDetails* CreateBlockDetails(Delegate delegateInstance, IntPtr invoker, string signature)
        {
            byte[] signatureBytes = Encoding.UTF8.GetBytes(signature);
            var details = (BlockDetails*)Marshal.AllocCoTaskMem(sizeof(BlockDetails) + signatureBytes.Length + 1);

            // Copy the siganture into the extra space at the end of the BlockDetails structure.
            byte* sigDest = (byte*)details + sizeof(BlockDetails);
            var signatureDest = new Span<byte>(sigDest, signatureBytes.Length + 1);
            signatureBytes.CopyTo(signatureDest);

            // Initialize description.
            details->Desc.Reserved = 0;
            details->Desc.Size = sizeof(BlockLiteral);
            details->Desc.Copy_helper = &CopyBlock;
            details->Desc.Dispose_helper = &DisposeBlock;
            details->Desc.Signature = sigDest;

            // Initialize lifetime.
            var delegateHandle = GCHandle.Alloc(delegateInstance);
            details->Lifetime.GCHandle = GCHandle.ToIntPtr(delegateHandle);
            details->Lifetime.RefCount = 1;

            // Store the invoke function pointer.
            details->Invoker = invoker;

            // Since the Block Descriptor is required by the Block ABI, we
            // utilize that address as the opaque handle that will be released
            // when the associated Delegate is collected when the lifetime
            // reference count hits 0.
            Debug.Assert(details == &details->Desc);
            return details;
        }

        // See https://clang.llvm.org/docs/Block-ABI-Apple.html
        // Our Block contains:
        private const int DotNetBlockLiteralFlags =
            (1 << 25)       // copy/dispose helpers
            | (1 << 30);    // Function signature

        private unsafe static BlockLiteral CreateBlock(BlockDescriptor* desc, BlockLifetime* lifetime, IntPtr invoker)
        {
            return new BlockLiteral()
            {
                Isa = (IntPtr)xm._NSConcreteStackBlock_Raw,
                Flags = DotNetBlockLiteralFlags,
                Invoke = invoker,
                BlockDescriptor = desc,
                Lifetime = lifetime
            };
        }

        private static bool TryInspectInstanceAsDelegateWrapper(IntPtr blockWrapperMaybe, out Delegate del)
        {
            Debug.Assert(blockWrapperMaybe != default(IntPtr));
            del = null;

            unsafe
            {
                var block = (BlockLiteral*)blockWrapperMaybe;

                // First check if the flags we set are present and the size is correct.
                if ((block->Flags & DotNetBlockLiteralFlags) != DotNetBlockLiteralFlags
                    || block->BlockDescriptor->Size != sizeof(BlockLiteral))
                {
                    return false;
                }

                // Our flags indicate we set copy/dispose helper functions and a signature.
                // This means the block could be one we created or another block with
                // helpers and a signature. In order to fully clarify, check if the helpers
                // are our internal versions.
                void* copyHelper = block->BlockDescriptor->Copy_helper;
                void* disposeHelper = block->BlockDescriptor->Dispose_helper;
                if (copyHelper != (delegate* unmanaged[Cdecl]<BlockLiteral*, BlockLiteral*, void>)&CopyBlock
                    || disposeHelper != (delegate* unmanaged[Cdecl]<BlockLiteral*, void>)&DisposeBlock)
                {
                    return false;
                }

                // At this point we know this is a block we created. Therefore, we can
                // use our internal fields on the BlockLiteral data structure to get
                // at the underlying managed object.
                Debug.Assert(block->BlockDescriptor->Size == sizeof(BlockLiteral));
                Debug.Assert(block->Lifetime->RefCount != 0);
                del = (Delegate)GCHandle.FromIntPtr(block->Lifetime->GCHandle).Target;
                return true;
            }
        }

        private enum RuntimeOrigin
        {
            DotNet,
            ObjectiveC,
        }

        private static class Internals
        {
            private static readonly ReaderWriterLockSlim RegisteredIdentityLock = new ReaderWriterLockSlim();
            private static readonly Dictionary<object, IntPtr> ObjectToIdentity_DotNet = new Dictionary<object, IntPtr>();
            private static readonly Dictionary<IntPtr, object> IdentityToObject_DotNet = new Dictionary<IntPtr, object>();
            private static readonly Dictionary<object, IntPtr> ObjectToIdentity_ObjectiveC = new Dictionary<object, IntPtr>();
            private static readonly Dictionary<IntPtr, object> IdentityToObject_ObjectiveC = new Dictionary<IntPtr, object>();
            public static void RegisterIdentity(object managed, IntPtr native, RuntimeOrigin origin)
            {
                RegisteredIdentityLock.EnterWriteLock();

                try
                {
                    if (origin == RuntimeOrigin.DotNet)
                    {
                        ObjectToIdentity_DotNet.Add(managed, native);
                        IdentityToObject_DotNet.Add(native, managed);
                    }
                    else
                    {
                        Debug.Assert(origin == RuntimeOrigin.ObjectiveC);
                        ObjectToIdentity_ObjectiveC.Add(managed, native);
                        IdentityToObject_ObjectiveC.Add(native, managed);
                    }
                }
                finally
                {
                    RegisteredIdentityLock.ExitWriteLock();
                }
            }
            public static void UnregisterObjectWrapper(object wrapper)
            {
                RegisteredIdentityLock.EnterWriteLock();

                try
                {
                    IntPtr native = ObjectToIdentity_ObjectiveC[wrapper];

                    ObjectToIdentity_ObjectiveC.Remove(wrapper);
                    IdentityToObject_ObjectiveC.Remove(native);
                }
                finally
                {
                    RegisteredIdentityLock.ExitWriteLock();
                }
            }
            public static bool TryGetIdentity(object managed, RuntimeOrigin origin, out IntPtr native)
            {
                RegisteredIdentityLock.EnterReadLock();

                try
                {
                    if (origin == RuntimeOrigin.DotNet)
                    {
                        return ObjectToIdentity_DotNet.TryGetValue(managed, out native);
                    }
                    else
                    {
                        Debug.Assert(origin == RuntimeOrigin.ObjectiveC);
                        return ObjectToIdentity_ObjectiveC.TryGetValue(managed, out native);
                    }
                }
                finally
                {
                    RegisteredIdentityLock.ExitReadLock();
                }
            }
            public static bool TryGetObject(IntPtr native, RuntimeOrigin origin, out object managed)
            {
                RegisteredIdentityLock.EnterReadLock();

                try
                {
                    if (origin == RuntimeOrigin.DotNet)
                    {
                        return IdentityToObject_DotNet.TryGetValue(native, out managed);
                    }
                    else
                    {
                        Debug.Assert(origin == RuntimeOrigin.ObjectiveC);
                        return IdentityToObject_ObjectiveC.TryGetValue(native, out managed);
                    }
                }
                finally
                {
                    RegisteredIdentityLock.ExitReadLock();
                }
            }
        }
        #endregion Implementation Details
    }
}