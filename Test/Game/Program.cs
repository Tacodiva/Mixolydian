﻿
namespace Game;

public class GameProgram {

    private TestGenericBox<int> Value;

    public GameProgram(string abcde) : this() {
        
    }

    public GameProgram() {
        Value = new TestGenericBox<int>(69420);
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

    public static void Main(string[] args) {
        GameProgram gameProgram = new();
        Console.WriteLine(gameProgram.Test("Hello"));
        gameProgram = new(7729);
        Console.WriteLine(gameProgram.Test("Hello"));
        gameProgram = new("Hello, World!");
        Console.WriteLine(gameProgram.Test("Hello"));
        StaticTest();
    }

    public static void StaticTest() {
        Console.WriteLine("Hello from static test!");
    }

    public sealed class TestGenericBox<TObject> {

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
    }

}