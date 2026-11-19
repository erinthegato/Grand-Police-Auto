#!/usr/bin/env python3
"""
Grand Police Auto - 2D sprite generator (gritty top-down vector style).

Every sprite is drawn as SVG in code, then rendered to PNG at 2x with rsvg-convert.
Scale contract: 1 SVG unit = 2 px, sprites import at 64 PPU  ->  1 SVG unit = 1/32 m.
  car body 80 x 150 units  ~ 2.5 m x 4.7 m
  ground tile 128 units    = 4 m

Usage:  python3 generate_sprites.py <out_dir>
Re-running overwrites PNGs with identical output (fixed noise seeds), so Unity keeps the same
sprite assets and .meta GUIDs.
"""
import os
import subprocess
import sys

OUT = sys.argv[1] if len(sys.argv) > 1 else "out"

# ---------------------------------------------------------------- shared helpers
OUTLINE = "#161616"
# Style switch. False = clean look of the original approved style test (current choice).
# True = gritty variant (noise speckle + grime blotches).
GRIT = False


def grit_defs(seed, fine=0.85, coarse=0.035, fine_alpha=1.5, coarse_alpha=1.1):
    """Two noise layers clipped to the shape: fine speckle (wear) + large blotches (grime).
    With GRIT off this is a pass-through filter so every sprite keeps the same structure."""
    if not GRIT:
        return f'<filter id="grit{seed}"><feOffset dx="0" dy="0"/></filter>'
    return f'''
<filter id="grit{seed}" x="0" y="0" width="1" height="1" color-interpolation-filters="sRGB">
  <feTurbulence type="fractalNoise" baseFrequency="{fine}" numOctaves="2" seed="{seed}" result="f"/>
  <feColorMatrix in="f" type="matrix" values="0 0 0 0 0.06  0 0 0 0 0.055  0 0 0 0 0.05  0 0 0 -{fine_alpha} 0.95" result="fa"/>
  <feTurbulence type="fractalNoise" baseFrequency="{coarse}" numOctaves="3" seed="{seed + 7}" result="c"/>
  <feColorMatrix in="c" type="matrix" values="0 0 0 0 0.09  0 0 0 0 0.075  0 0 0 0 0.05  0 0 0 -{coarse_alpha} 0.62" result="ca"/>
  <feMerge result="m"><feMergeNode in="ca"/><feMergeNode in="fa"/></feMerge>
  <feComposite in="m" in2="SourceAlpha" operator="in" result="d"/>
  <feMerge><feMergeNode in="SourceGraphic"/><feMergeNode in="d"/></feMerge>
</filter>'''


def svg(w, h, body, defs=""):
    return (f'<svg xmlns="http://www.w3.org/2000/svg" width="{w}" height="{h}" viewBox="0 0 {w} {h}">'
            f'<defs>{defs}</defs>{body}</svg>')


def write(name, content):
    os.makedirs(OUT, exist_ok=True)
    path_svg = os.path.join(OUT, name + ".svg")
    path_png = os.path.join(OUT, name + ".png")
    with open(path_svg, "w") as f:
        f.write(content)
    subprocess.run(["rsvg-convert", "-z", "2", path_svg, "-o", path_png], check=True)
    os.remove(path_svg)


