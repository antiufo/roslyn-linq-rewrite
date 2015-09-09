using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Microsoft.Dnx.Compilation.Caching
{
    public class CompilationCache2
    {
        public ICache Cache { get; }
        public ICacheContextAccessor CacheContextAccessor { get; }
        public INamedCacheDependencyProvider NamedCacheDependencyProvider { get; }

        public CompilationCache2()
        {
            CacheContextAccessor = new CacheContextAccessor();
            Cache = new Cache(CacheContextAccessor);
            NamedCacheDependencyProvider = new NamedCacheDependencyProvider();
        }
    }
}
