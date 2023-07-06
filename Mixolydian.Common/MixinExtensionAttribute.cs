
using System;

namespace Mixolydian.Common {
    [AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
    public sealed class MixinExtensionAttribute : Attribute {
        public MixinExtensionAttribute(Type mixinType, string mixinMethodName) { }
    }
}
