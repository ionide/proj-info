using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;

namespace Dotnet.ProjInfo.Sln.Shared
{
    public static class FileUtilities
    {
        /// <summary>
        /// A variation of Path.GetFullPath that will return the input value
        /// instead of throwing any IO exception.
        /// Useful to get a better path for an error message, without the risk of throwing
        /// if the error message was itself caused by the path being invalid!
        /// </summary>
        internal static string GetFullPathNoThrow(string path)
        {
            try
            {
                path = NormalizePath(path);
            }
            catch (Exception ex) when (ExceptionHandling.IsIoRelatedException(ex))
            {
            }

            return path;
        }

        internal static string NormalizePath(string path)
        {
            return FixFilePath(Path.GetFullPath(path));
        }

        internal static string FixFilePath(string path)
        {
            return string.IsNullOrEmpty(path) || Path.DirectorySeparatorChar == '\\' ? path : path.Replace('\\', '/');//.Replace("//", "/");
        }

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
