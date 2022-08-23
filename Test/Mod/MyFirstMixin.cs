﻿using Game;
using Mixolydian.Common;

namespace Mod;

[TypeMixin(typeof(GameProgram.TestGenericBox<object>))]
public class TestGenerixBoxMixin<T> {

    [MixinThis]
    public readonly GameProgram.TestGenericBox<T> @this;

    [MixinFieldAccessor("Value")]
    public T Value;

    [MixinMethodAccessor("SecretUncalledMethod")]
    public extern void SecretUncalledMethod(string value);

    [MethodMixin("GetValue")]
    public MixinReturn<T> GletValueMixin<B>() {
        Console.WriteLine("Getting value " + @this.Value);
        @this.UncalledMethod("Hello, Mixolydian!");
        SecretUncalledMethod("Secret hello mixolydian shhh...");
        return MixinReturn<T>.Continue();
    }
}

[TypeMixin(typeof(GameProgram))]
public class MyFirstMixin {

    [MixinThis]
    public readonly GameProgram @this;

    [MixinFieldAccessor("Value")]
    private GameProgram.TestGenericBox<int> Value;

    public static string Hello;

    public string InitalizedString = "Fuck You";
    public object AHh = new object();

    public int TestField;

    public static void Test<A, B>(A a, B b) {
        Console.WriteLine("A = " + a);
        Console.WriteLine("B = " + b);
        Console.WriteLine("Hello = " + Hello);
    }

    public static void Test<T>(T arg) {
        Console.WriteLine("Instance mixin called!");
        Console.WriteLine(arg);
    }

    public void Test(string arg) {
        Value.Value = 7729;
        Test<string>(arg);
        Console.WriteLine("Test Field = " + TestField);
        Console.WriteLine("Initalized string = " + InitalizedString);
        Console.WriteLine("AHh = " + AHh);
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

    [MethodMixin("StaticTest", Priority.VERY_HIGH)]
    public static MixinReturn GetValueOther() {
        Console.WriteLine("Other static mixin called!");
        return MixinReturn.Continue();
    }

    [MethodMixin("StaticTest", Priority.HIGH)]
    public static MixinReturn GetValueMixin() {
        Console.WriteLine("Static mixin called!");
        Test<int, string>(420, "Blaze it");
        return MixinReturn.Continue();
    }

    [ConstructorMixin]
    public void TestConstructorMixin() {
        Console.WriteLine("Injected into the constructor >:)");
    }

}
