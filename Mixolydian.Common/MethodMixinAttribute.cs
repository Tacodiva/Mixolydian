using System;

namespace Mixolydian.Common {
    [AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = true)]
    public sealed class MethodMixinAttribute : Attribute {

        // TODO Priority?
        
        public readonly string Target;

        public MethodMixinAttribute(string target) {
            Target = target;
        }
    }
}
