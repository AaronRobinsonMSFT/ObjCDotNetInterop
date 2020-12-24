#include <stdlib.h>
#include <stdio.h>
#include <assert.h>
#include <string.h>
#include <stdatomic.h>

#include <objc/objc.h>
#include <objc/runtime.h>
#include <objc/message.h>

// Print out Objective-C class details
static void debug_class(Class cls)
{
    printf("=== Class === 0x%p\n", cls);
    const char* clsName = class_getName(cls);
    BOOL isMeta = class_isMetaClass(cls);
    printf("\tName: %s, MetaClass: %s\n", clsName, (isMeta ? "Yes" : "No"));

    Class super = class_getSuperclass(cls);
    printf("\tSuper: ");
    while (super != NULL)
    {
        clsName = class_getName(super);
        printf("%s->", clsName);
        super = class_getSuperclass(super);
    }
    printf("NULL\n");

    unsigned len;
    Method* lst = class_copyMethodList(cls, &len);
    printf("\tMethods %u\n", len);
    for (int i = 0; i < len; ++i)
    {
        const char* name = sel_getName(method_getName(lst[i]));
        printf("\t\t%s\n", name);
    }
    free(lst);

    Class metaClass = object_getClass(cls);
    if (metaClass != NULL && metaClass != cls)
        debug_class(metaClass);
}
 
// Print out Objective-C id details
static void debug_inst(id inst)
{
    printf("=== Instance === 0x%p\n", inst);

    const char* instClsName = object_getClassName(inst);
    printf("\tClass name: %s\n", instClsName);

    Class instCls = object_getClass(inst);
    debug_class(instCls);
}

static const char* clr_strdup(const char* str)
{
    if (!str)
        return NULL;

    size_t len = strlen(str);
    char* buffer = (char*)malloc(len + 1); // CLR memory contract
    return strcpy(buffer, str);
}

void Initialize()
{
    //printf("long int: (%zd) bytes\n", sizeof(long int));
}

void dummy(void* ptr)
{
    debug_inst(ptr);
}

typedef struct
{
    size_t gcHandle;
    atomic_int refCount;
    int increment;
} ManagedObjectWrapperLifetime;

static int NormalInc = 1;
static int CleanupInc = -1;
static int ObjCWeakRefSentinel = 1;
static int ClrWeakRefSentinel = 0;
static int DeallocSentinel = -1;
static int CleanupSentinel = -2;

// Called by GC to determine if lifetime is rooted (i.e. strong reference).
static bool is_rooted(id self)
{
    ManagedObjectWrapperLifetime* lifetime = *(ManagedObjectWrapperLifetime**)object_getIndexedIvars(self);

    return (lifetime->refCount != ObjCWeakRefSentinel)
        && (lifetime->refCount != ClrWeakRefSentinel);
}

// Called by GC to perform cheap finalization if possible.
// i.e. GCToEEInterface::EagerFinalized
static bool eager_finalize(id self)
{
    ManagedObjectWrapperLifetime* lifetime = *(ManagedObjectWrapperLifetime**)object_getIndexedIvars(self);

    if (lifetime->refCount == ObjCWeakRefSentinel)
    {
        assert(lifetime->increment == NormalInc);

        // Indicate start of clean up sequence.
        lifetime->refCount = CleanupSentinel;
        lifetime->increment = CleanupInc;

        printf("** Autorelease: %p\n", (void*)self);
        SEL autorelease_sel = sel_registerName("autorelease");
        ((void(*)(id, SEL))objc_msgSend)(self, autorelease_sel);

        return false;
    }
    else
    {
        assert(lifetime->refCount == ClrWeakRefSentinel);
        // [TODO] Determine if the user actually wants the finalizer to run.
        return true;
    }
}

static id clr_retain(id self, SEL sel)
{
    ManagedObjectWrapperLifetime* lifetime = *(ManagedObjectWrapperLifetime**)object_getIndexedIvars(self);
    (void)atomic_fetch_add(&lifetime->refCount, lifetime->increment);
    printf("** Retain: %p, Count: %d\n", (void*)self, lifetime->refCount);
    return self;
}

static void clr_release(id self, SEL sel)
{
    ManagedObjectWrapperLifetime* lifetime = *(ManagedObjectWrapperLifetime**)object_getIndexedIvars(self);
    assert(lifetime->refCount != ClrWeakRefSentinel);

    atomic_int prevCount = atomic_fetch_sub(&lifetime->refCount, lifetime->increment);
    if (lifetime->refCount == DeallocSentinel)
    {
        assert(prevCount == CleanupSentinel);
        printf("** Dealloc: %p\n", (void*)self);
        SEL dealloc_sel = sel_registerName("dealloc");
        ((void(*)(id, SEL))objc_msgSend)(self, dealloc_sel);
    }

    printf("** Release: %p, Prev: %d, Count: %d\n", (void*)self, prevCount, lifetime->refCount);
}

