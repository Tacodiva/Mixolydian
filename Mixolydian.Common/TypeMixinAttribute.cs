using System;

namespace Mixolydian.Common {

    [AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
    public sealed class TypeMixinAttribute : Attribute {      
        public readonly Type Target;

        public TypeMixinAttribute(Type target) {
            Target = target;
        }
    }
}