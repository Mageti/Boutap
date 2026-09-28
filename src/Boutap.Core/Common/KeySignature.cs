// SPDX-FileCopyrightText: Boutap contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace Boutap.Core.Common;

/// <summary>Lettre de la tonique.</summary>
public enum Letter
{
    /// <summary>Do.</summary>
    C,

    /// <summary>Re.</summary>
    D,

    /// <summary>Mi.</summary>
    E,

    /// <summary>Fa.</summary>
    F,

    /// <summary>Sol.</summary>
    G,

    /// <summary>La.</summary>
    A,

    /// <summary>Si.</summary>
    B,
}

/// <summary>Alteration portee par la tonique.</summary>
public enum Accidental
{
    /// <summary>Aucune alteration.</summary>
    None,

    /// <summary>Diese.</summary>
    Sharp,

    /// <summary>Bemol.</summary>
    Flat,
}

/// <summary>Qualite harmonique d'une tonalite.</summary>
public enum KeyMode
{
    /// <summary>Mode majeur, ou ionien.</summary>
    Major,

    /// <summary>Mode mineur, ou eolien.</summary>
    Minor,

    /// <summary>Mode diminue.</summary>
    Diminished,

    /// <summary>Mode augmente.</summary>
    Augmented,
}

/// <summary>
/// Tonique et qualite d'une tonalite, conformement au motif
/// <c>^[A-G](#|b)?(maj|min|m|dim|aug)?$</c> de <c>schema/manifest.schema.json</c>.
/// </summary>
/// <remarks>
/// <para>
/// L'absence de suffixe de qualite vaut mode majeur : c'est ce que le schema
/// autorise et ce que produisent la plupart des analyseurs. On note alors
/// <c>C</c> et non <c>Cmaj</c>, pour rester dans la forme la plus courte.
/// </para>
/// <para>
/// <c>Bb</c> se lit « si bemol » et <c>Bm</c> « si mineur » : la lettre
/// <c>b</c> seule n'est jamais un suffixe de qualite, donc le format n'a pas
/// d'ambiguite a trancher.
/// </para>
/// <para>
/// La graphie est preservee telle qu'ecrite, parce que le champ <c>key</c>
/// d'un manifeste published ne doit pas changer quand on relit le pack.
/// <c>F#</c> et <c>Gb</c> restent donc distincts a l'egalite, mais ont le
/// meme <see cref="PitchClass"/>.
/// </para>
/// <para>
/// Ce type ne modelise volontairement pas le nombre d' alterations de la
/// signature — cinq bemols pour <c>Eb</c>, six dièses pour <c>F#</c> majeur.
/// Cette arithmetique depend de la convention d'ecriture, pas du son, et
/// n'arrive qu'en S3, quand le transposeur d'accords en aura besoin.
/// </para>
/// </remarks>
public sealed class KeySignature : IEquatable<KeySignature>
{
    /// <summary>
    /// Le mode qu'on suppose quand rien n'est dit : l'absence de suffixe vaut
    /// majeur. C'est aussi la valeur que <c>Of</c> interpretait jusqu'alors
    /// comme « modo majeur a imposer », ce qui transformait <c>Of("Am")</c> en
    /// la majeur. Elle sert donc de « rien a imposer » : voir
    /// <see cref="Of(string, KeyMode)"/>.
    /// </summary>
    public const KeyMode DefaultMode = KeyMode.Major;

    private KeySignature(Letter letter, Accidental accidental, KeyMode mode)
    {
        Letter = letter;
        Accidental = accidental;
        Mode = mode;
        TonicName = letter.ToString() + AccidentalMark(accidental);
    }

    /// <summary>Letre de la tonique.</summary>
    public Letter Letter { get; }

    /// <summary>Alteration de la tonique.</summary>
    public Accidental Accidental { get; }

    /// <summary>Qualite harmonique.</summary>
    public KeyMode Mode { get; }

    /// <summary>Nom de la tonique, tel qu'il s'ecrit dans le manifeste.</summary>
    public string TonicName { get; }

    /// <summary>
    /// Classe de hauteur du son, de 0 pour <c>Do</c> a 11 pour <c>Si</c>.
    /// </summary>
    /// <remarks>
    /// C'est la seule information dont le code a besoin pour raisonner sur le
    /// son : la correspondance des accords, la transposition, la detection de
    /// la tonique. Elle ne depend pas de la graphie.
    /// </remarks>
    public int PitchClass => ((int)Semitone(Letter) + AccidentalOffset(Accidental) + 12) % 12;

