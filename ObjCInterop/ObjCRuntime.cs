using System;
using System.Runtime.InteropServices;

namespace ObjCRuntime
{
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