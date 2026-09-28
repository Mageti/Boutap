// SPDX-FileCopyrightText: Boutap contributors
// SPDX-License-Identifier: AGPL-3.0-or-later
//
// La ligne de commande de boutap-gen. Le corps du programme tient dans
// GenProgram.Run, qui recoit ses deux flux : on teste donc la commande
// entiere sans lancer de processus.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Boutap.Audio;
using Boutap.Core.Pack;
using Boutap.Core.Tests;
using Boutap.Gen.Cli;
using Xunit;

namespace Boutap.Gen.Tests;

/// <summary>La ligne de commande de <c>boutap-gen</c>, commande par commande.</summary>
public sealed class CliTests : IDisposable
{
    private const string Hex = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
    private const string UpperHex = "0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF";
    private static readonly string[] AllLevels = ["berceau", "ronde", "cascade"];

    private readonly string directory = TestPaths.NewTemporaryDirectory();

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    // ---------------------------------------------------------------- outils

    /// <summary>Le code rendu, ce qui sort du programme et ce qui va sur erreur.</summary>
    private static (int Code, string Out, string Error) Invoke(params string[] args)
    {
        using var sortie = new StringWriter(CultureInfo.InvariantCulture);
        using var erreur = new StringWriter(CultureInfo.InvariantCulture);
        int code = GenProgram.Run(args, sortie, erreur);
        return (code, sortie.ToString(), erreur.ToString());
    }

    private static void Says(string haystack, string needle, string because)
    {
        Assert.True(
            haystack.Contains(needle, StringComparison.Ordinal),
            because + "\nattendu : « " + needle + " »\nobtenu :\n" + haystack);
    }

    private static void Silent(string haystack, string because)
    {
        Assert.True(haystack.Length == 0, because + "\nobtenu :\n" + haystack);
    }

    private static void Code(int obtained, int expected, string because)
    {
        Assert.True(obtained == expected, because + "\ncode obtenu : " + obtained);
    }

    private string Path_(string name) => System.IO.Path.Combine(directory, name);

    /// <summary>
    /// L'audio du pack de demonstration, pose sur le disque. Il n'y a pas
    /// d'encodeur WAV dans la bibliotheque, alors on recopie l'octet pour
    /// l'octet ce que le pack contient deja.
    /// </summary>
    private string DemoWav(string name = "demo.wav")
    {
        string target = Path_(name);
        File.WriteAllBytes(target, RawDemoAudio());
        return target;
    }

    private static byte[] RawDemoAudio()
    {
        using LoadedPack pack = PackReader.Read(TestPaths.DemoPackPath);
        string entry = pack.Manifest.Audio?.Path ?? throw new InvalidOperationException("Audio sans chemin.");
        using Stream raw = pack.OpenEntry(entry)
            ?? throw new InvalidOperationException("L'entree audio ne s'ouvre pas.");
        using var buffer = new MemoryStream();
        raw.CopyTo(buffer);
        return buffer.ToArray();
    }

    private static JsonNode Report((int Code, string Out, string Error) run)
    {
        string[] lines = run.Out.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Single(lines);
        return JsonNode.Parse(lines[0]) ?? throw new InvalidOperationException("Sortie JSON vide.");
    }

    private static JsonNode[] Items(JsonNode node, string name)
    {
        JsonArray array = node[name]?.AsArray()
            ?? throw new InvalidOperationException("« " + name + " » n'est pas un tableau.");
        return array.Select(item => item!).ToArray();
    }

    // ------------------------------------------------------------ GenProgram

    [Fact]
    public void Sans_argument_la_ligne_de_commande_donne_son_usage()
    {
        var run = Invoke();

        Code(run.Code, GenProgram.UsageError, "Sans argument, il n'y a rien a faire.");
        Says(run.Out, "Usage", "L'usage va sur la sortie normale.");
        Silent(run.Error, "Sans argument, ce n'est pas une erreur de saisie.");
    }

