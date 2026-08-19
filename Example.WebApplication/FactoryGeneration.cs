// Generate component factories for ASP.NET Core types
#pragma warning disable IDE0001
[assembly: global::BunnyTail.DependencyInjection.GenerateComponentFactory(typeof(global::Microsoft.AspNetCore.Http.DefaultHttpContextFactory))]
[assembly: global::BunnyTail.DependencyInjection.GenerateComponentFactory(typeof(global::Microsoft.AspNetCore.Http.MiddlewareFactory))]
[assembly: global::BunnyTail.DependencyInjection.GenerateComponentFactory(typeof(global::Microsoft.AspNetCore.Hosting.Builder.ApplicationBuilderFactory))]
[assembly: global::BunnyTail.DependencyInjection.GenerateComponentFactory(typeof(global::Microsoft.Extensions.ObjectPool.DefaultObjectPoolProvider))]
[assembly: global::BunnyTail.DependencyInjection.GenerateComponentFactory(typeof(global::Microsoft.AspNetCore.Routing.DefaultInlineConstraintResolver))]
