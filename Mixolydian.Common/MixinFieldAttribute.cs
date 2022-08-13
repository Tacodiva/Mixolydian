using System;

namespace Mixolydian.Common {
    [AttributeUsage(AttributeTargets.Field, Inherited = false, AllowMultiple = false)]
    public sealed class MixinFieldAttribute : Attribute {

        public readonly string Target;

        public MixinFieldAttribute(string target) {
            Target = target;
        }
    }
}
