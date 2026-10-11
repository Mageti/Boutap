// SPDX-FileCopyrightText: Boutap contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Boutap.Core.Pack;
using Boutap.Game.Judgement;
using Boutap.Game.Session;
using Godot;

namespace Boutap.Shell;

/// <summary>
/// L'aire de jeu : neuf couloirs, des notes qui tombent, une ligne de juge.
/// </summary>
/// <remarks>
/// <para>
/// Tout est dessine dans <see cref="_Draw"/> plutot qu'avec un noeud par note. Un
/// morceau de cascade depasse les mille notes, et un noeud par note ferait autant
/// d'objets que de fenetres : la boucle de jeu finirait par ramer sur la meme
/// machine ou le jeu doit tenir vingt-cinq millisecondes de latence.
/// </para>
/// <para>
/// Le trace ne decide de rien : il recoit un instant et une liste de vues, et il
/// dessine. C'est <see cref="GameSession"/> qui arbitre, et c'est elle qu'on teste.
/// </para>
/// </remarks>
public sealed partial class NoteField : Control
{
    /// <summary>Neuf couloirs de grille, puis les deux volants.</summary>
    public const int LaneCount = KeyMap.SlotCount;

    /// <summary>Poids d'un couloir : les volants sont plus etroits que la grille.</summary>
    private static readonly double[] LaneWeight = BuildWeights();

    /// <summary>Teintes des couloirs de grille, repetees par colonne.</summary>
    private static readonly Color[] Palette =
    {
        new(0.92f, 0.30f, 0.45f),
        new(0.35f, 0.75f, 0.95f),
        new(0.55f, 0.90f, 0.45f),
    };

    /// <summary>Hauteur du socle des touches, en pixels.</summary>
    private const float ReceptorHeight = 18.0f;

    /// <summary>Hauteur d'une note, en pixels.</summary>
    private const float NoteHeight = 20.0f;

    /// <summary>Retrait d'une note dans son couloir, en pixels de chaque cote.</summary>
    private const float NoteInset = 3.0f;

    /// <summary>Duree de vie d'une explosion de resultat, en secondes.</summary>
    private const double BurstLife = 0.35;

    /// <summary>Secondes de notes visibles a l'avance.</summary>
    private const double Lookahead = 2.2;

    /// <summary>Marge entre le haut de l'aire et la naissance des notes.</summary>
    private const float TopMargin = 40.0f;

    private readonly Burst[] _bursts = new Burst[LaneCount];
    private readonly float[] _flash = new float[LaneCount];
    private readonly float[] _left = new float[LaneCount];
    private readonly float[] _width = new float[LaneCount];

    private GameSession? _session;
    private double _now;

    /// <summary>Une explosion de resultat, une par couloir.</summary>
    /// <param name="Age">Age en secondes, negatif quand la place est libre.</param>
    private readonly struct Burst(float age, JudgementGrade grade, bool assisted, bool missed)
    {
        public readonly float Age = age;
        public readonly JudgementGrade Grade = grade;
        public readonly bool Assisted = assisted;
        public readonly bool Missed = missed;

        public bool Alive => Age >= 0;
    }

    /// <summary>Construit l'aire, les places d'explosion libres.</summary>
    public NoteField()
    {
        for (int lane = 0; lane < LaneCount; lane++)
        {
            _bursts[lane] = new Burst(-1, JudgementGrade.None, false, false);
        }
    }

    /// <summary>Attache la partie a dessiner.</summary>
    /// <param name="session">La partie en cours.</param>
    public void Play(GameSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        _session = session;
        QueueRedraw();
    }

    /// <summary>Oublie la partie, pour l'ecran de resultat.</summary>
    public void Clear()
    {
        _session = null;
        QueueRedraw();
    }

    /// <summary>Fait avancer les effets et redessine.</summary>
    /// <param name="session">La partie en cours.</param>
    /// <param name="now">Instant courant, sur l'horloge audio.</param>
    /// <param name="delta">Temps ecoule depuis l'image precedente, en secondes.</param>
    public void Advance(GameSession session, double now, double delta)
    {
        _now = now;
        _session = session;
        for (int lane = 0; lane < LaneCount; lane++)
        {
            _flash[lane] = Math.Max(0, _flash[lane] - ((float)delta * 5f));
            if (_bursts[lane].Alive)
            {
                float age = _bursts[lane].Age + (float)delta;
                if (age > BurstLife)
                {
                    _bursts[lane] = new Burst(-1, JudgementGrade.None, false, false);
                }
                else
                {
                    _bursts[lane] = new Burst(age, _bursts[lane].Grade, _bursts[lane].Assisted, _bursts[lane].Missed);
                }
            }
        }

        QueueRedraw();
    }

