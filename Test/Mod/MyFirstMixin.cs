using Game;
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

    [MethodHeadMixin("GetValue")]
    public MixinReturn<T> GletValueMixin<B>() {
        Console.WriteLine("Getting value " + @this.Value);
        @this.UncalledMethod("Hello, Mixolydian!");
        SecretUncalledMethod("Secret hello mixolydian shhh...");
        return MixinReturn<T>.Continue();
    }


    [MethodHeadMixin("-")]
    public static MixinReturn<GameProgram.TestGenericBox<T>> NegationMixin(GameProgram.TestGenericBox<T> input) {
        Console.WriteLine("Negation head");
        return MixinReturn<GameProgram.TestGenericBox<T>>.Continue();
    }

    [MethodTailMixin("-")]
    public static GameProgram.TestGenericBox<T> NegationTailMixin(GameProgram.TestGenericBox<T> input, GameProgram.TestGenericBox<T> @return) {
        Console.WriteLine("Negation tail");
        return @return;
    }
}

[TypeMixin(typeof(GameProgram))]
public class MyFirstMixin {

    [MixinThis]
    public readonly GameProgram @this;

    [MixinFieldAccessor("Value")]
    private GameProgram.TestGenericBox<int> Value;

    public static string Hello;

    public static readonly string MessageInject = "World!";

    public string InitalizedString = "Fuck You";
    public object AHh = new object();

    public int TestField { get; set; }

    [MixinFieldAccessor("TestGetterSetter")]
    public extern int TestGetterSetter {
        get;
        set;
    }

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
        Console.WriteLine("Test Field = " + (TestField++));
        Console.WriteLine("Initalized string = " + InitalizedString + TestField);
        Console.WriteLine("AHh = " + AHh + " " + (TestGetterSetter++));
    }

    public void Test(int arg) {
        Test<int>(arg);
    }

    [MethodHeadMixin("Concat")]
    public MixinReturn<T2> ConcatMixin<T1, T2>(List<T1> a, T2 b) {
        return MixinReturn<T2>.Return(default!);
    }

    [MethodTailMixin("Concat")]
    public T2 ConcatMixin2<T1, T2>(List<T1> a, T2 b, T2 @return) {
        Console.WriteLine("Concat returning " + @return);
        return @return;
    }

    [MethodHeadMixin("Test")]
    public MixinReturn<int> VoidMixin(string arg) {
        Test(arg);
        Test(27);
        return MixinReturn<int>.Continue();
    }

    [MethodHeadMixin("StaticTest", MixinPriority.VERY_HIGH)]
    public static MixinReturn GetValueOther() {
        Console.WriteLine("Other static mixin called!");
        return MixinReturn.Continue();
    }

    [MethodHeadMixin("StaticTest", MixinPriority.HIGH)]
    public static MixinReturn GetValueMixin() {
        Console.WriteLine("Static mixin called!");
        Test<int, string>(420, "Blaze it");
        return MixinReturn.Continue();
    }

    [ConstructorMixin(MixinPosition.HEAD)]
    public void TestConstructorMixin() {
        Console.WriteLine("Injected into the constructor head >:)");
    }

    [ConstructorMixin(MixinPosition.TAIL)]
    public void TestConstructorMixinII() {
        Console.WriteLine("Injected into the constructor tail >:)");
    }

    [MethodTailMixin("GetFinalMessage")]
    public static string GetFinalMessageMixin(string @return) {
        return @return + " " + MessageInject;
    }

    [ConstructorMixin(MixinPosition.HEAD)]
    public static void TestStaticConstructorMixin() {
        Console.WriteLine("Static constructor head O_O");
    }

    [ConstructorMixin(MixinPosition.TAIL)]
    public static void TestStaticConstructorMixinII() {
        Console.WriteLine("Static constructor tail O_O");
    }

}
