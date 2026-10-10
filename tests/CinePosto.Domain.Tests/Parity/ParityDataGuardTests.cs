namespace CinePosto.Domain.Tests.Parity;

public class ParityDataGuardTests
{
    [Theory]
    [InlineData("levenshtein.tsv")]
    [InlineData("fuzzy_match.tsv")]
    public void ParityFile_HasEnoughDataRows(string fileName)
    {
        IReadOnlyList<string?[]> rows = TsvParityLoader.Load(fileName);
        Assert.True(rows.Count >= 50, $"Parity file '{fileName}' has only {rows.Count} data rows; expected at least 50.");
    }
}
