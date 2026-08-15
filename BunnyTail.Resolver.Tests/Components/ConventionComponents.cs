namespace BunnyTail.Resolver.Tests.Components;

// 命名規約ベース登録 (SPEC 3.3) のサンプル。"Service$" にマッチする

public interface IBarService;

public interface IMixed1;

public interface IMixed2;

public sealed class EchoService;

public sealed class BarService : IBarService;

public sealed class MixedService : IMixed1, IMixed2;
