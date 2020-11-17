
#import <Foundation/Foundation.h>

typedef double (^doubleDoubleBlock)(double);

@interface TestDotNet : NSObject
- (int)doubleInt: (int)a;
- (float)doubleFloat: (float)a;
- (double)doubleDouble: (double)a;

- (doubleDoubleBlock)getDoubleDoubleBlock;
@end

@interface TestObjC : NSObject
- (int)doubleInt: (int)a;
- (float)doubleFloat: (float)a;
- (double)doubleDouble: (double)a;

- (doubleDoubleBlock)getDoubleDoubleBlock;

- (void)setProxy: (TestDotNet*) proxy;
- (double)callDoubleDoubleBlockThroughProxy:(double) a;
@end

@implementation TestObjC
TestDotNet* _proxy;
doubleDoubleBlock _block;

- (int)doubleInt: (int) a
{
    NSLog(@"doubleInt: %i", a);
    if (_proxy != nil)
    {
        return [_proxy doubleInt: a];
    }
    return a * 2;
}
- (float)doubleFloat: (float)a
{
    NSLog(@"doubleFloat: %f", a);
    if (_proxy != nil)
    {
        return [_proxy doubleFloat: a];
    }
    return a * 2.;
}
- (double)doubleDouble: (double)a
{
    NSLog(@"doubleDouble: %lf", a);
    if (_proxy != nil)
    {
        return [_proxy doubleDouble: a];
    }
    return a * 2.;
}
- (doubleDoubleBlock)getDoubleDoubleBlock
{
    doubleDoubleBlock blk = ^(double a) { return [self doubleDouble: a]; };
    return [blk copy];
}
- (void)setProxy: (TestDotNet*) proxy
{
    _proxy = proxy;
}
- (double)callDoubleDoubleBlockThroughProxy:(double) a
{
    if (_proxy == nil)
    {
        NSLog(@"Proxy not set");
        return -1;
    }

    doubleDoubleBlock blk = [_proxy getDoubleDoubleBlock];
    return blk(a);
}
@end
