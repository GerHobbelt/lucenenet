using System;
using System.Collections.Generic;
using System.Text;
using Lucene.Net.Documents;

namespace Utilities.BibTex.Parsing
{
    public class BibTexItem
    {
        internal readonly string Type;
        internal readonly string Key;

        internal IEnumerable<object> EnumerateFields()
        {
            throw new NotImplementedException();
        }
    }
}
