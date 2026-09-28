// SPDX-FileCopyrightText: Boutap contributors
// SPDX-License-Identifier: AGPL-3.0-or-later
//
// Tests de la chaine complete : analyse, generation, ecriture du pack,
// et la promesse de reproductibilite octet pour octet.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Boutap.Core.Common;
using Boutap.Core.Determinism;
using Boutap.Core.Pack;
using Boutap.Gen.Compose;
using Xunit;

namespace Boutap.Gen.Tests;

/// <summary>La chaine complete, de l'audio brut au pack pret a distribuer.</summary>
public class PipelineTests
{
    private const int Rate = 22050;
    private const string Version = "0.1.0";
    private static readonly string Fingerprint = new('c', 64);

    /// <summary>
    /// Un train de clics a cent vingt temps par minute, construit sans
    /// generateur aleatoire : le signal de test doit lui-meme etre reproductible,
    /// sinon un echec de determinisme ne voudrait rien dire.
    /// </summary>
    private static double[] Clicks(int seconds, double bpm)
    {
        double[] signal = new double[seconds * Rate];
        int beatSamples = (int)Math.Round(Rate * 60.0 / bpm);
        for (int start = 0; start < signal.Length; start += beatSamples)
        {
            for (int offset = 0; offset < 1200 && start + offset < signal.Length; offset++)
            {
                double decay = Math.Exp(-offset / 300.0);
                signal[start + offset] += Math.Sin((2.0 * Math.PI * 900.0 * offset) / Rate) * decay * 0.6;
            }
        }

        return signal;
    }

    private static GenerationResult Generate(
        double[]? signal = null,
        IReadOnlyList<LevelProfile>? profiles = null)
    {
        double[] samples = signal ?? Clicks(seconds: 8, bpm: 120.0);
        return Pipeline.Run(samples, Rate, samples.Length / (double)Rate, Fingerprint, Version, profiles);
    }

    private static PackIdentity Identity() => new("essai", "Essai", string.Empty, "CC0-1.0", "audio.wav");

    private static byte[] Written(GenerationResult result)
    {
        PackContent pack = Pipeline.BuildPack(
            result, Identity(), [1, 2, 3, 4], GaplessTag.None, SemanticVersion.Current);
        using var stream = new MemoryStream();
        PackWriter.WriteTo(stream, pack);
        return stream.ToArray();
    }

    [Theory]
    [InlineData(1024)]
    [InlineData(2048)]
    public void Un_audio_echantillonne_trop_bas_est_refuse_avant_meme_lanalyse(int sampleRate)
    {
        // La transformee de Fourier de la fenetre d'analyse demande au moins
        // 2048 echantillons. Refuser ici plutot qu'apres l'analyse evite de
        // rapporter une erreur de fenetre alors que la cause est la frequence.
        double[] signal = new double[sampleRate * 3];

        ArgumentException error = Assert.Throws<ArgumentException>(
            () => Pipeline.Run(signal, sampleRate, 3.0, Fingerprint, Version));
        Assert.Equal("sampleRate", error.ParamName);
    }

    [Fact]
    public void Une_empreinte_absente_est_refusee()
    {
        double[] signal = Clicks(seconds: 3, bpm: 120.0);

        Assert.Throws<ArgumentException>(() => Pipeline.Run(signal, Rate, 3.0, string.Empty, Version));
    }

    [Fact]
    public void La_generation_ne_produit_que_les_niveaux_demandes()
    {
        GenerationResult result = Generate(profiles: [new LevelProfile(ChartLevel.Ronde, KeptSubdivisions: 4)]);

        ChartDraft only = Assert.Single(result.Drafts);
        Assert.Equal(ChartLevel.Ronde, only.Level);
    }

    /// <summary>
    /// Des clics sur trois sous-cases d'un temps : le temps, la double-croche
    /// et la crochee pointee. C'est la seule facon de distinguer les trois
    /// niveaux, parce qu'ils ne filtrent pas sur la position mais sur le
    /// <em>nombre</em> de subdivisions retenues.
    /// </summary>
    private static double[] Syncopated(int seconds, double bpm, int[] subdivisions)
    {
        double[] signal = new double[seconds * Rate];
        int beatSamples = (int)Math.Round(Rate * 60.0 / bpm);
        int subCaseSamples = beatSamples / 16;
        for (int beat = 0; beat * beatSamples < signal.Length; beat++)
        {
            foreach (int subdivision in subdivisions)
            {
                int start = (beat * beatSamples) + (subdivision * subCaseSamples);
                for (int offset = 0; offset < 1200 && start + offset < signal.Length; offset++)
                {
                    double decay = Math.Exp(-offset / 300.0);
                    signal[start + offset] += Math.Sin((2.0 * Math.PI * 900.0 * offset) / Rate) * decay * 0.6;
                }
            }
        }

        return signal;
    }

