namespace Example;

using BunnyTail.DependencyInjection;

using Microsoft.Extensions.DependencyInjection;

// 参照アセンブリのメタデータ走査による規約登録 (案 2)。Generator を参照しないライブラリの
// 素の型を、アプリ側から正規表現で拾って登録する
// Convention registration through referenced assembly metadata scanning. Plain types of a library
// that does not reference the generator are registered from the application side by regex.
internal static partial class ExternalRegistrations
{
    [ComponentRegistration(Lifetime.Transient, "^ExternalWorker$", Assembly = "Example.Library2")]
    public static partial IServiceCollection AddLibraryWorkers(this IServiceCollection services);
}
