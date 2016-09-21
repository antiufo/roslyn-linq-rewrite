# roslyn-linq-rewrite
This tool compiles C# code by first rewriting the syntax trees of LINQ expressions using plain procedural code, minimizing allocations and dynamic dispatch.

## Example input code
```csharp
public int Method1()
{
    var arr = new[] { 1, 2, 3, 4 };
    var q = 2;
    return arr.Where(x => x > q).Select(x => x + 3).Sum();
}
```
**Allocations**: input array, array enumerator, closure for `q`, `Where` delegate, `Select` delegate, `Where` enumerator, `Select` enumerator. 
## Decompiled output code
```csharp
public int Method1()
{
    int[] arr = new[] { 1, 2, 3, 4 };
    int q = 2;
    return this.Method1_ProceduralLinq1(arr, q);
}
private int Method1_ProceduralLinq1(int[] _linqitems, int q)
{
    if (_linqitems == null)
        throw new System.ArgumentNullException();
    var sum_ = (int)0;
    for (int _index = 0; _index < _linqitems.Length; _index++)
    {
        var _linqitem = _linqitems[_index];
        if (_linqitem > q)
        {
            var _linqitem1 = _linqitem + 3;
            checked
            {
                sum_ += _linqitem1;
            }
        }
    }

    return sum_;
}
```
**Allocations**: input array.
## Supported LINQ methods
* `Select`, `Where`, `Reverse`, `Cast`, `OfType`
* `First`, `FirstOrDefault`, `Single`, `SingleOrDefault`, `Last`, `LastOrDefault`
* `ToList`, `ToArray`, `ToDictionary`
* `Count`, `LongCount`, `Any`, `All`
* `ElementAt`, `ElementAtOrDefault`
* `Contains`, `ForEach`

## Usage
* Add the following to your `project.json`:
```json
    "tools": {
        "dotnet-compile-csc-linq-rewrite": {
          "version": "1.0.1.9",
          "imports": "portable-net45+win8+wp8+wpa81"
        }
    }
```
* In the `buildOptions` of your `project.json`, specify the custom compiler:
```json
    "buildOptions": {
        "compilerName": "csc-linq-rewrite"
    }
```
* Compile your project with `dotnet restore` and `dotnet build`.
* If you need to exclude a specific method, apply a `[NoLinqRewrite]` attribute to that method:
```csharp
namespace Shaman.Runtime
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Method)]
    public class NoLinqRewriteAttribute : Attribute
    {
    }
}
```

## Development
* dotnet-compile-csc-linq-rewrite, the entrypoint, is available at [antiufo/cli (csc-linq-rewrite branch)](https://github.com/antiufo/cli/tree/csc-linq-rewrite/src/dotnet/commands/dotnet-compile-csc-linq-rewrite)
* Shaman.Roslyn.LinqRewrite, the rewriting library, is available in the current repository.
* [LINQ test results](https://github.com/antiufo/linqtests/blob/master/tests/Shaman.Roslyn.LinqRewrite.Tests/Results_diff.diff) (and [code](https://github.com/antiufo/linqtests/blob/master/tests/Shaman.Roslyn.LinqRewrite.Tests/))

## Shaman.FastLinq
To further reduce allocations, install `Shaman.FastLinq` or `Shaman.FastLinq.Sources`. These packages include LINQ methods specific for `T[]` and `List<>` (not all method calls are optimized by LINQ rewrite, like individual, non-chained `.First()` or `.Last()` calls).

## Comparison to LinqOptimizer
* Code is optimized at build time (as opposed to run time)
* Uses existing LINQ syntax, no need for `AsQueryExpr().Run()`
* No allocations for Expression<> trees and enumerator boxing
* Parallel LINQ is not supported (i.e. left intact)
* No support for F#
