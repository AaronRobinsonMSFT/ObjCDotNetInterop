using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;

using ObjCRuntime;

namespace System.Runtime.InteropServices.ObjectiveC
{
    [Flags]
    public enum CreateInstanceFlags
    {
        None,
    }

    [Flags]
    public enum CreateObjectFlags
    {
        None,

        /// <summary>
        /// The supplied Objective-C instance should be check if it is a
        /// wrapped managed object and not a pure Objective-C instance.
        ///
        /// If the instance is wrapped return the underlying managed object
        /// instead of creating a new wrapper.
        /// </summary>
        Unwrap,
    }

    [Flags]
    public enum CreateBlockFlags
    {
        None,
    }

    [Flags]
    public enum CreateDelegateFlags
    {
        None,

        /// <summary>
        /// The supplied Objective-C block should be check if it is a
        /// wrapped Delegate and not a pure Objective-C Block.
        ///
        /// If the instance is wrapped return the underlying Delegate
        /// instead of creating a new wrapper.
        /// </summary>
        Unwrap,
    }

    // Internal data structure for managing reference counted lifetime.
    internal struct ManagedObjectWrapperLifetime
    {
        public IntPtr GCHandle;
        public uint RefCount;
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
            var lifetime = (ManagedObjectWrapperLifetime*)xm.object_getIndexedIvars((nint)instancePtr);
            var gcHandle = GCHandle.FromIntPtr(lifetime->GCHandle);
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
        internal unsafe ManagedObjectWrapperLifetime* Lifetime;

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

    /// <summary>
    /// Type used to represent a Block's invoking function and context.
    /// </summary>
    public sealed class BlockDispatch
    {
        /// <summary>
        /// Block instance.
        /// </summary>
        public IntPtr Block { get; init; }

        /// <summary>
        /// The function pointer to invoke.
        /// </summary>
        /// <remarks>
        /// The C# function pointer syntax is dependent on the signature of the
        /// Block, but does takes the <see cref="Block"/> as the first argument.
        /// For example:
        /// <code>
        /// ((delegate* unmanaged[Cdecl]&lt;IntPtr [, arg]*, ret&gt)Invoker)(Block, ...);
        /// </code>
        /// </remarks>
        public IntPtr Invoker { get; init; }
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
        /// Get or create a Objective-C wrapper for the supplied managed object.
        /// </summary>
        /// <param name="instance">A managed object to wrap</param>
        /// <param name="flags">Flags for creation</param>
        /// <returns>An Objective-C wrapper</returns>
        /// <see cref="ComputeInstanceClass(object, CreateInstanceFlags)"/>
        public IntPtr GetOrCreateInstanceForObject(object instance, CreateInstanceFlags flags)
        {
            IntPtr wrapper;
            if (Internals.TryGetIdentity(instance, RuntimeOrigin.DotNet, out wrapper))
            {
                return wrapper;
            }

            unsafe
            {
                IntPtr klass = this.ComputeInstanceClass(instance, flags);

                // Add a lifetime size for the GC Handle.
                wrapper = xm.class_createInstance(klass, sizeof(ManagedObjectWrapperLifetime)).value;

                var lifetime = (ManagedObjectWrapperLifetime*)xm.object_getIndexedIvars(wrapper);
                IntPtr gcptr = GCHandle.ToIntPtr(GCHandle.Alloc(instance));
                Trace.WriteLine($"Object: Lifetime: 0x{(nint)lifetime:x}, GCHandle: 0x{gcptr.ToInt64():x}");
                lifetime->GCHandle = gcptr;
                lifetime->RefCount = 1;
            }

            Internals.RegisterIdentity(instance, wrapper, RuntimeOrigin.DotNet);
            return wrapper;
        }

        /// <summary>
        /// Called if there is currently no existing Objective-C wrapper for an object.
        /// </summary>
        /// <param name="instance">A managed object to wrap</param>
        /// <param name="flags">Flags for creation</param>
        /// <returns>An Objective-C pointer to a class</returns>
        /// <remarks>
        /// Defer to the implementer for determining the <see cref="https://developer.apple.com/documentation/objectivec/class">Objective-C Class</see>
        /// that should be used to wrap the managed object.
        /// </remarks>
        protected abstract IntPtr ComputeInstanceClass(object instance, CreateInstanceFlags flags);

        /// <summary>
        /// Get or create a managed wrapper for the supplied Objective-C object.
        /// </summary>
        /// <param name="instance">An Objective-C object</param>
        /// <param name="flags">Flags for creation</param>
        /// <returns>A managed wrapper</returns>
        /// <see cref="CreateObject(IntPtr, CreateObjectFlags)"/>
        public object GetOrCreateObjectForInstance(IntPtr instance, CreateObjectFlags flags)
        {
            object wrapper;
            if (flags.HasFlag(CreateObjectFlags.Unwrap)
                && Internals.TryGetObject(instance, RuntimeOrigin.DotNet, out wrapper))
            {
                return wrapper;
            }

            if (Internals.TryGetObject(instance, RuntimeOrigin.ObjectiveC, out wrapper))
            {
                return wrapper;
            }

