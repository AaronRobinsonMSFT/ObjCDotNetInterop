using System;
using System.Runtime.InteropServices;

namespace ObjCRuntime
{
    // Objective-C runtime and .NET runtime APIs.
    internal unsafe class xm
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

        [DllImport(nameof(xm))]
        public extern static void clr_SetGlobalMessageSendCallbacks(
            IntPtr objc_msgSend,
            IntPtr objc_msgSend_fpret,
            IntPtr objc_msgSend_stret,
            IntPtr objc_msgSendSuper,
            IntPtr objc_msgSendSuper_stret);

        public static readonly void* objc_msgSend_Raw = Get_objc_msgSend();

        [DllImport(nameof(xm))]
        private extern static void* Get_objc_msgSend();

        public static readonly void* objc_msgSendSuper_Raw = Get_objc_msgSendSuper();

        [DllImport(nameof(xm))]
        private extern static void* Get_objc_msgSendSuper();

        public static readonly void* _NSConcreteStackBlock_Raw = Get_NSConcreteStackBlock();

        [DllImport(nameof(xm))]
        private extern static void* Get_NSConcreteStackBlock();

        [DllImport(nameof(xm), EntryPoint = "objc_getMetaClass_proxy")]
        public extern static IntPtr objc_getMetaClass(
            [MarshalAs(UnmanagedType.LPStr)] string name);

        [DllImport(nameof(xm), EntryPoint = "objc_getClass_proxy")]
        public extern static IntPtr objc_getClass(
            [MarshalAs(UnmanagedType.LPStr)] string name);

        [DllImport(nameof(xm), EntryPoint = "object_getClassName_proxy")]
        [return: MarshalAs(UnmanagedType.LPStr)]
        public extern static string object_getClassName(IntPtr obj);

        [DllImport(nameof(xm), EntryPoint = "object_getClass_proxy")]
        public extern static IntPtr object_getClass(IntPtr obj);

        [DllImport(nameof(xm), EntryPoint = "objc_allocateClassPair_proxy")]
        public extern static IntPtr objc_allocateClassPair(
            IntPtr superclass,
            [MarshalAs(UnmanagedType.LPStr)] string name,
            nint extraBytes);

        [DllImport(nameof(xm), EntryPoint = "sel_registerName_proxy")]
        public extern static IntPtr sel_registerName([MarshalAs(UnmanagedType.LPStr)] string str);

        // types string - https://developer.apple.com/library/archive/documentation/Cocoa/Conceptual/ObjCRuntimeGuide/Articles/ocrtTypeEncodings.html#//apple_ref/doc/uid/TP40008048-CH100
        [DllImport(nameof(xm), EntryPoint = "class_addMethod_proxy")]
        [return: MarshalAs(UnmanagedType.U1)]
        public extern static bool class_addMethod(
            IntPtr cls,
            IntPtr name,
            IntPtr imp,
            [MarshalAs(UnmanagedType.LPStr)] string types);

        [DllImport(nameof(xm), EntryPoint = "objc_registerClassPair_proxy")]
        public extern static void objc_registerClassPair(IntPtr cls);

        [DllImport(nameof(xm), EntryPoint = "class_createInstance_proxy")]
        public extern static IntPtr class_createInstance(IntPtr cls, nint extraBytes);

        [DllImport(nameof(xm), EntryPoint = "class_getName_proxy")]
        [return: MarshalAs(UnmanagedType.LPStr)]
        public extern static string class_getName(IntPtr cls);

        [DllImport(nameof(xm), EntryPoint = "objc_destructInstance_proxy")]
        public extern static void objc_destructInstance(IntPtr obj);

        [DllImport(nameof(xm), EntryPoint = "object_getIndexedIvars_proxy")]
        public extern static void* object_getIndexedIvars(IntPtr obj);

        [DllImport(nameof(xm), EntryPoint = "Block_copy_proxy")]
        public extern static void* Block_copy(void* block);

        [DllImport(nameof(xm), EntryPoint = "Block_release_proxy")]
        public extern static void Block_release(void* block);

        [DllImport(nameof(xm), EntryPoint = "objc_msgSend_proxy")]
        public extern static void objc_msgSend(IntPtr self, IntPtr sel);
    }
}