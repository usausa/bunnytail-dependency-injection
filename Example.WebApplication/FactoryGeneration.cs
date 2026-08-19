// 診断 (DescribeRuntimeFallbacks) が出力した候補をそのまま貼り付けた例。
// ASP.NET Core 側の型は自分で制御できないが、public な具象クラスならファクトリだけ生成できる
// Example of pasting the candidates printed by the diagnostic (DescribeRuntimeFallbacks) as-is.
// The ASP.NET Core types are outside your control, yet publicly accessible concrete classes can still get a factory.
#pragma warning disable IDE0001
[assembly: global::BunnyTail.DependencyInjection.GenerateComponentFactory(typeof(global::Microsoft.AspNetCore.Http.DefaultHttpContextFactory))]
[assembly: global::BunnyTail.DependencyInjection.GenerateComponentFactory(typeof(global::Microsoft.AspNetCore.Http.MiddlewareFactory))]
[assembly: global::BunnyTail.DependencyInjection.GenerateComponentFactory(typeof(global::Microsoft.AspNetCore.Hosting.Builder.ApplicationBuilderFactory))]
[assembly: global::BunnyTail.DependencyInjection.GenerateComponentFactory(typeof(global::Microsoft.Extensions.ObjectPool.DefaultObjectPoolProvider))]
[assembly: global::BunnyTail.DependencyInjection.GenerateComponentFactory(typeof(global::Microsoft.AspNetCore.Routing.DefaultInlineConstraintResolver))]
