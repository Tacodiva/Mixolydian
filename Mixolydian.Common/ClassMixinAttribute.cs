using System;

namespace Mixolydian.Common {

    [AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
    public sealed class ClassMixinAttribute : Attribute {
       
        public int Priority { get; set; }
        public readonly Type Target;

        public ClassMixinAttribute(Type target) {
            Target = target;
        }
    }
}