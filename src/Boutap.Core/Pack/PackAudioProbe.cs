// SPDX-FileCopyrightText: Boutap contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace Boutap.Core.Pack;

/// <summary>Ce qu'un lecteur d'audio peut dire d'un fichier audio.</summary>
/// <param name="Sha256Hex">SHA-256 de la forme <em>decodee</em>, 64 hex minuscules.</param>
/// <param name="DurationSeconds">Duree decodee, en secondes.</param>
/// <param name="SampleRate">Frequence d'echantillonnage.</param>
/// <param name="Channels">Nombre de canaux.</param>
/// <param name="BitsPerSample">Bits par echantillon dans le fichier.</param>
public readonly record struct PackAudioFacts(
    string Sha256Hex,
    double DurationSeconds,
    int SampleRate,
    int Channels,
    int BitsPerSample);

/// <summary>
/// L'interface dont <see cref="PackValidator"/> a besoin pour verifier que le
/// <c>audio.sha256</c> du manifeste decrit bien l'audio.
/// </summary>
/// <remarks>
/// <para>
/// Elle existe pour une raison de dependances. Le decodeur d'audio vit dans
/// <c>Boutap.Audio</c>, qui depend de <c>Boutap.Core</c>. Core ne peut donc
/// pas l'appeler. Plutot que de deplacer le decodeur vers le haut — ou
/// d'inverser la fleche de dependance pour le besoin d'un test — on declare
/// l'interface ici et on l'implemente la-bas.
/// </para>
/// <para>
/// C'est aussi ce qui permet a <c>Core</c> de tester ses regles sans audio.
/// </para>
/// </remarks>
public interface IPackAudioProbe
{
    /// <summary>
    /// Lit l'audio en flux et dit ce qu'il contient. Le flux n'est pas consomme
    /// au-dela de ce qui est necessaire.
    /// </summary>
    /// <param name="audio">Flux des octets stockes dans le pack.</param>
    /// <param name="facts">Ce qui a ete mesure, si la lecture a reussi.</param>
    /// <returns>Vrai si l'audio a pu etre decode.</returns>
    bool TryInspect(Stream audio, out PackAudioFacts facts);
}
