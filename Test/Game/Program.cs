
namespace Game;

public class GameProgram {

    private TestGenericBox<int> Value;

    public int TestGetterSetter {
        get;
        set;
    }

    public GameProgram(string abcde) : this() {
        TestGetterSetter = 1 + TestGetterSetter * 2;
    }

    public GameProgram() {
        if (Random.Shared.NextDouble() != 10) {
            Value = -new TestGenericBox<int>(69420);
            return;
        }
        Value = null!;
        Console.WriteLine("Unreachable");
    }

    public GameProgram(int a) {
        Value = new TestGenericBox<int>(a);
    }

    public int Test(string arg) {
        Console.WriteLine(Concat<string, int>(new List<string>() { "Throw away " }, Value.GetValue<string>()));
        return Value.GetValue<string>();
    }

    public B Concat<A, B>(List<A> a, B b) {
        return b;
    }

    ////////////////////////////////////////////////////////

    static GameProgram() {
        Console.WriteLine("Static constructor");
    }

    public static void Main(string[] args) {
        GameProgram gameProgram = new();
        Console.WriteLine(gameProgram.Test("Hello"));
        gameProgram = new(7729);
        Console.WriteLine(gameProgram.Test("Hello"));
        gameProgram = new("Hello, World!");
        Console.WriteLine(gameProgram.Test("Hello"));
        StaticTest();
        Console.WriteLine(GetFinalMessage());
    }

    public static void StaticTest() {
        Console.WriteLine("Hello from static test!");
    }

    public static string GetFinalMessage() {
        return "Hello";
    }

    public sealed class TestGenericBox<TObject> where TObject : struct {

        public TObject Value;

        public TestGenericBox(TObject value) {
            Value = value;
        }

        public TObject GetValue<X>() {
            // UncalledMethod("Internal test");
            return Value;
        }

        public void UncalledMethod(string message) {
            Console.WriteLine($"Got the secret message '{message}'.");
        }

        private void SecretUncalledMethod(string messageII) {
            Console.WriteLine($"Got the very secret message '{messageII}'.");
        }

        public static TestGenericBox<TObject> operator -(TestGenericBox<TObject> a) => a;
    }

}