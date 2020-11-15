
#import <Foundation/Foundation.h>

@interface TestDotNet : NSObject
- (int)doubleInt: (int)a;
- (float)doubleFloat: (float)a;
- (double)doubleDouble: (double)a;
@end

@interface TestObjC : NSObject
- (int)doubleInt: (int)a;
- (float)doubleFloat: (float)a;
- (double)doubleDouble: (double)a;

- (void)setProxy: (TestDotNet*) proxy;
@end

@implementation TestObjC
TestDotNet* _proxy;

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
- (void)setProxy: (TestDotNet*) proxy
{
    _proxy = proxy;
}
@end