    [Fact]
    public void L_aide_demandee_reussit_et_recite_les_commandes()
    {
        var run = Invoke("--help");

        Code(run.Code, GenProgram.Success, "Demander l'aide, c'est reussir.");
        foreach (string command in AllLevels)
        {
            Says(run.Out, command, "L'usage doit nommer la commande.");
        }

        Says(run.Out, "generate", "L'usage doit nommer la commande qui ecrit.");
        Says(run.Out, "analyse", "L'usage doit nommer la commande qui mesure.");
        Says(run.Out, "--seed", "L'usage doit nommer les options.");
    }

    [Fact]
    public void Les_deux_ecritures_de_l_aide_donnent_le_meme_texte()
    {
        string avecTiret = Invoke("-h").Out;
        string avecMotCle = Invoke("help").Out;

        Assert.Equal(avecTiret, avecMotCle);
        Assert.True(avecTiret.Length > 0, "L'aide ne peut pas etre vide.");
    }

    [Fact]
    public void La_version_annonce_le_nom_le_numero_et_la_licence()
    {
        var run = Invoke("--version");

        Code(run.Code, GenProgram.Success, "Demander la version, c'est reussir.");
        Says(run.Out, "boutap-gen", "La version doit se nommer.");
        Says(run.Out, "0.1.0", "La version doit dire son numero.");
        Says(run.Out, "AGPL-3.0-or-later", "La version doit dire sous quelle licence.");
    }

    [Fact]
    public void Le_mot_cle_version_dit_la_meme_chose_que_l_option()
    {
        Assert.Equal(Invoke("--version").Out, Invoke("version").Out);
    }

    [Fact]
    public void Une_commande_inconnue_est_signalee_sur_erreur_et_laissera_la_sortie_vide()
    {
        var run = Invoke("bruit");

        Code(run.Code, GenProgram.UsageError, "Une commande qui n'existe pas est une erreur de saisie.");
        Says(run.Error, "Commande inconnue", "Le motif doit etre dit.");
        Says(run.Error, "« bruit »", "Le mot tape doit etre repris.");
        Says(run.Error, "Usage", "L'usage doit suivre sur la sortie d'erreur.");
        Silent(run.Out, "Une erreur d'usage n'a rien a dire sur la sortie normale.");
    }

    [Fact]
    public void Une_liste_vide_d_argument_est_refusee_non_ignoree()
    {
        Assert.Throws<ArgumentNullException>(() => GenProgram.Run(null!));
    }

    // --------------------------------------------------- options sans valeur

    [Fact]
    public void Un_niveau_sans_valeur_est_refuse_au_lieu_d_etre_ignore()
    {
        var run = Invoke("generate", "a.wav", "--level");

        Code(run.Code, GenProgram.UsageError, "Une option a-la-quelle il manque un mot doit etre refusee.");
        Says(run.Error, "--level attend une valeur", "Le message doit nommer l'option fautive.");
        Silent(run.Out, "Une erreur d'usage n'a rien a dire sur la sortie normale.");
    }

    [Fact]
    public void Une_graine_sans_valeur_est_refusee_au_lieu_d_etre_ignoree()
    {
        var run = Invoke("generate", "a.wav", "--seed");

        Code(run.Code, GenProgram.UsageError, "Une graine tiree au sort n'est pas une graine demandee.");
        Says(run.Error, "--seed attend une valeur", "Le message doit nommer l'option fautive.");
    }

    [Fact]
    public void Une_option_sans_valeur_ne_devore_pas_l_option_suivante()
    {
        var run = Invoke("generate", "a.wav", "--seed", "--json");

        Code(run.Code, GenProgram.UsageError, "« --json » n'est pas une graine, meme s'il ressemble a une valeur.");
        Says(run.Error, "--seed attend une valeur", "Le message doit nommer l'option fautive.");
        Silent(run.Out, "Une erreur de saisie n'a rien a dire sur la sortie normale.");
    }

    [Fact]
    public void Un_niveau_inconnu_est_une_erreur_de_saisie_et_non_un_plantage()
    {
        var run = Invoke("generate", "a.wav", "--level", "plateau");

        Code(run.Code, GenProgram.UsageError, "Un nom de niveau qu'on ne connait pas est une erreur de saisie.");
        Says(run.Error, "« plateau »", "Le mot tape doit etre repris.");
        Says(run.Error, "n'est pas un niveau", "Le message doit dire ce qui ne va pas.");
        Silent(run.Out, "Une erreur de saisie n'a rien a dire sur la sortie normale.");
    }

