"""Assemble Unity's captured, unmodified sprite frames into a review GIF (requires Pillow)."""
from pathlib import Path
from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / "Logs" / "TravelReview"
TYPES = ["dash", "flight", "burrow", "swim", "blink", "bounce"]
original = Image.open(OUT / "original-runtime.png").convert("RGBA")
available = [kind for kind in TYPES if all((OUT / f"{kind}_{i}.png").exists() for i in range(8))]


def review(kinds, name):
    frames = []
    for index in range(8):
        image = Image.new("RGB", (560, 240 * len(kinds)), "#242633")
        draw = ImageDraw.Draw(image)
        for row, kind in enumerate(kinds):
            base = row * 240
            draw.text((32, base + 12), "ORIGINAL", fill="#f2e8d8")
            draw.text((286, base + 12), kind.upper() + " - ORIGINAL PIXELS", fill="#c4b5e6")
            ref = original.resize((original.width * 5, original.height * 5), Image.Resampling.NEAREST)
            image.paste(ref, (45, base + 215 - (original.height - 1) * 5), ref)
            pose = Image.open(OUT / f"{kind}_{index}.png").convert("RGBA")
            pose = pose.resize((pose.width * 5, pose.height * 5), Image.Resampling.NEAREST)
            image.paste(pose, (260, base + 215 - (44 - 3) * 5), pose)
            draw.line((25, base + 220, 535, base + 220), fill="#444459")
        frames.append(image)
    frames[0].save(OUT / name, save_all=True, append_images=frames[1:],
                   duration=[130, 130, 110, 110, 110, 110, 130, 300], loop=0, optimize=False, disposal=2)


if "flight" in available:
    review(["flight"], "rabbit-flight-original-comparison.gif")
if len(available) == 6:
    review(TYPES, "rabbit-six-motions.gif")
    sheet = Image.open(OUT / "rabbit-motion-comparison.png")
    sheet.resize((sheet.width * 2, sheet.height * 2), Image.Resampling.NEAREST).save(OUT / "rabbit-motion-comparison-2x.png")
