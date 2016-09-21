using System;
using System.Collections.Generic;
using System.Linq;

class asd{
    struct BigStruct
    {
        public int a{get; set;}
        public int? b;
        public int c;
        public object d;
    }
    void asdf(){
        var guid = default(BigStruct);
        Enumerable.Empty<BigStruct>().Where(x => x.a == guid.a).Count();
        }
}