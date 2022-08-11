using Game;
using Mixolydian.Common;

namespace Mod;

[ClassMixin(typeof(GameProgram))]
public class MyFirstMixin {

    [MethodMixin("Print")]
    public MixinReturn StringMixin(string input) {
        int variable = 10;
        variable *= 20;
        Console.WriteLine("String overload called! " + variable);
        return MixinReturn.Continue();
    }

    [MethodMixin("Print")]
    public MixinReturn VoidMixin() {
        Console.WriteLine("Void overload called!");
        return MixinReturn.Return();
    }

    [MethodMixin("GetValue")]
    public MixinReturn<int> GetValueMixin() {
        Console.WriteLine("Get value called!");
        return MixinReturn<int>.Return(69);
    }

}
