"""Register the authored pivots of the Combat V4 source atlas at native size."""
from pathlib import Path
import re
from uuid import NAMESPACE_URL, uuid5

from PIL import Image

ROOT = Path(__file__).resolve().parents[1]
RESOURCES = ROOT / "Assets/_Project/Resources"
SPRITES = RESOURCES / "Sprites/Combat"
SOURCE = ROOT / "ArtSource/CombatV4/impact_atlas.png"
FAMILIES = (
    ("bluntHit", ((159, 192), (125, 192), (125, 192), (123, 192)), .16),
    ("daggerHit", ((163, 178), (151, 178), (151, 178), (151, 178)), .25),
    ("gunFlash", ((162, 145), (121, 145), (109, 145), (108, 145)), .21),
    ("crystalHit", ((161, 146), (117, 146), (111, 146), (111, 146)), .16),
)


def main():
    atlas = Image.open(SOURCE).convert("RGBA")
    width, height = atlas.size
    library = RESOURCES / "CombatVisualLibrary.asset"
    data = library.read_text(encoding="utf-8")
    template = (SPRITES / "cutHit_0.png.meta").read_text(encoding="utf-8")
    for row, (family, pivots, factor) in enumerate(FAMILIES):
        entry = f"  {family}:\n"
        for column, (pivot_x, pivot_y) in enumerate(pivots):
            cell = atlas.crop((column * width // 4, row * height // 4,
                               (column + 1) * width // 4, (row + 1) * height // 4))
            cell = cell.resize((round(cell.width * factor), round(cell.height * factor)), Image.Resampling.BOX)
            cell.putalpha(cell.getchannel("A").point(lambda alpha: 255 if alpha >= 148 else 0))
            frame = Image.new("RGBA", (64, 64))
            frame.paste(cell, (32 - round(pivot_x * factor), 32 - round(pivot_y * factor)))
            name = f"combatV4_{family}_{column}.png"
            frame.save(SPRITES / name)
            meta = SPRITES / (name + ".meta")
            guid = uuid5(NAMESPACE_URL, "Diverse/CombatV4/" + name).hex
            meta.write_text(re.sub(r"(?m)^guid: .+$", f"guid: {guid}", template, count=1), encoding="utf-8")
            entry += f"  - {{fileID: 21300000, guid: {guid}, type: 3}}\n"
        pattern = rf"(?m)^  {family}:\n(?:  -[^\n]*\n)*"
        data = re.sub(pattern, lambda _: entry, data) if re.search(pattern, data) else data + entry
    library.write_text(data, encoding="utf-8")


if __name__ == "__main__":
    main()
