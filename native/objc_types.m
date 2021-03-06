
#include <objc/runtime.h>
#import <Foundation/Foundation.h>

typedef int (^intBlock)(int);

@class DotNetObject;

@interface ObjCObject : NSObject {
}

@property (copy, class) intBlock intBlockPropStatic;
@property (copy) intBlock intBlockProp;

- (float)doubleFloat: (float)a;
- (double)doubleDouble: (double)a;

- (void)useProperties;
- (void)useTestDotNet:(DotNetObject*) dn;
@end

@interface DotNetObject : ObjCObject {
}

@property (copy) intBlock intBlockProp;

- (float)doubleFloat: (float)a;
- (double)doubleDouble: (double)a;
@end

void dummy(id i);

@implementation ObjCObject

static intBlock _intBlockPropStatic;
+ (intBlock)intBlockPropStatic {
    return _intBlockPropStatic;
}
+ (void)setIntBlockPropStatic:(intBlock) blk {
    if (_intBlockPropStatic != blk) {
        [_intBlockPropStatic release];
        _intBlockPropStatic = [blk copy];
    }
}

- (void)dealloc {
    [self calledDuringDealloc];
    [super dealloc];
    printf("Leaving TestObjC.dealloc\n");
}
- (void)calledDuringDealloc {
    printf("TestObjC.calledDuringDealloc\n");
}

- (float)doubleFloat: (float)a {
    //NSLog(@"doubleFloat: %f", a);
    return a * 2.;
}
- (double)doubleDouble: (double)a
{
    //NSLog(@"doubleDouble: %lf", a);
    return a * 2.;
}
- (void)useProperties {
    int a = 13;
    int b;
    intBlock blk;

    @autoreleasepool {
        blk = self.intBlockProp;
        if (blk != nil) {
            b = blk(a);
            printf("Called intBlockProp(%d) = %d\n", a, b);
        }

        blk = ObjCObject.intBlockPropStatic;
        if (blk != nil) {
            b = blk(a);
            printf("Called intBlockPropStatic(%d) = %d\n", a, b);
        }
    }
}
- (void)useTestDotNet:(DotNetObject*) dn {
    int a = 13;
    int b;
    intBlock blk;

    if (dn == nil) {
        Class Class_TestDotNet = objc_lookUpClass("DotNetObject");
        //dn = [[Class_TestDotNet alloc] init];
        dn = [Class_TestDotNet new];
    } else {
        [dn retain];
    }

    @autoreleasepool {
        blk = dn.intBlockProp;
        if (blk != nil) {
            b = blk(a);
            printf("Called DotNetObject.intBlockProp(%d) = %d\n", a, b);
        }

        dn.intBlockProp = ^(int a) {
            return a * 4;
        };
        printf("Set DotNetObject.intBlockProp\n");

        blk = dn.intBlockProp;
        if (blk != nil) {
            b = blk(a);
            printf("Called DotNetObject.intBlockProp(%d) = %d\n", a, b);
        }
    }

    //[dn release];
}
@end
