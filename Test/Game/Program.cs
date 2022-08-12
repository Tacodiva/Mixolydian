
namespace Game;

public class GameProgram {

    private TestGenericBox<int> Value;

    public GameProgram() {
        Value = new TestGenericBox<int>(69420);
    }

    public int Test(string arg) {
        Console.WriteLine(Concat<string, int>(new List<string>() { "Throw away " }, Value.GetValue()));
        return Value.GetValue();
    }

    public B Concat<A, B>(List<A> a, B b) {
        return b;
    }

    ////////////////////////////////////////////////////////

    public static void Main(string[] args) {
        GameProgram gameProgram = new();
        Console.WriteLine(gameProgram.Test("Hello"));
        StaticTest();
    }

    public static void StaticTest() {
        Console.WriteLine("Hello from static test!");
    }

    public class TestGenericBox<T> {

        public T Value;

        public TestGenericBox(T value) {
            Value = value;
        }

        public T GetValue() {
            return Value;
        }
    }

}