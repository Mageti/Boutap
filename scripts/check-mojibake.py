#!/usr/bin/env python3
# SPDX-FileCopyrightText: Boutap contributors
# SPDX-License-Identifier: AGPL-3.0-or-later
"""Detecte les caracteres parasites dans les fichiers texte du projet.

La generation de texte (modeles de langue) introduit parfois des caracteres
cyrilliques, CJK, arabes ou hebreus dans du texte francais. Ces artefacts sont
invisibles a la relecture rapide et finissent dans un fichier versionne.

Les caracteres grecs utilises en notation mathematique (Sigma, gamma, sigma,
Delta, epsilon, lambda, mu, tau, phi, alpha, beta, omega, theta, pi) sont en
revanche legitimes et explicitement autorises.

Usage :
    python3 scripts/check-mojibake.py [--allow hebrew,indic,thai] [fichier ...]

Sans argument, tous les fichiers .md, .json, .yml, .cs, .sh du depot
(hors .git) sont verifies.

--allow sert lorsqu'on controle un corpus qui contient legitimement des
caracteres absents du depot (par exemple l'hebreu de la documentation du
wiki sur la theologie et la gematria).

Code de sortie : 0 si aucun artefact, 1 sinon, 2 si les options sont invalides.
"""

import io
import os
import re
import sys

# --- Caracteres grecs autorises : notation mathematique uniquement. ---------
# (Sigma, gamma, sigma, Delta, epsilon, lambda, mu, tau, phi,
#  alpha, beta, omega, theta, pi, et les mots grecs recurrentes
#  parametrikos / besthetai)
_ALLOWED_CODEPOINTS = (
    0x03A3, 0x03B3, 0x03C3, 0x0394, 0x03B5, 0x03BB, 0x03BC, 0x03C4, 0x03C6,
    0x03B1, 0x03B2, 0x03C9, 0x03B8, 0x03C0, 0x03BC, 0x03B5, 0x03BD, 0x03B4,
    0x03C0, 0x03B1, 0x03C1, 0x03B1, 0x03BC, 0x03B5, 0x03C4, 0x03C1, 0x03B9,
    0x03BA, 0x03B2, 0x03B5, 0x03C3, 0x03B8, 0x03B5, 0x03BD, 0x03B5, 0x03B9,
    0x03B1,
)
ALLOWED = set(chr(cp) for cp in _ALLOWED_CODEPOINTS)

# --- Blocs de caracteres « parasites » -------------------------------------
# Chaque bloc est (nom, premier, dernier). Un bloc optionnel disparait du
# motif quand il est autorise via --allow.
#
# Les intervalles sont ecrits en entiers, jamais en caracteres literaux :
# un caractere de CJK transcrit a la main peut se retrouver decale d'un
# demi-bloc etendre le motif a tout le plan BMP.
BLOCKS = (
    ('cyrillic',    0x0400, 0x04FF),
    ('greek',       0x0370, 0x03FF),
    ('kana',        0x3040, 0x30FF),
    ('cjk',         0x4E00, 0x9FFF),
    ('cjk-compat',  0xF900, 0xFAFF),
    ('hangul',      0xAC00, 0xD7AF),
    ('arabic',      0x0600, 0x06FF),
    ('arabic-ext',  0x0750, 0x077F),
    ('arabic-fp-a', 0xFB50, 0xFDFF),
    ('arabic-fp-b', 0xFE70, 0xFEFF),
    ('hebrew',      0x0590, 0x05FF),
    ('hebrew-fp',   0xFB1D, 0xFB4F),
    ('indic',       0x0900, 0x097F),
    ('thai',        0x0E00, 0x0E7F),
)

# Blocs qu'un corpus externe peut legitimement contenir, et qu'on peut donc
# autoriser explicitement.
OPTIONAL = {
    'hebrew': ('hebrew', 'hebrew-fp'),
    'indic': ('indic',),
    'thai': ('thai',),
    'greek': ('greek',),
}

EXTENSIONS = ('.md', '.json', '.yml', '.yaml', '.cs', '.sh', '.py',
              '.cff', '.editorconfig', '.gitattributes', '.gitignore')

SKIP_DIRS = ('.git', 'third_party', 'build', 'obj', '.venv', 'bin')


def build_pattern(allowed_blocks):
    """Compile le motif en retirant les blocs explicitement autorises."""
    dropped = set()
    for name in allowed_blocks:
        dropped.update(OPTIONAL[name])
    ranges = [(lo, hi) for name, lo, hi in BLOCKS if name not in dropped]
    source = ''.join('%s-%s' % (chr(lo), chr(hi)) for lo, hi in ranges)
    return re.compile('[' + source + ']')


def is_artifact(char):
    """Vrai si le caractere ne peut pas etre legitime dans ce projet."""
    return char not in ALLOWED


def iter_files(explicit):
    if explicit:
        for name in explicit:
            if os.path.isdir(name):
                for root, _dirs, files in os.walk(name):
                    for f in files:
                        yield os.path.join(root, f)
            else:
                yield name
        return

    for root, dirs, files in os.walk('.'):
        dirs[:] = [d for d in dirs if d not in SKIP_DIRS]
        for f in sorted(files):
            if f.endswith(EXTENSIONS):
                yield os.path.join(root, f)


def parse_args(argv):
    allow = []
    args = []
    i = 1
    while i < len(argv):
        if argv[i] == '--allow':
            i += 1
            if i >= len(argv):
                raise ValueError('--allow attend une liste de blocs')
            allow.extend(name.strip()
                         for name in argv[i].split(',') if name.strip())
        else:
            args.append(argv[i])
        i += 1
    return allow, args


def main(argv):
    try:
        allow, args = parse_args(argv)
    except ValueError as exc:
        print(str(exc), file=sys.stderr)
        return 2

    if args and args[0] in ('-h', '--help'):
        print(__doc__.strip())
        return 0

    unknown = set(allow) - set(OPTIONAL)
    if unknown:
        print('Bloc inconnu dans --allow : %s' % ', '.join(sorted(unknown)),
              file=sys.stderr)
        print('Blocs connus : %s' % ', '.join(sorted(OPTIONAL)),
              file=sys.stderr)
        return 2

    pattern = build_pattern(allow)

    total = 0
    for path in iter_files(args):
        try:
            handle = io.open(path, encoding='utf-8')
        except (IOError, OSError, UnicodeDecodeError):
            continue
        with handle:
            for lineno, line in enumerate(handle, 1):
                bad = [c for c in pattern.findall(line) if is_artifact(c)]
                if bad:
                    print('%s:%d: %s' % (path, lineno, line.rstrip()))
                    total += 1

    if total:
        print('--- %d ligne(s) avec caractere parasite ---' % total)
        return 1
    print('Aucun caractere parasite detecte.')
    return 0


if __name__ == '__main__':
    sys.exit(main(sys.argv))