    [Fact]
    public void Une_graine_trop_courte_est_refusee()
    {
        var run = Invoke("analyse", "a.wav", "--seed", "0");

        Code(run.Code, GenProgram.UsageError, "Une graine trop courte n'est pas une graine.");
        Says(run.Error, "64", "Le message doit dire la taille attendue.");
        Silent(run.Out, "Une erreur de saisie n'a rien a dire sur la sortie normale.");
    }

    [Fact]
    public void Une_graine_en_majuscules_est_refusee()
    {
        var run = Invoke("analyse", "a.wav", "--seed", UpperHex);

        Code(run.Code, GenProgram.UsageError, "Les empreintes du format sont en minuscules, la graine aussi.");
        Says(run.Error, "64", "Le message doit dire la taille attendue.");
        Silent(run.Out, "Une erreur de saisie n'a rien a dire sur la sortie normale.");
    }

    [Fact]
    public void Une_graine_bien_ecrite_passe_meme_si_le_fichier_est_absent()
    {
        var run = Invoke("analyse", Path_("absent.wav"), "--seed", Hex);

        // C'est la graine qui a ete acceptee : sinon le code serait 2, celui de
        // la faute de saisie. L'erreur qui reste est celle du fichier absent,
        // et elle a droit au sien.
        Code(run.Code, GenProgram.Failure, "La graine est bonne, le fichier n'existe pas.");
        Says(run.Error, "absent.wav", "Le fichier manquant doit etre nomme.");
    }

    // ------------------------------------------------------- options inconnues

    [Fact]
    public void Une_option_inconnue_est_refusee_et_non_ignoree()
    {
        var run = Invoke("generate", DemoWav(), "-o", Path_("p.btp"), "--tempo", "100");

        Code(run.Code, GenProgram.UsageError, "« --tempo » n'existe pas, et une faute de frappe doit se voir.");
        Says(run.Error, "--tempo", "L'option fautive doit etre nommee, sinon on ne la corrigerait pas.");
        Says(run.Error, "--help", "Le message doit dire ou chercher la liste.");
        Assert.Empty(Directory.GetFiles(directory, "*.btp"));
    }

    [Fact]
    public void Une_option_inconnue_est_refusee_meme_si_el_tient_pour_une_option_existante()
    {
        // « --levl » n'est pas « --level ». La faute la plus courante est la
        // transposition, pas l'invention : il faut la rattraper aussi.
        var run = Invoke("analyse", DemoWav(), "--levl", "ronde");

        Code(run.Code, GenProgram.UsageError, "« --levl » n'est pas « --level ».");
        Says(run.Error, "--levl", "L'option fautive doit etre nommee telle qu'elle a ete ecrite.");
    }

    [Fact]
    public void Une_option_inconnue_est_refusee_sur_chaque_commande()
    {
        string[] commands = ["analyse", "generate", "levels"];

        foreach (string command in commands)
        {
            var run = Invoke(command, DemoWav(), "--color", "always");

            Code(run.Code, GenProgram.UsageError, command + " doit refuser une option qu'elle ne connait pas.");
            Says(run.Error, "--color", command + " doit nommer l'option refusee.");
        }
    }

    [Fact]
    public void La_cible_de_generate_ne_trompart_pas_la_lecture_des_options()
    {
        // -o et --output appartiennent a generate et a lui seul. Les tolérer
        // ici, sans les manger, est ce qui permet a generate de les relire.
        string target = Path_("cible.btp");
        var run = Invoke("generate", DemoWav(), "--output", target);

        Code(run.Code, GenProgram.Success, "--output est une vraie option de generate.");
        Assert.True(File.Exists(target), "Le pack n'a pas ete ecrit a l'adresse demandee.");
    }

    [Fact]
    public void La_cible_de_generate_ne_vaut_pas_pour_les_autres_commandes()
    {
        var run = Invoke("analyse", DemoWav(), "--output", Path_("cible.btp"));

        Code(run.Code, GenProgram.UsageError, "analyse n'ecrit rien : lui proposer une cible n'a pas de sens.");
        Says(run.Error, "--output", "L'option refusee doit etre nommee.");
    }

