"""Genera recursos/marca/testado.ico y testado.png: hoja con renglones, algunos testados."""
from pathlib import Path
from PIL import Image, ImageDraw

RAIZ = Path(__file__).resolve().parent.parent / "recursos" / "marca"
L = 1024
GUINDA = (109, 33, 53, 255)
HOJA = (255, 255, 255, 255)
RENGLON = (176, 170, 160, 255)
TESTADO = (20, 20, 20, 255)


def dibujar(lado: int) -> Image.Image:
    img = Image.new("RGBA", (L, L), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    d.rounded_rectangle((32, 32, L - 32, L - 32), radius=200, fill=GUINDA)
    # Hoja con esquina doblada.
    x0, y0, x1, y1, doblez = 250, 170, 774, 854, 130
    d.polygon([(x0, y0), (x1 - doblez, y0), (x1, y0 + doblez), (x1, y1), (x0, y1)], fill=HOJA)
    d.polygon([(x1 - doblez, y0), (x1 - doblez, y0 + doblez), (x1, y0 + doblez)], fill=(225, 214, 216, 255))
    # Renglones: (inicio, fin, testado)
    renglones = [(0.00, 0.55, False), (0.00, 0.30, False), (0.35, 0.95, True), (0.00, 0.95, False),
                 (0.00, 0.40, True), (0.45, 0.80, False), (0.00, 0.70, False)]
    margen, ancho = x0 + 60, (x1 - 60) - (x0 + 60)
    alto, paso, y = 46, 82, y0 + 150
    for ini, fin, testado in renglones:
        xa, xb = margen + ini * ancho, margen + fin * ancho
        if testado:
            d.rounded_rectangle((xa, y - 8, xb, y + alto + 8), radius=10, fill=TESTADO)
        else:
            d.rounded_rectangle((xa, y + 10, xb, y + alto - 10), radius=13, fill=RENGLON)
        y += paso
    return img.resize((lado, lado), Image.LANCZOS)


def main() -> None:
    RAIZ.mkdir(parents=True, exist_ok=True)
    grande = dibujar(256)
    grande.save(RAIZ / "testado.png")
    grande.save(RAIZ / "testado.ico", sizes=[(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)])


if __name__ == "__main__":
    main()
