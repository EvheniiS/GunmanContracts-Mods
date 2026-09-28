"""Combine the two passes from render_arsenal_icon.py into out/billy_clubs_icon.png (see that script)."""
import os

from PIL import Image, ImageChops

HERE = os.path.dirname(os.path.abspath(__file__))
clay = Image.open(os.path.join(HERE, "out", "icon_pass_clay.png")).convert("RGBA")
tex = Image.open(os.path.join(HERE, "out", "icon_pass_texture.png")).convert("RGBA")
lum = tex.convert("L")
lo, hi = lum.getextrema()
lo = max(lo, 1)
tone = lum.point(lambda v: int(255 * (0.80 + 0.20 * min(1.0, max(0.0, (v - lo) / max(1, hi - lo))))))
a = clay.getchannel("A")
res = ImageChops.multiply(clay.convert("L"), tone)
final = Image.merge("RGBA", (res, res, res, a))
final.save(os.path.join(HERE, "out", "billy_clubs_icon.png"))
for p in ("clay", "texture"):
    os.remove(os.path.join(HERE, "out", f"icon_pass_{p}.png"))
print("wrote out/billy_clubs_icon.png", final.size)
