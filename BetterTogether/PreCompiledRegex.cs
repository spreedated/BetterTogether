using System.Text.RegularExpressions;

namespace BetterTogetherCore
{
    internal static partial class PreCompiledRegex
    {
        [GeneratedRegex(@"^[a-f0-9]{8}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{12}", RegexOptions.Compiled)]
        internal static partial Regex GuidRegex();
    }
}