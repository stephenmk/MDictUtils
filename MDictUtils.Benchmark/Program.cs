using BenchmarkDotNet.Running;

namespace MDictUtils.Benchmark;

public static class Program
{
    public static void Main()
    {
        BenchmarkRunner.Run<Benchmarks>();
    }
}
