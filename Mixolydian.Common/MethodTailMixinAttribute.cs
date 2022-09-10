using System;

namespace Mixolydian.Common {
    [AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
    public sealed class MethodTailMixinAttribute : Attribute {
        public MethodTailMixinAttribute(string target, MixinPriority priority = MixinPriority.NORMAL) { }
    }
}
