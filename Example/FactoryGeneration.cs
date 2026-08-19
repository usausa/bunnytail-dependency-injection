// Example.ThirdPartyLibrary knows nothing about the generator and registers through its own AddReportedService()
[assembly: BunnyTail.DependencyInjection.GenerateComponentFactory(
    typeof(Example.ThirdPartyLibrary.ReportedService),
    PostConstruct = nameof(Example.ThirdPartyLibrary.ReportedService.Prepare))]
