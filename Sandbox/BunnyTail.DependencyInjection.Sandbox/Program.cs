using BenchmarkDotNet.Running;

using BunnyTail.DependencyInjection.Sandbox;

// 測定前に必ず全候補の等価性を検証する
// Equivalence of all candidates is verified before every measurement.
Verify.RunAll();

if (args.Contains("--verify"))
{
    return;
}

BenchmarkSwitcher.FromAssembly(typeof(BenchmarkConfig).Assembly).Run(args);
