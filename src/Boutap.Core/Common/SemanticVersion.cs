// SPDX-FileCopyrightText: Boutap contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Globalization;

namespace Boutap.Core.Common;

/// <summary>
/// Version semantique telle que le format `.btp` l'exige :
/// <c>MAJEUR.MINEUR.PATCH</c>, sans prefixe <c>v</c>, avec un suffixe
/// pre-release optionnel.
/// </summary>
/// <remarks>
/// La construction passe par <see cref="Parse(string?)"/>, qui n'accepte que
/// la forme ecrite dans <c>schema/manifest.schema.json</c>. Le type ne tente
/// pas d'etre plus permissif que le schema : un manifeste valide ne doit pas
/// pouvoir decrire une version que le lecteur refuserait.
/// </remarks>
public sealed class SemanticVersion : IComparable<SemanticVersion>, IEquatable<SemanticVersion>
{
    private SemanticVersion(int major, int minor, int patch, string? prerelease)
    {
        Major = major;
        Minor = minor;
        Patch = patch;
        Prerelease = prerelease;
    }

    /// <summary>Composant majeur.</summary>
    public int Major { get; }

    /// <summary>Composant mineur.</summary>
    public int Minor { get; }

    /// <summary>Composant de correctif.</summary>
    public int Patch { get; }

    /// <summary>Suffixe pre-release, sans le tiret initial, ou <c>null</c>.</summary>
    public string? Prerelease { get; }

    /// <summary>Version de reference du projet, tant qu'aucune release n'est publiee.</summary>
    public static SemanticVersion Current { get; } = new(0, 1, 0, null);

    /// <summary>Indique si la version porte un suffixe pre-release.</summary>
    public bool IsPrerelease => Prerelease is not null;

    /// <summary>
    /// Analyse une version semantique.
    /// </summary>
    /// <param name="text">Texte a analyser, eventuellement nul.</param>
    /// <returns>La version analysee.</returns>
    /// <exception cref="FormatException">Le texte n'est pas une version semantique.</exception>
    public static SemanticVersion Parse(string? text)
    {
        if (!TryParse(text, out SemanticVersion? version, out string? error) || version is null)
        {
            throw new FormatException(error);
        }

        return version;
    }

    /// <summary>Essaie d'analyser une version semantique.</summary>
    /// <param name="text">Texte a analyser, eventuellement nul.</param>
    /// <param name="version">Version analysee en cas de succes.</param>
    /// <param name="error">Motif de l'echec en cas d'echec.</param>
    /// <returns>Vrai si le texte est une version semantique valide.</returns>
    public static bool TryParse(string? text, out SemanticVersion? version, out string? error)
    {
        version = null;
        error = null;

        if (string.IsNullOrEmpty(text))
        {
            error = "La version est vide.";
            return false;
        }

        ReadOnlySpan<char> rest = text.AsSpan();
        if (!TryReadNumber(rest, out int major, out ReadOnlySpan<char> tail))
        {
            error = $"Version invalide : « {text} » n'est pas de la forme MAJEUR.MINEUR.PATCH.";
            return false;
        }

        if (tail.Length == 0 || tail[0] != '.')
        {
            error = $"Version invalide : « {text} » n'est pas de la forme MAJEUR.MINEUR.PATCH.";
            return false;
        }

        rest = tail[1..];
        if (!TryReadNumber(rest, out int minor, out tail))
        {
            error = $"Version invalide : « {text} » n'est pas de la forme MAJEUR.MINEUR.PATCH.";
            return false;
        }

        if (tail.Length == 0 || tail[0] != '.')
        {
            error = $"Version invalide : « {text} » n'est pas de la forme MAJEUR.MINEUR.PATCH.";
            return false;
        }

        rest = tail[1..];
        if (!TryReadNumber(rest, out int patch, out tail))
        {
            error = $"Version invalide : « {text} » n'est pas de la forme MAJEUR.MINEUR.PATCH.";
            return false;
        }

        string? prerelease = null;
        if (tail.Length > 0)
        {
            if (tail[0] != '-')
            {
                error = $"Version invalide : « {text} » a un suffixe qui ne commence pas par « - ».";
                return false;
            }

            prerelease = tail[1..].ToString();
            if (!IsValidPrerelease(prerelease))
            {
                error = $"Version invalide : le suffixe pre-release « {prerelease} » est vide ou mal forme.";
                return false;
            }
        }

        version = new SemanticVersion(major, minor, patch, prerelease);
        return true;
    }

    /// <summary>Indique si le texte est une version semantique valide.</summary>
    /// <param name="text">Texte a verifier, eventuellement nul.</param>
    /// <returns>Vrai si <see cref="Parse(string?)"/> aboutirait.</returns>
    public static bool IsValid(string? text) => TryParse(text, out _, out _);

    /// <inheritdoc/>
    public int CompareTo(SemanticVersion? other)
    {
        if (other is null)
        {
            return 1;
        }

        int result = Major.CompareTo(other.Major);
        if (result != 0)
        {
            return result;
        }

        result = Minor.CompareTo(other.Minor);
        if (result != 0)
        {
            return result;
        }

        result = Patch.CompareTo(other.Patch);
        if (result != 0)
        {
            return result;
        }

        return ComparePrerelease(Prerelease, other.Prerelease);
    }

