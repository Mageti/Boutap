// SPDX-FileCopyrightText: Boutap contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Boutap.Core.Common;
using Xunit;

namespace Boutap.Core.Tests;

/// <summary>La tonique telle que l'ecrit le manifeste.</summary>
public sealed class KeySignatureTests
{
    [Theory]
    [InlineData("C", "C", KeyMode.Major)]
    [InlineData("Am", "A", KeyMode.Minor)]
    [InlineData("F#m", "F#", KeyMode.Minor)]
    [InlineData("Bb", "Bb", KeyMode.Major)]
    [InlineData("Ebm", "Eb", KeyMode.Minor)]
    [InlineData("Dmaj", "D", KeyMode.Major)]
    [InlineData("Adim", "A", KeyMode.Diminished)]
    [InlineData("Eaug", "E", KeyMode.Augmented)]
    public void KeySignature_Accepte_Les_Formes_Du_Schema(string text, string tonic, KeyMode mode)
    {
        KeySignature key = Parse(text);

        Assert.Equal(tonic, key.TonicName);
        Assert.Equal(mode, key.Mode);
    }

    [Theory]
    [InlineData("H")]
    [InlineData("Cb#")]
    [InlineData("Cmaj7")]
    [InlineData("c")]
    [InlineData("")]
    [InlineData("C#m##")]
    public void KeySignature_Refuse_Les_Formes_Etrangeres(string text)
    {
        Assert.False(KeySignature.TryParse(text, out KeySignature? key));
        Assert.Null(key);
    }

    [Theory]
    [InlineData("C", "C")]
    [InlineData("Am", "Am")]
    [InlineData("B#", "B#")]
    [InlineData("Cb", "Cb")]
    [InlineData("F##", null)]
    public void KeySignature_Garde_La_Graphie_Ecrite(string text, string? expected)
    {
        // Un manifeste relu puis reecrit doit rendre le meme champ « key ».
        // C'est pourquoi la graphie n'est pas normalisee.
        Assert.Equal(expected, KeySignature.IsValid(text) ? Parse(text).ToString() : null);
    }

    [Fact]
    public void KeySignature_Ecrit_Le_Majeur_Sans_Suffixe()
    {
        Assert.Equal("C", Parse("C").ToString());
        Assert.Equal("A", Parse("A").ToString());

        // Le mode majeur s'omet : « C » est plus court que « Cmaj », et le
        // schema autorise les deux.
        Assert.Equal("D", Parse("Dmaj").ToString());
        Assert.Equal("Am", Parse("Am").ToString());
        Assert.Equal("Cdim", Parse("Cdim").ToString());
    }

    [Theory]
    [InlineData("C", 0)]
    [InlineData("B", 11)]
    [InlineData("C#", 1)]
    [InlineData("Db", 1)]
    [InlineData("F#", 6)]
    [InlineData("Gb", 6)]
    [InlineData("Bb", 10)]
    [InlineData("A#", 10)]
    [InlineData("B#", 0)]
    [InlineData("Cb", 11)]
    [InlineData("E#", 5)]
    public void KeySignature_Classe_De_Hauteur_Ignore_La_Graphie(string text, int pitchClass)
    {
        Assert.Equal(pitchClass, Parse(text).PitchClass);
    }

    [Fact]
    public void KeySignature_Enharmonicues_Ont_Le_Meme_Son_Mais_Pas_La_Meme_Valeur()
    {
        Assert.Equal(Parse("F#").PitchClass, Parse("Gb").PitchClass);
        Assert.NotEqual(Parse("F#"), Parse("Gb"));
    }

    [Fact]
    public void KeySignature_Comparer_Tient_Compte_De_Tout()
    {
        Assert.Equal(KeySignature.Of("Am"), KeySignature.Of("A", KeyMode.Minor));
        Assert.NotEqual(KeySignature.Of("C"), KeySignature.Of("C", KeyMode.Minor));
        Assert.NotEqual(KeySignature.Of("C"), KeySignature.Of("Am"));
        Assert.NotEqual(KeySignature.Of("F#"), KeySignature.Of("Gb"));
    }

    private static KeySignature Parse(string text)
    {
        Assert.True(
            KeySignature.TryParse(text, out KeySignature? key),
            $"« {text} » devrait etre une tonalite valide.");
        return key ?? throw new InvalidOperationException("La tonalite vient d'etre validee.");
    }
}