# ---------------------------------------------------------------- vehicles
def car(name, seed, paint, roof=None, stripe=None, extras="", bumper="#1a1a1b", dirt=0.0):
    """paint = (edge, mid). Car faces UP (front at y=0). No baked shadow: see car_shadow."""
    edge, mid = paint
    roof = roof or mid
    defs = grit_defs(seed, fine=0.9, fine_alpha=2.1, coarse_alpha=1.3) + f'''
<linearGradient id="p{seed}" x1="0" x2="1"><stop offset="0" stop-color="{edge}"/><stop offset=".5" stop-color="{mid}"/><stop offset="1" stop-color="{edge}"/></linearGradient>
<linearGradient id="gl{seed}" y1="0" y2="1"><stop offset="0" stop-color="#9fc4d8"/><stop offset="1" stop-color="#2f4b5c"/></linearGradient>
<linearGradient id="dirt{seed}" y1="0" y2="1"><stop offset="0" stop-color="#2a2318" stop-opacity="{dirt*0.35}"/><stop offset=".5" stop-color="#2a2318" stop-opacity="0"/><stop offset="1" stop-color="#2a2318" stop-opacity="{dirt*0.6}"/></linearGradient>
<linearGradient id="side{seed}" x1="0" x2="1"><stop offset="0" stop-color="#20190f" stop-opacity="{dirt*0.55}"/><stop offset=".18" stop-color="#20190f" stop-opacity="0"/><stop offset=".82" stop-color="#20190f" stop-opacity="0"/><stop offset="1" stop-color="#20190f" stop-opacity="{dirt*0.55}"/></linearGradient>'''
    stripe_svg = f'<rect x="7" y="68" width="66" height="12" fill="{stripe}"/>' if stripe else ""
    body = f'''
<g>
 <rect x="4" y="28" width="8" height="21" rx="2.5" fill="#121213"/><rect x="68" y="28" width="8" height="21" rx="2.5" fill="#121213"/>
 <rect x="4" y="101" width="8" height="21" rx="2.5" fill="#121213"/><rect x="68" y="101" width="8" height="21" rx="2.5" fill="#121213"/>
 <g filter="url(#grit{seed})">
  <rect x="7" y="5" width="66" height="140" rx="16" fill="url(#p{seed})"/>
  {stripe_svg}
  <rect x="7" y="5" width="66" height="140" rx="16" fill="url(#dirt{seed})"/>
  <rect x="7" y="5" width="66" height="140" rx="16" fill="url(#side{seed})"/>
  <path d="M13 3 L67 3 Q73 4 73 10 L7 10 Q7 4 13 3 Z" fill="{bumper}"/>
  <path d="M7 140 L73 140 Q73 146 67 147 L13 147 Q7 146 7 140 Z" fill="{bumper}"/>
  <rect x="17" y="58" width="46" height="50" rx="6" fill="{roof}"/>
 </g>
 <rect x="7" y="5" width="66" height="140" rx="16" fill="none" stroke="{OUTLINE}" stroke-width="2.6"/>
 <path d="M14 45 Q40 34 66 45 L62 59 Q40 53 18 59 Z" fill="url(#gl{seed})" stroke="{OUTLINE}" stroke-width="2"/>
 <path d="M22 44 Q32 39 41 39 L36 47 Q28 47 24 50 Z" fill="#ffffff" opacity=".14"/>
 <rect x="17" y="58" width="46" height="50" rx="6" fill="none" stroke="{OUTLINE}" stroke-width="2"/>
 <path d="M18 108 Q40 113 62 108 L64 121 Q40 127 16 121 Z" fill="url(#gl{seed})" stroke="{OUTLINE}" stroke-width="2"/>
 <path d="M12 62 L16 62 L16 104 L12 104 Z" fill="#2f4b5c" opacity=".9"/><path d="M64 62 L68 62 L68 104 L64 104 Z" fill="#2f4b5c" opacity=".9"/>
 <rect x="13" y="7" width="13" height="5" rx="2" fill="#fff6c8" stroke="{OUTLINE}" stroke-width="1"/>
 <rect x="54" y="7" width="13" height="5" rx="2" fill="#fff6c8" stroke="{OUTLINE}" stroke-width="1"/>
 <rect x="12" y="139" width="13" height="4.5" rx="1.5" fill="#c0201b" stroke="{OUTLINE}" stroke-width="1"/>
 <rect x="55" y="139" width="13" height="4.5" rx="1.5" fill="#c0201b" stroke="{OUTLINE}" stroke-width="1"/>
 <path d="M7 34 L3 36 L3 40 L7 40 Z" fill="{OUTLINE}"/><path d="M73 34 L77 36 L77 40 L73 40 Z" fill="{OUTLINE}"/>
 {extras}
</g>'''
    write(name, svg(80, 150, body, defs))