    [Fact]
    public void L_option_de_niveau_tient_toujours_et_prend_encore_une_seule_valeur()
    {
        var run = Invoke("analyse", DemoWav(), "--level", "ronde", "--level", "cascade", "--json");

        // Deux --level : la derniere gagne. C'est le comportement de la version
        // precedente, on ne le change pas au passage d'un correctif d'options.
        Code(run.Code, GenProgram.Success, "Deux niveaux demandes doivent rester acceptables.");
        JsonNode report = Report(run);
        JsonNode[] levels = Items(report, "levels");
        Assert.Single(levels);
        Says(levels[0]!.ToJsonString(), "cascade", "C'est le dernier niveau demande qui compte.");
    }

    [Fact]
    public void Un_tiret_seul_reste_un_positionnel_et_non_une_option()
    {
        // Aucun des trois chemins n'a de nom de fichier « - », mais « - »
        // n'est pas non plus une option : le refuser serait une faute de trop.
        var run = Invoke("analyse", Path_("absent.wav"), "-");

        Code(run.Code, GenProgram.Failure, "Le fichier manquant reste l'erreur la plus parlante.");
        Says(run.Error, "absent.wav", "Le premier positionnel reste le fichier a analyser.");
    }

    // ----------------------------------------------------------------- levels

    [Fact]
    public void Levels_donne_un_tableau_qui_explique_chaque_niveau()
    {
        var run = Invoke("levels");

        Code(run.Code, GenProgram.Success, "Levels ne fait qu'expliquer, il n'a rien a mesurer.");
        foreach (string level in AllLevels)
        {
            Says(run.Out, level, "Chaque niveau doit figurer dans le tableau.");
        }

        // Seize sous-cases par temps. Le berceau n'en garde qu'une, la ronde
        // quatre, la cascade huit : en clair, une note sur seize, une sur
        // quatre, une sur deux.
        foreach (string densite in new[] { "16", "4", "2" })
        {
            Says(run.Out, "une sur " + densite, "Chaque niveau doit dire sa densite en clair.");
        }

        Says(run.Out, "version alleg", "Le tableau doit dire ce qu'il ne fait pas.");
    }

    [Fact]
    public void Levels_en_json_donne_une_seule_ligne_de_json()
    {
        var run = Invoke("levels", "--json");

        Code(run.Code, GenProgram.Success, "Levels ne fait qu'expliquer, il n'a rien a mesurer.");
        string[] lines = run.Out.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Single(lines);

        JsonNode report = Report(run);
        Assert.Equal("boutap/gen-levels/1", report["schema"]!.GetValue<string>());
        Assert.Equal(16, report["subdivisions_per_beat"]!.GetValue<int>());
        Assert.Equal(12, report["pitch_classes"]!.GetValue<int>());
        Assert.Equal(3, Items(report, "levels").Length);
    }

    [Fact]
    public void Levels_en_json_donne_les_memes_densites_que_le_tableau()
    {
        IReadOnlyList<JsonNode?> reported = Items(Report(Invoke("levels", "--json")), "levels");

        string[] kept = reported.Select(level => level!["kept_subdivisions"]!.GetValue<int>().ToString(CultureInfo.InvariantCulture)).ToArray();

        Assert.Equal(AllLevels, reported.Select(level => level!["level"]!.GetValue<string>()).ToArray());
        Assert.Equal(["1", "4", "8"], kept);

        // La grille reste la meme pour les trois niveaux : sixteen sous-cases
        // par temps. Ce qui change, c'est le nombre qu'on en garde.
        Assert.All(reported, level => Assert.Equal(16, level!["subdivisions_per_beat"]!.GetValue<int>()));
    }

    [Fact]
    public void Levels_en_json_ne_ressort_pas_le_tableau_lisible()
    {
        string json = Invoke("levels", "--json").Out;
        string tableau = Invoke("levels").Out;

        // Le JSON se reconnait a son schéma, pas a son absence de texte.
        Says(json, "\"schema\"", "La sortie JSON doit se declarer.");
        foreach (string motif in new[] { "Niveau", "jouables", "une sur", "contre-temps" })
        {
            Assert.False(json.Contains(motif, StringComparison.Ordinal), "La sortie JSON ne doit pas contenir « " + motif + " ».");
        }

        Assert.True(tableau.Contains("une sur", StringComparison.Ordinal), "Le tableau, lui, dit des choses.");
    }

