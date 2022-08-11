using System;

namespace Mixolydian.Common {
    public sealed class MixinReturn {
        private MixinReturn() {}
        public static MixinReturn Continue() => throw new SystemException("MixinReturn.Continue() should only be called from a mixin method!");
        public static MixinReturn Return() => throw new SystemException("MixinReturn.Return() should only be called from a mixin method!");
    }

    public sealed class MixinReturn<T> {
        private MixinReturn() {}
        public static MixinReturn<T> Continue() => throw new SystemException("MixinReturn.Continue() should only be called from a mixin method!");
        public static MixinReturn<T> Return(T returnValue) => throw new SystemException("MixinReturn.Return() should only be called from a mixin method!");
    }
}