def build_vehicles():
    lightbar = f'''
<rect x="16" y="64" width="48" height="10" rx="3.5" fill="#151515" stroke="{OUTLINE}" stroke-width="1.6"/>
<rect x="18.5" y="66" width="20" height="6" rx="2" fill="#e8302a"/><rect x="41.5" y="66" width="20" height="6" rx="2" fill="#2b62e8"/>
<rect x="38.5" y="66" width="3" height="6" fill="#bbb"/>
<text x="40" y="99" font-family="Arial Black,Arial" font-weight="900" font-size="9" fill="#1d3a7a" text-anchor="middle">4127</text>
<rect x="30" y="2" width="20" height="5" rx="1" fill="#0f0f10" stroke="{OUTLINE}" stroke-width="1"/>'''
    car("car_police_cruiser", 11, ("#d9dde2", "#ffffff"), roof="#f4f6f8", stripe="#1d3a7a", extras=lightbar)
    car("car_sedan_maroon", 23, ("#7a1414", "#c42c24"), roof="#a32019")
    car("car_sedan_gunmetal", 37, ("#3e454c", "#7a848d"), roof="#5f6870")
    car("car_sedan_olive", 41, ("#3f4a26", "#788a46"), roof="#5f6e37")
    taxi_sign = f'''<rect x="27" y="75" width="26" height="9" rx="2" fill="#141414" stroke="{OUTLINE}" stroke-width="1"/>
<text x="40" y="82" font-family="Arial" font-weight="700" font-size="6.5" fill="#f6c518" text-anchor="middle">TAXI</text>
'''
    car("car_taxi", 53, ("#c99a0a", "#f6c518"), roof="#e9b812", extras=taxi_sign)

    # Soft contact shadow under every car. Stays fixed while the body sprite rolls/pitches.
    defs = '<filter id="b" x="-30%" y="-30%" width="160%" height="160%"><feGaussianBlur stdDeviation="4.5"/></filter>'
    write("car_shadow", svg(96, 166, '<rect x="10" y="10" width="76" height="146" rx="18" fill="#000" opacity=".32" filter="url(#b)"/>', defs))

    # Emergency light glows (additive-looking radial blobs), flashed by the lightbar script.
    for nm, col in (("glow_red", "#ff2a1f"), ("glow_blue", "#2f6bff"), ("glow_white", "#fff6dc")):
        d = f'<radialGradient id="g"><stop offset="0" stop-color="{col}" stop-opacity=".95"/><stop offset=".35" stop-color="{col}" stop-opacity=".45"/><stop offset="1" stop-color="{col}" stop-opacity="0"/></radialGradient>'
        write(nm, svg(64, 64, '<circle cx="32" cy="32" r="32" fill="url(#g)"/>', d))
    # Headlight cone, projected forward from the car (points UP).
    d = '<radialGradient id="h" cx=".5" cy="1" r="1"><stop offset="0" stop-color="#fff3c4" stop-opacity=".55"/><stop offset="1" stop-color="#fff3c4" stop-opacity="0"/></radialGradient>'
    write("headlight_cone", svg(64, 128, '<path d="M24 128 L0 0 L64 0 L40 128 Z" fill="url(#h)"/>', d))


# ---------------------------------------------------------------- people
def unarmed_arms(uniform, skin, step):
    """Arms at the sides with hands slightly ahead of the shoulders (character faces UP, so ahead = smaller y).
    Walk frames swing each arm opposite its foot."""
    swing = {0: 0, 1: 4, 2: -4}[step]          # step 1: left foot forward -> right arm forward
    ly, ry = 27 + swing, 27 - swing             # hand y for left / right arm
    return (f'<path d="M16.5 36 Q14.5 {ly + 5} 17.5 {ly} L21.5 {ly + 1} Q20 {ly + 5} 21.5 36 Z" fill="{uniform}" stroke="{OUTLINE}" stroke-width="1.5"/>'
            f'<path d="M47.5 36 Q49.5 {ry + 5} 46.5 {ry} L42.5 {ry + 1} Q44 {ry + 5} 42.5 36 Z" fill="{uniform}" stroke="{OUTLINE}" stroke-width="1.5"/>'
            f'<circle cx="19.5" cy="{ly}" r="2.6" fill="{skin}" stroke="{OUTLINE}" stroke-width="1"/>'
            f'<circle cx="44.5" cy="{ry}" r="2.6" fill="{skin}" stroke="{OUTLINE}" stroke-width="1"/>')


