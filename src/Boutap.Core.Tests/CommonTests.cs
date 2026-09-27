// SPDX-FileCopyrightText: Boutap contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Boutap.Core.Common;
using Xunit;

namespace Boutap.Core.Tests;

/// <summary>Le contrat du noyau qui ne releve pas du generateur.</summary>
public sealed class CommonTests
{
    [Theory]
    [InlineData("0.0.4", "0.0.4")]
    [InlineData("1.2.3", "1.2.3")]
    [InlineData("10.20.30", "10.20.30")]
    [InlineData("0.1.0-alpha", "0.1.0-alpha")]
    [InlineData("0.1.0-alpha.1", "0.1.0-alpha.1")]
    [InlineData("1.0.0-0.3.7", "1.0.0-0.3.7")]
    public void SemanticVersion_Accepte_Les_Formes_Semver(string text, string expected)
    {
        Assert.True(SemanticVersion.TryParse(text, out SemanticVersion? version, out string? error));
        Assert.Null(error);
        Assert.Equal(expected, version?.ToString());
    }

    [Theory]
    [InlineData("1.2")]
    [InlineData("1.2.3.4")]
    [InlineData("v1.2.3")]
    [InlineData("1.2.3-")]
    [InlineData("1.0.0+build.5")]
    [InlineData("01.2.3")]
    [InlineData("1.02.3")]
    [InlineData("1.2.03")]
    [InlineData("")]
    public void SemanticVersion_Refuse_Les_Formes_Fabriques(string text)
    {
        Assert.False(SemanticVersion.IsValid(text));
    }

    [Fact]
    public void SemanticVersion_Classe_Les_Pre_Releases_Avant_La_Sortie()
    {
        SemanticVersion alpha = SemanticVersion.Parse("1.0.0-alpha");
        SemanticVersion alpha1 = SemanticVersion.Parse("1.0.0-alpha.1");
        SemanticVersion beta = SemanticVersion.Parse("1.0.0-beta");
        SemanticVersion final = SemanticVersion.Parse("1.0.0");

        Assert.True(alpha < alpha1);
        Assert.True(alpha1 < beta);
        Assert.True(beta < final);
    }

    [Fact]
    public void SemanticVersion_Ordre_Par_Composante_Quelconque()
    {
        Assert.True(SemanticVersion.Parse("0.9.9") < SemanticVersion.Parse("0.10.0"));
        Assert.True(SemanticVersion.Parse("1.9.9") < SemanticVersion.Parse("1.10.0"));
        Assert.Equal(SemanticVersion.Parse("1.2.3"), SemanticVersion.Parse("1.2.3"));
    }

    [Fact]
    public void SemanticVersion_Donne_Une_Raison_Refusable()
    {
        Assert.False(SemanticVersion.TryParse("1.2", out SemanticVersion? version, out string? error));
        Assert.Null(version);
        Assert.NotNull(error);

        Assert.False(SemanticVersion.TryParse("1.2.3-", out _, out string? other));
        Assert.NotNull(other);
    }

    [Fact]
    public void SemanticVersion_Min_Ret_La_Plus_Petite_Des_Deux()
    {
        Assert.Equal(
            SemanticVersion.Parse("0.1.0"),
            SemanticVersion.Min(SemanticVersion.Parse("0.1.0"), SemanticVersion.Parse("0.2.0")));
    }

    [Theory]
    [InlineData("")]
    [InlineData("g")]
    [InlineData("0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcde")]
    [InlineData("0123456789ABCDEF0123456789abcdef0123456789abcdef0123456789abcdef")]
    public void Sha256Hex_Refuse_Les_Hex_Invalides(string text)
    {
        Assert.False(Sha256Hex.IsLowerHex64(text));
    }

    [Fact]
    public void Sha256Hex_Produit_Sixty_Quatre_Chiffres_Minuscules()
    {
        string hex = Sha256Hex.OfBytesHex("abc"u8);

        Assert.True(Sha256Hex.IsLowerHex64(hex));
        Assert.Equal(
            "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad",
            hex);
    }

    [Fact]
    public void Sha256Hex_De_La_Vide_Est_Le_Zero_Officiel()
    {
        Assert.Equal(
            "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
            Sha256Hex.OfBytesHex([]));
    }
}
