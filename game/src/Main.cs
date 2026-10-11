// SPDX-FileCopyrightText: Boutap contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Boutap.Audio;
using Boutap.Core.Pack;
using Boutap.Game.Judgement;
using Boutap.Game.Session;
using Godot;

namespace Boutap.Shell;

/// <summary>
/// La coque : elle branche le son, l'horloge, le chart et les touches.
/// </summary>
/// <remarks>
/// <para>
/// Ce script ne contient aucune regle de jeu. Il ouvre un paquet, demande a
/// <see cref="AudioClock"/> l'instant audible, le donne a
/// <see cref="GameSession"/>, et dessine ce qu'elle lui repond. C'est le sens de
/// la regle 5 de la spec : tout ce qui s'arbitre doit etre testable sans
/// fenetre, et tout ce qui s'affiche doit etre remplacable sans changer le jeu.
/// </para>
/// <para>
/// Le temps de jeu vient de l'audio, jamais de <c>_Process</c>. Une image peut
/// durer cinquante millisecondes apres un ramasse-miettes, ou six sur un ecran a
/// cent soixante hertz : si le jugement se reglait la-dessus, la meme partition
/// serait jouable sur une machine et pas sur l'autre. La boucle ne sert donc
/// qu'a demander l'heure et a repeindre.
/// </para>
/// </remarks>
public partial class Main : Control
{
    private const double JudgementDisplaySeconds = 0.75;
    private const double MissFlashSeconds = 0.18;

    private LoadedPack? _pack;
    private GameSession? _session;
    private AudioStreamPlayer? _music;
    private AudioStreamPlayer? _effects;
    private AudioClock? _clock;
    private RhythmAudioSource? _source;
    private NoteField? _field;

    private Label? _scoreLabel;
    private Label? _comboLabel;
    private Label? _judgementLabel;
    private Label? _titleLabel;
    private Label? _summaryLabel;
    private ColorRect? _progressFill;
    private ColorRect? _missFlash;
    private SongList? _list;
    private string _libraryDirectory = string.Empty;

    private IReadOnlyDictionary<Key, KeyBinding>? _bindings;
    private readonly Dictionary<Key, bool> _held = new();

    private ChartLevel _level = ChartLevel.Ronde;
    private string _packTitle = string.Empty;
    private double _judgementAge = JudgementDisplaySeconds;
    private double _missAge = MissFlashSeconds;

    /// <inheritdoc/>
    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;

        _field = new NoteField
        {
            MouseFilter = MouseFilterEnum.Ignore,
        };
        AddChild(_field);

        BuildHud();

        _bindings = LoadBindings();
        _effects = new AudioStreamPlayer { VolumeDb = -6 };
        AddChild(_effects);

        _libraryDirectory = ProjectSettings.GlobalizePath("user://songs");
        _list = new SongList { MouseFilter = MouseFilterEnum.Ignore };
        _list.SongChosen += OnSongChosen;
        _list.RefreshRequested += ReloadLibrary;
        AddChild(_list);

        ReloadLibrary();

        // Un paquet donne sur la ligne de commande se joue tout de suite : c'est
        // la voie du developpeur, et celle de « je veux juste essayer ce fichier ».
        // Sinon la liste s'ouvre, et le joueur choisit.
        string? immediate = FindPack();
        if (immediate is not null && PlayPack(immediate))
        {
            return;
        }

