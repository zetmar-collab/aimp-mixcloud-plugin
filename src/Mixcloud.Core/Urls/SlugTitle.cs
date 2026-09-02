using System;
using System.Globalization;
using System.Linq;

namespace Mixcloud.Core.Urls
{
    public static class SlugTitle
    {
        public static string FromSlug(string slug)
        {
            if (string.IsNullOrWhiteSpace(slug)) return string.Empty;

            var words = slug
                .Split(new[] { '-', '_' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(Capitalize);

            return string.Join(" ", words);
        }

        private static string Capitalize(string word)
        {
            if (word.Length == 0) return word;
            return char.ToUpper(word[0], CultureInfo.InvariantCulture) + word.Substring(1);
        }
    }
}
