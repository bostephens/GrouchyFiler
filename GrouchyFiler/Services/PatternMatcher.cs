using System;
using System.IO;
using System.Text.RegularExpressions;
using GrouchyFiler.Models;

namespace GrouchyFiler.Services
{
    public static class PatternMatcher
    {
        private static readonly TimeSpan MatchTimeout = TimeSpan.FromMilliseconds(100);

        public static bool Matches(RootConfig root, string fullPath)
        {
            string fileName = Path.GetFileName(fullPath);
            if (string.IsNullOrEmpty(fileName)) return false;

            foreach (var pattern in root.Patterns)
            {
                if (pattern is null || string.IsNullOrEmpty(pattern.Value)) continue;
                if (string.Equals(pattern.Type, "glob", StringComparison.OrdinalIgnoreCase) && GlobMatch(pattern.Value, fileName))
                    return true;
                if (string.Equals(pattern.Type, "regex", StringComparison.OrdinalIgnoreCase) && RegexMatch(pattern.Value, fileName))
                    return true;
                if (string.Equals(pattern.Type, "literal", StringComparison.OrdinalIgnoreCase) && LiteralMatch(pattern.Value, fileName))
                    return true;
            }
            return false;
        }

        private static bool LiteralMatch(string pattern, string fileName)
        {
            return string.Equals(fileName, pattern, StringComparison.OrdinalIgnoreCase);
        }

        private static bool RegexMatch(string pattern, string fileName)
        {
            try
            {
                return Regex.IsMatch(fileName, pattern,
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, MatchTimeout);
            }
            catch (ArgumentException) { return false; }
            catch (RegexMatchTimeoutException) { return false; }
        }

        private static bool GlobMatch(string pattern, string fileName)
        {
            string regex = "\\A" + Regex.Escape(pattern)
                .Replace("\\*", ".*")
                .Replace("\\?", ".") + "\\z";
            return RegexMatch(regex, fileName);
        }
    }
}
