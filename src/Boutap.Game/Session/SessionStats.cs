// SPDX-FileCopyrightText: Boutap contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using Boutap.Game.Judgement;

namespace Boutap.Game.Session;

/// <summary>Ce que la partie a produit, en nombres.</summary>
/// <remarks>
/// <para>
/// Le score brut somme les points de chaque coup. Le score normalise divise par
/// le nombre de notes multiplie par la valeur d'un coup parfait : un morceau de
/// 80 notes et un morceau de 800 ont le meme plafond, 1 000 000 (wiki:
/// generateur.md 7.1). Le compte est necessaire des la construction, pas a la
/// fin, parce que l'ecran de resultat n'est pas le seul endroit ou on le lit.
/// </para>
/// <para>
/// Une note manquee compte pour zero. Il n'y a pas de penalite : C1 interdit
/// l'echec punitif, et un score qui descend quand on rate ne produit qu'une
/// partie jouee prudemment, donc ennuyeuse.
/// </para>
/// </remarks>
public sealed class SessionStats
{
    private int _perfect;
    private int _great;
    private int _good;
    private int _miss;
    private int _assisted;
    private int _raw;
    private int _combo;
    private double _drift;
    private int _driftCount;

    /// <summary>Points du coup parfait, base de la normalisation.</summary>
    public const int PerfectPoints = 1000;

    /// <summary>Notes frappees, tous grades confondus.</summary>
    public int Judged => _perfect + _great + _good;

    /// <summary>Notes manquees.</summary>
    public int Missed => _miss;

    /// <summary>Notes traitees, frappees ou manquees.</summary>
    public int Total => Judged + _miss;

    /// <summary>Coups juges parfaits.</summary>
    public int Perfect => _perfect;

    /// <summary>Coups juges excellents.</summary>
    public int Great => _great;

    /// <summary>Coups juges bons.</summary>
    public int Good => _good;

    /// <summary>Coups tombes juste grace a l'aide au geste.</summary>
    public int Assisted => _assisted;

    /// <summary>Combo courant.</summary>
    public int Combo => _combo;

    /// <summary>Meilleur combo de la partie.</summary>
    public int MaxCombo { get; private set; }

    /// <summary>Somme des points, avant normalisation.</summary>
    public int RawScore => _raw;

    /// <summary>Ecart moyen du geste, en millisecondes.</summary>
    /// <remarks>
    /// Positif en retard, negatif en avance. C'est la lecture qui permet a un
    /// joueur de regler son <c>UserOffsetSeconds</c> sans qu'on lui demande de
    /// faire confiance a un ressenti : la derive moyenne dit dans quel sens on
    /// decale.
    /// </remarks>
    public double MeanDriftMilliseconds =>
        _driftCount == 0 ? 0 : _drift / _driftCount * 1000.0;

    /// <summary>Score rapporte a 1 000 000.</summary>
    /// <param name="noteCount">Nombre de notes du morceau entier.</param>
    /// <returns>Le score normalise, borne a 1 000 000.</returns>
    public int NormalizedScore(int noteCount)
    {
        if (noteCount <= 0)
        {
            return 0;
        }

        double score = _raw * 1_000_000.0 / (noteCount * (double)PerfectPoints);
        if (score < 0)
        {
            return 0;
        }

        return score > 1_000_000 ? 1_000_000 : (int)Math.Round(score, MidpointRounding.AwayFromZero);
    }

    /// <summary>Enregistre un coup porte.</summary>
    /// <param name="grade">Le grade obtenu.</param>
    /// <param name="delta">Ecart du geste, en secondes.</param>
    /// <param name="assisted">Vrai si l'aide au geste a servi.</param>
    public void Register(JudgementGrade grade, double delta, bool assisted)
    {
        int points = JudgementWindows.ScoreFor(grade);
        _raw += points;
        _drift += delta;
        _driftCount++;
        if (assisted)
        {
            _assisted++;
        }

        switch (grade)
        {
            case JudgementGrade.Perfect:
                _perfect++;
                break;
            case JudgementGrade.Great:
                _great++;
                break;
            case JudgementGrade.Good:
                _good++;
                break;
            default:
                break;
        }

        _combo++;
        if (_combo > MaxCombo)
        {
            MaxCombo = _combo;
        }
    }

    /// <summary>Enregistre une note manquee.</summary>
    public void RegisterMiss()
    {
        _miss++;
        _combo = 0;
    }

    /// <summary>
    /// Remplace un grade par un moins bon, une tenue lachee avant sa fin.
    /// </summary>
    /// <param name="from">Grade deja compte.</param>
    /// <param name="to">Grade corrige.</param>
    /// <param name="delta">Ecart du geste d'origine, en secondes.</param>
    /// <param name="assisted">Vrai si l'aide au geste avait servi.</param>
    /// <remarks>
    /// Une note dont le grade tombe a zero n'est pas une note « moins bonne »,
    /// c'est une note ratee : elle quitte les coups comptes et rejoint les
    /// manquees. Laisser <see cref="Good"/> tomber a -1 aurait compte une note
    /// qui n'existe pas, et le denominateur du score normalise se serait
    /// deplace tout seul.
    /// </remarks>
    public void Demote(JudgementGrade from, JudgementGrade to, double delta, bool assisted)
    {
        if (from == JudgementGrade.None)
        {
            return;
        }

        _raw += JudgementWindows.ScoreFor(to) - JudgementWindows.ScoreFor(from);
        Decrement(from);
        _drift -= delta;
        _driftCount--;
        if (assisted)
        {
            _assisted--;
        }

        if (to == JudgementGrade.None)
        {
            RegisterMiss();
            return;
        }

        Increment(to);
    }

    /// <summary>Coupe le combo courant.</summary>
    public void BreakCombo()
    {
        _combo = 0;
    }

    private void Increment(JudgementGrade grade)
    {
        switch (grade)
        {
            case JudgementGrade.Perfect:
                _perfect++;
                break;
            case JudgementGrade.Great:
                _great++;
                break;
            case JudgementGrade.Good:
                _good++;
                break;
            default:
                break;
        }
    }

    private void Decrement(JudgementGrade grade)
    {
        switch (grade)
        {
            case JudgementGrade.Perfect:
                _perfect--;
                break;
            case JudgementGrade.Great:
                _great--;
                break;
            case JudgementGrade.Good:
                _good--;
                break;
            default:
                break;
        }
    }
}
