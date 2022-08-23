using System;

namespace Mixolydian.Common {
    [AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
    public sealed class MethodMixinAttribute : Attribute {
        public MethodMixinAttribute(string target, Priority priority = Priority.NORMAL) { }
    }
}
