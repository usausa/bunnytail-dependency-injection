// 自分で制御できないライブラリの型に対する、登録を伴わないファクトリ生成の指示。
// Example.Library2 はジェネレータを参照しておらず、登録も同ライブラリの AddReportedService() が行うため、
// この 1 行がないと ReportedService は実行時経路 (ConstructorInvoker) で解決される
// Requests factory generation, without registration, for a type of a library you do not control.
// Example.Library2 does not reference the generator and registers through its own AddReportedService(), so without
// this single line ReportedService would resolve through the runtime path (ConstructorInvoker).
[assembly: BunnyTail.DependencyInjection.GenerateComponentFactory(
    typeof(Example.Library2.ReportedService),
    PostConstruct = nameof(Example.Library2.ReportedService.Prepare))]
