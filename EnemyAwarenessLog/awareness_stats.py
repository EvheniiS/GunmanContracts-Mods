"""Numbers from one play session, for comparing runs with and without Enemy Awareness Fix.

Usage:
  python awareness_stats.py <session-*.log> [<MelonLoader log>]        one session
  python awareness_stats.py --game "<game dir>" <session name> ...     by session name, MelonLoader log found by time

The session file is UserData\\EnemyAwarenessLog\\session-<time>.log (Enemy Awareness Log). The MelonLoader
log (MelonLoader\\Logs\\<date>.log) only adds the Fix's own lines and the Physical Dodge wave summaries, and
tells which mod versions were loaded. Only combat scenes count: the range, menus and loader are skipped.
Works with Enemy Awareness Log 0.4.x and 0.5.x lines.
"""
import os
import re
import sys
from datetime import datetime

SKIP_SCENES = {'GameLoader', 'MainMenu', 'The_Range_001'}
T = re.compile(r'^\[t=\s*([\d.]+)\]\s?(.*)')


def avg(xs):
    return sum(xs) / len(xs) if xs else None


def session_stats(path):
    s = dict(scenes=[], combat_s=0.0, waves=0, visits=0, spawns=0, spawn_dist=[], first_sight=0, sight_dist=[], sight_after=[],
             chases=0, live=0, searched=0, unclear=0, timer_runs=0, stuck=0, died=0, heard=0,
             door_kicks=0, your_shots=0, bow=0, enemy_shots=0, enemy_harmless=0, vis=[])
    scene, scene_t, last_t = None, 0.0, 0.0

    def close(t):
        if scene and scene not in SKIP_SCENES:
            s['combat_s'] += max(0.0, t - scene_t)

    for line in open(path, encoding='utf-8', errors='replace'):
        m = T.match(line.rstrip())
        if not m:
            continue
        t, x = float(m.group(1)), m.group(2)
        last_t = t
        sm = re.match(r"---- scene '([^']+)' ----", x)
        if sm:
            close(t)
            scene, scene_t = sm.group(1), t
            if scene not in SKIP_SCENES:
                s['visits'] += 1          # a scene's first wave gets no header line
                if scene not in s['scenes']:
                    s['scenes'].append(scene)
            continue
        if scene in SKIP_SCENES or scene is None:
            continue
        if x.startswith('==== wave'):
            s['waves'] += 1
        elif x.startswith('spawn E'):
            s['spawns'] += 1
            d = re.search(r"', ([\d.]+) m ", x)
            if d:
                s['spawn_dist'].append(float(d.group(1)))
        elif 'FIRST SAW YOU' in x:
            s['first_sight'] += 1
            d = re.search(r'([\d.]+) m [NSEW]{1,2}\b', x)
            a = re.search(r'([\d.]+) s after spawn', x)
            if d:
                s['sight_dist'].append(float(d.group(1)))
            if a:
                s['sight_after'].append(float(a.group(1)))
        elif x.startswith('STUCK?'):
            s['stuck'] += 1
        elif x.endswith(' died'):
            s['died'] += 1
        elif x.startswith('HEARD'):
            s['heard'] += 1
        else:
            m2 = re.search(r'unseen chases: (\d+) - live-tracked (\d+), searched where they lost you (\d+), unclear (\d+)', x)
            if m2:
                for k, v in zip(('chases', 'live', 'searched', 'unclear'), m2.groups()):
                    s[k] += int(v)
            m2 = re.search(r'lost-target timer ran out (\d+)x', x)
            if m2:
                s['timer_runs'] += int(m2.group(1))
            m2 = re.search(r'(?:door kicks|doors kicked/breached) (\d+)', x)
            if m2:
                s['door_kicks'] += int(m2.group(1))
            m2 = re.search(r'enemy gunshots (\d+) \((?:harmless|marked harmless by the game:) (\d+)\)', x)
            if m2:
                s['enemy_shots'] += int(m2.group(1))
                s['enemy_harmless'] += int(m2.group(2))
            m2 = re.search(r'(?:your gunshots|gunshots) (\d+) \(silenced \d+\), (?:bow|bow shots) (\d+)', x)
            if m2:
                s['your_shots'] += int(m2.group(1))
                s['bow'] += int(m2.group(2))
            m2 = re.search(r'your visibility[^:]*: avg ([\d.]+)', x)
            if m2:
                s['vis'].append(float(m2.group(1)))
    close(last_t)
    return s