    [Fact]
    public void Les_trois_niveaux_vont_du_plus_simple_au_plus_dense()
    {
        // Trois sous-cases occupees par temps. Le bouton de densite garde une
        // sous-case sur seize en berceau, une sur quatre en ronde, une sur
        // deux en cascade : ce morceau, la, ne donne la meme chose qu'a un
        // niveau a la fois, et le niveau le plus dense doit vraiment etre plus
        // charge que le plus simple.
        GenerationResult result = Generate(signal: Syncopated(8, 120.0, [0, 2, 10]));
        int[] counts = [.. result.Drafts.Select(draft => draft.Notes.Count)];

        Assert.Equal(3, result.Drafts.Count);
        Assert.InRange(result.Track.Beats.TempoBpm, 110.0, 130.0);
        Assert.InRange(counts[0], 1, counts[1]);
        Assert.InRange(counts[1], 1, counts[2]);
        Assert.True(counts[2] > counts[0], $"Le niveau le plus dense doit ajouter des notes : {counts[0]}, {counts[1]}, {counts[2]}.");
    }

    [Fact]
    public void Un_morceau_sans_contretemps_donne_le_meme_chart_aux_trois_niveaux()
    {
        // Un morceau dont tous les departs sont sur le temps ne donne rien a
        // distinguer : les trois niveaux retiennent la meme sous-case et
        // produisent le meme nombre de notes. C'est une limite du generateur
        // sur un tel morceau, et la raison pour laquelle le seul fichier audio
        // du depot ne prouve rien sur la densite des niveaux.
        GenerationResult result = Generate(signal: Syncopated(8, 120.0, [0]));

        Assert.Single(result.Drafts.Select(draft => draft.Notes.Count).Distinct());
    }

    [Fact]
    public void Chaque_note_tient_dans_les_bornes_du_format()
    {
        GenerationResult result = Generate();

        foreach (ChartDraft draft in result.Drafts)
        {
            Assert.NotEmpty(draft.Notes);
            foreach (Note note in draft.Notes)
            {
                Assert.InRange(note.ForceOrDefault, 0, PackFormat.DefaultForce);
                Assert.InRange(note.WindowScaleOrDefault, 0.5, 3.0);
                Assert.Null(note.Duration);
                Assert.True(note.Time >= 0, $"Une note ne peut pas preceder le debut de l'audio : {note.Time}.");
            }
        }
    }

    [Fact]
    public void Le_generateur_ne_produit_que_des_frappes()
    {
        // Le generateur ne sait pas ecrire de tenu : une note tenue demande de
        // savoir quelle main la maintient, ce que l'analyse audio ignore.
        GenerationResult result = Generate();

        Assert.All(result.Drafts.SelectMany(draft => draft.Notes), note => Assert.False(note.IsHold));
    }

    [Fact]
    public void Un_audio_sans_onset_ne_produit_aucune_note()
    {
        GenerationResult result = Generate(signal: new double[Rate * 8]);

        Assert.All(result.Drafts, draft => Assert.Empty(draft.Notes));
    }

    [Fact]
    public void Le_manifeste_annonce_exactement_ce_que_les_charts_contiennent()
    {
        GenerationResult result = Generate();
        PackContent pack = Pipeline.BuildPack(
            result, Identity(), [1, 2, 3, 4], GaplessTag.None, SemanticVersion.Current);

        Assert.Equal(result.Drafts.Count, pack.Manifest.Charts!.Count);
        for (int index = 0; index < pack.Charts.Count; index++)
        {
            Chart chart = pack.Charts[index];
            ChartEntry entry = pack.Manifest.Charts![index];
            Assert.Equal(entry.Level, chart.Level!.Value);
            Assert.Equal(PackFormat.ChartFileName(entry.Level!.Value), entry.File);
            Assert.Equal(chart.NoteCount, entry.NoteCount);
            Assert.Equal(PackFormat.ChartSchema, chart.Schema);
            Assert.Equal(Fingerprint, chart.AudioSha256);
        }
    }

