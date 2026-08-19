using BenchmarkDotNet.Running;

using BunnyTail.DependencyInjection.Sandbox;

// Equivalence of every candidate is always verified before measuring.
// Equivalence of all candidates is verified before every measurement.
Verify.RunAll();

if (args.Contains("--verify"))
{
    return;
}

BenchmarkSwitcher.FromAssembly(typeof(BenchmarkConfig).Assembly).Run(args);
