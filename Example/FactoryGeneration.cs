// Example.Library2 does not reference the generator and registers through its own AddReportedService()
[assembly: BunnyTail.DependencyInjection.GenerateComponentFactory(
    typeof(Example.Library2.ReportedService),
    PostConstruct = nameof(Example.Library2.ReportedService.Prepare))]