    // ---------------------------------------------------------------- analyse

    [Fact]
    public void Analyse_sans_fichier_donne_son_usage()
    {
        var run = Invoke("analyse");

        Code(run.Code, GenProgram.UsageError, "Sans fichier, analyse n'a rien a mesurer.");
        Says(run.Error, "boutap-gen analyse", "L'usage doit nommer la commande.");
    }

    [Fact]
    public void Analyse_d_un_fichier_absent_echoue_sans_laisser_de_trace()
    {
        var run = Invoke("analyse", Path_("absent.wav"));

        Code(run.Code, GenProgram.Failure, "Un fichier absent est un echec, pas une faute de saisie.");
        Says(run.Error, "absent.wav", "Le fichier manquant doit etre nomme.");
        Silent(run.Out, "Un echec n'a rien a dire sur la sortie normale.");
    }

    [Fact]
    public void Analyse_d_un_fichier_qui_n_est_pas_un_wav_echoue_sans_laisser_de_trace()
    {
        string path = Path_("faux.wav");
        File.WriteAllText(path, "ceci n'est pas du son");
        var run = Invoke("analyse", path);

        Code(run.Code, GenProgram.Failure, "Un fichier illisible est un echec, pas une faute de saisie.");
        Says(run.Error, "Lecture impossible", "Le message doit dire que la lecture a echoue.");
        Says(run.Error, "RIFF", "Le message doit dire ce qui manque dans le fichier.");
        Silent(run.Out, "Un echec n'a rien a dire sur la sortie normale.");
    }

    [Fact]
    public void Analyse_decrit_le_morceau_de_demonstration()
    {
        var run = Invoke("analyse", DemoWav());

        Code(run.Code, GenProgram.Success, "Le morceau de demonstration doit s'analyser.");
        Silent(run.Error, "Sans option de trace, rien ne doit partir sur la sortie d'erreur.");
        foreach (string champ in AllLevels)
        {
            Says(run.Out, champ, "Le rapport doit nommer chaque niveau.");
        }

        foreach (string champ in new[] { "Audio", "Empreinte", "Gapless", "Tempo", "Tonalite" })
        {
            Says(run.Out, champ + " ", "Le rapport doit porter le champ « " + champ + " ».");
        }

        double tempo = TempoOf(run.Out);
        Assert.True(tempo > 100.0 && tempo < 140.0, "Le morceau est a 120 BPM : lu " + tempo.ToString("F2", CultureInfo.InvariantCulture));
    }

    [Fact]
    public void Analyse_en_json_dit_le_meme_tempo_que_le_texte()
    {
        string wav = DemoWav();
        string texte = Invoke("analyse", wav).Out;
        JsonNode rapport = Report(Invoke("analyse", wav, "--json"));

        double attendu = rapport["tempo_bpm"]!.GetValue<double>();
        Assert.True(
            Math.Abs(attendu - TempoOf(texte)) < 0.005,
            "Les deux sorties doivent dire le meme tempo : " + attendu + " contre " + TempoOf(texte));
    }

