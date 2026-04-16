using BenchmarkDotNet.Running;

namespace MDictUtils.Benchmark;

public class Program
{
    public static void Main()
    {
        BenchmarkRunner.Run<Benchmarks>();
    }
}
