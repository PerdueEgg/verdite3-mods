#!/usr/bin/env python3
"""Write the Text mod's US text (text/UsText.cs) from the translation workbook's
"Current US" column, so the Text mod can letter the unmodded game in its fonts.

    python3 tools/build_us_text.py retranslation/KF3_retranslation.ods text/UsText.cs

Standard library only. Three tables come out:

  - Pictures: every sign, gravestone, label, dialogue line, Truth Glass card,
    description, appraisal and place card (Messages sheet), as the US disc has
    it, line breaks and all. Their capitals are kept.
  - Menu: the menu-font text (Menus & Names: Menu, Label, Weapon, Armor, Item,
    Magic and Code rows), keyed by its US capitals, with its normal
    capitalisation: title case for menus, labels and names, sentence case for
    the memory-card messages.
  - Captions: the short messages in play (Menus & Names, Caption rows), by slot,
    worded and capitalised as the US disc has them.

The capitalisation is worked out here, by the rules below; a string the rules
get wrong goes in OVERRIDES.
"""
import re
import sys
import zipfile
import xml.etree.ElementTree as ET

CHARSET = set(' !"#%&\'()*+,-./0123456789:;=?ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz\u2190\u2192\n')

# Words kept in capitals wherever they are.
ACRONYMS = {'HP', 'MP', 'HP/MP', 'MAG', 'QTY', 'PWR\u00b7WIS'}
# Names, capitalised even in sentence case.
PROPER = set()
# Small words left in lower case inside a title.
SMALL = {'A', 'AN', 'THE', 'OF', 'FOR', 'AND', 'IN', 'ON', 'TO', 'AT', 'BY', 'OR'}

# Menu-font labels in sentence case: the memory-card messages. The ones that carry
# on a message from the line above start in lower case.
SENTENCE = range(0x8007F470, 0x8007F609)   # CHECKING MEMORY CARD .. INSERT A DIFFERENT CARD
CONTINUES = {'FOR PLAYING THIS GAME', 'THEN REQUIRED', 'WANT TO FORMAT IT?', 'INSERTED INTO THE SLOT',
             'CARD IS NOT FORMATTED'}

# Whole strings the rules would get wrong.
OVERRIDES = {
    'FORMATTED YET. DO YOU': 'formatted yet. Do you',
    'A': 'A',                                  # a spell's whole name
}

T = 'urn:oasis:names:tc:opendocument:xmlns:table:1.0'
X = 'urn:oasis:names:tc:opendocument:xmlns:text:1.0'


def cs(s):
    return '"' + str(s).replace('\\', '\\\\').replace('"', '\\"').replace('\r', '').replace('\n', '\\n') + '"'


def para(p):
    """A paragraph's text, with <text:s> runs of spaces, tabs and line breaks."""
    out = [p.text or '']
    for e in p:
        tag = e.tag.split('}')[1]
        if tag == 's': out.append(' ' * int(e.get('{%s}c' % X, '1')))
        elif tag == 'tab': out.append('\t')
        elif tag == 'line-break': out.append('\n')
        else: out.append(para(e))
        out.append(e.tail or '')
    return ''.join(out)


def table(ods, name):
    """A sheet's rows as dicts by its header row."""
    root = ET.fromstring(zipfile.ZipFile(ods).read('content.xml'))
    for t in root.iter('{%s}table' % T):
        if t.get('{%s}name' % T) != name: continue
        rows = []
        for r in t.iter('{%s}table-row' % T):
            cells = []
            for c in r.findall('{%s}table-cell' % T):
                rep = int(c.get('{%s}number-columns-repeated' % T, '1'))
                text = '\n'.join(para(p) for p in c.findall('{%s}p' % X))
                cells += [text] * (rep if rep < 1000 else 1)
            rows.append(cells)
        hdr = rows[0]
        return [dict(zip(hdr, r)) for r in rows[1:] if any(r)]
    raise SystemExit(f'{ods}: no sheet "{name}"')


def word_case(w, first, title):
    """One word (it may hold hyphens or a slash) in its case."""
    if w in ACRONYMS: return w
    if any(ch.isdigit() for ch in w): return w
    if w in PROPER or title and not (w in SMALL and not first):
        return '/'.join('-'.join(p[:1] + p[1:].lower() for p in q.split('-')) for q in w.split('/'))
    return w.lower() if not first else w[:1] + w[1:].lower()


