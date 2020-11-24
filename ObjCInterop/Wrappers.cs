using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;

using ObjCRuntime;

namespace System.Runtime.InteropServices.ObjectiveC
{
    [Flags]
    public enum CreateInstanceFlags
    {
        None,
        Block,
    }

    [Flags]
    public enum CreateObjectFlags
    {
        None,
        Block,
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
        /// Type when entering the managed environment from an Objective-C instance.
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
        /// Type when entering the managed environment from an Objective-C block.
        /// </summary>
        public struct BlockDispatch
        {
            public IntPtr Isa;
            public unsafe static T GetInstance<T>(BlockDispatch* dispatchPtr) where T : class
            {
                var block = (BlockLiteral*)dispatchPtr;
                var gcHandle = GCHandle.FromIntPtr(block->DelegateGCHandle);
                return (T)gcHandle.Target;
            }
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
            id native;
            if (Internals.TryGetIdentity(instance, out native))
            {
                return native.value;
            }

            if (flags.HasFlag(CreateInstanceFlags.Block))
            {
                Debug.Assert(instance is Delegate);

                var asDelegate = (Delegate)instance;
                string signature;
                var invoker = this.GetBlockInvokeAndSignature(asDelegate, flags, out signature);
                native = this.CreateBlock(asDelegate, invoker, signature);
            }
            else
            {
                unsafe
                {
                    IntPtr klass = this.ComputeInstanceClass(instance, flags);

                    // Add a lifetime size for the GC Handle.
                    native = xm.class_createInstance(klass, sizeof(ManagedObjectWrapperLifetime));

                    var lifetime = (ManagedObjectWrapperLifetime*)xm.object_getIndexedIvars(native);
                    IntPtr gcptr = GCHandle.ToIntPtr(GCHandle.Alloc(instance));
                    Trace.WriteLine($"Object: Lifetime: 0x{(nint)lifetime:x}, GCHandle: 0x{gcptr.ToInt64():x}");
                    lifetime->GCHandle = gcptr;
                    lifetime->RefCount = 1;
                }
            }

            Internals.RegisterIdentity(instance, native);
            return native.value;
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
        protected abstract IntPtr GetBlockInvokeAndSignature(Delegate del, CreateInstanceFlags flags, out string signature);

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
            Trace.WriteLine($"CopyBlock: dst: {(long)dst:x} src: {(long)src:x}");

            var blockSrc = (BlockLiteral*)src;
            var blockDescSrc = (DelegateBlockDesc*)blockSrc->BlockDescriptor;
            var blockDst = (BlockLiteral*)dst;

            uint count = Interlocked.Increment(ref blockDescSrc->RefCount);
            Debug.Assert(count > 1);
            blockDst->BlockDescriptor = blockDescSrc;
            blockDst->DelegateGCHandle = blockSrc->DelegateGCHandle;

            object del = GCHandle.FromIntPtr(blockSrc->DelegateGCHandle).Target;
            Internals.RegisterIdentity(del, (nint)dst);
        }

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        private unsafe static void DisposeBlock(void* blk)
        {
            Trace.WriteLine($"DisposeBlock: blk: {(long)blk:x}");

            var block = (BlockLiteral*)blk;
            var blockDesc = (DelegateBlockDesc*)block->BlockDescriptor;

            // Cleanup the delegate's handle
            GCHandle.FromIntPtr(block->DelegateGCHandle).Free();

            uint count = Interlocked.Decrement(ref blockDesc->RefCount);
            Debug.Assert(blockDesc->RefCount != uint.MaxValue);
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
        /// <param name="invoker">Function to invoke during dispatch</param>
        /// <param name="signature">The Objective-C type signature of the function</param>
        /// <returns>An Objective-C block</returns>
        /// <remarks>
        /// See <see href="https://developer.apple.com/library/archive/documentation/Cocoa/Conceptual/ObjCRuntimeGuide/Articles/ocrtTypeEncodings.html">Objective-C Type Encodings</see> for how to specify <paramref name="signature"/>.
        ///
        /// See <see href="http://clang.llvm.org/docs/Block-ABI-Apple.html">Objective-C Block ABI</see> for the supported contract.
        /// </remarks>
        private IntPtr CreateBlock(Delegate delegateInstance, IntPtr invoker, string signature)
        {
            var delegateHandle = GCHandle.Alloc(delegateInstance);

            unsafe
            {
                var desc = (DelegateBlockDesc*)Marshal.AllocCoTaskMem(sizeof(DelegateBlockDesc));
                desc->Reserved = 0;
                desc->Size = sizeof(BlockLiteral);
                desc->Copy_helper = &CopyBlock;
                desc->Dispose_helper = &DisposeBlock;
                desc->Signature = Marshal.StringToCoTaskMemUTF8(signature).ToPointer();
                desc->RefCount = 1;

                var block = (BlockLiteral*)Marshal.AllocCoTaskMem(sizeof(BlockLiteral));
                block->Isa = xm._NSConcreteStackBlock_Raw;
                block->Flags = (1 << 25) | (1 << 30); // Descriptor contains copy/dispose and signature.
                block->Invoke = invoker.ToPointer();
                block->BlockDescriptor = desc;
                block->DelegateGCHandle = GCHandle.ToIntPtr(delegateHandle);

                return (nint)block;
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
            private static readonly Dictionary<object, List<id>> ObjectToIdentity = new Dictionary<object, List<id>>();
            private static readonly Dictionary<id, object> IdentityToObject = new Dictionary<id, object>();
            public static void RegisterIdentity(object managed, id native)
            {
                RegisteredIdentityLock.EnterWriteLock();

                try
                {
                    List<id> ids;
                    if (!ObjectToIdentity.TryGetValue(managed, out ids))
                    {
                        ids = new List<id>();
                        ObjectToIdentity.Add(managed, ids);
                    }

                    ids.Add(native);
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
                    List<id> ids;
                    if (!ObjectToIdentity.TryGetValue(managed, out ids))
                    {
                        native = xm.nil;
                        return false;
                    }

                    native = ids[0];
                    return true;
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
}