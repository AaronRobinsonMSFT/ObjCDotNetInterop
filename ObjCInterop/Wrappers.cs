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
    public enum CreateBlockFlags
    {
        None,
    }

    [Flags]
    public enum CreateObjectFlags
    {
        None,

        /// <summary>
        /// The supplied instance is a Block.
        /// </summary>
        Block,

        /// <summary>
        /// The supplied instance should be check if it is actually a
        /// wrapped managed object and not a pure Objective-C instance.
        ///
        /// If the instance is wrapped return the underlying managed object
        /// instead of creating a new wrapper.
        /// </summary>
        Unwrap,
    }

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
        internal unsafe DelegateBlockDesc* BlockDescriptor;

        // Extension of ABI to help with .NET lifetime.
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

    /// <summary>
    /// 
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct DelegateBlockDesc
    {
        public nint Reserved;
        public nint Size;
        public delegate* unmanaged[Cdecl]<BlockLiteral*, BlockLiteral*, void> Copy_helper;
        public delegate* unmanaged[Cdecl]<BlockLiteral*, void> Dispose_helper;
        public void* Signature;
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
        /// pass the pointers to the runtime to override the global settings.
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
            IntPtr native;
            if (Internals.TryGetIdentity(instance, out native))
            {
                return native;
            }

            unsafe
            {
                IntPtr klass = this.ComputeInstanceClass(instance, flags);

                // Add a lifetime size for the GC Handle.
                native = xm.class_createInstance(klass, sizeof(ManagedObjectWrapperLifetime)).value;

                var lifetime = (ManagedObjectWrapperLifetime*)xm.object_getIndexedIvars(native);
                IntPtr gcptr = GCHandle.ToIntPtr(GCHandle.Alloc(instance));
                Trace.WriteLine($"Object: Lifetime: 0x{(nint)lifetime:x}, GCHandle: 0x{gcptr.ToInt64():x}");
                lifetime->GCHandle = gcptr;
                lifetime->RefCount = 1;
            }

            Internals.RegisterIdentity(instance, native);
            return native;
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

            // If the supplied instance is a block and the caller has requested
            // unwrapping of instances if they are actually managed objects, we
            // take a closer look at the instance.
            if (flags.HasFlag(CreateObjectFlags.Block | CreateObjectFlags.Unwrap))
            {
                if (TryInspectInstanceAsDelegateWrapper(instance, out wrapper))
                {
                    return wrapper;
                }
            }

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
        public object GetOrRegisterObjectForInstance(IntPtr instance, CreateObjectFlags flags, object wrapperMaybe)
        {
            object wrapper;
            if (!Internals.TryGetObject(instance, out wrapper))
            {
                Internals.RegisterIdentity(wrapperMaybe, instance);
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
        /// <see cref="ComputeInstanceClass(object, CreateInstanceFlags)"/>
        public BlockLiteral GetOrCreateBlockForDelegate(Delegate instance, CreateBlockFlags flags)
        {
            IntPtr blockDetails = default;

            unsafe
            {
                BlockDetails* details;
                if (Internals.TryGetIdentity(instance, out blockDetails))
                {
                    details = (BlockDetails*)blockDetails;
                }
                else
                {
                    string signature;
                    var invoker = this.GetBlockInvokeAndSignature(instance, flags, out signature);

                    details = CreateBlockDetails(instance, invoker, signature);
                    Internals.RegisterIdentity(instance, (IntPtr)details);
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
        /// Release the supplied block.
        /// </summary>
        /// <param name="block">The block to release</param>
        public void ReleaseBlockLiteral(ref BlockLiteral block)
        {
            ReleaseBlock(ref block);
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

            DelegateBlockDesc* blockDesc = block.BlockDescriptor;

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
            public DelegateBlockDesc Desc;
            public ManagedObjectWrapperLifetime Lifetime;
            public IntPtr Invoker;
        }

        // See https://developer.apple.com/library/archive/documentation/Cocoa/Conceptual/ObjCRuntimeGuide/Articles/ocrtTypeEncodings.html for signature details
        private unsafe static BlockDetails* CreateBlockDetails(Delegate delegateInstance, IntPtr invoker, string signature)
        {
            byte[] signatureBytes = Encoding.UTF8.GetBytes(signature);
            var details = (BlockDetails*)Marshal.AllocCoTaskMem(sizeof(BlockDetails) + signatureBytes.Length + 1);

            // Copy the siganture types into the extra space at the end of the BlockDetails structure.
            byte* sigDest = (byte*)details + sizeof(BlockDetails);
            var signatureDest = new Span<byte>(sigDest, signatureBytes.Length + 1);
            signatureBytes.CopyTo(signatureDest);

            // Set up description.
            details->Desc.Reserved = 0;
            details->Desc.Size = sizeof(BlockLiteral);
            details->Desc.Copy_helper = &CopyBlock;
            details->Desc.Dispose_helper = &DisposeBlock;
            details->Desc.Signature = sigDest;

            // Set up lifetime portion.
            var delegateHandle = GCHandle.Alloc(delegateInstance);
            details->Lifetime.GCHandle = GCHandle.ToIntPtr(delegateHandle);
            details->Lifetime.RefCount = 1;

            // Store the invoke function pointer.
            details->Invoker = invoker;

            Debug.Assert(details == &details->Desc);
            return details;
        }

        // See https://clang.llvm.org/docs/Block-ABI-Apple.html
        // Our Block contains:
        private const int DotNetBlockLiteralFlags =
            (1 << 25)       // copy/dispose helpers
            | (1 << 30);    // Function signature

        private unsafe static BlockLiteral CreateBlock(DelegateBlockDesc* desc, ManagedObjectWrapperLifetime* lifetime, IntPtr invoker)
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

        private static bool TryInspectInstanceAsDelegateWrapper(IntPtr blockWrapperMaybe, out object managed)
        {
            Debug.Assert(blockWrapperMaybe != default(IntPtr));
            managed = null;

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
                managed = GCHandle.FromIntPtr(block->Lifetime->GCHandle).Target;
                return true;
            }
        }

        /// <summary>
        /// Type used to represent the dispatching function and associated context.
        /// </summary>
        public sealed class BlockProxy : IDisposable
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

            /// <inheritdoc />
            public void Dispose()
            {
                this.Dispose(true);
                GC.SuppressFinalize(this);
            }

            private bool isDisposed = false;

            ~BlockProxy()
            {
                this.Dispose(false);
            }

            private void Dispose(bool disposing)
            {
                if (this.isDisposed)
                {
                    return;
                }

                xm.Block_release(this.Block);
                this.isDisposed = true;
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
        protected BlockProxy CreateBlockProxy(IntPtr block)
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

        private static class Internals
        {
            private static readonly ReaderWriterLockSlim RegisteredIdentityLock = new ReaderWriterLockSlim();
            private static readonly Dictionary<object, IntPtr> ObjectToIdentity = new Dictionary<object, IntPtr>();
            private static readonly Dictionary<IntPtr, object> IdentityToObject = new Dictionary<IntPtr, object>();
            public static void RegisterIdentity(object managed, IntPtr native)
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
            public static bool TryGetIdentity(object managed, out IntPtr native)
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
            public static bool TryGetObject(IntPtr native, out object managed)
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
}