def person(name, seed, uniform, uniform_dark, skin, head_top, weapon=True, cap=True, step=0, hair_tail=None, shoes="#1b1b1b"):
    """Character faces UP. 64x64 units. Arms extended forward when armed."""
    defs = grit_defs(seed, fine=1.2, coarse=0.08, fine_alpha=1.7, coarse_alpha=1.2)
    arms = f'''
 <path d="M21 30 Q23 18 30 13 L33.5 14.5 Q29 22 28.5 31 Z" fill="{uniform}" stroke="{OUTLINE}" stroke-width="1.5"/>
 <path d="M43 30 Q41 19 35 14 L31.5 15.5 Q35.5 23 35.5 31 Z" fill="{uniform}" stroke="{OUTLINE}" stroke-width="1.5"/>
 <rect x="29.8" y="2" width="4.6" height="14" rx="1" fill="#1a1a1a" stroke="{OUTLINE}" stroke-width="1"/>
 <circle cx="32.2" cy="14.5" r="3.3" fill="{skin}" stroke="{OUTLINE}" stroke-width="1"/>''' if weapon else unarmed_arms(uniform, skin, step)
    head = (f'<circle cx="32" cy="34" r="7.6" fill="{head_top}" stroke="{OUTLINE}" stroke-width="1.8"/>'
            f'<path d="M25 30.5 Q32 25.5 39 30.5 L38 33 Q32 29.5 26 33 Z" fill="#0b0d12"/>'
            f'<circle cx="32" cy="35.5" r="1.8" fill="#8f7a3a"/>') if cap else \
           (f'<circle cx="32" cy="34" r="7.2" fill="{head_top}" stroke="{OUTLINE}" stroke-width="1.8"/>')
    # Walk frames: feet poke out ahead/behind the body (character faces up, so "ahead" is smaller y).
    feet = ""
    if step:
        fl, fr = (22, 47) if step == 1 else (47, 22)
        feet = (f'<ellipse cx="26" cy="{fl}" rx="3.4" ry="4.6" fill="{shoes}" stroke="{OUTLINE}" stroke-width="1"/>'
                f'<ellipse cx="38" cy="{fr}" rx="3.4" ry="4.6" fill="{shoes}" stroke="{OUTLINE}" stroke-width="1"/>')
    tail = f'<ellipse cx="32" cy="43" rx="4" ry="6" fill="{hair_tail}" stroke="{OUTLINE}" stroke-width="1.2"/>' if hair_tail else ""
    body = f'''
<g>
 {feet}
 {arms}
 <g filter="url(#grit{seed})">
  <ellipse cx="32" cy="35" rx="16" ry="9.5" fill="{uniform}"/>
  <rect x="17" y="33" width="30" height="3" fill="{uniform_dark}"/>
 </g>
 <ellipse cx="32" cy="35" rx="16" ry="9.5" fill="none" stroke="{OUTLINE}" stroke-width="2"/>
 {tail}
 {head}
</g>'''
    write(name, svg(64, 64, body, defs))


PEOPLE = [
    # name, seed, top, top_dark, skin, head/hair, weapon, cap, hair_tail
    ("officer_armed", 61, "#24345c", "#12192e", "#c98f6b", "#1a1f2b", True, True, None),
    ("officer_unarmed", 62, "#24345c", "#12192e", "#c98f6b", "#1a1f2b", False, True, None),
    ("civ_jacket_brown", 63, "#7a5634", "#4f3720", "#c49a7c", "#2a2018", False, False, None),
    ("civ_hoodie_grey", 64, "#6d7278", "#474b50", "#7b5a43", "#1f2124", False, False, None),
    ("civ_coat_green", 65, "#4b6340", "#2f3f28", "#d6ae90", "#5a3b22", False, False, None),
    ("civ_suit_navy", 66, "#2b3348", "#1a1f2d", "#e0b896", "#3b2a1c", False, False, None),
    ("civ_athletic_red", 67, "#b8322a", "#7d211b", "#8d5f43", "#151515", False, False, None),
    ("civ_dress_purple", 68, "#6b3f7a", "#47294f", "#f0c8a8", "#7a4a26", False, False, "#7a4a26"),
    ("civ_jacket_tan_f", 69, "#b08a5a", "#7c6040", "#5b3d2a", "#111111", False, False, "#111111"),
    ("civ_vest_orange", 70, "#d97a1f", "#9c5614", "#c49a7c", "#6b6b6b", False, False, None),
]


