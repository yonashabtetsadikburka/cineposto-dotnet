using CinePosto.Domain.Text;

namespace CinePosto.Domain.Tests.Text;

public class TitleNormalizerTests
{
    [Theory]
    [InlineData("Avatar", "Avatar", "avatar")]
    [InlineData("Avatar - 3D", "Avatar", "avatar")]
    [InlineData("AVATAR (3D)", "AVATAR (3D)", "avatar3d")]
    [InlineData("Il Signore degli Anelli", "Signore degli Anelli", "signoredeglianelli")]
    [InlineData("Star Wars: Il Risveglio della Forza", "Il Risveglio della Forza", "ilrisvegliodellaforza")]
    [InlineData("Fast & Furious 9", "Fast e Furious", "fastefurious")]
    [InlineData("Fast&Furious", "Fast e Furious", "fastefurious")]
    [InlineData("Toy Story 4", "Toy Story", "toystory")]
    // Legacy quirk: a bare year is stripped entirely, so the real title "2001" is lost.
    [InlineData("2001", "", "")]
    [InlineData("2001: Odissea nello spazio", "2001: Odissea nello spazio", "2001odisseanellospazio")]
    [InlineData("Blade Runner (1982)", "Blade Runner", "bladerunner")]
    [InlineData("Blade Runner 2049", "Blade Runner", "bladerunner")]
    // Legacy quirk: without the 4K marker only the year part is stripped, leaving "(RIED" behind.
    [InlineData("Il Padrino (RIED. 2022)", "Padrino (RIED", "padrinoried")]
    [InlineData("Il Padrino 4K", "Padrino 4K", "padrino4k")]
    [InlineData("Perché non ci credo", "Perché non ci credo", "perchénoncicredo")]
    [InlineData("L’uomo che uccise Don Chisciotte", "L'uomo che uccise Don Chisciotte", "luomocheuccisedonchisciotte")]
    [InlineData("L&#39;uomo che non c&#39;era", "L'uomo che non c'era", "luomochenoncera")]
    [InlineData("Tom &amp; Jerry", "Tom e Jerry", "tomejerry")]
    [InlineData("Mission: Impossible - Dead Reckoning Parte Uno", "Mission: Impossible - Dead Reckoning Parte Uno", "missionimpossibledeadreckoningparteuno")]
    [InlineData("Dune - VOST", "Dune", "dune")]
    [InlineData("Dune - OV", "Dune", "dune")]
    [InlineData("Dune (VO)", "Dune (VO)", "dunevo")]
    [InlineData("   Oppenheimer   ", "Oppenheimer", "oppenheimer")]
    [InlineData("Barbie - IMAX 3D", "Barbie - IMAX 3D", "barbieimax3d")]
    [InlineData("Barbie - IMAX - 3D", "Barbie", "barbie")]
    [InlineData("The Marvels", "Marvels", "marvels")]
    [InlineData("Marvel Studios: Captain America", "Studios: Captain America", "studioscaptainamerica")]
    [InlineData("Pixar: Inside Out 2", "Pixar: Inside Out", "pixarinsideout")]
    [InlineData("Disney: Wicked", "Disney: Wicked", "disneywicked")]
    [InlineData("Cinema Paradiso (C.A.)", "Cinema Paradiso (C.A.)", "cinemaparadisoca")]
    [InlineData("Mulholland Drive 4K (RIED. 2024)", "Mulholland Drive", "mulhollanddrive")]
    [InlineData("La Vita è Bella", "Vita è Bella", "vitaèbella")]
    [InlineData("Più forte ragazzi!", "Più forte ragazzi", "piùforteragazzi")]
    [InlineData("!!!", "", "")]
    [InlineData("Il Re Leone - Family", "Re Leone", "releone")]
    [InlineData("Napoleon - Live", "Napoleon", "napoleon")]
    [InlineData("Il Gladiatore II", "Gladiatore II", "gladiatoreii")]
    [InlineData("", "", "")]
    public void Parity_MatchesLegacyOutputs(string input, string expectedNormalized, string expectedKey)
    {
        Assert.Equal(expectedNormalized, TitleNormalizer.Normalize(input));
        Assert.Equal(expectedKey, TitleNormalizer.Key(input));
    }

    [Theory]
    [InlineData("  Hello   World  ", "Hello World")]
    [InlineData("FILM: The Beginning", "FILM: The Beginning")]
    [InlineData("Film (2024)", "Film")]
    [InlineData("", "")]
    public void Normalize_MatchesLegacyCases(string input, string expected)
    {
        Assert.Equal(expected, TitleNormalizer.Normalize(input));
    }

    [Theory]
    [InlineData("hello world", "helloworld")]
    [InlineData("Hello World", "helloworld")]
    [InlineData("  Hello   World  ", "helloworld")]
    [InlineData("", "")]
    public void Key_MatchesLegacyCases(string input, string expected)
    {
        Assert.Equal(expected, TitleNormalizer.Key(input));
    }

    [Fact]
    public void Key_AmpersandFoldsToE()
    {
        Assert.Equal(TitleNormalizer.Key("Amori e incantesimi 2"), TitleNormalizer.Key("AMORI & INCANTESIMI 2"));
        Assert.Equal("Tom e Jerry", TitleNormalizer.Normalize("Tom & Jerry"));
    }

    [Fact]
    public void Normalize_YearOnlyTitle_ReturnsEmpty_LegacyQuirk()
    {
        Assert.Equal(string.Empty, TitleNormalizer.Normalize("2001"));
    }

    [Fact]
    public void Key_YearOnlyTitle_ReturnsEmpty_LegacyQuirk()
    {
        Assert.Equal(string.Empty, TitleNormalizer.Key("2001"));
    }

    [Fact]
    public void Normalize_RieditionWithout4K_LeavesPartialSuffix_LegacyQuirk()
    {
        Assert.Equal("Padrino (RIED", TitleNormalizer.Normalize("Il Padrino (RIED. 2022)"));
    }
}
