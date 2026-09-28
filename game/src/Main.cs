// SPDX-FileCopyrightText: Boutap contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Boutap.Audio;
using Boutap.Core.Common;
using Boutap.Core.Pack;
using Boutap.Game.Judgement;
using Godot;

namespace Boutap.Shell;

/// <summary>
/// La racine de la coque Godot.
/// </summary>
/// <remarks>
/// <para>
/// Cette scene ne joue pas encore un morceau. Elle montre que la chaine
/// assemblee fonctionne : l'horloge audio, les fenetres de jugement et le
/// modele de chart viennent de <c>Boutap.Game</c> et <c>Boutap.Core</c>, et
/// la coque ne fait que les afficher.
/// </para>
/// <para>
/// Le temps affiche vient de <see cref="AudioClock"/>, jamais d'un compteur
/// d'images : c'est la regle 1 de wiki: spec.md 7.3, visible des la premiere
/// image.
/// </para>
/// </remarks>
public partial class Main : Control
{
    /// <summary>Secondes de notes affichees en avance.</summary>
    private const double LookaheadSeconds = 2.0;

    private SimulatedAudioSource? _source;
    private AudioClock? _clock;
    private Label? _clockLabel;
    private Label? _chartLabel;
    private Label? _statusLabel;
    private GridContainer? _grid;

    /// <inheritdoc/>
    public override void _Ready()
    {
        _source = new SimulatedAudioSource();
        _clock = new AudioClock(_source);
        _source.Start();

        // Pas de AnchorsPreset ici : la grille est posee dans une
        // VBoxContainer, qui la dimensionne. Un preset d'ancrage la
        // contredirait des la premiere image.
        _grid = new GridContainer { Columns = 3 };
        foreach (int key in Enumerable.Range(0, KeyBinding.GridSize))
        {
            _grid.AddChild(new Label
            {
                Text = key.ToString(System.Globalization.CultureInfo.InvariantCulture),
                CustomMinimumSize = new Vector2(120, 120),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            });
        }

        _clockLabel = MakeLabel();
        _chartLabel = MakeLabel();
        _statusLabel = MakeLabel();

        VBoxContainer column = new();
        column.AddChild(new Label
        {
            Text = $"Boutap {SemanticVersion.Current}",
            HorizontalAlignment = HorizontalAlignment.Center,
        });
        column.AddChild(_clockLabel);
        column.AddChild(_grid);
        column.AddChild(_chartLabel);
        column.AddChild(_statusLabel);
        AddChild(column);

        _statusLabel.Text = "Coque assemblee. La lecture d'un pack arrive en S4 ; "
            + "le backend audio natif (miniaudio) en S3.1.";
        _statusLabel.HorizontalAlignment = HorizontalAlignment.Center;
    }

    /// <inheritdoc/>
    public override void _Process(double delta)
    {
        if (_source is null || _clock is null)
        {
            return;
        }

        _source.Advance(delta);
        _clock.Resync();

        double position = _clock.PositionSeconds;
        if (_clockLabel is not null)
        {
            _clockLabel.Text = $"Horloge audio : {position:F3} s (image {Engine.GetProcessFrames()})";
        }

        if (_chartLabel is not null)
        {
            _chartLabel.Text = DescribeWindows(position);
        }
    }

    private string DescribeWindows(double position)
    {
        ChartTimeline berceau = new(ChartLevel.Berceau, []);
        ChartTimeline ronde = new(ChartLevel.Ronde, []);
        return string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"Fenetres au temps {position:F3} s — berceau {berceau.BaseWindow} · ronde {ronde.BaseWindow}");
    }

    private static Label MakeLabel() => new()
    {
        HorizontalAlignment = HorizontalAlignment.Center,
    };
}
