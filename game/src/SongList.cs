// SPDX-FileCopyrightText: Boutap contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Boutap.Core.Pack;
using Boutap.Game.Library;
using Godot;

namespace Boutap.Shell;

/// <summary>L'écran qui liste les morceaux et laisse choisir.</summary>
/// <remarks>
/// <para>
/// Le catalogue vient de <see cref="PackLibrary"/>, qui ne connaît ni Godot ni le
/// temps. Cet écran ne fait que l'afficher et annoncer un choix : la coque
/// reste le dernier endroit où l'on décide, elle ne calcule rien.
/// </para>
/// <para>
/// Les morceaux se déposent dans le dossier utilisateur, ils ne sont pas
/// embarqués. Un pack illisible reste listé avec sa raison : un joueur qui
/// dépose un fichier doit savoir pourquoi rien ne part, pas croire le jeu
/// cassé.
/// </para>
/// </remarks>
public sealed partial class SongList : PanelContainer
{
    private const string Header = "Choisis un morceau";
    private const string Hint = "Haut/Bas : choisir    Entrée : jouer    Échap : quitter";

    private readonly List<LibraryEntry> _entries = [];
    private int _selected;
    private Label? _countLabel;
    private VBoxContainer? _rows;

    /// <summary>Émis quand le joueur valide un morceau.</summary>
    [Signal]
    public delegate void SongChosenEventHandler(string path);

    /// <summary>Émis quand la liste doit être relue (bouton « rafraîchir »).</summary>
    [Signal]
    public delegate void RefreshRequestedEventHandler();

    /// <inheritdoc/>
    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop;

        var style = new StyleBoxFlat
        {
            BgColor = new Color(0.07f, 0.08f, 0.11f, 1f),
        };
        AddThemeStyleboxOverride("panel", style);

        var root = new VBoxContainer
        {
            MouseFilter = MouseFilterEnum.Stop,
        };
        root.SetAnchorsPreset(LayoutPreset.FullRect);
        root.AddThemeConstantOverride("separation", 14);
        AddChild(root);

