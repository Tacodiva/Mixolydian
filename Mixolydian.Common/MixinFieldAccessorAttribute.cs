using System;

namespace Mixolydian.Common {
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, Inherited = false, AllowMultiple = false)]
    public sealed class MixinFieldAccessorAttribute : Attribute {

        public readonly string Target;

        public MixinFieldAccessorAttribute(string target) {
            Target = target;
        }
    }
}
