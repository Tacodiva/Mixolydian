using System;

namespace Mixolydian.Common {
    [AttributeUsage(AttributeTargets.Field, Inherited = false, AllowMultiple = false)]
    public sealed class MixinThisAttribute : Attribute {
    }
}