def build_people():
    for name, seed, top, dark, skin, head, weapon, cap, tail in PEOPLE:
        for step, suffix in ((0, ""), (1, "_walk_a"), (2, "_walk_b")):
            person(name + suffix, seed, top, dark, skin, head, weapon=weapon, cap=cap, step=step, hair_tail=tail)
    d = '<filter id="b" x="-40%" y="-40%" width="180%" height="180%"><feGaussianBlur stdDeviation="2.5"/></filter>'
    write("person_shadow", svg(64, 64, '<ellipse cx="32" cy="35" rx="18" ry="12" fill="#000" opacity=".28" filter="url(#b)"/>', d))


# ---------------------------------------------------------------- ground tiles (128 units = 4 m)
def tile(name, seed, base, content="", fine=0.7, coarse=0.02):
    defs = grit_defs(seed, fine=fine, coarse=coarse, fine_alpha=1.85, coarse_alpha=1.35)
    write(name, svg(128, 128, f'<g filter="url(#grit{seed})"><rect width="128" height="128" fill="{base}"/>{content}</g>', defs))


def build_tiles():
    cracks = ('<path d="M10 20 L22 31 L19 44 L31 58 M84 90 L97 99 L95 112 M60 8 L66 18" '
              'stroke="#2f3133" stroke-width="1" fill="none" opacity=".6"/>')
    oil = '<ellipse cx="78" cy="44" rx="14" ry="9" fill="#303234" opacity=".55"/>'
    tile("tile_asphalt", 101, "#3b3d40", cracks)
    tile("tile_asphalt_oil", 102, "#3b3d40", cracks + oil)
    tile("tile_asphalt_manhole", 103, "#3b3d40",
         '<circle cx="64" cy="64" r="15" fill="#35373a" stroke="#26282a" stroke-width="2"/>'
         '<path d="M52 64 L76 64 M64 52 L64 76 M55 56 L73 72 M73 56 L55 72" stroke="#2b2d2f" stroke-width="1.4"/>')
    # Lane tiles: road runs vertically through the tile.
    tile("tile_asphalt_lane_dash", 104, "#3b3d40",
         '<rect x="62" y="8" width="4" height="48" fill="#e8e2c8"/><rect x="62" y="72" width="4" height="48" fill="#e8e2c8"/>' + cracks)
    tile("tile_asphalt_lane_double_yellow", 105, "#3b3d40",
         '<rect x="59" y="0" width="3.5" height="128" fill="#d9a521"/><rect x="65.5" y="0" width="3.5" height="128" fill="#d9a521"/>')
    tile("tile_asphalt_stopline", 106, "#3b3d40", '<rect x="0" y="4" width="128" height="7" fill="#e8e2c8"/>')
    tile("tile_crosswalk", 107, "#3b3d40",
         "".join(f'<rect x="{x}" y="6" width="9" height="116" fill="#e8e2c8"/>' for x in range(6, 128, 17)))
    slabs = "".join(f'<path d="M0 {y} L128 {y}" stroke="#7d786f" stroke-width="1.2"/>' for y in (32, 64, 96)) + \
            "".join(f'<path d="M{x} 0 L{x} 128" stroke="#7d786f" stroke-width="1.2"/>' for x in (32, 64, 96))
    gum = "".join(f'<circle cx="{x}" cy="{y}" r="1.6" fill="#827d74"/>' for x, y in ((14, 80), (70, 22), (101, 107), (45, 50), (90, 70)))
    tile("tile_sidewalk", 111, "#8f8a80", slabs + gum, fine=0.9, coarse=0.03)
    # Sidewalk with the curb on its LEFT edge (rotate in Unity for other sides).
    tile("tile_sidewalk_curb", 112, "#8f8a80",
         slabs + gum + '<rect x="0" y="0" width="9" height="128" fill="#a19c92"/><rect x="0" y="0" width="2.5" height="128" fill="#2f3133" opacity=".8"/>',
         fine=0.9, coarse=0.03)
    tile("tile_sidewalk_corner", 113, "#8f8a80",
         slabs + '<path d="M0 0 L9 0 L9 119 L128 119 L128 128 L0 128 Z" fill="#a19c92"/>', fine=0.9, coarse=0.03)

    # Rooftops (tar + gravel), with vents / AC units / hatches. Buildings are drawn as roof tiles.
    tile("tile_roof_tar", 121, "#45474a",
         '<rect x="4" y="4" width="120" height="120" fill="none" stroke="#5a5c60" stroke-width="5"/>'
         '<rect x="20" y="22" width="22" height="16" fill="#4b4c4c" stroke="#111" stroke-width="1.5"/>'
         '<circle cx="31" cy="30" r="5" fill="#2c2d2d" stroke="#111"/>'
         '<rect x="80" y="76" width="16" height="16" fill="#3d3a35" stroke="#111" stroke-width="1.5"/>'
         '')
    tile("tile_roof_gravel", 122, "#77736a",
         '<rect x="4" y="4" width="120" height="120" fill="none" stroke="#5c5850" stroke-width="5"/>'
         '<rect x="60" y="18" width="40" height="28" fill="#6b6c69" stroke="#111" stroke-width="1.5"/>'
         '<path d="M64 22 L96 42 M96 22 L64 42" stroke="#3a3b39" stroke-width="1.5"/>'
         '<circle cx="34" cy="88" r="13" fill="#3f3a33" stroke="#111" stroke-width="1.5"/>'
         '<circle cx="34" cy="88" r="8" fill="#2a2621"/>', fine=1.1, coarse=0.04)
    tile("tile_roof_brick_edge", 123, "#45474a",
         '<rect x="0" y="0" width="128" height="10" fill="#8a4a36"/>'
         + "".join(f'<path d="M{x} 0 L{x} 10" stroke="#2a1712" stroke-width="1"/>' for x in range(8, 128, 12)))

    # Water, parks and special roofs (district map).
    tile("tile_water", 141, "#2c4a5e",
         '<path d="M8 20 Q24 14 40 20 T72 20 M60 58 Q76 52 92 58 T124 58 M14 96 Q30 90 46 96 T78 96" '
         'stroke="#3f6479" stroke-width="2.5" fill="none" opacity=".9"/>')
    tile("tile_grass", 142, "#4f7a3c",
         "".join(f'<path d="M{x} {y} l2 -5 l2 5" stroke="#62914a" stroke-width="1.2" fill="none"/>'
                 for x, y in ((12, 20), (40, 34), (88, 18), (110, 60), (26, 78), (70, 96), (100, 112), (54, 64))))
    tile("tile_pier", 143, "#7a6248",
         "".join(f'<path d="M0 {y} L128 {y}" stroke="#5e4a35" stroke-width="1.5"/>' for y in range(16, 128, 16))
         + '<circle cx="10" cy="8" r="2" fill="#4a3a2a"/><circle cx="118" cy="72" r="2" fill="#4a3a2a"/>')
    tile("tile_roof_light", 124, "#8e8c87",
         '<rect x="4" y="4" width="120" height="120" fill="none" stroke="#a8a6a0" stroke-width="5"/>'
         '<rect x="18" y="70" width="34" height="24" fill="#b9b7b1" stroke="#161616" stroke-width="1.5"/>'
         '<rect x="76" y="20" width="22" height="22" fill="#6d6b66" stroke="#161616" stroke-width="1.5"/>'
         '<circle cx="87" cy="31" r="6" fill="#4a4946"/>')
    tile("tile_roof_police", 125, "#3d4a63",
         '<rect x="4" y="4" width="120" height="120" fill="none" stroke="#56647f" stroke-width="5"/>'
         '<circle cx="64" cy="64" r="34" fill="none" stroke="#e8e2c8" stroke-width="4"/>'
         '<path d="M50 46 L50 82 M78 46 L78 82 M50 64 L78 64" stroke="#e8e2c8" stroke-width="5"/>')

    d = grit_defs(144, fine=1.0, coarse=0.05)
    write("prop_tree", svg(72, 72,
          '<ellipse cx="40" cy="40" rx="30" ry="28" fill="#000" opacity=".22"/>'
          '<circle cx="34" cy="34" r="28" fill="#3f6b2e" stroke="#1f3a17" stroke-width="2"/>'
          '<circle cx="26" cy="28" r="12" fill="#4f8239"/><circle cx="44" cy="40" r="10" fill="#4a7a35"/>'
          '<circle cx="38" cy="22" r="7" fill="#5c9443"/>', d))