def melon_stats(path):
    m = dict(versions={}, told=0, told_age=[], reached=0, stopped_short=0, couldnt_reach=0, search_over=0,
             gave_up=0, guesses=0, dodged=0, hit=0, hurt=0, free_miss=0)
    for line in open(path, encoding='utf-8', errors='replace'):
        v = re.search(r'\] (Enemy Awareness (?:Fix|Log)|Physical Dodge) v([\d.]+)', line)
        if v:
            m['versions'][v.group(1)] = v.group(2)
        if '[Enemy_Awareness_Fix]' in line:
            a = re.search(r'told \(sighting ([\d.]+) s old\)|told where you were ([\d.]+) s ago', line)
            if a:
                m['told'] += 1
                m['told_age'].append(float(a.group(1) or a.group(2)))
            elif ' reached ' in line:
                m['reached'] += 1
            elif 'stopped short of' in line:
                m['stopped_short'] += 1
            elif "couldn't reach" in line:
                m['couldnt_reach'] += 1
            elif 'search over' in line:
                m['search_over'] += 1
            if 'gave up' in line:
                m['gave_up'] += 1
            if 'never saw you' in line or 'new guess' in line or 'new rough guess' in line:
                m['guesses'] += 1
        if '[Physical_Dodge]' in line:
            w = re.search(r'summary .*?: (\d+) shots at you: dodged (\d+).*?hit (\d+) \(hurt (\d+)x\), free miss (\d+)', line)
            if w:
                m['dodged'] += int(w.group(2)); m['hit'] += int(w.group(3))
                m['hurt'] += int(w.group(4)); m['free_miss'] += int(w.group(5))
    return m


def find_melon_log(game, session_name):
    """The MelonLoader log whose start time is the latest one before the session file's time."""
    st = datetime.strptime(re.search(r'(\d{8}-\d{6})', session_name).group(1), '%Y%m%d-%H%M%S')
    best, best_t = None, None
    logs = os.path.join(game, 'MelonLoader', 'Logs')
    for f in os.listdir(logs):
        mm = re.match(r'(\d+)-(\d+)-(\d+)_(\d+)-(\d+)-(\d+)\.log$', f)
        if not mm:
            continue
        y, mo, d, h, mi, se = map(int, mm.groups())
        t = datetime(2000 + y, mo, d, h, mi, se)
        if t <= st and (best_t is None or t > best_t):
            best, best_t = os.path.join(logs, f), t
    return best


def fmt(v, n=1):
    return '-' if v is None else f'{v:.{n}f}'


def report(session, melon=None):
    s = session_stats(session)
    m = melon_stats(melon) if melon else None
    mins = s['combat_s'] / 60 or 1e-9
    concl = s['live'] + s['searched']
    out = [f"### {os.path.basename(session)}",
           f"- versions: {m['versions'] if m else '?'}; MelonLoader log: {os.path.basename(melon) if melon else '-'}",
           f"- combat scenes: {', '.join(s['scenes']) or '-'}; {s['combat_s'] / 60:.1f} min, {s['waves'] + s['visits']} waves started",
           f"- spawns {s['spawns']} (avg {fmt(avg(s['spawn_dist']))} m from you), enemies died {s['died']}",
           f"- first sightings {s['first_sight']}: avg {fmt(avg(s['sight_dist']))} m away, avg {fmt(avg(s['sight_after']), 0)} s after spawn",
           f"- unseen chases {s['chases']}: live-tracked {s['live']}, searched {s['searched']}, unclear {s['unclear']}"
           f" -> live-tracked share of conclusive {fmt(100 * s['live'] / concl if concl else None, 0)}%",
           f"- lost-target timer ran out {s['timer_runs']}, STUCK? {s['stuck']}, HEARD (someone reacted) {s['heard']}",
           f"- door kicks {s['door_kicks']} ({s['door_kicks'] / mins:.1f}/min); your gunshots {s['your_shots']}, bow {s['bow']}",
           f"- enemy gunshots {s['enemy_shots']} ({s['enemy_shots'] / mins:.1f}/min), harmless {s['enemy_harmless']}"
           f" ({fmt(100 * s['enemy_harmless'] / s['enemy_shots'] if s['enemy_shots'] else None, 0)}%); visibility avg {fmt(avg(s['vis']), 2)}"]
    if m:
        out.append(f"- Fix: told {m['told']} (avg age {fmt(avg(m['told_age']))} s), reached {m['reached']}, stopped short "
                   f"{m['stopped_short']}, couldn't reach {m['couldnt_reach']}, search over {m['search_over']}, gave up {m['gave_up']}, guesses {m['guesses']}")
        if m['dodged'] + m['hit'] + m['free_miss']:
            out.append(f"- Physical Dodge: dodged {m['dodged']}, hit {m['hit']} (hurt {m['hurt']}), free miss {m['free_miss']}")
    return '\n'.join(out)


if __name__ == '__main__':
    a = sys.argv[1:]
    if a and a[0] == '--game':
        game = a[1]
        for name in a[2:]:
            sp = os.path.join(game, 'UserData', 'EnemyAwarenessLog', name)
            print(report(sp, find_melon_log(game, name)), end='\n\n')
    elif a:
        print(report(a[0], a[1] if len(a) > 1 else None))
    else:
        print(__doc__)
