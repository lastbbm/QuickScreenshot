from PIL import Image, ImageDraw
import os
import struct
import sys

root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
assets = os.path.join(root, "assets")
source_path = os.path.join(assets, "QuickScreenshot-icon-source.png")
if not os.path.exists(source_path):
    raise SystemExit("Missing " + source_path)

sizes = [16, 20, 24, 32, 48, 256]
source = Image.open(source_path).convert("RGBA")

payloads = []
for size in sizes:
    image = source.resize((size, size), Image.Resampling.LANCZOS)
    mask = Image.new("L", (size, size), 0)
    draw = ImageDraw.Draw(mask)
    radius = max(1, round(size * 0.18))
    draw.rounded_rectangle((0, 0, size - 1, size - 1), radius=radius, fill=255)
    image.putalpha(mask)

    if size >= 256:
        import io
        buffer = io.BytesIO()
        image.save(buffer, "PNG", optimize=True)
        payloads.append((size, buffer.getvalue()))
        image.save(os.path.join(assets, "QuickScreenshot-256.png"), "PNG", optimize=True)
        continue

    rgba = image.tobytes()
    xor = bytearray()
    for y in range(size - 1, -1, -1):
        for x in range(size):
            offset = (y * size + x) * 4
            r, g, b, a = rgba[offset], rgba[offset + 1], rgba[offset + 2], rgba[offset + 3]
            xor += bytes((b, g, r, a))

    stride = ((size + 31) // 32) * 4
    and_mask = bytearray(stride * size)
    for y in range(size - 1, -1, -1):
        row = size - 1 - y
        for x in range(size):
            alpha = rgba[(y * size + x) * 4 + 3]
            if alpha == 0:
                and_mask[row * stride + x // 8] |= 1 << (7 - (x % 8))

    bitmap_info = struct.pack(
        "<IiiHHIIiiII", 40, size, size * 2, 1, 32, 0,
        len(xor) + len(and_mask), 0, 0, 0, 0)
    payloads.append((size, bitmap_info + bytes(xor) + bytes(and_mask)))

header = bytearray(struct.pack("<HHH", 0, 1, len(payloads)))
offset = 6 + 16 * len(payloads)
for size, data in payloads:
    width = 0 if size >= 256 else size
    height = 0 if size >= 256 else size
    header += struct.pack("<BBBBHHII", width, height, 0, 0, 1, 32, len(data), offset)
    offset += len(data)

ico_path = os.path.join(assets, "QuickScreenshot.ico")
with open(ico_path, "wb") as stream:
    stream.write(header)
    for _, data in payloads:
        stream.write(data)

print("Built", ico_path, os.path.getsize(ico_path), "bytes")
