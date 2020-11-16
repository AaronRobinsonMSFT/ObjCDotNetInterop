using System;
using System.Collections.Generic;
using System.Threading;

namespace System.Runtime.InteropServices.ObjC
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
        public static bool operator ==(Class l, Class r) => l.value == r.value;
        public static bool operator !=(Class l, Class r) => !(l == r);
    };

    public unsafe class xm
    {
        [DllImport(nameof(xm))]
        public extern static void Initialize();

        [DllImport(nameof(xm))]
        public extern static void dummy();

        public static readonly void* objc_msgSend_Raw = Get_objc_msgSend();

        [DllImport(nameof(xm))]
        private extern static void* Get_objc_msgSend();

        [DllImport(nameof(xm), EntryPoint = "objc_getMetaClass_proxy")]
        public extern static Class objc_getMetaClass(
            [MarshalAs(UnmanagedType.LPStr)] string name);

        [DllImport(nameof(xm), EntryPoint = "objc_getClass_proxy")]
        public extern static Class objc_getClass(
            [MarshalAs(UnmanagedType.LPStr)] string name);

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

        [DllImport(nameof(xm), EntryPoint = "objc_destructInstance_proxy")]
        public extern static void objc_destructInstance(id obj);

        public static readonly Class noclass = new Class(0);
        public static readonly id nil = new id(0);
        public static readonly SEL nosel = new SEL(0);

        [DllImport(nameof(xm), EntryPoint = "objc_msgSend_proxy")]
        public extern static void objc_msgSend(id self, SEL sel);
    }

    public static class Internals
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

    public static class Registrar
    {
        public static void Initialize((Type mt, string nt)[] typeMapping)
        {
            foreach (var tm in typeMapping)
            {
                Class nClass = xm.objc_getClass(tm.nt);
                RegisterClass(tm.mt, nClass);
            }
        }

        private static readonly ReaderWriterLockSlim RegisteredClassesLock = new ReaderWriterLockSlim();
        private static readonly Dictionary<Type, Class> RegisteredClasses = new Dictionary<Type, Class>();
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

        public static void RegisterClass(Type type, Class klass)
        {
            if (klass == xm.noclass)
            {
                throw new Exception("Invalid native class");
            }

            RegisteredClassesLock.EnterWriteLock();

            try
            {
                RegisteredClasses.Add(type, klass);
            }
            finally
            {
                RegisteredClassesLock.ExitWriteLock();
            }
        }
    }
}