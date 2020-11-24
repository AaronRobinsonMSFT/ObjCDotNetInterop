
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

intBlock _intBlockProp;
- (intBlock)intBlockProp {
    return _intBlockProp;
}
- (void)setIntBlockProp:(intBlock) blk {
    if (_intBlockProp != blk) {
        [_intBlockProp release];
        _intBlockProp = [blk copy];
    }
}

TestDotNet* _proxy;

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

    blk = self.intBlockProp;
    b = blk(13);
    printf("Calling intBlockProp(%d) = %d\n", a, b);

    //dummy(blk);

    blk = TestObjC.intBlockPropStatic;
    b = blk(13);
    printf("Calling intBlockPropStatic(%d) = %d\n", a, b);

    //dummy(blk);
}
@end
