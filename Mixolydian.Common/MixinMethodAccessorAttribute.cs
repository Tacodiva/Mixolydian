using System;

namespace Mixolydian.Common {
    [AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
    public sealed class MixinMethodAccessorAttribute : Attribute {

        public readonly string Target;

        public MixinMethodAccessorAttribute(string target) {
            Target = target;
        }
    }
}
