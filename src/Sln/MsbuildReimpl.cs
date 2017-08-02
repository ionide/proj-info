using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

//WHY? no official sln parser avaiable https://github.com/Microsoft/msbuild/issues/1708#issuecomment-280693611

namespace Microsoft.Build.Shared.XMakeAttributes { }

namespace Microsoft.Build.Shared
{
    public static class ExceptionHandling
    {
        public static bool IsIoRelatedException(Exception ex)
        {
            return false;
        }
    }

    public static class EscapingUtilities
    {
        internal static string UnescapeAll(string targetFrameworkMoniker)
        {
            return targetFrameworkMoniker;
        }
    }

    public static class ProjectFileErrorUtilities
    {
        public static void VerifyThrowInvalidProjectFile(bool b, object e, object f, object c)
        {
        }

        internal static void VerifyThrowInvalidProjectFile(bool v1, string v2, BuildEventFileInfo buildEventFileInfo, string v3, object slnFileMinUpgradableVersion, object slnFileMaxVersion)
        {
        }

        internal static void VerifyThrowInvalidProjectFile(bool success, string v1, BuildEventFileInfo buildEventFileInfo, string v2, string projectName)
        {
        }

        internal static void ThrowInvalidProjectFile(BuildEventFileInfo buildEventFileInfo, string v, string relativePath)
        {
        }
    }

    public class BuildEventFileInfo
    {
        public BuildEventFileInfo(string s)
        {
        }

        public BuildEventFileInfo(string s, int a, int b)
        {

        }
    }

    public class InternalErrorException : Exception {
        public InternalErrorException(string m) : base(m) { }
        public InternalErrorException(string m, Exception iex) : base(m, iex) { }
    }

    public class ResourceUtilities
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

    // Source: https://github.com/Microsoft/msbuild/blob/1501e3b9e17da067ab7f1d2449720d0ad09448f2/src/Shared/FileUtilities.cs
    public static class FileUtilities
    {
        internal static bool PathIsInvalid(string path)
        {
            if (path.IndexOfAny(InvalidPathChars) >= 0)
            {
                return true;
            }

            var filename = GetFileName(path);

            return filename.IndexOfAny(InvalidFileNameChars) >= 0;
        }

        #region Used by top methods

        internal static readonly char[] InvalidPathChars = new char[]
        {
            '|', '\0',
            (char)1, (char)2, (char)3, (char)4, (char)5, (char)6, (char)7, (char)8, (char)9, (char)10,
            (char)11, (char)12, (char)13, (char)14, (char)15, (char)16, (char)17, (char)18, (char)19, (char)20,
            (char)21, (char)22, (char)23, (char)24, (char)25, (char)26, (char)27, (char)28, (char)29, (char)30,
            (char)31
        };

        internal static readonly char[] InvalidFileNameChars = new char[]
        {
            '\"', '<', '>', '|', '\0',
            (char)1, (char)2, (char)3, (char)4, (char)5, (char)6, (char)7, (char)8, (char)9, (char)10,
            (char)11, (char)12, (char)13, (char)14, (char)15, (char)16, (char)17, (char)18, (char)19, (char)20,
            (char)21, (char)22, (char)23, (char)24, (char)25, (char)26, (char)27, (char)28, (char)29, (char)30,
            (char)31, ':', '*', '?', '\\', '/'
        };

        // Path.GetFileName does not react well to malformed filenames.
        // For example, Path.GetFileName("a/b/foo:bar") returns bar instead of foo:bar
        // It also throws exceptions on illegal path characters
        private static string GetFileName(string path)
        {
            var lastDirectorySeparator = path.LastIndexOfAny(Slashes);
            return lastDirectorySeparator >= 0 ? path.Substring(lastDirectorySeparator + 1) : path;
        }

        private static readonly char[] Slashes = { '/', '\\' };

        #endregion
    }
}