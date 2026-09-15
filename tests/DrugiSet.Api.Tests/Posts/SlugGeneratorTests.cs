using System.Collections.Generic;
using DrugiSet.Api.Posts;
using Xunit;

namespace DrugiSet.Api.Tests.Posts;

public class SlugGeneratorTests
{
    [Fact]
    public void Generate_LowercasesAndDashesSpaces()
    {
        Assert.Equal("harmonogram-zimowej-edycji", SlugGenerator.Generate("Harmonogram zimowej edycji"));
    }

    [Fact]
    public void Generate_TransliteratesPolishDiacritics()
    {
        Assert.Equal("mistrz-drugiego-seta-zakonczenie", SlugGenerator.Generate("Mistrz Drugiego Seta — zakończenie"));
    }

    [Fact]
    public void Generate_CollapsesPunctuationIntoSingleDashes()
    {
        Assert.Equal("a-b-c", SlugGenerator.Generate("A!!  B,,, C"));
    }

    [Fact]
    public void Generate_TrimsLeadingAndTrailingDashes()
    {
        Assert.Equal("start-koniec", SlugGenerator.Generate("  Start koniec!  "));
    }

    [Fact]
    public void MakeUnique_ReturnsBaseSlugWhenNoCollision()
    {
        Assert.Equal("nowy-post", SlugGenerator.MakeUnique("nowy-post", new HashSet<string>()));
    }

    [Fact]
    public void MakeUnique_AppendsIncrementingSuffixOnCollision()
    {
        var existing = new HashSet<string> { "nowy-post", "nowy-post-2" };
        Assert.Equal("nowy-post-3", SlugGenerator.MakeUnique("nowy-post", existing));
    }
}
