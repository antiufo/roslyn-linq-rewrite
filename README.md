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
    if (_linqitems == null) throw new ArgumentNullException();

    int num = 0;
    for (int i = 0; i < _linqitems.Length; i++)
    {
        if (_linqitems[i] > q)
            num += num2 + 3;
    }
    return num;
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
          "version": "1.0.1.7",
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
* [LINQ tests](https://github.com/antiufo/linqtests/)
