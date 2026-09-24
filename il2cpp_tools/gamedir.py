"""Where the game is installed.

Order: the GUNMAN_CONTRACTS_DIR environment variable, then a one-line game_dir.txt next to this
file (git-ignored, for your own machine), then the default Steam library path.
"""
import os

_here = os.path.dirname(os.path.abspath(__file__))
_local = os.path.join(_here, 'game_dir.txt')


def game_dir():
    d = os.environ.get('GUNMAN_CONTRACTS_DIR')
    if not d and os.path.exists(_local):
        d = open(_local, encoding='utf-8').read().strip()
    return d or r"C:\Program Files (x86)\Steam\steamapps\common\Gunman Contracts - Stand Alone"
