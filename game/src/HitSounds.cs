// SPDX-FileCopyrightText: Boutap contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Boutap.Game.Judgement;
using Godot;

namespace Boutap.Shell;

/// <summary>
/// Les sons de resultat, fabriques a la volee.
/// </summary>
/// <remarks>
/// <para>
/// Aucun asset sonore n'est embarque : le depot n'heberge aucun binaire, et un
/// jeu de rythme sans retour sonore est un jeu ou l'on ne sait pas si son geste
/// a compte. Un clic synthetise de cinquante millisecondes coute trois kilo
/// octets de code et repond au besoin.
/// </para>
/// <para>
/// Le son est synthetise en flottants 16 bits signes, petit-boutiste, ce qui
/// leve toute ambiguete sur le centrage du zero. Le resultat est mis en cache :
/// <see cref="AudioStreamWav"/> decode son tampon une fois a la lecture, et le
/// refabriquer a chaque coup ferait ramer la boucle de jeu.
/// </para>
/// </remarks>
public static class HitSounds
{
    private const int SampleRate = 44100;

    /// <summary>Duree du clic, en secondes.</summary>
    private const double Duration = 0.05;

    private readonly record struct Tone(double Frequency, double Level);

    private static readonly Tone Perfect = new(1320.0, 0.32);
    private static readonly Tone Great = new(1050.0, 0.28);
    private static readonly Tone Good = new(820.0, 0.24);
    private static readonly Tone Missed = new(180.0, 0.20);

    private readonly record struct Voice(Tone Tone, AudioStreamWav Stream);

    private static readonly Dictionary<JudgementGrade, Voice> Voices = new();

    /// <summary>Le son d'un coup juge, fabrique au premier appel.</summary>
    /// <param name="grade">Le grade, pour choisir la hauteur.</param>
    /// <returns>Le flux, reutilisable.</returns>
    public static AudioStreamWav Of(JudgementGrade grade)
    {
        if (Voices.TryGetValue(grade, out Voice voice))
        {
            return voice.Stream;
        }

        Tone tone = grade switch
        {
            JudgementGrade.Perfect => Perfect,
            JudgementGrade.Great => Great,
            JudgementGrade.Good => Good,
            _ => Missed,
        };

        AudioStreamWav built = Build(tone);
        Voices[grade] = new Voice(tone, built);
        return built;
    }

    /// <summary>Vide le cache. Sert aux tests de la coque.</summary>
    public static void Reset() => Voices.Clear();

    private static AudioStreamWav Build(Tone tone)
    {
        int count = (int)(SampleRate * Duration);
        var data = new byte[count * 2];

        // Deux partiels : la fondamentale et une quinte, c'est ce qui donne au
        // clic un timbre reconnaissable plutot qu'un bip de test.
        double partial = tone.Frequency;
        for (int index = 0; index < count; index++)
        {
            double t = index / (double)SampleRate;
            double envelope = Math.Exp(-t * 80.0);
            double value = (Math.Sin(2 * Math.PI * partial * t)
                + (0.35 * Math.Sin(2 * Math.PI * partial * 1.5 * t))) * envelope * tone.Level;
            if (value > 1)
            {
                value = 1;
            }
            else if (value < -1)
            {
                value = -1;
            }

            short sample = (short)Math.Round(value * 32000);
            data[index * 2] = (byte)(sample & 0xFF);
            data[(index * 2) + 1] = (byte)((sample >> 8) & 0xFF);
        }

        return new AudioStreamWav
        {
            Data = data,
            Format = AudioStreamWav.FormatEnum.Format16Bits,
            MixRate = SampleRate,
            Stereo = false,
        };
    }
}
