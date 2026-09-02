using Mixcloud.Core.Urls;
using Xunit;

public class SlugTitleTests
{
    [Theory]
    [InlineData("mental-place-26", "Mental Place 26")]
    [InlineData("si-those-days-enr42", "Si Those Days Enr42")]
    [InlineData("loraine-james-1st-september-2026", "Loraine James 1st September 2026")]
    [InlineData("single", "Single")]
    public void ZamieniaSlugNaTytul(string slug, string oczekiwany)
    {
        Assert.Equal(oczekiwany, SlugTitle.FromSlug(slug));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void PustySlugDajePustyTytul(string slug)
    {
        Assert.Equal(string.Empty, SlugTitle.FromSlug(slug));
    }

    [Fact]
    public void ScalaWielokrotneMyslniki()
    {
        Assert.Equal("A B", SlugTitle.FromSlug("a---b"));
    }
}
