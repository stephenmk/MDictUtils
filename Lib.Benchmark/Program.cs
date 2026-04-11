using BenchmarkDotNet.Running;

namespace Lib.Benchmark;

public class Program
{
    public static void Main()
    {
        BenchmarkRunner.Run<Benchmarks>();
    }
}
