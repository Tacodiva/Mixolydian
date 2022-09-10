using System;

namespace Mixolydian.Common {
    [AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
    public sealed class MethodHeadMixinAttribute : Attribute {
        public MethodHeadMixinAttribute(string target, MixinPriority priority = MixinPriority.NORMAL) { }
    }
}