    /// <summary>Construit une tonalite a partir de sa graphie.</summary>
    /// <param name="tonicName">
    /// Nom de la tonique, par exemple <c>F#</c> ou <c>Bb</c>, eventuellement
    /// suivi de son mode, par exemple <c>Am</c>.
    /// </param>
    /// <param name="mode">
    /// Qualite a imposer. <see cref="DefaultMode"/> signifie « ne rien
    /// imposer » et non « remplacer » : sans cela, <c>Of("Am")</c>
    /// transformerait un la mineur en la majeur sans qu'aucune erreur ne soit visible.
    /// </param>
    /// <returns>La tonalite construite.</returns>
    /// <exception cref="FormatException">Le nom n'est pas une tonique valide.</exception>
    public static KeySignature Of(string? tonicName, KeyMode mode = DefaultMode)
    {
        if (!TryParse(tonicName, out KeySignature? key) || key is null)
        {
            throw new FormatException($"« {tonicName} » n'est pas une tonalite valide.");
        }

        return mode == DefaultMode ? key : new KeySignature(key.Letter, key.Accidental, mode);
    }

    /// <summary>Essaie d'analyser une tonalite ecrite dans le manifeste.</summary>
    /// <param name="text">Texte a analyser, eventuellement nul.</param>
    /// <param name="key">Tonalite analysee en cas de succes.</param>
    /// <returns>Vrai si le texte est une tonalite valide.</returns>
    public static bool TryParse(string? text, out KeySignature? key)
    {
        key = null;
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        if (!TryReadLetter(text[0], out Letter letter))
        {
            return false;
        }

        Accidental accidental = Accidental.None;
        int index = 1;
        if (index < text.Length)
        {
            if (text[index] == '#')
            {
                accidental = Accidental.Sharp;
                index++;
            }
            else if (text[index] == 'b')
            {
                accidental = Accidental.Flat;
                index++;
            }
        }

        if (index >= text.Length)
        {
            key = new KeySignature(letter, accidental, DefaultMode);
            return true;
        }

        KeyMode mode;
        switch (text[index..])
        {
            case "maj": mode = KeyMode.Major; break;
            case "m":
            case "min": mode = KeyMode.Minor; break;
            case "dim": mode = KeyMode.Diminished; break;
            case "aug": mode = KeyMode.Augmented; break;
            default: return false;
        }

        key = new KeySignature(letter, accidental, mode);
        return true;
    }

    /// <summary>Indique si le texte est une tonalite valide.</summary>
    /// <param name="text">Texte a verifier, eventuellement nul.</param>
    /// <returns>Vrai si <see cref="TryParse(string?, out KeySignature?)"/> aboutirait.</returns>
    public static bool IsValid(string? text) => TryParse(text, out _);

    /// <inheritdoc/>
    public bool Equals(KeySignature? other) =>
        other is not null
        && Letter == other.Letter
        && Accidental == other.Accidental
        && Mode == other.Mode;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => Equals(obj as KeySignature);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(Letter, Accidental, Mode);

    /// <inheritdoc/>
    public override string ToString() => Mode == DefaultMode ? TonicName : TonicName + ModeSuffix(Mode);

    private static string AccidentalMark(Accidental accidental) => accidental switch
    {
        Accidental.Sharp => "#",
        Accidental.Flat => "b",
        _ => string.Empty,
    };

    private static string ModeSuffix(KeyMode mode) => mode switch
    {
        KeyMode.Major => "maj",
        KeyMode.Minor => "m",
        KeyMode.Diminished => "dim",
        _ => "aug",
    };

    private static int Semitone(Letter letter) => letter switch
    {
        Letter.C => 0,
        Letter.D => 2,
        Letter.E => 4,
        Letter.F => 5,
        Letter.G => 7,
        Letter.A => 9,
        _ => 11,
    };

    private static int AccidentalOffset(Accidental accidental) => accidental switch
    {
        Accidental.Sharp => 1,
        Accidental.Flat => -1,
        _ => 0,
    };

    private static bool TryReadLetter(char c, out Letter letter)
    {
        switch (c)
        {
            case 'C': letter = Letter.C; return true;
            case 'D': letter = Letter.D; return true;
            case 'E': letter = Letter.E; return true;
            case 'F': letter = Letter.F; return true;
            case 'G': letter = Letter.G; return true;
            case 'A': letter = Letter.A; return true;
            case 'B': letter = Letter.B; return true;
            default: letter = Letter.C; return false;
        }
    }
}
