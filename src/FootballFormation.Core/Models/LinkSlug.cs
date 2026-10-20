using System.Globalization;
using System.Text;

namespace FootballFormation.Core.Models;

/// A club or team name as it reads in a team link. Two names giving the same one would share a link.
public static class LinkSlug
{
    public static string Of(string name)
    {
        var slug = new StringBuilder(name.Length);
        foreach (var c in name.Trim().Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;

            if (char.IsAsciiLetterOrDigit(c) || c == '-') slug.Append(char.ToLowerInvariant(c));
            else if (slug.Length > 0 && slug[^1] != '_') slug.Append('_');
        }

        var text = slug.ToString().TrimEnd('_');
        return text.Length > 0 ? text : "_";
    }

    public static bool SameLink(string a, string b) => Of(a) == Of(b);
}
