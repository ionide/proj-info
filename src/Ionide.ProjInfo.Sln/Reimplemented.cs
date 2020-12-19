using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Ionide.ProjInfo.Sln.Shared
{
    internal interface IElementLocation
    {
        string File { get; }
        int Line { get; }
        int Column { get; }
        string LocationString { get; }
    }

    internal class InternalErrorException : Exception
    {
        public InternalErrorException(string m) : base(m) { }
        public InternalErrorException(string m, Exception iex) : base(m, iex) { }
    }
}
