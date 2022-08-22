using Mixolydian;
using Mono.Cecil;

namespace Mixolydian;

[System.Serializable]
public class InvalidModException : System.Exception {

    public readonly Mod Mod;

    public readonly TypeMixin? Mixin;
    public readonly MemberReference? Member;

    public InvalidModException(string message, TypeMixin mixin, MemberReference? member = null, System.Exception? inner = null)
         : this(message, mixin.Mod, mixin, member, inner) {
    }

    public InvalidModException(string message, Mod mod, TypeMixin? mixin = null, MemberReference? member = null, System.Exception? inner = null) : base(message, inner) {
        Mod = mod;
        Mixin = mixin;
        Member = member;
    }
}