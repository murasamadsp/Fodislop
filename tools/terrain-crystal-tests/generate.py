"""Extract the original OpenMines phase map without resizing or recoloring."""
from pathlib import Path
from PIL import Image

ROOT = Path(__file__).resolve().parents[2]
SOURCE = ROOT.parent / 'OpenMines/client/Assets/Images/terrain.png'
TARGET = ROOT / 'Assets/Resources/PrismaticFlowMap.bytes'

if __name__ == '__main__':
    image = Image.open(SOURCE).convert('RGBA')
    assert image.size == (2048, 2048)
    # CellRender dx2=25,dy2=23,wx2=10,wy2=8; one unit = 16 pixels.
    TARGET.write_bytes(image.crop((400,368,560,496)).transpose(Image.Transpose.FLIP_TOP_BOTTOM).tobytes())
