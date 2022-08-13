﻿
namespace Game;

public class GameProgram {

    private TestGenericBox<int> Value;

    public GameProgram() {
        Value = new TestGenericBox<int>(69420);
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
        StaticTest();
    }

    public static void StaticTest() {
        Console.WriteLine("Hello from static test!");
    }

    public class TestGenericBox<TObject> {

        public TObject Value;

        public TestGenericBox(TObject value) {
            Value = value;
        }

        public TObject GetValue<X>() {
            return Value;
        }

        public void UncalledMethod(string message) {
            Console.WriteLine($"Got the secret message '{message}'.");
        }
    }

}