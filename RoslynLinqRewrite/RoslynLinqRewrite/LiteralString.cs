using Microsoft.CodeAnalysis;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Shaman.Roslyn.LinqRewrite
{

    public class LiteralString : LocalizableString
    {
        private string str;
        public LiteralString(string str)
        {
            this.str = str;
        }
        protected override bool AreEqual(object other)
        {
            var o = other as LiteralString;
            if (o != null) return o.str == this.str;
            return false;
        }

        protected override int GetHash()
        {
            return str.GetHashCode();
        }

        protected override string GetText(IFormatProvider formatProvider)
        {
            return str;
        }
    }
}
