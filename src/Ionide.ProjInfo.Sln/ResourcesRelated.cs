using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Ionide.ProjInfo.Sln.Shared
{
    internal static class ResourceUtilities
    {
        public static string FormatResourceString(out string e, out string x, string s, params object[] args)
        {
            e = x = "";
            return string.Format(s, args);
        }

        public static string FormatResourceString(string s, params object[] args)
        {
            return string.Format(s, args);
        }

        public static string FormatString(string s, params object[] args)
        {
            return string.Format(s, args);
        }

        internal static void VerifyResourceStringExists(string resourceName)
        {
        }
    }

    internal static class AssemblyResources
    {
        internal static string GetString(string name)
        {
            return name;
        }
    }
}