static void clr_dealloc(id self, SEL sel)
{
    ManagedObjectWrapperLifetime* lifetime = *(ManagedObjectWrapperLifetime**)object_getIndexedIvars(self);
    printf("** Dealloc: %p, Count: %d\n", (void*)self, lifetime->refCount);

    // The super dealloc may call back into the runtime and rely upon
    // the manage object.
    struct objc_super super;
    super.receiver = self;
    super.super_class = class_getSuperclass(object_getClass(self));
    ((void(*)(struct objc_super*, SEL))objc_msgSendSuper)(&super, sel);

    // N.B. The management of the lifetime memory is handled by the SyncBlock cleanup for the object.
    printf("** CLR weak reference: %p\n", (void*)self);
    lifetime->refCount = ClrWeakRefSentinel;
}

void* Get_clr_retain()
{
    return (void*)&clr_retain;
}

void* Get_clr_release()
{
    return (void*)&clr_release;
}

void* Get_clr_dealloc()
{
    return (void*)&clr_dealloc;
}

void clr_SetGlobalMessageSendCallbacks(
    void* fptr_objc_msgSend,
    void* fptr_objc_msgSend_fpret,
    void* fptr_objc_msgSend_stret,
    void* fptr_objc_msgSendSuper,
    void* fptr_objc_msgSendSuper_stret)
{
    // Provided overrides to send to CLR.
}

// Get the supplied object's dealloc implementation
bool clr_isRuntimeAllocated(id obj)
{
    Class cls = object_getClass(obj);
    SEL dealloc_sel = sel_registerName("dealloc");
    Method method = class_getInstanceMethod(cls, dealloc_sel);
    IMP impl = method_getImplementation(method);
    return ((void*)impl == (void*)clr_dealloc);
}

void* Get_objc_msgSend()
{
    return (void*)&objc_msgSend;
}

void* Get_objc_msgSendSuper()
{
    return (void*)&objc_msgSendSuper;
}

// Forward declare the needed stack block class.
extern void* _NSConcreteStackBlock;

void* Get_NSConcreteStackBlock()
{
    return (void*)&_NSConcreteStackBlock;
}

Class objc_getMetaClass_proxy(const char *name)
{
    Class cls = objc_getMetaClass(name);
    return cls;
}

Class objc_getClass_proxy(const char *name)
{
    Class cls = objc_getClass(name);
    return cls;
}

const char* object_getClassName_proxy(id obj)
{
    const char* name = object_getClassName(obj);
    return clr_strdup(name);
}

id object_getClass_proxy(id obj)
{
    id mc = object_getClass(obj);
    return mc;
}

Class class_getSuperclass_proxy(Class cls)
{
    Class cls_super = class_getSuperclass(cls);
    return cls_super;
}

Class objc_allocateClassPair_proxy(Class superclass, const char *name, size_t extraBytes)
{
    Class cls = objc_allocateClassPair(superclass, name, extraBytes);
    return cls;
}

SEL sel_registerName_proxy(const char *str)
{
    SEL name = sel_registerName(str);
    return name;
}

bool class_addMethod_proxy(Class cls, SEL name, IMP imp, const char *types)
{
    bool suc = class_addMethod(cls, name, imp, types);
    return suc;
}

void objc_registerClassPair_proxy(Class cls)
{
    objc_registerClassPair(cls);
}

id class_createInstance_proxy(Class cls, size_t extraBytes)
{
    id inst = class_createInstance(cls, extraBytes);
    return inst;
}

const char* class_getName_proxy(Class cls)
{
    const char* name = class_getName(cls);
    return clr_strdup(name);
}

void objc_destructInstance_proxy(id obj)
{
    void* addr = objc_destructInstance(obj);
    assert(addr == (void*)obj);
    (void)addr;
}

void* object_getIndexedIvars_proxy(id obj)
{
    void* ptr = object_getIndexedIvars(obj);
    return ptr;
}

extern id _Block_copy(id);
extern void _Block_release(id);

id Block_copy_proxy(id block)
{
    id new_block = _Block_copy(block);
    return new_block;
}

void Block_release_proxy(id block)
{
    _Block_release(block);
}

void objc_msgSend_proxy(id self, SEL sel)
{
    ((void(*)(id,SEL))objc_msgSend)(self, sel);
}
