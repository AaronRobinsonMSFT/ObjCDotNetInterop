#include <stdlib.h>
#include <stdio.h>
#include <assert.h>

#include <objc/objc.h>
#include <objc/runtime.h>
#include <objc/message.h>

void Initialize()
{
}

void dummy()
{
    printf("Inside xm\n");
}

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

void* Get_objc_msgSend()
{
    return (void*)&objc_msgSend;
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

void objc_destructInstance_proxy(id obj)
{
    void* addr = objc_destructInstance(obj);
    assert(addr == obj);
    (void)addr;
}

// namespace
// {
//     template<typename Ret, typename ...ArgN>
//     Ret objc_msgSend_Typed(id self, SEL sel, ArgN ...args)
//     {
//         // The objc_msgSend prototype is intentionally declared incorrectly to
//         // avoid misuse. We can use C++ templates to compute the appropriate signature.
//         // See https://developer.apple.com/documentation/objectivec/1456712-objc_msgsend?language=objc
//         // See https://www.mikeash.com/pyblog/objc_msgsends-new-prototype.html
//         using callsig_t = Ret(*)(id, SEL, ArgN...);
//         return ((callsig_t)objc_msgSend)(self, sel, args...);
//     }
// }
//
// extern "C" id objc_msgSend_int_proxy(id self, SEL sel, int a)
// {
//     self = objc_msgSend_Typed<id>(self, sel, a);
//     return self;
// }

void objc_msgSend_proxy(id self, SEL sel)
{
    ((void(*)(id,SEL))objc_msgSend)(self, sel);
}