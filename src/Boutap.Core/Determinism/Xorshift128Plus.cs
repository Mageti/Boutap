// SPDX-FileCopyrightText: Boutap contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace Boutap.Core.Determinism;

/// <summary>
/// Generateur pseudo-aleatoire xorshift128+, reimplemente mot pour mot d'apres
/// l'ADR 0005 (<c>docs/idees/boutap/adr/0005</c>).
/// </summary>
/// <remarks>
/// <para>
/// Ce type est la seule source d'alea du projet. Il n'est pas chiffre :
/// voir ADR 0005, section « Consequences », pour la raison de savoir qu'un
/// joueur local peut prevoir l'humanisation d'un chart sans que ce soit un
/// probleme.
/// </para>
/// <para>
/// L'algorithme est fige. Toute modification doit passer par un ADR de
/// remplacement, sinon les packs deja publies cesseraient d'etre reproductibles.
/// </para>
/// </remarks>
public sealed class Xorshift128Plus
{
    /// <summary>Nombre d'octets de graine consommes pour un etat 128 bits.</summary>
    public const int SeedByteLength = 16;

    // Constantes de repli (fraction doree et son derivee) : convention de
    // Vigna pour lever un etat entierement nul. Voir ADR 0005.
    private const ulong FallbackS0 = 0x9E3779B97F4A7C15UL;
    private const ulong FallbackS1 = 0xBF58476D1CE4E5B9UL;

    private ulong _s0;
    private ulong _s1;

    /// <summary>Construit un generateur a partir des deux mots de son etat.</summary>
    /// <param name="s0">Premier mot de l'etat.</param>
    /// <param name="s1">Second mot de l'etat.</param>
    /// <remarks>
    /// L'etat nul est accepte tel quel pour que le comportement brut reste
    /// observable et testable. Pour un generateur utilisable, passer par
    /// <see cref="Create"/>.
    /// </remarks>
    public Xorshift128Plus(ulong s0, ulong s1)
    {
        _s0 = s0;
        _s1 = s1;
    }

    /// <summary>Vrai si l'etat est entierement nul, donc si la suite le serait.</summary>
    public bool IsDegenerate => _s0 == 0 && _s1 == 0;

    /// <summary>Cree un generateur utilisable en levant un eventuel etat nul.</summary>
    /// <param name="s0">Premier mot de l'etat.</param>
    /// <param name="s1">Second mot de l'etat.</param>
    /// <returns>Un generateur dont l'etat n'est pas degenere.</returns>
    public static Xorshift128Plus Create(ulong s0, ulong s1)
    {
        if (s0 == 0 && s1 == 0)
        {
            return new Xorshift128Plus(FallbackS0, FallbackS1);
        }

        return new Xorshift128Plus(s0, s1);
    }

    /// <summary>Cree un generateur a partir d'au moins 16 octets de graine.</summary>
    /// <param name="seed">Octets de graine, en boutisme.</param>
    /// <returns>Un generateur non degenere.</returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="seed"/> fait moins de <see cref="SeedByteLength"/> octets.
    /// </exception>
    public static Xorshift128Plus FromSeed(ReadOnlySpan<byte> seed)
    {
        if (seed.Length < SeedByteLength)
        {
            throw new ArgumentException(
                $"La graine doit faire au moins {SeedByteLength} octets, elle en fait {seed.Length}.",
                nameof(seed));
        }

        ulong s0 = System.Buffers.Binary.BinaryPrimitives.ReadUInt64BigEndian(seed[..8]);
        ulong s1 = System.Buffers.Binary.BinaryPrimitives.ReadUInt64BigEndian(seed.Slice(8, 8));
        return Create(s0, s1);
    }

    /// <summary>Retourne le prochain mot de 64 bits de la suite.</summary>
    /// <returns>Un entier non signe de 64 bits.</returns>
    public ulong NextUInt64()
    {
        ulong x = _s0;
        ulong y = _s1;
        _s0 = y;
        x ^= x << 23;
        _s1 = x ^ y ^ (x >> 17) ^ (y >> 26);
        return _s1 + y;
    }

    /// <summary>Retourne un reel dans l'intervalle [0, 1).</summary>
    /// <returns>Un double dans [0, 1).</returns>
    public double NextDouble() => (NextUInt64() >> 11) * (1.0 / 9007199254740992.0);

    /// <summary>Retourne un reel dans l'intervalle semi-ouvert [min, max).</summary>
    /// <param name="min">Borne basse incluse.</param>
    /// <param name="max">Borne haute exclue.</param>
    /// <returns>Un double dans [min, max).</returns>
    /// <exception cref="ArgumentException"><paramref name="max"/> est inferieur a <paramref name="min"/>.</exception>
    public double NextDouble(double min, double max)
    {
        if (max < min)
        {
            throw new ArgumentException(
                $"La borne haute ({max}) est inferieure a la borne basse ({min}).", nameof(max));
        }

        return min + ((NextDouble() * (max - min)));
    }

    /// <summary>Retourne un entier dans l'intervalle semi-ouvert [min, max).</summary>
    /// <param name="min">Borne basse incluse.</param>
    /// <param name="max">Borne haute exclue.</param>
    /// <returns>Un entier dans [min, max).</returns>
    /// <exception cref="ArgumentException">La plage est vide ou deborde.</exception>
    public int NextInt32(int min, int max)
    {
        if (max <= min)
        {
            throw new ArgumentException(
                $"La plage [{min}, {max}) est vide ou negative.", nameof(max));
        }

        // Rejection sampling : un modulo simple introduirait un biais
        // mesurable sur les petites plages, ce qui fausserait exactement ce
        // que l'humanisation est censee repartir uniformement.
        ulong span = (ulong)((long)max - min);
        ulong limit = ulong.MaxValue - (ulong.MaxValue % span);
        ulong draw;
        do
        {
            draw = NextUInt64();
        }
        while (draw >= limit);

        return (int)((long)(draw % span) + min);
    }

    /// <summary>Melange une liste en place (Fisher-Yates).</summary>
    /// <typeparam name="T">Type des elements.</typeparam>
    /// <param name="items">Liste a melanger.</param>
    /// <exception cref="ArgumentNullException"><paramref name="items"/> est nul.</exception>
    public void Shuffle<T>(IList<T> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        for (int i = items.Count - 1; i > 0; i--)
        {
            int j = NextInt32(0, i + 1);
            (items[i], items[j]) = (items[j], items[i]);
        }
    }
}
