using System;
using System.Linq;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Exporters;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Loggers;
using BenchmarkDotNet.Running;
using Nessos.LinqOptimizer.CSharp;

namespace LinqOptimizer.Benchmarks.CSharp
{
    class Program
    {
        static void Main(string[] args)
        {
            typeof(SequentialBenchmarks)
                .GetNestedTypes()
                .Concat(typeof(ParallelBenchmarks).GetNestedTypes())
                .ToList()
                .ForEach(type => BenchmarkRunner.Run(type, new CustomConfig()));
        }
    }

    internal class CustomConfig : ManualConfig
    {
        public CustomConfig()
        {
            Add(Job.Default.WithLaunchCount(1));
            Add(PropertyColumn.Method);
            Add(StatisticColumn.Median, StatisticColumn.StdDev);
            Add(BaselineDiffColumn.Scaled);
            Add(MarkdownExporter.GitHub);
            Add(new ConsoleLogger());
        }
    }

    public class BenchmarkBase
    {
        // the size used to be 200000000 but currently BenchmarkDotNet does not support gcAllowVeryLargeObjects (https://github.com/PerfDotNet/BenchmarkDotNet/issues/76)
        [Params(0, 10, 100, 10000, 1000000)]
        public int Count;

        const int useSameSeedEveryTimeToHaveSameData = 08041988;
        protected Random rnd = new Random(useSameSeedEveryTimeToHaveSameData);

        protected double[] values;

        [Setup]
        public virtual void SetUp()
        {
            values = Enumerable.Range(1, Count).Select(x => rnd.NextDouble()).ToArray();
        }
    }

    public class SequentialBenchmarks
    {
        public class SumBechmarks : BenchmarkBase
        {
            [Benchmark(Description = "Sum Linq", Baseline = true)]
            public double SumLinq()
            {
                return values.Sum();
            }

        }

        public class SumOfSquaresBechmarks : BenchmarkBase
        {
            [Benchmark(Description = "Sum of Squares Linq", Baseline = true)]
            public double SumSqLinq()
            {
                return values.Select(x => x * x).Sum();
            }

        }

        public class CartesianBenchmarks : BenchmarkBase
        {
            private double[] dim1, dim2;

            [Setup]
            public override void SetUp()
            {
                base.SetUp();

                dim1 = values.Take(values.Length / 10).ToArray();
                dim2 = values.Take(20).ToArray();
            }

            [Benchmark(Description = "Cartesian Linq", Baseline = true)]
            public double CartLinq()
            {
                return (from x in dim1
                        from y in dim2
                        select x * y).Sum();
            }

        }

        public class GroupByBenchmarks : BenchmarkBase
        {
            [Setup]
            public override void SetUp()
            {
                base.SetUp();

                values =
                    Enumerable.Range(1, Count).Select(x => 100000000 * rnd.NextDouble() - 50000000).ToArray();
            }

            [Benchmark(Description = "Group By Linq", Baseline = true)]
            public int[] GroupLinq()
            {
                return values
                    .GroupBy(x => (int)x / 100)
                    .OrderBy(x => x.Key)
                    .Select(k => k.Count())
                    .ToArray();
            }

        }

        public class PythagoreanTriplesBenchmarks
        {
            [Params(0, 10, 100, 1000)]
            public int max = 1000;

            [Benchmark(Description = "Pythagorean Triples Linq", Baseline = true)]
            public int PythagoreanTriplesLinq()
            {
                return (from a in Enumerable.Range(1, max + 1)
                        from b in Enumerable.Range(a, max + 1 - a)
                        from c in Enumerable.Range(b, max + 1 - b)
                        where a * a + b * b == c * c
                        select true).Count();
            }

        }
    }

}
