"""Derive a tangent-space asphalt normal texture from StreetlightAsphalt.png."""
from pathlib import Path

import numpy as np
from PIL import Image, ImageFilter

root = Path(__file__).parent
source = Image.open(root / "StreetlightAsphalt.png").convert("RGB")
gray = source.convert("L")
large = np.asarray(gray.filter(ImageFilter.GaussianBlur(9)), np.float32) / 255.0
fine = np.asarray(gray.filter(ImageFilter.GaussianBlur(0.8)), np.float32) / 255.0
height = (fine - large) * 0.8 + large * 0.12

# The 1 px gradient preserves the aggregate while the broader relief stays subtle.
dx = np.roll(height, -1, axis=1) - np.roll(height, 1, axis=1)
dy = np.roll(height, -1, axis=0) - np.roll(height, 1, axis=0)
# Image rows grow downward, opposite the texture V axis.
normal = np.stack((-dx * 3.0, dy * 3.0, np.ones_like(height)), axis=2)
normal /= np.linalg.norm(normal, axis=2, keepdims=True)
rgb = np.clip((normal * 0.5 + 0.5) * 255.0, 0, 255).astype(np.uint8)
# Opaque alpha keeps the X component correct in both RGB and RG-or-AG decoders.
rgba = np.dstack((rgb, np.full(height.shape, 255, dtype=np.uint8)))
Image.fromarray(rgba, "RGBA").save(root / "StreetlightAsphaltNormal.png", optimize=True)