    [Fact]
    public void Analyse_en_json_donne_le_schema_attendu_et_trois_niveaux()
    {
        JsonNode rapport = Report(Invoke("analyse", DemoWav(), "--json"));

        Assert.Equal("boutap/gen-analysis/1", rapport["schema"]!.GetValue<string>());
        Assert.Equal(22050, rapport["sample_rate"]!.GetValue<int>());
        Assert.Equal(20.0, rapport["duration_seconds"]!.GetValue<double>(), 3);
        string empreinte = rapport["audio_sha256"]!.GetValue<string>();
        Assert.Equal(64, empreinte.Length);
        Assert.True(
            empreinte.All(c => (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f')),
            "Une empreinte est de l'hexadecimal minuscule, 64 caracteres : " + empreinte);
        Assert.True(rapport["tempo_bpm"]!.GetValue<double>() > 100.0, "Le tempo doit etre plausible.");
        Assert.True(rapport["beat_count"]!.GetValue<int>() > 20, "Le morceau a quarante temps.");
        Assert.Equal(3, Items(rapport, "levels").Length);

        foreach (JsonNode? level in Items(rapport, "levels"))
        {
            Assert.Contains(level!["level"]!.GetValue<string>(), AllLevels);
            Assert.True(level!["notes"]!.GetValue<int>() > 0, "Chaque niveau a des notes.");
            Assert.InRange(level["peak_load"]!.GetValue<double>(), 0.0, 10.0);
            Assert.InRange(level["rating"]!.GetValue<int>(), 1, 20);
            Assert.True(level["removed"]!.GetValue<int>() >= 0, "Un compte ne peut pas etre negatif.");
            Assert.True(level["unreachable"]!.GetValue<int>() >= 0, "Un compte ne peut pas etre negatif.");
        }
    }

    [Fact]
    public void Une_option_de_niveau_n_change_que_la_ligne_du_niveau_demande()
    {
        string wav = DemoWav();
        JsonNode complet = Report(Invoke("analyse", wav, "--json"));
        JsonNode seul = Report(Invoke("analyse", wav, "--level", "ronde", "--json"));

        Assert.Equal(3, Items(complet, "levels").Length);
        Assert.Single(Items(seul, "levels"));
        Assert.Equal("ronde", Items(seul, "levels")[0]!["level"]!.GetValue<string>());
        Assert.Equal(
            complet["audio_sha256"]!.GetValue<string>(),
            seul["audio_sha256"]!.GetValue<string>());
        Assert.Equal(complet["tempo_bpm"]!.GetValue<double>(), seul["tempo_bpm"]!.GetValue<double>());
    }

    [Fact]
    public void Le_niveau_demande_apparait_meme_sans_option_json()
    {
        string wav = DemoWav();
        string complet = Invoke("analyse", wav).Out;
        string seul = Invoke("analyse", wav, "--level", "ronde").Out;

        foreach (string level in AllLevels)
        {
            Says(complet, level, "Sans option, le rapport porte les trois niveaux.");
        }

        Says(seul, "ronde", "Le niveau demande doit rester.");
        foreach (string autre in AllLevels.Where(level => level != "ronde"))
        {
            Assert.False(seul.Contains(autre, StringComparison.Ordinal),
                "Le rapport ne doit pas parler du niveau « " + autre + " ».");
        }
    }

    // --------------------------------------------------------------- generate

    [Fact]
    public void Generate_sans_fichier_donne_son_usage()
    {
        var run = Invoke("generate");

        Code(run.Code, GenProgram.UsageError, "Sans fichier, generate n'a rien a transformer.");
        Says(run.Error, "boutap-gen generate", "L'usage doit nommer la commande.");
    }

    [Fact]
    public void Generate_sans_cible_demande_une_cible_ou_la_simulation()
    {
        var run = Invoke("generate", DemoWav());

        Code(run.Code, GenProgram.UsageError, "Ecrire un pack la ou on ne sait pas, non.");
        Says(run.Error, "--dry-run", "Le message doit dire les deux sorties possibles.");
    }

    [Fact]
    public void Generate_en_simulation_ne_requiert_pas_de_cible_et_n_ecrit_rien()
    {
        var run = Invoke("generate", DemoWav(), "--dry-run");

        Code(run.Code, GenProgram.Success, "La simulation est une generation comme une autre.");
        Says(run.Out, "rien n'a ete ecrit", "La simulation doit dire qu'elle n'a rien ecrit.");
        Says(run.Out, "Graine du pack", "La simulation doit quand meme dire quelle graine elle aurait employe.");
        Assert.Empty(Directory.GetFiles(directory, "*.btp"));
    }

    [Fact]
    public void Generate_en_simulation_et_generate_pour_de_vrai_donnent_le_meme_pack()
    {
        string wav = DemoWav();
        string cible = Path_("ecrit.btp");
        JsonNode simule = Report(Invoke("generate", wav, "--dry-run", "--json"));
        JsonNode ecrit = Report(Invoke("generate", wav, "-o", cible, "--json"));

        Assert.False(simule["written"]!.GetValue<bool>(), "La simulation n'ecrit rien.");
        Assert.True(ecrit["written"]!.GetValue<bool>(), "La generation ecrit.");
        Assert.True(string.IsNullOrEmpty(simule["path"]?.GetValue<string>()), "La simulation n'a pas de chemin.");
        Assert.Equal(cible, ecrit["path"]?.GetValue<string>());
        Assert.Equal(simule["seed"]!.GetValue<ulong>(), ecrit["seed"]!.GetValue<ulong>());
        Assert.Equal(
            Items(simule, "charts").Select(chart => chart!["note_count"]!.GetValue<int>()).ToArray(),
            Items(ecrit, "charts").Select(chart => chart!["note_count"]!.GetValue<int>()).ToArray());
    }

    [Fact]
    public void Generate_ecrit_un_pack_que_le_validateur_accepte()
    {
        string cible = Path_("bon.btp");
        var run = Invoke("generate", DemoWav(), "-o", cible);

        Code(run.Code, GenProgram.Success, "Le morceau de demonstration doit se transformer.");
        Silent(run.Error, "Rien ne doit partir sur la sortie d'erreur.");
        Says(run.Out, cible, "La commande doit dire ou elle a ecrit.");
        Assert.True(File.Exists(cible), "Le pack doit exister.");

        using LoadedPack pack = PackReader.Read(cible);
        string[] codes = PackValidator.Validate(pack, new WavAudioProbe()).Issues
            .Where(issue => issue.IsError)
            .Select(issue => issue.Code)
            .ToArray();
        Assert.True(codes.Length == 0, "Le pack ecrit doit etre valide : " + string.Join(" | ", codes));
        Assert.Equal(3, pack.Manifest.Charts?.Count);
    }

    [Fact]
    public void Generate_ne_construit_que_le_niveau_demande()
    {
        string cible = Path_("ronde.btp");
        var (code, output, error) = Invoke("generate", DemoWav(), "-o", cible, "--level", "ronde");
        Code(code, GenProgram.Success, "Un seul niveau, une seule generation.");

        using LoadedPack pack = PackReader.Read(cible);
        Assert.Single(pack.Manifest.Charts ?? []);
        Assert.Equal(ChartLevel.Ronde, pack.Manifest.Charts?[0].Level);
        Assert.Equal("charts/ronde.json", pack.Manifest.Charts?[0].File);
    }

    [Fact]
    public void Deux_generations_identiques_donnent_le_meme_octet()
    {
        string wav = DemoWav();
        string premier = Path_("un.btp");
        string second = Path_("deux.btp");

        Invoke("generate", wav, "-o", premier);
        Invoke("generate", wav, "-o", second);

        // Le nom du fichier ne doit pas non plus entrer dans le pack, sinon
        // deux generations ne seraient pas reproductibles d'un dossier a l'autre.
        Assert.Equal(File.ReadAllBytes(premier), File.ReadAllBytes(second));
    }

    [Fact]
    public void Le_nom_du_fichier_devient_le_titre_et_son_dos_l_identifiant()
    {
        string cible = Path_("sortie.btp");
        Invoke("generate", DemoWav("Mes Morceaux.wav"), "-o", cible);

        using LoadedPack pack = PackReader.Read(cible);
        Assert.Equal("Mes Morceaux", pack.Manifest.Title);
        Assert.Equal("mes-morceaux", pack.Manifest.Id);
    }

    [Fact]
    public void Un_nom_trop_court_donne_un_identifiant_qui_ne_depend_pas_du_dossier()
    {
        // Un nom de deux caracteres ne laisse pas de quoi batir un
        // identifiant lisible : le repli entre alors en jeu. Il ne doit pas
        //dependre du dossier, sinon le meme fichier donnerait deux packs.
        string premier = Path_("ab.wav");
        string second = System.IO.Path.Combine(TestPaths.NewTemporaryDirectory(), "ab.wav");
        try
        {
            File.WriteAllBytes(premier, RawDemoAudio());
            File.WriteAllBytes(second, RawDemoAudio());

            Assert.NotEqual(System.IO.Path.GetFullPath(premier), System.IO.Path.GetFullPath(second));
            IdentifiersOf(premier, second);
        }
        finally
        {
            Directory.Delete(System.IO.Path.GetDirectoryName(second)!, recursive: true);
        }
    }

    [Fact]
    public void La_graine_imposee_change_les_notes_sans_bouger_l_empreinte()
    {
        string wav = DemoWav();
        JsonNode parDefaut = Report(Invoke("generate", wav, "-o", Path_("a.btp"), "--json"));
        JsonNode impose = Report(Invoke("generate", wav, "-o", Path_("b.btp"), "--seed", Hex, "--json"));

        Assert.Equal(Hex, impose["seed_material"]!.GetValue<string>());
        Assert.NotEqual(parDefaut["seed"]!.GetValue<ulong>(), impose["seed"]!.GetValue<ulong>());

        // La graine deplace les notes, elle n'en ajoute pas : les deux packs
        // ont le meme nombre de notes, mais pas les memes notes, donc pas les
        // memes octets. C'est ce qu'on voulait d'une variante.
        Assert.Equal(
            Items(parDefaut, "charts").Select(chart => chart!["note_count"]!.GetValue<int>()).ToArray(),
            Items(impose, "charts").Select(chart => chart!["note_count"]!.GetValue<int>()).ToArray());
        Assert.NotEqual(File.ReadAllBytes(Path_("a.btp")), File.ReadAllBytes(Path_("b.btp")));

        // L'empreinte, elle, dit quel audio a ete analyse : elle ne bouge pas.
        using LoadedPack a = PackReader.Read(Path_("a.btp"));
        using LoadedPack b = PackReader.Read(Path_("b.btp"));
        Assert.Equal(a.Manifest.Audio?.Sha256, b.Manifest.Audio?.Sha256);
        Assert.NotEqual(Hex, a.Manifest.Audio?.Sha256);
    }

    [Fact]
    public void Deux_graines_imposees_identiques_donnent_le_meme_pack()
    {
        string wav = DemoWav();
        string premier = Path_("g1.btp");
        string second = Path_("g2.btp");

        Invoke("generate", wav, "-o", premier, "--seed", Hex);
        Invoke("generate", wav, "-o", second, "--seed", Hex);

        Assert.Equal(File.ReadAllBytes(premier), File.ReadAllBytes(second));
    }

    [Fact]
    public void L_empreinte_que_le_rapport_depose_est_cellule_du_pack_ecrit()
    {
        string wav = DemoWav();
        Invoke("generate", wav, "-o", Path_("n.btp"));

        JsonNode rapport = Report(Invoke("analyse", wav, "--json"));
        using LoadedPack pack = PackReader.Read(Path_("n.btp"));

        // Les deux passeports sont calcules separement : l'un par la commande
        // d'analyse, l'autre par le validateur du pack. Ils doivent tomber
        // juste, sinon on ne saurait plus dire de quel audio on parle.
        Assert.Equal(rapport["audio_sha256"]!.GetValue<string>(), pack.Manifest.Audio?.Sha256);
    }

    // ------------------------------------------------------------- internes

    private static double TempoOf(string text)
    {
        foreach (string line in text.Split('\n'))
        {
            if (line.Contains("Tempo", StringComparison.Ordinal))
            {
                int debut = line.IndexOf(':');
                string[] morceaux = line[(debut + 1)..]
                    .Split(['B', 'P'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                return double.Parse(morceaux[0], CultureInfo.InvariantCulture);
            }
        }

        throw new InvalidOperationException("Aucune ligne de tempo dans :\n" + text);
    }

    private void IdentifiersOf(string first, string second)
    {
        string un = Path_("un.btp");
        string deux = Path_("deux.btp");
        Invoke("generate", first, "-o", un);
        Invoke("generate", second, "-o", deux);

        using LoadedPack a = PackReader.Read(un);
        using LoadedPack b = PackReader.Read(deux);
        string premierId = a.Manifest.Id ?? throw new InvalidOperationException("Pack sans identifiant.");
        Assert.Equal(premierId, b.Manifest.Id);
        Assert.True(premierId.StartsWith("pack-", StringComparison.Ordinal), "Le repli doit se reconnaitre : " + premierId);
    }
}
