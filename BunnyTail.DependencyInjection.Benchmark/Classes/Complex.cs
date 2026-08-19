namespace BunnyTail.DependencyInjection.Benchmark.Classes;

public sealed class Complex(
    ISingleton1 singleton1,
    ISingleton2 singleton2,
    ISingleton3 singleton3,
    Combined1 combined1,
    Combined2 combined2,
    Combined3 combined3)
{
    public ISingleton1 Singleton1 { get; } = singleton1;

    public ISingleton2 Singleton2 { get; } = singleton2;

    public ISingleton3 Singleton3 { get; } = singleton3;

    public Combined1 Combined1 { get; } = combined1;

    public Combined2 Combined2 { get; } = combined2;

    public Combined3 Combined3 { get; } = combined3;
}
