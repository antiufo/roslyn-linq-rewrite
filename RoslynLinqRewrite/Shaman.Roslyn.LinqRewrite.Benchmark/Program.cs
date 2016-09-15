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

            [Benchmark(Description = "Sum Opt")]
            public double SumLinqOpt()
            {
                return values.AsQueryExpr().Sum().Run();
            }
        }

        public class SumOfSquaresBechmarks : BenchmarkBase
        {
            [Benchmark(Description = "Sum of Squares Linq", Baseline = true)]
            public double SumSqLinq()
            {
                return values.Select(x => x * x).Sum();
            }

            [Benchmark(Description = "Sum of Squares Opt")]
            public double SumSqLinqOpt()
            {
                return values.AsQueryExpr().Select(x => x * x).Sum().Run();
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

            [Benchmark(Description = "Cartesian Opt")]
            public double CartLinqOpt()
            {
                return (from x in dim1.AsQueryExpr()
                        from y in dim2
                        select x * y).Sum().Run();
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

            [Benchmark(Description = "Group By Opt")]
            public int[] GroupByOpt()
            {
                return values.AsQueryExpr()
                             .GroupBy(x => (int)x / 100)
                             .OrderBy(x => x.Key)
                             .Select(k => k.Count())
                             .ToArray()
                             .Run();
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

            [Benchmark(Description = "Pythagorean Triples Opt")]
            public int PythagoreanTriplesLinqOpt()
            {
                return (from a in QueryExpr.Range(1, max + 1)
                        from b in Enumerable.Range(a, max + 1 - a)
                        from c in Enumerable.Range(b, max + 1 - b)
                        where a * a + b * b == c * c
                        select true).Count().Run();
            }
        }
    }

    public class ParallelBenchmarks
    {
        public class ParallelSumBenchmarks : BenchmarkBase
        {
            [Benchmark(Description = "Parallel Sum Linq", Baseline = true)]
            public double ParallelSumLinq()
            {
                return values.AsParallel().Sum();
            }

            [Benchmark(Description = "Parallel Sum Opt")]
            public double ParallelSumOpt()
            {
                return values.AsParallelQueryExpr().Sum().Run();
            }
        }

        public class ParallelSumOfSquaresBenchmark : BenchmarkBase
        {
            [Benchmark(Description = "Parallel Sum of Squares Linq", Baseline = true)]
            public double ParallelSumSqLinq()
            {
                return values.AsParallel().Select(x => x * x).Sum();
            }

            [Benchmark(Description = "Parallel Sum of Squares Opt")]
            public double ParallelSumSqLinqOpt()
            {
                return values.AsParallelQueryExpr().Select(x => x * x).Sum().Run();
            }
        }

        public class ParallelCartesianBenchmarks : BenchmarkBase
        {
            private double[] dim1, dim2;

            [Setup]
            public override void SetUp()
            {
                base.SetUp();

                dim1 = values.Take(values.Length / 10).ToArray();
                dim2 = values.Take(20).ToArray();
            }

            [Benchmark(Description = "Parallel Cartesian Linq", Baseline = true)]
            public double ParallelCartLinq()
            {
                return (from x in dim1.AsParallel()
                        from y in dim2
                        select x * y).Sum();
            }

            [Benchmark(Description = "Parallel Cartesian Opt")]
            public double ParallelCartLinqOpt()
            {
                return (from x in dim1.AsParallelQueryExpr()
                        from y in dim2
                        select x * y).Sum().Run();
            }
        }

        public class ParallelGroupByBenchmarks : BenchmarkBase
        {
            [Setup]
            public override void SetUp()
            {
                values = Enumerable.Range(1, Count).Select(x => 100000000 * rnd.NextDouble() - 50000000).ToArray();
            }

            [Benchmark(Description = "Parallel Group By Linq", Baseline = true)]
            public int[] ParallelGroupLinq()
            {
                return values.AsParallel()
                             .GroupBy(x => (int)x / 100)
                             .OrderBy(x => x.Key)
                             .Select(k => k.Count())
                             .ToArray();
            }

            [Benchmark(Description = "Parallel Group By Opt")]
            public int[] ParallelGroupLinqOpt()
            {
                return values.AsParallelQueryExpr()
                             .GroupBy(x => (int)x / 100)
                             .OrderBy(x => x.Key)
                             .Select(k => k.Count())
                             .ToArray()
                             .Run();
            }
        }

        public class ParallelPythagoreanTriplesBenchmarks
        {
            [Params(0, 10, 100, 1000)]
            public int max = 1000;

            [Benchmark(Description = "Parallel Pythagorean Triples Linq", Baseline = true)]
            public int ParallelPythagoreanTriplesLinq()
            {
                return (from a in Enumerable.Range(1, max + 1).AsParallel()
                        from b in Enumerable.Range(a, max + 1 - a)
                        from c in Enumerable.Range(b, max + 1 - b)
                        where a * a + b * b == c * c
                        select true).Count();
            }

            [Benchmark(Description = "Parallel Pythagorean Triples Opt")]
            public int ParallelPythagoreanTriplesLinqOpt()
            {
                return (from a in Enumerable.Range(1, max + 1).AsParallelQueryExpr()
                        from b in Enumerable.Range(a, max + 1 - a)
                        from c in Enumerable.Range(b, max + 1 - b)
                        where a * a + b * b == c * c
                        select true).Count().Run();
            }
        }
    }
}
