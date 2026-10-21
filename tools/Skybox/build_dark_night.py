"""Adapt the ambientCG star panorama and NASA lunar albedo to a dark skybox.

Requires Python, Pillow, numpy and OpenCV. Source URLs and credits are in
assets/mmd/engine/Resources/Skybox/README.md.
Usage: python build_dark_night.py STARS.jpg MOON_ALBEDO.jpg OUTPUT.jpg
"""

from pathlib import Path
import argparse
import math

import cv2
import numpy as np
from PIL import Image


def build(star_path: Path, moon_path: Path, output_path: Path) -> None:
    source = np.asarray(Image.open(star_path).convert("RGB"), dtype=np.float32)
    height, width, _ = source.shape
    if width != 2 * height:
        raise ValueError("The star panorama must have a 2:1 aspect ratio.")

    # Separate local star contrast from the low-frequency sky background, so
    # darkening the sky does not dim the stars along with it.
    smooth = cv2.GaussianBlur(source, (0, 0), sigmaX=width / 2048)
    source -= smooth
    np.maximum(source, 0, out=source)
    source *= 2.4
    smooth *= 0.025
    source += smooth
    del smooth
    latitude = np.pi * (0.5 - (np.arange(height, dtype=np.float32) + 0.5) / height)
    horizon = np.cos(latitude)[:, None, None] ** 6
    base = np.array([3.0, 6.0, 13.0], dtype=np.float32) + horizon * np.array([3.0, 4.0, 6.0], dtype=np.float32)
    sky = source
    sky += base

    # Bake a textured moon at the same heading as the previous moonrise skybox.
    # A 2.8 degree disk is intentionally larger than the astronomical Moon so
    # its surface remains legible in an editor viewport. Use spherical angular
    # distance to keep the disk circular when the panorama is rendered.
    longitude = 0.2 * np.pi
    elevation = math.radians(28)
    radius = math.radians(1.4)
    cx = (longitude / (2 * np.pi) + 0.5) * width
    cy = (0.5 - elevation / np.pi) * height
    margin_y = math.ceil(radius * height / np.pi * 5)
    margin_x = math.ceil(margin_y / math.cos(elevation))
    x0, x1 = max(0, int(cx) - margin_x), min(width, int(cx) + margin_x + 1)
    y0, y1 = max(0, int(cy) - margin_y), min(height, int(cy) + margin_y + 1)
    x = np.arange(x0, x1)[None, :]
    y = np.arange(y0, y1)[:, None]
    lon = 2 * np.pi * ((x + 0.5) / width - 0.5)
    lat = np.pi * (0.5 - (y + 0.5) / height)
    delta = lon - longitude
    forward = np.sin(lat) * math.sin(elevation) + np.cos(lat) * math.cos(elevation) * np.cos(delta)
    east = np.cos(lat) * np.sin(delta)
    north = np.sin(lat) * math.cos(elevation) - np.cos(lat) * math.sin(elevation) * np.cos(delta)
    angular_distance = np.arccos(np.clip(forward, -1, 1))
    disk_radius = angular_distance / radius
    px = east / math.sin(radius)
    py = north / math.sin(radius)
    pz = np.sqrt(np.maximum(1 - px * px - py * py, 0))

    albedo = np.asarray(Image.open(moon_path).convert("RGB"), dtype=np.float32)
    mh, mw, _ = albedo.shape
    map_x = ((0.5 + np.arctan2(px, pz) / (2 * np.pi)) * mw).astype(np.float32)
    map_y = ((0.5 - np.arcsin(np.clip(py, -1, 1)) / np.pi) * mh).astype(np.float32)
    surface = cv2.remap(albedo, map_x, map_y, cv2.INTER_LINEAR, borderMode=cv2.BORDER_REPLICATE)
    gray = surface.mean(axis=2)
    surface = np.clip((gray - 115) * 1.25 + 100, 45, 225)
    illumination = np.maximum(-0.25 * px + 0.12 * py + 0.961 * pz, 0)
    moon = surface[..., None] * (0.32 + 0.68 * illumination[..., None]) * np.array([0.94, 0.97, 1.0])
    pixel_angle = np.pi / height
    alpha = np.clip((radius - angular_distance) / pixel_angle + 0.5, 0, 1)[..., None]
    patch = sky[y0:y1, x0:x1]
    halo = np.exp(-np.maximum(disk_radius - 1, 0) ** 2 / 0.7) * 5.0
    patch[:] = (patch + halo[..., None] * np.array([0.65, 0.8, 1.0])) * (1 - alpha) + moon * alpha

    # Make the seam and pole limits continuous for equirectangular sampling.
    seam = (sky[:, 0] + sky[:, -1]) / 2
    sky[:, 0] = seam
    sky[:, -1] = seam
    sky[0] = np.array([3.0, 6.0, 13.0])
    sky[-1] = np.array([3.0, 6.0, 13.0])
    np.rint(sky, out=sky)
    np.clip(sky, 0, 255, out=sky)
    image = Image.fromarray(sky.astype(np.uint8))
    output_path.parent.mkdir(parents=True, exist_ok=True)
    image.save(output_path, quality=97, subsampling=0)
    print(f"Saved {output_path}: {width} x {height}, {output_path.stat().st_size:,} bytes")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("stars", type=Path)
    parser.add_argument("moon", type=Path)
    parser.add_argument("output", type=Path)
    args = parser.parse_args()
    build(args.stars, args.moon, args.output)