        var title = new Label
        {
            Text = Header,
            MouseFilter = MouseFilterEnum.Ignore,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        title.AddThemeFontSizeOverride("font_size", 38);
        title.AddThemeColorOverride("font_color", new Color(0.95f, 0.85f, 0.45f));
        root.AddChild(title);

        var hint = new Label
        {
            Text = Hint,
            MouseFilter = MouseFilterEnum.Ignore,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        hint.AddThemeFontSizeOverride("font_size", 18);
        hint.AddThemeColorOverride("font_color", new Color(0.55f, 0.60f, 0.72f));
        root.AddChild(hint);

        _countLabel = new Label
        {
            MouseFilter = MouseFilterEnum.Ignore,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        _countLabel.AddThemeFontSizeOverride("font_size", 18);
        _countLabel.AddThemeColorOverride("font_color", new Color(0.45f, 0.50f, 0.62f));
        root.AddChild(_countLabel);

        var scroll = new ScrollContainer
        {
            MouseFilter = MouseFilterEnum.Stop,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        root.AddChild(scroll);

        _rows = new VBoxContainer
        {
            MouseFilter = MouseFilterEnum.Stop,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        _rows.AddThemeConstantOverride("separation", 6);
        scroll.AddChild(_rows);

        var buttons = new HBoxContainer
        {
            MouseFilter = MouseFilterEnum.Stop,
            Alignment = BoxContainer.AlignmentMode.Center,
        };
        buttons.AddThemeConstantOverride("separation", 12);
        root.AddChild(buttons);

        var refresh = new Button { Text = "Relire le dossier" };
        refresh.AddThemeFontSizeOverride("font_size", 20);
        refresh.Pressed += () => EmitSignal(SignalName.RefreshRequested);
        buttons.AddChild(refresh);

        var open = new Button { Text = "Ouvrir le dossier" };
        open.AddThemeFontSizeOverride("font_size", 20);
        open.Pressed += OpenFolder;
        buttons.AddChild(open);
    }

    /// <summary>Rend la liste visible.</summary>
    public void ShowList() => Visible = true;

    /// <summary>Cache la liste.</summary>
    public void HideList() => Visible = false;

    /// <summary>Recharge le contenu du dossier et redessine.</summary>
    /// <param name="directory">Dossier des morceaux, déjà créé si nécessaire.</param>
    public void Reload(string directory)
    {
        Directory.CreateDirectory(directory);

        _entries.Clear();
        foreach (LibraryEntry entry in PackLibrary.Scan(directory))
        {
            _entries.Add(entry);
        }

        if (_countLabel is not null)
        {
            _countLabel.Text = _entries.Count == 0
                ? "Aucun morceau. Dépose des fichiers .btp dans le dossier du joueur."
                : $"{_entries.Count} morceau(x)";
        }

        _selected = _entries.Count > 0 ? 0 : -1;
        Rebuild();
    }

    /// <summary>Le morceau choisi, ou <see langword="null"/>.</summary>
    public string? ChosenPath =>
        _selected >= 0 && _selected < _entries.Count ? _entries[_selected].Path : null;

    /// <summary>Nombre de morceaux reellement jouables, incorrects exclus.</summary>
    public int PlayableCount
    {
        get
        {
            int count = 0;
            foreach (LibraryEntry entry in _entries)
            {
                if (entry.IsPlayable)
                {
                    count++;
                }
            }

            return count;
        }
    }

    /// <summary>Sélection suivante, en boucle.</summary>
    public void MoveNext() => MoveBy(1);

    /// <summary>Sélection précédente, en boucle.</summary>
    public void MovePrevious() => MoveBy(-1);

    /// <inheritdoc/>
    public override void _UnhandledInput(InputEvent @event)
    {
        if (!Visible || @event is not InputEventKey key || key.Echo || !key.Pressed)
        {
            return;
        }

        switch (key.Keycode)
        {
            case Key.Down:
                MoveNext();
                GetViewport().SetInputAsHandled();
                break;
            case Key.Up:
                MovePrevious();
                GetViewport().SetInputAsHandled();
                break;
            case Key.Enter:
            case Key.KpEnter:
                Validate();
                GetViewport().SetInputAsHandled();
                break;
            default:
                break;
        }
    }

    private void MoveBy(int offset)
    {
        if (_entries.Count == 0)
        {
            return;
        }

        _selected = (_selected + offset + _entries.Count) % _entries.Count;
        Repaint();
    }

    private void Validate()
    {
        if (ChosenPath is not string path)
        {
            return;
        }

        EmitSignal(SignalName.SongChosen, path);
    }

    private void OpenFolder()
    {
        string folder = ProjectSettings.GlobalizePath("user://");
        if (!Directory.Exists(folder))
        {
            return;
        }

        // Ouvre le gestionnaire de fichiers du système. C'est la manière la
        // plus sûre de déposer un morceau : pas de dialogue de fichier à
        // dessiner et à maintenir dans un projet sans éditeur de scènes.
        OS.ShellOpen(folder);
    }

    private void Rebuild()
    {
        if (_rows is null)
        {
            return;
        }

        foreach (Node child in _rows.GetChildren())
        {
            _rows.RemoveChild(child);
            child.QueueFree();
        }

        for (int index = 0; index < _entries.Count; index++)
        {
            _rows.AddChild(BuildRow(_entries[index], index == _selected));
        }
    }

    private static Button BuildRow(LibraryEntry entry, bool selected)
    {
        string subtitle = entry.DisplaySubtitle;
        Button button = new()
        {
            Text = string.IsNullOrWhiteSpace(subtitle)
                ? entry.DisplayTitle
                : $"{entry.DisplayTitle}    {subtitle}",
            TooltipText = entry.Error ?? $"{entry.Levels.Count} niveaux",
            MouseFilter = MouseFilterEnum.Stop,
            Alignment = HorizontalAlignment.Center,
        };

        button.AddThemeFontSizeOverride("font_size", 24);
        button.AddThemeColorOverride(
            "font_color",
            entry.IsPlayable
                ? new Color(0.88f, 0.92f, 1f)
                : new Color(0.45f, 0.45f, 0.50f));
        button.AddThemeColorOverride(
            "font_color_hover",
            entry.IsPlayable
                ? new Color(1f, 0.92f, 0.60f)
                : new Color(0.55f, 0.55f, 0.60f));
        button.AddThemeColorOverride(
            "font_color_pressed",
            new Color(1f, 1f, 1f));
        button.AddThemeStyleboxOverride("normal", RowStyle(selected));

        return button;
    }

    private static StyleBoxFlat RowStyle(bool selected) => new()
    {
        BgColor = selected
            ? new Color(0.20f, 0.24f, 0.34f)
            : new Color(0.11f, 0.13f, 0.18f),
        CornerRadiusBottomLeft = 6,
        CornerRadiusBottomRight = 6,
        CornerRadiusTopLeft = 6,
        CornerRadiusTopRight = 6,
        ContentMarginLeft = 18,
        ContentMarginRight = 18,
        ContentMarginTop = 8,
        ContentMarginBottom = 8,
    };

    private void Repaint()
    {
        if (_rows is null)
        {
            return;
        }

        for (int index = 0; index < _rows.GetChildCount(); index++)
        {
            if (_rows.GetChild(index) is Button { } button)
            {
                button.AddThemeStyleboxOverride("normal", RowStyle(index == _selected));
            }
        }
    }
}