    [Fact]
    public void Les_graines_inscrites_viennent_de_l_empreinte_du_niveau_et_de_la_version()
    {
        GenerationResult result = Generate();
        PackContent pack = Pipeline.BuildPack(
            result, Identity(), [1, 2, 3, 4], GaplessTag.None, SemanticVersion.Current);

        // Trois graines pour un meme morceau : celle du pack, et celle de chaque
        // niveau. Sans la graine de niveau, deux niveaux se ressembleraient.
        Assert.Equal(SeedDerivation.PackSeedValue(Fingerprint, Version), pack.Manifest.Generator!.Seed);
        Assert.Equal(ChartGenerator.Name, pack.Manifest.Generator!.Name);
        foreach (Chart chart in pack.Charts)
        {
            Assert.Equal(
                SeedDerivation.ChartSeedValue(Fingerprint, Version, chart.Level!.Value.FileNameOf()),
                chart.Generator!.Seed);
            Assert.NotEqual(pack.Manifest.Generator.Seed, chart.Generator!.Seed);
        }
    }

    [Fact]
    public void Le_manifeste_reporte_le_tempo_et_la_tonalite_trouves_par_lanalyse()
    {
        GenerationResult result = Generate();
        PackContent pack = Pipeline.BuildPack(
            result, Identity(), [1, 2, 3, 4], GaplessTag.None, SemanticVersion.Current);

        double expected = Math.Round(result.Track.Beats.TempoBpm, 3, MidpointRounding.AwayFromZero);
        Assert.Equal(expected, pack.Manifest.Analysis!.Bpm!.Value, 3);
        Assert.Equal(result.Track.Key?.Key, pack.Manifest.Analysis!.Key);
        Assert.Equal(8.0, pack.Manifest.Analysis!.DurationSeconds!.Value, 6);
    }

    [Fact]
    public void Deux_generations_identiques_donnent_le_meme_pack_octet_pour_octet()
    {
        // C'est la promesse centrale du format : rejouer la meme graine sur le
        // meme audio redonne le meme fichier, compressions et horodatages
        // compris. Toute horloge, tout nom de fichier ou tout aleatoire mal
        // place ferait echouer ce test.
        byte[] first = Written(Generate());
        byte[] second = Written(Generate());

        Assert.NotEmpty(first);
        Assert.Equal(
            Convert.ToBase64String(first),
            Convert.ToBase64String(second));
    }

    [Fact]
    public void Une_autre_empreinte_donne_un_autre_pack()
    {
        // Le contre-test : si les deux packs etaient egaux, le test de
        // determinisme ci-dessus ne prouverait rien du tout.
        byte[] first = Written(Generate());
        GenerationResult other = Pipeline.Run(Clicks(seconds: 8, bpm: 120.0), Rate, 8.0, new string('d', 64), Version);
        byte[] second = Written(other);

        Assert.NotEqual(Convert.ToBase64String(first), Convert.ToBase64String(second));
    }

    [Fact]
    public void Une_partition_vide_vaut_une_difficulte_minimale()
    {
        ChartDraft empty = new(
            ChartLevel.Berceau, [], [], [], StrainCurve.Compute([]));

        Assert.Equal(1, Pipeline.DifficultyRating(empty));
    }

    [Fact]
    public void La_difficulte_reste_dans_l_echelle_du_format()
    {
        GenerationResult result = Generate();

        Assert.All(result.Drafts, draft => Assert.InRange(Pipeline.DifficultyRating(draft), 1, 20));
    }

    /// <summary>
    /// Le terme de rafale pese un quart de la note, et il doit donc pouvoir
    /// atteindre cette part. Il ne le pouvait pas : la rafale se comptait sur
    /// 100 ms, fenetre que la regle de lisibilite L5 borne a quatre notes, et
    /// le resultat etait divise par 32. Le terme plafonnait donc a 4/32, soit
    /// 3,1 % de la note finale, et les 96,9 % restants ne disaient rien du
    /// tout. Deux de ces partitions, mesurees :
    /// </summary>
    [Fact]
    public void Une_rafale_dense_rend_le_morceau_plus_difficile_que_les_memes_notes_reparties()
    {
        // Seize notes, dont quatre a cinquante millisecondes les une des
        // autres tous les trois secondes : c'est le plafond de L5, quatre
        // notes dans 100 ms, et rien de plus.
        List<double> dense = [];
        for (int group = 0; group < 4; group++)
        {
            for (int inside = 0; inside < 4; inside++)
            {
                dense.Add((group * 3.0) + (inside * 0.05));
            }
        }

        ChartDraft bunched = Draft(dense);
        ChartDraft spread = Draft([.. Enumerable.Range(0, 16).Select(index => index * 0.8)]);

        // Quatre notes dans la meme seconde contre deux. Mesure : 7 sur 20
        // pour la premiere, 4 sur 20 pour la seconde. L'ancien calcul, sur
        // 100 ms et divise par 32, voyait 3 et 1 : il ne distinguait presque
        // rien, et son plafond de 3,1 % rendait l'ecart invisible.
        Assert.Equal(4, Pipeline.DifficultyRating(spread));
        Assert.Equal(7, Pipeline.DifficultyRating(bunched));
    }