    /// <summary>Signale un coup, pour allumer le couloir et faire une explosion.</summary>
    /// <param name="lane">Couloir concerne.</param>
    /// <param name="grade">Grade obtenu, ou <see cref="JudgementGrade.None"/> si le coup est parti dans le vide.</param>
    /// <param name="assisted">Vrai si l'aide au geste a servi.</param>
    public void Flash(int lane, JudgementGrade grade, bool assisted)
    {
        if (lane < 0 || lane >= LaneCount)
        {
            return;
        }

        _flash[lane] = 1;
        _bursts[lane] = new Burst(0, grade, assisted, false);
    }

    /// <summary>Signale une note manquee, pour une explosion rouge.</summary>
    /// <param name="lane">Couloir concerne.</param>
    public void FlashMiss(int lane)
    {
        if (lane < 0 || lane >= LaneCount)
        {
            return;
        }

        _flash[lane] = 0.4f;
        _bursts[lane] = new Burst(0, JudgementGrade.None, false, true);
    }

    /// <summary>Ordre de la ligne de juge, en pixels depuis le haut de l'aire.</summary>
    public float JudgementY => Size.Y - ReceptorHeight - 8;

    /// <summary>Pixels par seconde de musique : la pente de la chute.</summary>
    public float PixelsPerSecond => (JudgementY - TopMargin) / (float)Lookahead;

    /// <summary>Centre horizontal d'un couloir, en pixels.</summary>
    /// <param name="lane">Index du couloir.</param>
    /// <returns>L'abscisse du milieu.</returns>
    public float LaneCenter(int lane)
    {
        Layout();
        return lane >= 0 && lane < LaneCount ? _left[lane] + (_width[lane] / 2) : 0;
    }

    /// <inheritdoc/>
    public override void _Draw()
    {
        Layout();

        for (int lane = 0; lane < LaneCount; lane++)
        {
            DrawStrip(lane);
        }

        for (int lane = 0; lane < LaneCount; lane++)
        {
            DrawReceptor(lane);
        }

        if (_session is null)
        {
            DrawBursts();
            return;
        }

        foreach (NoteView view in _session.Snapshot(_now, Lookahead))
        {
            int lane = KeyMap.SlotOf(view.Note.Key);
            if (lane >= 0)
            {
                DrawNote(view, lane);
            }
        }

        DrawBursts();
    }

    /// <summary>Recalcule la geometrie des couloirs depuis la taille courante.</summary>
    /// <remarks>
    /// Les tableaux sont reutilises d'une image a l'autre : les remplacer serait
    /// la seule allocation de la boucle de dessin, donc la seule chose qui
    /// finirait par se voir sur un morceau long.
    /// </remarks>
    private void Layout()
    {
        float total = 0;
        for (int lane = 0; lane < LaneCount; lane++)
        {
            total += (float)LaneWeight[lane];
        }

        float left = 0;
        float span = Size.X - 2;
        for (int lane = 0; lane < LaneCount; lane++)
        {
            float width = span * ((float)LaneWeight[lane] / total);
            _left[lane] = left;
            _width[lane] = width;
            left += width;
        }
    }

    private void DrawStrip(int lane)
    {
        bool wheel = lane >= KeyBinding.GridSize;
        float width = _width[lane] - 2;
        float left = _left[lane];

        // Une bande sombre par couloir : sans elles, une aire vide ne dit pas ou
        // sont les neuf touches.
        DrawRect(new Rect2(left, 0, width, Size.Y), new Color(0.07f, 0.08f, 0.14f));

        if (_flash[lane] > 0)
        {
            DrawRect(new Rect2(left, 0, width, Size.Y), new Color(0.30f, 0.55f, 1f, _flash[lane] * 0.30f));
        }

        if (wheel)
        {
            // Liserau au milieu du couloir : un volant se lit comme une piste,
            // pas comme une colonne de notes.
            DrawRect(new Rect2(left + 2, 0, 4, Size.Y), new Color(0.35f, 0.30f, 0.55f, 0.55f));
        }
    }