        ShowList();
    }

    /// <summary>Un morceau choisi dans la liste. Le resultat est jete :</summary>
    /// <remarks>
    /// Un signal Godot ne rend rien, et PlayPack est aussi appelee depuis la
    /// ligne de commande, ou le retour compte. L'echec est deja affiche par
    /// <see cref="Fail"/>, la liste reste donc derriere pour choisir autre
    /// chose — c'est le comportement voulu, pas une erreur avalee.
    /// </remarks>
    private void OnSongChosen(string path)
    {
        PlayPack(path);
    }

    /// <summary>Relit le dossier des morceaux.</summary>
    private void ReloadLibrary()
    {
        _list?.Reload(_libraryDirectory);
        SeedFirstRun();
    }

    /// <summary>
    /// Au premier lancement, la bibliotheque offre le morceau de demonstration.
    /// </summary>
    /// <remarks>
    /// Une bibliotheque vide ne dit pas a quoi sert le jeu : on voit une liste
    /// sans rien dedans, et rien n'explique ou mettre ses morceaux. La demo du
    /// depot est donc copiee dans <c>user://songs/</c>, mais seulement quand on
    /// tourne depuis le depot : une build livree n'en a pas et reste vide, sans
    /// mentir sur ce qu'elle contient.
    /// </remarks>
    private void SeedFirstRun()
    {
        if (_list is null || _list.PlayableCount > 0)
        {
            return;
        }

        string? root = RepositoryRoot();
        if (root is null)
        {
            return;
        }

        string source = Path.Combine(root, "tests", "data", "demo.btp");
        if (!File.Exists(source))
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(_libraryDirectory);
            File.Copy(source, Path.Combine(_libraryDirectory, "demo.btp"));
        }
        catch (IOException)
        {
            // Dossier en lecture seule, permissions, disque plein : la liste
            // reste vide et le dira. Rien d'autre a tenter ici.
            return;
        }

        _list.Reload(_libraryDirectory);
    }

    /// <summary>Affiche la liste et arrete le morceau en cours.</summary>
    private void ShowList()
    {
        if (_music is not null)
        {
            _music.Stop();
        }

        _session = null;
        _field?.Clear();
        _list?.ShowList();
        if (_titleLabel is not null)
        {
            _titleLabel.Text = "Boutap";
        }

        if (_summaryLabel is not null)
        {
            _summaryLabel.Visible = false;
        }

        if (_missFlash is not null)
        {
            _missFlash.Visible = false;
        }
    }

    /// <summary>Ouvre un paquet et demarre le niveau courant.</summary>
    /// <returns>Vrai si le paquet a pu etre ouvert et joue.</returns>
    private bool PlayPack(string path)
    {
        _pack?.Dispose();
        _pack = null;

        LoadedPack? loaded = null;
        try
        {
            loaded = PackReader.Read(path);
        }
        catch (PackFormatException error)
        {
            Fail($"Paquet illisible : {error.Message}");
            return false;
        }
        catch (IOException error)
        {
            Fail($"Paquet inaccessible : {error.Message}");
            return false;
        }

        _pack = loaded;
        _packTitle = loaded.Manifest.Title is { Length: > 0 } title
            ? title
            : Path.GetFileNameWithoutExtension(path);

        if (_list is not null)
        {
            _list.HideList();
        }

        if (!StartMusic())
        {
            Fail("L'audio du paquet n'a pas pu etre decode.");
            return false;
        }

        StartLevel(_level);
        return true;
    }

    /// <inheritdoc/>
    public override void _Process(double delta)
    {
        if (_session is null || _clock is null || _field is null)
        {
            return;
        }

        // L'horloge se resynchronise a chaque image : la position rapportee par
        // l'hote est plus sure que notre extrapolation, et elle ne coute rien
        // de plus depuis que la latence de sortie est lue dans la source.
        _clock.Resync();
        double now = _clock.JudgementTimeSeconds;

        _session.Update(now);
        ConsumeEvents();

        _judgementAge += delta;
        _missAge += delta;
        PaintHud(now);

        _field.Advance(_session, now, delta);
    }

    /// <inheritdoc/>
    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is not InputEventKey key)
        {
            return;
        }

        if (key.Pressed && !key.Echo)
        {
            switch (key.Keycode)
            {
                case Key.Escape:
                    // Echap revient a la liste depuis une partie, et quitte
                    // depuis la liste : sans ca, rejouer un autre morceau
                    // imposait de relancer le jeu.
                    if (_list is not null && _list.Visible)
                    {
                        GetTree().Quit();
                        return;
                    }

                    ShowList();
                    GetViewport().SetInputAsHandled();
                    return;
                case Key.R:
                    StartLevel(_level);
                    return;
                case Key.F1:
                    StartLevel(ChartLevel.Berceau);
                    return;
                case Key.F2:
                    StartLevel(ChartLevel.Ronde);
                    return;
                case Key.F3:
                    StartLevel(ChartLevel.Cascade);
                    return;
                default:
                    break;
            }
        }

        if (_bindings is null || !_bindings.TryGetValue(key.Keycode, out KeyBinding binding))
        {
            return;
        }

        if (_session is null)
        {
            return;
        }

        if (key.Pressed)
        {
            if (_held.ContainsKey(key.Keycode))
            {
                // Un clavier qui repete son evenement transformerait un seul
                // geste en rafale. La session s'en protege deja ; ce garde
                // evite en plus de repeindre pour rien.
                return;
            }

            _held[key.Keycode] = true;
            HitOutcome outcome = _session.Press(binding, _clock?.JudgementTimeSeconds ?? 0);
            _field?.Flash(KeyMap.SlotOf(binding), outcome.Grade, outcome.Assisted);
            return;
        }

        _held.Remove(key.Keycode);
        _session.Release(binding, _clock?.JudgementTimeSeconds ?? 0);
    }

    private IReadOnlyDictionary<Key, KeyBinding>? LoadBindings()
    {
        string? fromProfile = FindProfile();
        IReadOnlyDictionary<Key, KeyBinding>? loaded = fromProfile is null
            ? null
            : ProfileKeys.TryLoad(fromProfile);

        if (loaded is not null)
        {
            GD.Print($"Profil de touches : {fromProfile}");
            return loaded;
        }

        GD.Print("Profil introuvable, disposition par defaut.");
        return ProfileKeys.BuiltIn();
    }

    /// <summary>Cherche le profil dans les endroits ou il peut etre pose.</summary>
    /// <remarks>
    /// <c>res://</c> ne voit rien hors du paquet, et un profil exporte n'y est
    /// pas encore embarque (slice A4). On regarde donc dans le dossier
    /// utilisateur, puis dans le depot, pour qu'une partie lancee depuis
    /// l'editeur fonctionne aussi.
    /// </remarks>
    private string? FindProfile()
    {
        var candidates = new List<string>
        {
            Path.Combine(
                ProjectSettings.GlobalizePath("user://"),
                "profiles",
                $"{ProfileKeys.DefaultProfileName}.json"),
        };

        string? root = RepositoryRoot();
        if (root is not null)
        {
            candidates.Add(Path.Combine(root, "profiles", $"{ProfileKeys.DefaultProfileName}.json"));
        }

        foreach (string candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    /// <summary>Cherche le paquet a jouer.</summary>
    /// <summary>
    /// Cherche un paquet nomme explicitement sur la ligne de commande.
    /// </summary>
    /// <remarks>
    /// Ce n'est PAS la que le morceau de demonstration est choisi : le trouver
    /// ici ferait jouer la demo a chaque lancement, et la liste ne servirait a
    /// rien. La demo alimente la bibliotheque au premier lancement, elle ne
    /// s'impose pas.
    /// </remarks>
    private static string? FindPack()
    {
        foreach (string argument in OS.GetCmdlineUserArgs())
        {
            if (File.Exists(argument))
            {
                return argument;
            }
        }

        return null;
    }

    /// <summary>Racine du depot, quand la coque est lancee depuis le projet Godot.</summary>
    private static string? RepositoryRoot()
    {
        try
        {
            return Path.GetFullPath(Path.Combine(ProjectSettings.GlobalizePath("res://"), ".."));
        }
        catch (IOException)
        {
            return null;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    /// <summary>Ouvre l'audio du paquet et demarre la lecture.</summary>
    /// <remarks>
    /// L'audio est decode par Godot, conformement a la decision A0 : le paquet
    /// livre ne contient que du WAV, mais un Ogg Vorbis ou un MP3 passera aussi.
    /// Opus et Ogg-Theora restent hors de portee du moteur, ce qui garde a
    /// miniaudio son role du cote de <c>boutap-gen</c>.
    /// <para>
    /// La lecture demarre a zero. Les instants du chart sont absolus dans la
    /// piste, donc ils s'alignent tout seuls ; <c>pre_skip_seconds</c> et
    /// <c>offset_seconds</c> ne sont pas encore honoraires, et la coque le dit
    /// quand ils ne sont pas nuls plutot que de decaler en silence.
    /// </para>
    /// </remarks>
    private bool StartMusic()
    {
        if (_pack is null)
        {
            return false;
        }

        string path = _pack.Manifest.Audio?.Path ?? "audio.wav";
        byte[]? bytes = null;
        try
        {
            if (_pack.OpenEntry(path) is Stream stream)
            {
                using var buffer = new MemoryStream();
                stream.CopyTo(buffer);
                bytes = buffer.ToArray();
            }
        }
        catch (IOException error)
        {
            Fail($"Audio du paquet illisible ({path}) : {error.Message}");
            return false;
        }

        if (bytes is null || bytes.Length == 0)
        {
            Fail($"Le paquet ne contient pas d'audio a « {path} ».");
            return false;
        }

        // Godot 4.3 n'a pas de AudioStreamWav.LoadFromBuffer et pas de
        // PackedByteArray : le flux se construit directement, et Data est un
        // simple tableau d'octets. C'est la version minimaliste de la meme
        // idee — l'audio vit en memoire, jamais sur le disque du joueur.
        var decoded = new AudioStreamWav
        {
            Data = bytes,
            Format = AudioStreamWav.FormatEnum.Format16Bits,
            MixRate = 22050,
            Stereo = false,
        };

        WarnIfUnaligned();

        _music = new AudioStreamPlayer { Stream = decoded };
        AddChild(_music);
        _music.Play();

        // L'horloge se construit apres le demarrage de la lecture : la source
        // prend sa premiere ancre sur la position deja en cours. On garde la
        // source en main pour pouvoir la remettre a zero, ce que fait la touche
        // R et le changement de niveau.
        _source = new RhythmAudioSource(_music);
        _clock = new AudioClock(_source);
        return true;
    }

    /// <summary>Signale un paquet dont l'alignement n'est pas encore gere.</summary>
    private void WarnIfUnaligned()
    {
        if (_pack is null)
        {
            return;
        }

        double skip = _pack.Manifest.Audio?.PreSkipSeconds ?? 0;
        if (Math.Abs(skip) > 0.0005)
        {
            GD.PrintErr($"pre_skip_seconds = {skip} : la coque joue depuis zero, l'alignement n'en tient pas encore compte.");
        }
    }

    private void StartLevel(ChartLevel level)
    {
        _level = level;
        if (_pack is null)
        {
            return;
        }

        LoadedChart? found = null;
        foreach (LoadedChart candidate in _pack.Charts)
        {
            if (candidate.Chart is not null && (candidate.Chart.Level ?? ChartLevel.Ronde) == level)
            {
                found = candidate;
                break;
            }
        }

        if (found?.Chart is null)
        {
            Fail($"Le paquet n'a pas de niveau « {level.FileNameOf()} ».");
            return;
        }

        Chart chart = found.Chart;

        // Le morceau repart du debut avant que la partie ne prenne son instant
        // de depart. L'ordre compte : rejouer avec une horloge restee a la fin
        // du morceau rendrait toutes les notes deja passees, donc manquees d'un
        // seul coup, et le joueur croirait le niveau impossible.
        if (_music is not null)
        {
            _source?.Reset();
            _music.Play();
            _clock?.Resync();
        }

        // La session se construit avant d'etre rangee : si le chart est
        // injouable, la partie reste ce qu'elle etait, plutot que d'etre
        // remplacee par un objet a moitie construit.
        GameSession? next = null;
        try
        {
            next = new(chart);
            next.Start(_clock?.JudgementTimeSeconds ?? 0);
        }
        catch (ArgumentException error)
        {
            Fail($"Niveau injouable : {error.Message}");
            return;
        }

        _session = next;

        _field?.Play(next);
        _held.Clear();
        _judgementAge = JudgementDisplaySeconds;
        if (_missFlash is not null)
        {
            _missFlash.Visible = false;
        }

        if (_summaryLabel is not null)
        {
            _summaryLabel.Visible = false;
        }

        if (_titleLabel is not null)
        {
            _titleLabel.Text = $"{_packTitle}  ·  {level.FileNameOf()}";
        }
    }

    /// <summary>Vide la file d'evenements et joue ce qu'il faut, ou l'affiche.</summary>
    private void ConsumeEvents()
    {
        if (_session is null || _field is null)
        {
            return;
        }

        foreach (SessionEvent gameEvent in _session.DrainEvents())
        {
            switch (gameEvent.Kind)
            {
                case SessionEventKind.Hit:
                    _field.Flash(KeyMap.SlotOf(gameEvent.Note!.Key), gameEvent.Grade, gameEvent.Assisted);
                    ShowJudgement(gameEvent.Grade, gameEvent.Assisted);
                    PlaySound(gameEvent.Grade);
                    break;

                case SessionEventKind.HoldShort:
                    ShowJudgement(gameEvent.Grade, false, "TENUE LÂCHÉE");
                    PlaySound(gameEvent.Grade);
                    break;

                case SessionEventKind.HoldDone:
                    PlaySound(JudgementGrade.Great);
                    break;

                case SessionEventKind.Miss:
                    if (gameEvent.Note is null)
                    {
                        // Un coup dans le vide : on ne punit rien, on dit juste
                        // que le geste n'a rien trouve.
                        ShowJudgement(JudgementGrade.None, false, "…");
                        break;
                    }

                    _field.FlashMiss(KeyMap.SlotOf(gameEvent.Note.Key));
                    ShowJudgement(JudgementGrade.None, false, "MANQUÉ");
                    _missAge = 0;
                    if (_missFlash is not null)
                    {
                        _missFlash.Visible = true;
                    }

                    PlaySound(JudgementGrade.None);
                    break;

                case SessionEventKind.Finished:
                    ShowSummary();
                    break;

                default:
                    break;
            }
        }
    }

    private void PlaySound(JudgementGrade grade)
    {
        if (_effects is null)
        {
            return;
        }

        // Un son coupe par le suivant s'entend quand meme ; deux joues ensemble
        // se transforment en bruit. Sur une cascade, le dernier coup est celui
        // que le joueur vient de porter, et c'est celui-la qu'il doit entendre.
        _effects.Stop();
        _effects.Stream = HitSounds.Of(grade);
        _effects.Play();
    }

    private void ShowJudgement(JudgementGrade grade, bool assisted, string? text = null)
    {
        _judgementAge = 0;
        if (_judgementLabel is null)
        {
            return;
        }

        _judgementLabel.Text = text ?? Words(grade);
        _judgementLabel.AddThemeColorOverride("font_color", WordsColor(grade));
        _judgementLabel.Visible = true;
        _judgementLabel.Modulate = new Color(1, 1, 1, 1);
    }

    private static string Words(JudgementGrade grade) => grade switch
    {
        JudgementGrade.Perfect => "PARFAIT !",
        JudgementGrade.Great => "EXCELLENT",
        JudgementGrade.Good => "BON",
        _ => "MANQUÉ",
    };

    private static Color WordsColor(JudgementGrade grade) => grade switch
    {
        JudgementGrade.Perfect => new Color(1f, 0.85f, 0.30f),
        JudgementGrade.Great => new Color(0.45f, 0.85f, 1f),
        JudgementGrade.Good => new Color(0.55f, 1f, 0.65f),
        _ => new Color(0.95f, 0.35f, 0.40f),
    };

    private void ShowSummary()
    {
        if (_session is null || _summaryLabel is null)
        {
            return;
        }

        SessionStats stats = _session.Stats;
        _summaryLabel.Text =
            $"Score  {_session.Score}\n"
            + $"Combo  {stats.MaxCombo}\n"
            + $"Parfaits {stats.Perfect}   Excellents {stats.Great}   Bons {stats.Good}   Manqués {stats.Missed}\n"
            + $"Dérive moyenne  {stats.MeanDriftMilliseconds:+0.0;-0.0;0.0} ms\n"
            + "\nR : rejouer    F1 F2 F3 : niveau    Échap : quitter";
        _summaryLabel.Visible = true;
        _summaryLabel.Modulate = new Color(1, 1, 1, 1);
    }

    /// <summary>Repeint les compteurs : score, combo, avancement, effacements.</summary>
    private void PaintHud(double now)
    {
        if (_session is null || _field is null)
        {
            return;
        }

        if (_scoreLabel is not null)
        {
            _scoreLabel.Text = $"SCORE  {_session.Score:0000000}";
        }

        if (_comboLabel is not null)
        {
            int combo = _session.Stats.Combo;
            _comboLabel.Text = combo >= 2 ? $"{combo} COMBO" : string.Empty;
        }

        if (_progressFill is not null)
        {
            double progress = _session.Progress(now);
            float width = (float)((Size.X - 4) * progress);
            _progressFill.Size = new Vector2(Math.Max(0, width), 4);
        }

        if (_judgementLabel is not null && _judgementLabel.Visible)
        {
            // Le texte de resultat s'effface ; il ne clignote pas, il sort.
            float life = (float)Math.Clamp(_judgementAge / JudgementDisplaySeconds, 0, 1);
            _judgementLabel.Modulate = new Color(1, 1, 1, 1 - life);
            if (life >= 1)
            {
                _judgementLabel.Visible = false;
            }
        }

        if (_missFlash is not null && _missFlash.Visible)
        {
            float life = (float)Math.Clamp(_missAge / MissFlashSeconds, 0, 1);
            _missFlash.Color = new Color(0.8f, 0.1f, 0.15f, (1 - life) * 0.22f);
            if (life >= 1)
            {
                _missFlash.Visible = false;
            }
        }

        if (_summaryLabel is not null && _summaryLabel.Visible)
        {
            _summaryLabel.Modulate = new Color(1, 1, 1, 1);
        }
    }

    private void BuildHud()
    {
        _progressFill = new ColorRect
        {
            Color = new Color(1f, 0.85f, 0.30f),
            Position = new Vector2(2, 0),
            Size = new Vector2(0, 4),
        };
        AddChild(_progressFill);

        _titleLabel = CreateLabel(28, new Color(0.75f, 0.80f, 0.95f));
        Place(_titleLabel, new Vector2(18, 12), 700, 36);
        AddChild(_titleLabel);

        _scoreLabel = CreateLabel(30, new Color(1f, 0.90f, 0.45f));
        Place(_scoreLabel, new Vector2(18, 48), 400, 40);
        AddChild(_scoreLabel);

        _comboLabel = CreateLabel(44, new Color(0.55f, 0.85f, 1f));
        Place(_comboLabel, new Vector2(0, 300), Size.X, 60);
        _comboLabel.HorizontalAlignment = HorizontalAlignment.Center;
        AddChild(_comboLabel);

        _judgementLabel = CreateLabel(40, new Color(1f, 0.85f, 0.30f));
        Place(_judgementLabel, new Vector2(0, 366), Size.X, 52);
        _judgementLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _judgementLabel.Visible = false;
        AddChild(_judgementLabel);

        _summaryLabel = CreateLabel(24, new Color(0.92f, 0.94f, 1f));
        Place(_summaryLabel, new Vector2(0, 200), Size.X, 200);
        _summaryLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _summaryLabel.VerticalAlignment = VerticalAlignment.Center;
        _summaryLabel.Visible = false;
        AddChild(_summaryLabel);

        _missFlash = new ColorRect
        {
            Color = new Color(0.8f, 0.1f, 0.15f, 0f),
        };
        _missFlash.SetAnchorsPreset(LayoutPreset.FullRect);
        _missFlash.MouseFilter = MouseFilterEnum.Ignore;
        _missFlash.Visible = false;
        AddChild(_missFlash);
    }

    private static Label CreateLabel(int fontSize, Color color)
    {
        Label label = new()
        {
            MouseFilter = MouseFilterEnum.Ignore,
        };
        label.AddThemeFontSizeOverride("font_size", fontSize);
        label.AddThemeColorOverride("font_color", color);
        return label;
    }

    private static void Place(Label label, Vector2 position, float width, float height)
    {
        label.Position = position;
        label.Size = new Vector2(width, height);
        label.VerticalAlignment = VerticalAlignment.Center;
    }

    private void Fail(string message)
    {
        GD.PrintErr(message);
        if (_summaryLabel is null)
        {
            return;
        }

        _summaryLabel.Text = message;
        _summaryLabel.Visible = true;
        _summaryLabel.Modulate = new Color(1, 1, 1, 1);
    }
}