def recase(text, title, lower_start=False):
    lead = len(text) - len(text.lstrip(' '))
    words = text[lead:].split(' ')
    out = []
    for i, w in enumerate(words):
        if not w or w.startswith('\u25a0'): out.append(w); continue
        first = (i == 0 or words[i - 1].startswith('\u25a0')) and not lower_start
        out.append(word_case(w, first, title))
    return ' ' * lead + ' '.join(out)


def main(ods, out):
    warn = []

    pictures = {}
    for r in table(ods, 'Messages'):
        k, us = r.get('Key', '').strip(), r.get('Current US', '').replace('\r', '')
        us = us.replace('\u2019', "'").replace('\u2018', "'").replace('\u201c', '"').replace('\u201d', '"')
        us = '\n'.join(line.rstrip() for line in us.strip('\n').split('\n'))
        us = re.sub(r'([.!?,;:"\')])  +', r'\1 ', us)     # one space after a sentence (the US text's double ones come from the disc's monospaced lettering)
        if not k or not us.strip(): continue
        bad = sorted({c for c in us if c not in CHARSET})
        if bad:      # not US text (a leftover Japanese page): the disc's picture is left as it is
            warn.append(f'Messages {k}: left out, no letter for {" ".join(repr(c) for c in bad)}')
            continue
        pictures[k] = us

    menu, captions = {}, {}
    for r in table(ods, 'Menus & Names'):
        kind, us = r.get('Kind', '').strip(), r.get('Current US', '').rstrip()
        if not us.strip(): continue
        if kind == 'Caption':
            try: captions[int(r.get('ID', ''))] = us.strip()
            except ValueError: warn.append(f'Caption "{us}": no slot number')
            continue
        try: a = int(r.get('US address', '').strip(), 16)
        except ValueError: a = None
        core = us.strip()
        if core in OVERRIDES: cased = OVERRIDES[core]
        elif a is not None and a in SENTENCE: cased = recase(core, title=False, lower_start=core in CONTINUES)
        else: cased = recase(core, title=True)
        if core in menu and menu[core] != cased:
            warn.append(f'Menus & Names {kind} {r.get("US address", "")}: "{core}" is "{menu[core]}" elsewhere; keeping that')
            continue
        menu[core] = cased

    for us, cased in menu.items():
        if len(cased) != len(us) or cased.upper() != us.upper():
            warn.append(f'Menu "{us}" -> "{cased}": more than its case changed; skipped')
    menu = {us: c for us, c in menu.items() if len(c) == len(us) and c.upper() == us.upper() and c != us}

    lines = [
        '// The US text, for lettering the unmodded game in the Text mod\'s fonts.',
        '// Written by tools/build_us_text.py from the translation workbook\'s',
        '// "Current US" column: edit the workbook (or the tool\'s rules) and run it again.',
        'using System.Collections.Generic;',
        '',
        'namespace Kf3.Mods.Text;',
        '',
        'static class UsText',
        '{',
        '    /// <summary>Every sign, dialogue line, card and description, by its US disc',
        '    /// entry: T = TALK.T (and the same page of STALK.T, the log), I = ITEM.T.</summary>',
        '    public static readonly Dictionary<string, string> Pictures = new()',
        '    {',
    ]
    lines += [f'        [{cs(k)}] = {cs(v)},' for k, v in pictures.items()]
    lines += [
        '    };',
        '',
        '    /// <summary>Menu-font text in its normal capitalisation, by the US capitals the',
        '    /// game draws, without the spaces some are padded with. Each is the same length as its key.</summary>',
        '    public static readonly Dictionary<string, string> Menu = new()',
        '    {',
    ]
    lines += [f'        [{cs(k)}] = {cs(v)},' for k, v in menu.items()]
    lines += [
        '    };',
        '',
        '    /// <summary>The short messages in play, by their slot in the captions page.</summary>',
        '    public static readonly Dictionary<int, string> Captions = new()',
        '    {',
    ]
    lines += [f'        [{k}] = {cs(v)},' for k, v in sorted(captions.items())]
    lines += ['    };', '}', '']
    open(out, 'w', encoding='utf-8', newline='\n').write('\n'.join(lines))
    print(f'wrote {out}: {len(pictures)} pictures, {len(menu)} menu strings, {len(captions)} messages in play')
    for w in warn: print('  note:', w)


if __name__ == '__main__':
    if len(sys.argv) != 3: raise SystemExit(__doc__)
    main(sys.argv[1], sys.argv[2])