    private void DrawReceptor(int lane)
    {
        float y = JudgementY;
        float width = _width[lane] - 2;

        Color baseColor = lane >= KeyBinding.GridSize
            ? new Color(0.42f, 0.36f, 0.68f)
            : Palette[lane % Palette.Length];
        Color color = _flash[lane] > 0 ? baseColor.Lerp(Colors.White, _flash[lane]) : baseColor;

        DrawRect(new Rect2(_left[lane] + 2, y, width, ReceptorHeight), color);
        DrawRect(new Rect2(_left[lane] + 2, y + ReceptorHeight, width, 3), new Color(1f, 1f, 1f, 0.35f));
    }

    private void DrawNote(NoteView view, int lane)
    {
        float width = _width[lane] - (NoteInset * 2);
        float top = JudgementY - ((float)view.DistanceSeconds * PixelsPerSecond);
        bool hold = view.HoldEnd is not null;
        float height = hold
            ? Math.Max(NoteHeight, ((float)(view.HoldEnd!.Value - view.Time) * PixelsPerSecond) + NoteHeight)
            : NoteHeight;

        // Une note deja jouee s'effface avec son age : sinon on la voit
        // traverser la ligne alors qu'elle est deja comptee.
        float alpha;
        if (view.Judged)
        {
            alpha = (float)Math.Max(0, 1 - (Math.Max(0, -view.DistanceSeconds) * 6));
        }
        else
        {
            // Fondu a l'approche : une note qui nait en haut de l'ecran ne doit
            // pas apparaitre d'un coup.
            alpha = Math.Max(0.25f, 1f - ((float)view.DistanceSeconds / (float)Lookahead));
        }

        Color body;
        if (view.Grade == JudgementGrade.None && view.Judged)
        {
            body = new Color(0.55f, 0.30f, 0.32f, alpha);
        }
        else if (hold)
        {
            // Une tenue se distingue a la teinte, pas seulement a la longueur :
            // sur une cascade, la longueur est la premiere chose qu'on perd.
            body = new Color(0.55f, 0.85f, 0.65f, alpha);
        }
        else
        {
            body = new Color(
                Math.Clamp((view.Note.ForceOrDefault / 10f) + 0.35f, 0f, 1f), 0.62f, 0.98f, alpha);
        }

        Rect2 rect = new(_left[lane] + NoteInset, top, width, height);
        DrawRect(rect, body);

        // Bord clair : sans lui, deux notes voisines de la meme teinte se
        // confondent sur une cascade.
        DrawRect(new Rect2(rect.Position, new Vector2(rect.Size.X, 3)), new Color(1f, 1f, 1f, alpha * 0.5f));
        if (hold)
        {
            DrawRect(new Rect2(rect.Position, new Vector2(3, rect.Size.Y)), new Color(1f, 1f, 1f, alpha * 0.4f));
            DrawRect(
                new Rect2(rect.Position + new Vector2(0, rect.Size.Y - 3), new Vector2(rect.Size.X, 3)),
                new Color(1f, 1f, 1f, alpha * 0.5f));
        }
    }

    private void DrawBursts()
    {
        for (int lane = 0; lane < LaneCount; lane++)
        {
            Burst burst = _bursts[lane];
            if (!burst.Alive)
            {
                continue;
            }

            float center = _left[lane] + (_width[lane] / 2);
            float y = JudgementY + (ReceptorHeight / 2);
            float life = burst.Age / (float)BurstLife;

            Color color = burst.Missed
                ? new Color(0.95f, 0.25f, 0.30f)
                : ColorOf(burst.Grade);
            DrawCircle(new Vector2(center, y), 10 + (life * 42), new Color(color, 1 - life));

            // Un coup aide se signale par un anneau : le joueur doit savoir que
            // son doigt a glisse, sans que ca lui soit reproche.
            if (burst.Assisted)
            {
                DrawCircle(
                    new Vector2(center, y),
                    6 + (life * 18),
                    new Color(1f, 0.85f, 0.35f, (1 - life) * 0.9f));
            }
        }
    }

    private static Color ColorOf(JudgementGrade grade) => grade switch
    {
        JudgementGrade.Perfect => new Color(1f, 0.85f, 0.30f),
        JudgementGrade.Great => new Color(0.45f, 0.85f, 1f),
        JudgementGrade.Good => new Color(0.55f, 1f, 0.65f),
        _ => new Color(0.9f, 0.4f, 0.45f),
    };

    private static double[] BuildWeights()
    {
        var weights = new double[LaneCount];
        for (int lane = 0; lane < LaneCount; lane++)
        {
            weights[lane] = lane >= KeyBinding.GridSize ? 0.55 : 1.0;
        }

        return weights;
    }
}