    /// <summary>
    /// Le meme nombre de notes, mais une par temps a 120 BPM, doit rester
    /// un morceau facile : c'est le morceau de reference du dépôt, et le
    /// generateur en sort 33 notes a la note 5 sur 20.
    /// </summary>
    [Fact]
    public void Une_note_par_temps_reste_un_morceau_facile()
    {
        ChartDraft steady = Draft([.. Enumerable.Range(0, 38).Select(index => index * 0.5)]);

        Assert.Equal(5, Pipeline.DifficultyRating(steady));
    }

    /// <summary>
    /// Le terme de rafale doit encore s'etcher sur toute l'echelle de la
    /// note, pas seulement au plancher. Neuf notes dans la meme seconde
    /// sont injouables, et la note doit le dire : 16 sur 20.
    /// </summary>
    [Fact]
    public void Une_seconde_entierement_remplie_sature_le_terminal_de_rafale()
    {
        ChartDraft wall = Draft([.. Enumerable.Range(0, 9).Select(index => index * 0.1)]);

        Assert.Equal(16, Pipeline.DifficultyRating(wall));
    }

    private static ChartDraft Draft(IReadOnlyList<double> times)
    {
        List<Note> notes = times
            .Select((time, index) => Note.Tap(time, KeyBinding.Grid(index % 9)))
            .ToList();

        return new ChartDraft(ChartLevel.Ronde, notes, [], [], StrainCurve.Compute(notes));
    }

    [Fact]
    public void Le_pic_de_notes_par_seconde_compte_la_seconde_la_plus_dense()
    {
        // Quatre notes dans la premiere seconde, une dans la troisieme : la
        // densite annoncee est celle de la pire seconde, pas la moyenne.
        Note[] notes =
        [
            Note.Tap(0.0, KeyBinding.Grid(0)),
            Note.Tap(0.2, KeyBinding.Grid(1)),
            Note.Tap(0.4, KeyBinding.Grid(2)),
            Note.Tap(0.6, KeyBinding.Grid(3)),
            Note.Tap(2.5, KeyBinding.Grid(4)),
        ];

        Assert.Equal(4.0, Pipeline.PeakNotesPerSecond(notes), 9);
        Assert.Equal(0.0, Pipeline.PeakNotesPerSecond(Array.Empty<Note>()), 9);
    }

    [Fact]
    public void La_charge_de_pointe_pese_les_notes_par_leur_force()
    {
        // Trois frappes a pleine force dans la meme fenetre de cent
        // millisecondes valent six poids bruts, ramenes a 0,6 dans l'echelle
        // du manifeste, ou dix est la limite.
        Note[] notes =
        [
            Note.Tap(0.0, KeyBinding.Grid(0)) with { Force = PackFormat.DefaultForce },
            Note.Tap(0.0, KeyBinding.Grid(1)) with { Force = PackFormat.DefaultForce },
            Note.Tap(0.05, KeyBinding.Grid(2)) with { Force = PackFormat.DefaultForce },
        ];

        Assert.Equal(0.6, Pipeline.PeakLoad(notes), 9);
    }

    [Fact]
    public void Une_note_isolee_ne_charge_presque_rien()
    {
        Note[] notes = [Note.Tap(0.0, KeyBinding.Grid(0)) with { Force = PackFormat.DefaultForce }];

        Assert.Equal(0.1, Pipeline.PeakLoad(notes), 9);
        Assert.Equal(0.0, Pipeline.PeakLoad(Array.Empty<Note>()), 9);
    }

    [Fact]
    public void La_fenetre_de_jugement_se_multiplie_sans_changer_de_theorie()
    {
        // Le generateur ne connait pas la fenetre de base : c'est l'ecran qui
        // la fixe. Le chart ne porte qu'un facteur, jamais une duree.
        Assert.Equal(0.33, ChartGenerator.RescaleWindow(0.22, 1.5), 9);
        Assert.Equal(0.22, ChartGenerator.RescaleWindow(0.22, 1.0), 9);
    }

    [Fact]
    public void Un_chart_herite_du_schema_et_du_niveau_du_projet()
    {
        GenerationResult result = Generate();
        ChartDraft draft = result.Drafts[1];

        Chart chart = ChartGenerator.ToChart(draft, Fingerprint, Version, offsetSeconds: 0.25);

        Assert.Equal(PackFormat.ChartSchema, chart.Schema);
        Assert.Equal(draft.Level, chart.Level!.Value);
        Assert.Equal(0.25, chart.OffsetSeconds!.Value, 9);
        Assert.Equal(draft.Notes, chart.Notes);
    }
}
