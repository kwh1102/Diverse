"""Compatibility entry point for current inventory asset authoring.

Layout is authored in UIBuilderInventoryArtwork.cs, not patched from a static mock.
The original ImageGen satchel body and handle are restored from ArtSource/InventoryPolish.
"""
import sys
sys.dont_write_bytecode=True
from author_combat_ui_v2 import ui

if __name__=='__main__':
    ui()
    print('V2 inventory PNGs imported. Apply layout with Diverse/가방 및 전투 아트 V2 반영 in Unity.')