            wrapper = this.CreateObject(instance, flags);
            Internals.RegisterIdentity(wrapper, instance, RuntimeOrigin.ObjectiveC);
            return wrapper;
        }

        /// <summary>
        /// Called if there is currently no existing managed wrapper.
        /// </summary>
        /// <param name="instance">An Objective-C instance</param>
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
        public object GetOrRegisterObjectForInstance(IntPtr instance, CreateObjectFlags flags, object wrapperMaybe)
        {
            object wrapper;
            if (flags.HasFlag(CreateObjectFlags.Unwrap)
                && Internals.TryGetObject(instance, RuntimeOrigin.DotNet, out wrapper))
            {
                return wrapper;
            }

            if (!Internals.TryGetObject(instance, RuntimeOrigin.ObjectiveC, out wrapper))
            {
                Internals.RegisterIdentity(wrapperMaybe, instance, RuntimeOrigin.ObjectiveC);
                wrapper = wrapperMaybe;
            }

            return wrapper;
        }

        /// <summary>
        /// Get or create a Objective-C Block for the supplied Delegate.
        /// </summary>
        /// <param name="instance">A Delegate to wrap</param>
        /// <param name="flags">Flags for creation</param>
        /// <returns>An Objective-C Block</returns>
        /// <see cref="GetBlockInvokeAndSignature(Delegate, CreateBlockFlags, out string)"/>
        public BlockLiteral GetOrCreateBlockForDelegate(Delegate instance, CreateBlockFlags flags)
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
        /// <returns>A callable delegate to the Objective-C runtime</returns>
        /// <remarks>
        /// Defer to the implementer for determining the <see cref="https://developer.apple.com/library/archive/documentation/Cocoa/Conceptual/ObjCRuntimeGuide/Articles/ocrtTypeEncodings.html#//apple_ref/doc/uid/TP40008048-CH100">Block signature</see>
        /// that should be used to project the managed Delegate.
        /// </remarks>
        protected abstract IntPtr GetBlockInvokeAndSignature(Delegate del, CreateBlockFlags flags, out string signature);

        /// <summary>
        /// Delegate describing a factory function for creation from a <see cref="BlockDispatch"/>.
        /// </summary>
        /// <param name="dispatch">BlockDispatch instance</param>
        /// <returns>A Delegate</returns>
        public delegate Delegate CreateDelegate(BlockDispatch dispatch);

        /// <summary>
        /// Get or create a Delegate to represent the supplied Objective-C Block.
        /// </summary>
        /// <param name="block">Objective-C Block instance.</param>
        /// <param name="flags">Flags for creation</param>
        /// <param name="createDelegate">Delegate to call if one doesn't exist.</param>
        /// <returns>A Delegate</returns>
        public Delegate GetOrCreateDelegateForBlock(IntPtr block, CreateDelegateFlags flags, CreateDelegate createDelegate)
        {
            Delegate registeredDelegate;
            if (flags.HasFlag(CreateDelegateFlags.Unwrap))
            {
                // Check if block is a wrapper for an actual delegate.
                if (TryInspectInstanceAsDelegateWrapper(block, out registeredDelegate))
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
            BlockDispatch dispatch;
            unsafe
            {
                var blockLiteral = (BlockLiteral*)block;
                dispatch = new BlockDispatch()
                {
                    Block = block,
                    Invoker = blockLiteral->Invoke
                };
            }

            // Call the supplied create delegate with the values needed to
            // invoke the Block.
            Delegate wrappedBlock = createDelegate(dispatch);

            // [TODO] Register for block release (i.e. Block_release).
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

        /// <summary>
        /// Create a <see cref="Wrappers"/> instance.
        /// </summary>
        protected Wrappers()
        {
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

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        private unsafe static void CopyBlock(BlockLiteral* blockDst, BlockLiteral* blockSrc)
        {
            Trace.WriteLine($"CopyBlock: dst: {(long)blockDst:x} src: {(long)blockSrc:x}");

            Debug.Assert(blockSrc->Lifetime != null);
            Debug.Assert(blockSrc->BlockDescriptor != null);

            var blockDescSrc = blockSrc->BlockDescriptor;

            // Extend the lifetime of the delegate
            {
                uint count = Interlocked.Increment(ref blockSrc->Lifetime->RefCount);
                Debug.Assert(count != 1);
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
                uint count = Interlocked.Decrement(ref block.Lifetime->RefCount);
                Debug.Assert(count != uint.MaxValue);

                block.Lifetime = null;
            }

            // Remove the associated block description.
            block.BlockDescriptor = null;
        }

        private struct BlockDetails
        {
            public BlockDescriptor Desc;
            public ManagedObjectWrapperLifetime Lifetime;
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

        private unsafe static BlockLiteral CreateBlock(BlockDescriptor* desc, ManagedObjectWrapperLifetime* lifetime, IntPtr invoker)
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

                // First check if the flags we set are present.
                if ((block->Flags & DotNetBlockLiteralFlags) != DotNetBlockLiteralFlags)
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
                Debug.Assert(block->Lifetime->RefCount > 0);
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
    }
}