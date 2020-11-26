
#import <Foundation/Foundation.h>

typedef int (^intBlock)(int);

@interface TestDotNet : NSObject {
}

@property (copy) intBlock intBlockProp;

- (float)doubleFloat: (float)a;
- (double)doubleDouble: (double)a;
@end

@interface TestObjC : NSObject {
}

@property (copy, class) intBlock intBlockPropStatic;
@property (copy) intBlock intBlockProp;

- (float)doubleFloat: (float)a;
- (double)doubleDouble: (double)a;

- (void)useProperties;
- (void)useTestDotNet:(TestDotNet*) dn;
@end

void dummy(id i);

@implementation TestObjC

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

        blk = TestObjC.intBlockPropStatic;
        if (blk != nil) {
            b = blk(a);
            printf("Called intBlockPropStatic(%d) = %d\n", a, b);
        }
    }
}
- (void)useTestDotNet:(TestDotNet*) dn {
    int a = 13;
    int b;
    intBlock blk;

    @autoreleasepool {
        blk = dn.intBlockProp;
        if (blk != nil) {
            b = blk(a);
            printf("Called TestDotNet.intBlockProp(%d) = %d\n", a, b);
        }

        dn.intBlockProp = ^(int a) {
            return a * 4;
        };
        printf("Set TestDotNet.intBlockProp\n");

        blk = dn.intBlockProp;
        if (blk != nil) {
            b = blk(a);
            printf("Called TestDotNet.intBlockProp(%d) = %d\n", a, b);
        }
    }
}
@end
