using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Mixcloud.Core.Localization;
using Xunit;

public class LanguageFileTests
{
    private static string LangDir()
    {
        var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Mixcloud.sln")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(dir.FullName, "src", "Mixcloud.Plugin", "Langs");
    }

    private static Dictionary<string, string> ReadLng(string path)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        var section = string.Empty;
        foreach (var raw in File.ReadAllLines(path, Encoding.UTF8))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith(";", StringComparison.Ordinal)) continue;
            if (line.StartsWith("[", StringComparison.Ordinal) && line.EndsWith("]", StringComparison.Ordinal))
            {
                section = line.Substring(1, line.Length - 2);
                continue;
            }
            var eq = line.IndexOf('=');
            if (eq <= 0) continue;
            result[section + "\\" + line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
        }
        return result;
    }

    [Fact]
    public void ObaPlikiMajaIdentyczneZbioryKluczy()
    {
        var pl = ReadLng(Path.Combine(LangDir(), "polish.lng"));
        var en = ReadLng(Path.Combine(LangDir(), "english.lng"));

        var brakujeWPl = en.Keys.Except(pl.Keys).OrderBy(k => k).ToList();
        var brakujeWEn = pl.Keys.Except(en.Keys).OrderBy(k => k).ToList();

        Assert.True(brakujeWPl.Count == 0, "Brak w polish.lng: " + string.Join(", ", brakujeWPl));
        Assert.True(brakujeWEn.Count == 0, "Brak w english.lng: " + string.Join(", ", brakujeWEn));
    }

    [Fact]
    public void KazdaStalaZStringKeysMaOdpowiednikWObuPlikach()
    {
        var pl = ReadLng(Path.Combine(LangDir(), "polish.lng"));
        var en = ReadLng(Path.Combine(LangDir(), "english.lng"));

        var klucze = typeof(StringKeys)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue())
            .ToList();

        Assert.NotEmpty(klucze);
        foreach (var k in klucze)
        {
            Assert.True(en.ContainsKey(k), "english.lng nie ma klucza " + k);
            Assert.True(pl.ContainsKey(k), "polish.lng nie ma klucza " + k);
        }
    }

    [Fact]
    public void ZadenNapisNieJestPusty()
    {
        foreach (var plik in new[] { "polish.lng", "english.lng" })
            foreach (var para in ReadLng(Path.Combine(LangDir(), plik)))
                Assert.False(string.IsNullOrWhiteSpace(para.Value), plik + ": pusty napis dla " + para.Key);
    }
}
