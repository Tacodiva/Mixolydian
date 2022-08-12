using Game;
using Mixolydian.Common;

namespace Mod;

[ClassMixin(typeof(GameProgram.TestGenericBox<object>))]
public class TestGenerixBoxMixin<T> {
    [MethodMixin("GetValue")]
    public MixinReturn<T> GetValueMixin() {
        return MixinReturn<T>.Return(default!);
    }
}

[ClassMixin(typeof(GameProgram))]
public class MyFirstMixin {

    public static void Test<A, B>(A a, B b) {
        Console.WriteLine("A = " + a);
        Console.WriteLine("B = " + b);
    }

    public static void Test<T>(T arg) {
        Console.WriteLine("Instance mixin called!");
        Console.WriteLine(arg);
    }

    public void Test(string arg) {
        Test<string>(arg);
    }

    public void Test(int arg) {
        Test<int>(arg);
    }

    [MethodMixin("Concat")]
    public MixinReturn<T2> ConcatMixin<T1, T2>(List<T1> a, T2 b) {
        return MixinReturn<T2>.Return(default!);
    }

    [MethodMixin("Test")]
    public MixinReturn<int> VoidMixin(string arg) {
        Test(arg);
        Test(27);
        return MixinReturn<int>.Continue();
    }

    [MethodMixin("StaticTest")]
    public static MixinReturn GetValueMixin() {
        Console.WriteLine("Static mixin called!");
        Test<int, string>(420, "Blaze it");
        return MixinReturn.Continue();
    }

}