    /// <summary>Compare deux versions et renvoie l'identite du minimum.</summary>
    /// <param name="left">Premiere version.</param>
    /// <param name="right">Seconde version.</param>
    /// <returns>La plus petite des deux.</returns>
    /// <exception cref="ArgumentNullException">Un parametre est nul.</exception>
    public static SemanticVersion Min(SemanticVersion left, SemanticVersion right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        return left.CompareTo(right) <= 0 ? left : right;
    }

    /// <inheritdoc/>
    public bool Equals(SemanticVersion? other) => CompareTo(other) == 0;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => Equals(obj as SemanticVersion);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(Major, Minor, Patch, Prerelease);

    /// <inheritdoc/>
    public override string ToString()
    {
        string core = string.Create(
            CultureInfo.InvariantCulture, $"{Major}.{Minor}.{Patch}");
        return Prerelease is null ? core : core + "-" + Prerelease;
    }

    /// <summary>Compare deux versions pour l'egalite.</summary>
    /// <param name="left">Premiere version.</param>
    /// <param name="right">Seconde version.</param>
    /// <returns>Vrai si les deux versions sont equivalentes.</returns>
    public static bool operator ==(SemanticVersion? left, SemanticVersion? right) =>
        left is null ? right is null : left.Equals(right);

    /// <summary>Compare deux versions pour l'inegalite.</summary>
    /// <param name="left">Premiere version.</param>
    /// <param name="right">Seconde version.</param>
    /// <returns>Vrai si les deux versions different.</returns>
    public static bool operator !=(SemanticVersion? left, SemanticVersion? right) => !(left == right);

    /// <summary>Indique si une version est strictement anterieure a une autre.</summary>
    /// <param name="left">Premiere version.</param>
    /// <param name="right">Seconde version.</param>
    /// <returns>Vrai si <paramref name="left"/> precede <paramref name="right"/>.</returns>
    public static bool operator <(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) < 0;

    /// <summary>Indique si une version est anterieure ou egale a une autre.</summary>
    /// <param name="left">Premiere version.</param>
    /// <param name="right">Seconde version.</param>
    /// <returns>Vrai si <paramref name="left"/> ne suit pas <paramref name="right"/>.</returns>
    public static bool operator <=(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) <= 0;

    /// <summary>Indique si une version est strictement posterieure a une autre.</summary>
    /// <param name="left">Premiere version.</param>
    /// <param name="right">Seconde version.</param>
    /// <returns>Vrai si <paramref name="left"/> suit <paramref name="right"/>.</returns>
    public static bool operator >(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) > 0;

    /// <summary>Indique si une version est posterieure ou egale a une autre.</summary>
    /// <param name="left">Premiere version.</param>
    /// <param name="right">Seconde version.</param>
    /// <returns>Vrai si <paramref name="left"/> ne precede pas <paramref name="right"/>.</returns>
    public static bool operator >=(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) >= 0;

    private static bool TryReadNumber(ReadOnlySpan<char> text, out int value, out ReadOnlySpan<char> tail)
    {
        int index = 0;
        while (index < text.Length && char.IsAsciiDigit(text[index]))
        {
            index++;
        }

        if (index == 0)
        {
            value = 0;
            tail = text;
            return false;
        }

        // « 01 » n'est pas « 1 » : le schema n'autorise aucun zero de tete.
        if (index > 1 && text[0] == '0')
        {
            value = 0;
            tail = text;
            return false;
        }

        // Un nombre trop grand n'est pas « analyse puis arrondi » : c'est
        // invalide, et un manifeste valide ne doit pas pouvoir decrire une
        // version que le lecteur refuserait.
        if (!int.TryParse(text[..index], NumberStyles.None, CultureInfo.InvariantCulture, out value))
        {
            tail = text;
            return false;
        }

        tail = text[index..];
        return true;
    }

    private static bool IsValidPrerelease(string prerelease)
    {
        if (prerelease.Length == 0 || prerelease[0] == '.' || prerelease[^1] == '.')
        {
            return false;
        }

        bool previousDot = true;
        foreach (char c in prerelease)
        {
            if (c == '.')
            {
                if (previousDot)
                {
                    return false;
                }

                previousDot = true;
                continue;
            }

            bool alnum = c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '-';
            if (!alnum)
            {
                return false;
            }

            previousDot = false;
        }

        return !previousDot;
    }

    // Regle semver : une version avec suffixe precede la version sans suffixe.
    // Deux suffixes se comparent identiquement, puis par table ASCII.
    private static int ComparePrerelease(string? left, string? right)
    {
        if (left is null && right is null)
        {
            return 0;
        }

        if (left is null)
        {
            return 1;
        }

        if (right is null)
        {
            return -1;
        }

        string[] a = left.Split('.');
        string[] b = right.Split('.');
        int shared = Math.Min(a.Length, b.Length);
        for (int i = 0; i < shared; i++)
        {
            bool aNumeric = int.TryParse(a[i], NumberStyles.None, CultureInfo.InvariantCulture, out int aValue);
            bool bNumeric = int.TryParse(b[i], NumberStyles.None, CultureInfo.InvariantCulture, out int bValue);

            int result;
            if (aNumeric && bNumeric)
            {
                result = aValue.CompareTo(bValue);
            }
            else if (aNumeric != bNumeric)
            {
                // Les identifiants numeriques ont une precedence plus basse.
                result = aNumeric ? -1 : 1;
            }
            else
            {
                result = string.CompareOrdinal(a[i], b[i]);
            }

            if (result != 0)
            {
                return result;
            }
        }

        return a.Length.CompareTo(b.Length);
    }
}