# ---------------------------------------------------------------- props
def build_props():
    d = grit_defs(131, fine=1.0, coarse=0.06)
    write("prop_dumpster", svg(64, 40,
          f'<g filter="url(#grit131)"><rect x="2" y="2" width="60" height="36" rx="2" fill="#2f6a46"/>'
          f'<rect x="4" y="4" width="27" height="32" fill="#357650"/><rect x="33" y="4" width="27" height="32" fill="#327049"/></g>'
          f'<rect x="2" y="2" width="60" height="36" rx="2" fill="none" stroke="{OUTLINE}" stroke-width="2"/>'
          f'<path d="M32 3 L32 37" stroke="{OUTLINE}" stroke-width="1.5"/>', d))
    d = grit_defs(132, fine=1.4, coarse=0.1)
    write("prop_hydrant", svg(24, 24,
          f'<g filter="url(#grit132)"><circle cx="12" cy="12" r="8" fill="#c0281f"/><circle cx="12" cy="12" r="4" fill="#962019"/>'
          f'<rect x="2" y="10" width="20" height="4" fill="#a8231b"/></g><circle cx="12" cy="12" r="8" fill="none" stroke="{OUTLINE}" stroke-width="1.6"/>', d))
    d = grit_defs(133, fine=1.3, coarse=0.1)
    write("prop_trash_bags", svg(40, 32,
          f'<g filter="url(#grit133)"><ellipse cx="13" cy="15" rx="11" ry="10" fill="#2a2c2f"/>'
          f'<ellipse cx="27" cy="18" rx="10" ry="9" fill="#313337"/><ellipse cx="20" cy="9" rx="8" ry="6" fill="#383a3e"/></g>'
          f'<path d="M8 10 Q12 7 16 10 M24 14 Q28 12 31 15" stroke="#3a3c40" stroke-width="1"/>', d))
    d = grit_defs(134, fine=1.2, coarse=0.08)
    write("prop_signal_head", svg(20, 20,
          f'<rect x="2" y="2" width="16" height="16" rx="3" fill="#1d1e20" stroke="{OUTLINE}" stroke-width="1.6"/>'
          '<circle cx="10" cy="10" r="5" fill="#0c0c0c"/>'))
    d = '<radialGradient id="g"><stop offset="0" stop-color="#ffffff"/><stop offset=".45" stop-color="#ffffff" stop-opacity=".9"/><stop offset="1" stop-color="#ffffff" stop-opacity="0"/></radialGradient>'
    write("signal_lamp", svg(16, 16, '<circle cx="8" cy="8" r="8" fill="url(#g)"/>', d))
    write("prop_streetlight", svg(24, 24,
          f'<circle cx="12" cy="12" r="6" fill="#4a4c50" stroke="{OUTLINE}" stroke-width="1.6"/><circle cx="12" cy="12" r="2.5" fill="#fff1b0"/>', d))


if __name__ == "__main__":
    build_vehicles()
    build_people()
    build_tiles()
    build_props()
    print("sprites written to", OUT)
