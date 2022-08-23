using System;

namespace Mixolydian.Common {
    [AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
    public sealed class ConstructorMixinAttribute : Attribute {

        public ConstructorMixinAttribute(Priority priority = Priority.NORMAL) { }

    }
}
