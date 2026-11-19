#!/usr/bin/env python3
"""
Grand Police Auto - extra vehicle sprites (same style and scale as generate_sprites.py).
1 SVG unit = 1/32 m, rendered at 2x, imported at 64 PPU. Vehicles face UP (front at y=0).
Usage: python3 generate_vehicles.py <out_dir>
"""
import sys
import generate_sprites as G

OUTLINE = G.OUTLINE

# Body style -> (windshield start, windshield end, roof end, rear glass end) as fractions of length.
STYLES = {
    "sedan":   (0.30, 0.39, 0.72, 0.81),
    "compact": (0.27, 0.37, 0.82, 0.88),
    "coupe":   (0.33, 0.45, 0.66, 0.75),
    "suv":     (0.22, 0.31, 0.90, 0.94),
    "pickup":  (0.22, 0.31, 0.50, 0.53),
    "van":     (0.10, 0.19, 0.97, 0.97),
    "armored": (0.20, 0.27, 0.92, 0.92),
}


def wheels(W, L, front=0.2, rear=0.8, tw=8, th=None):
    th = th or max(18, L * 0.14)
    out = ""
    for fy in (front, rear):
        y = L * fy - th / 2
        out += f'<rect x="{3}" y="{y:.1f}" width="{tw}" height="{th:.1f}" rx="2.5" fill="#121213"/>'
        out += f'<rect x="{W - 3 - tw}" y="{y:.1f}" width="{tw}" height="{th:.1f}" rx="2.5" fill="#121213"/>'
    return out


def lightbar(W, y, width=None):
    width = width or W * 0.6
    x = (W - width) / 2
    h = 10
    half = (width - 9) / 2
    return (f'<rect x="{x:.1f}" y="{y:.1f}" width="{width:.1f}" height="{h}" rx="3.5" fill="#151515" stroke="{OUTLINE}" stroke-width="1.6"/>'
            f'<rect x="{x + 2.5:.1f}" y="{y + 2:.1f}" width="{half:.1f}" height="6" rx="2" fill="#e8302a"/>'
            f'<rect x="{x + 6.5 + half:.1f}" y="{y + 2:.1f}" width="{half:.1f}" height="6" rx="2" fill="#2b62e8"/>'
            f'<rect x="{x + 2.5 + half + 0.5:.1f}" y="{y + 2:.1f}" width="3" height="6" fill="#bbb"/>')


def roof_text(W, y, text, colour, size=9):
    return f'<text x="{W / 2:.1f}" y="{y:.1f}" font-family="Arial Black,Arial" font-weight="900" font-size="{size}" fill="{colour}" text-anchor="middle">{text}</text>'


def vehicle(name, seed, W, L, style, paint, roof=None, extras="", bumper="#1a1a1b", livery=None, bed=None):
    """Generic top-down car/truck. livery = (door colour) for black-and-white police paint."""
    edge, mid = paint
    roof = roof or mid
    ws0, ws1, rf1, rg1 = (f * L for f in STYLES[style])
    bx, bw, by, bl = 7, W - 14, 5, L - 10
    r = min(16, bw * 0.24)
    defs = G.grit_defs(seed, fine=0.9, fine_alpha=2.1, coarse_alpha=1.3) + f'''
<linearGradient id="p{seed}" x1="0" x2="1"><stop offset="0" stop-color="{edge}"/><stop offset=".5" stop-color="{mid}"/><stop offset="1" stop-color="{edge}"/></linearGradient>
<linearGradient id="gl{seed}" y1="0" y2="1"><stop offset="0" stop-color="#9fc4d8"/><stop offset="1" stop-color="#2f4b5c"/></linearGradient>'''
    liv = ""
    if livery:
        # Black-and-white: dark front and rear, white doors (classic US patrol paint).
        liv = (f'<rect x="{bx}" y="{by}" width="{bw}" height="{ws0 - by + 2:.1f}" rx="{r:.1f}" fill="{livery}"/>'
               f'<rect x="{bx}" y="{rg1 - 2:.1f}" width="{bw}" height="{by + bl - rg1 + 2:.1f}" rx="{r:.1f}" fill="{livery}"/>')
    inset = bw * 0.14
    rx0, rw = bx + inset, bw - 2 * inset
    glass = "url(#gl%d)" % seed
    body = f'''<g>{wheels(W, L)}
 <g filter="url(#grit{seed})">
  <rect x="{bx}" y="{by}" width="{bw}" height="{bl}" rx="{r:.1f}" fill="url(#p{seed})"/>
  {liv}
  <rect x="{bx + 6}" y="{by - 2}" width="{bw - 12}" height="6" rx="2" fill="{bumper}"/>
  <rect x="{bx + 6}" y="{by + bl - 4}" width="{bw - 12}" height="6" rx="2" fill="{bumper}"/>
  <rect x="{rx0:.1f}" y="{ws1:.1f}" width="{rw:.1f}" height="{rf1 - ws1:.1f}" rx="6" fill="{roof}"/>
 </g>
 <rect x="{bx}" y="{by}" width="{bw}" height="{bl}" rx="{r:.1f}" fill="none" stroke="{OUTLINE}" stroke-width="2.6"/>
 <path d="M{rx0 - 3:.1f} {ws0 + 2:.1f} Q{W / 2:.1f} {ws0 - 9:.1f} {rx0 + rw + 3:.1f} {ws0 + 2:.1f} L{rx0 + rw:.1f} {ws1:.1f} Q{W / 2:.1f} {ws1 - 5:.1f} {rx0:.1f} {ws1:.1f} Z" fill="{glass}" stroke="{OUTLINE}" stroke-width="2"/>
 <rect x="{rx0:.1f}" y="{ws1:.1f}" width="{rw:.1f}" height="{rf1 - ws1:.1f}" rx="6" fill="none" stroke="{OUTLINE}" stroke-width="2"/>'''
    if rg1 - rf1 > 2:
        body += f'<path d="M{rx0 + 1:.1f} {rf1:.1f} Q{W / 2:.1f} {rf1 + 5:.1f} {rx0 + rw - 1:.1f} {rf1:.1f} L{rx0 + rw + 2:.1f} {rg1:.1f} Q{W / 2:.1f} {rg1 + 6:.1f} {rx0 - 2:.1f} {rg1:.1f} Z" fill="{glass}" stroke="{OUTLINE}" stroke-width="2"/>'
    # side windows
    body += (f'<rect x="{bx + 5:.1f}" y="{ws1 + 4:.1f}" width="4" height="{max(8, rf1 - ws1 - 8):.1f}" fill="#2f4b5c" opacity=".9"/>'
             f'<rect x="{bx + bw - 9:.1f}" y="{ws1 + 4:.1f}" width="4" height="{max(8, rf1 - ws1 - 8):.1f}" fill="#2f4b5c" opacity=".9"/>')
    if bed:  # pickup bed
        b0 = rg1 + L * 0.04
        body += (f'<rect x="{bx + 6:.1f}" y="{b0:.1f}" width="{bw - 12:.1f}" height="{by + bl - b0 - 8:.1f}" rx="3" fill="{bed}" stroke="{OUTLINE}" stroke-width="2"/>'
                 + "".join(f'<rect x="{bx + 10:.1f}" y="{b0 + 6 + i * 10:.1f}" width="{bw - 20:.1f}" height="2" fill="#000" opacity=".25"/>' for i in range(int((by + bl - b0 - 14) / 10))))
    # lights and mirrors
    body += (f'<rect x="{bx + 6:.1f}" y="{by + 2}" width="13" height="5" rx="2" fill="#fff6c8" stroke="{OUTLINE}" stroke-width="1"/>'
             f'<rect x="{bx + bw - 19:.1f}" y="{by + 2}" width="13" height="5" rx="2" fill="#fff6c8" stroke="{OUTLINE}" stroke-width="1"/>'
             f'<rect x="{bx + 5:.1f}" y="{by + bl - 6:.1f}" width="13" height="4.5" rx="1.5" fill="#c0201b" stroke="{OUTLINE}" stroke-width="1"/>'
             f'<rect x="{bx + bw - 18:.1f}" y="{by + bl - 6:.1f}" width="13" height="4.5" rx="1.5" fill="#c0201b" stroke="{OUTLINE}" stroke-width="1"/>'
             f'<path d="M{bx} {ws0 - 8:.1f} L{bx - 4} {ws0 - 6:.1f} L{bx - 4} {ws0 - 2:.1f} L{bx} {ws0 - 2:.1f} Z" fill="{OUTLINE}"/>'
             f'<path d="M{bx + bw} {ws0 - 8:.1f} L{bx + bw + 4} {ws0 - 6:.1f} L{bx + bw + 4} {ws0 - 2:.1f} L{bx + bw} {ws0 - 2:.1f} Z" fill="{OUTLINE}"/>')
    body += extras + "</g>"
    G.write(name, G.svg(W, L, body, defs))
    return ws1, rf1


def box_truck(name, seed, W, L, cab, box, stripe=None, text=None):
    defs = G.grit_defs(seed) + f'<linearGradient id="gl{seed}" y1="0" y2="1"><stop offset="0" stop-color="#9fc4d8"/><stop offset="1" stop-color="#2f4b5c"/></linearGradient>'
    cab_l = L * 0.24
    body = f'''<g>{wheels(W, L, 0.11, 0.78, 9, 22)}{wheels(W, L, 0.11, 0.9, 9, 22)}
 <rect x="10" y="5" width="{W - 20}" height="{cab_l:.1f}" rx="10" fill="{cab}" stroke="{OUTLINE}" stroke-width="2.6"/>
 <path d="M16 {cab_l * 0.3:.1f} Q{W / 2:.1f} {cab_l * 0.18:.1f} {W - 16} {cab_l * 0.3:.1f} L{W - 18} {cab_l * 0.55:.1f} L18 {cab_l * 0.55:.1f} Z" fill="url(#gl{seed})" stroke="{OUTLINE}" stroke-width="2"/>
 <rect x="14" y="7" width="12" height="5" rx="2" fill="#fff6c8" stroke="{OUTLINE}" stroke-width="1"/><rect x="{W - 26}" y="7" width="12" height="5" rx="2" fill="#fff6c8" stroke="{OUTLINE}" stroke-width="1"/>
 <path d="M10 {cab_l * 0.32:.1f} L5 {cab_l * 0.36:.1f} L5 {cab_l * 0.46:.1f} L10 {cab_l * 0.46:.1f} Z" fill="{OUTLINE}"/><path d="M{W - 10} {cab_l * 0.32:.1f} L{W - 5} {cab_l * 0.36:.1f} L{W - 5} {cab_l * 0.46:.1f} L{W - 10} {cab_l * 0.46:.1f} Z" fill="{OUTLINE}"/>
 <rect x="6" y="{cab_l + 9:.1f}" width="{W - 12}" height="{L - cab_l - 14:.1f}" rx="4" fill="{box}" stroke="{OUTLINE}" stroke-width="2.6"/>'''
    for i in range(1, 8):
        y = cab_l + 9 + i * (L - cab_l - 14) / 8
        body += f'<rect x="10" y="{y:.1f}" width="{W - 20}" height="1.6" fill="#000" opacity=".12"/>'
    if stripe:
        body += f'<rect x="6" y="{cab_l + 9:.1f}" width="10" height="{L - cab_l - 14:.1f}" fill="{stripe}"/><rect x="{W - 16}" y="{cab_l + 9:.1f}" width="10" height="{L - cab_l - 14:.1f}" fill="{stripe}"/>'
    if text:
        body += f'<text x="{W / 2:.1f}" y="{cab_l + (L - cab_l) / 2:.1f}" font-family="Arial Black,Arial" font-weight="900" font-size="14" fill="{stripe or "#333"}" text-anchor="middle" transform="rotate(90 {W / 2:.1f} {cab_l + (L - cab_l) / 2:.1f})">{text}</text>'
    body += f'<rect x="10" y="{L - 6:.1f}" width="{W - 20}" height="4" rx="1.5" fill="#c0201b" opacity=".85"/></g>'
    G.write(name, G.svg(W, L, body, defs))


def motorcycle(name, seed, paint, police=False):
    W, L = 30, 72
    defs = G.grit_defs(seed)
    body = f'''<g>
 <rect x="11" y="2" width="8" height="14" rx="3" fill="#121213"/><rect x="11" y="56" width="8" height="15" rx="3" fill="#121213"/>
 <rect x="7" y="12" width="16" height="48" rx="7" fill="{paint}" stroke="{OUTLINE}" stroke-width="2"/>
 <rect x="1" y="18" width="28" height="4" rx="2" fill="#2a2a2a" stroke="{OUTLINE}" stroke-width="1"/>
 <rect x="9" y="30" width="12" height="16" rx="5" fill="#1b1b1b"/>
 <circle cx="15" cy="38" r="7" fill="#1d2b44" stroke="{OUTLINE}" stroke-width="1.6"/>
 <rect x="12" y="10" width="6" height="4" rx="1.5" fill="#fff6c8"/>
 <rect x="12" y="58" width="6" height="3" rx="1" fill="#c0201b"/>'''
    if police:
        body += '<rect x="6" y="50" width="6" height="5" rx="1.5" fill="#e8302a"/><rect x="18" y="50" width="6" height="5" rx="1.5" fill="#2b62e8"/>'
    body += "</g>"
    G.write(name, G.svg(W, L, body, defs))


def helicopter(name, seed):
    W, L = 96, 380
    defs = G.grit_defs(seed) + f'<linearGradient id="h{seed}" x1="0" x2="1"><stop offset="0" stop-color="#14213d"/><stop offset=".5" stop-color="#2c4a86"/><stop offset="1" stop-color="#14213d"/></linearGradient><linearGradient id="gl{seed}" y1="0" y2="1"><stop offset="0" stop-color="#9fc4d8"/><stop offset="1" stop-color="#2f4b5c"/></linearGradient>'
    body = f'''<g>
 <rect x="14" y="40" width="5" height="120" rx="2" fill="#2a2a2a"/><rect x="77" y="40" width="5" height="120" rx="2" fill="#2a2a2a"/>
 <rect x="19" y="70" width="10" height="5" fill="#2a2a2a"/><rect x="67" y="70" width="10" height="5" fill="#2a2a2a"/>
 <rect x="19" y="130" width="10" height="5" fill="#2a2a2a"/><rect x="67" y="130" width="10" height="5" fill="#2a2a2a"/>
 <rect x="43" y="160" width="10" height="190" rx="4" fill="url(#h{seed})" stroke="{OUTLINE}" stroke-width="2"/>
 <rect x="30" y="325" width="36" height="8" rx="3" fill="#1f2f55" stroke="{OUTLINE}" stroke-width="1.6"/>
 <rect x="52" y="345" width="4" height="30" rx="1.5" fill="#333"/>
 <ellipse cx="48" cy="95" rx="30" ry="78" fill="url(#h{seed})" stroke="{OUTLINE}" stroke-width="2.6"/>
 <path d="M24 60 Q48 6 72 60 Q48 46 24 60 Z" fill="url(#gl{seed})" stroke="{OUTLINE}" stroke-width="2"/>
 <rect x="30" y="66" width="36" height="5" fill="#f2f2f2"/>
 <rect x="30" y="112" width="36" height="8" fill="#f2f2f2"/>
 <text x="48" y="140" font-family="Arial Black,Arial" font-weight="900" font-size="11" fill="#f2f2f2" text-anchor="middle">POLICE</text>
 <rect x="40" y="150" width="16" height="10" rx="2" fill="#e8302a"/><rect x="42" y="30" width="12" height="7" rx="2" fill="#2b62e8"/>
 <circle cx="48" cy="95" r="7" fill="#222" stroke="{OUTLINE}" stroke-width="1.6"/>
</g>'''
    G.write(name, G.svg(W, L, body, defs))
    # Rotor disc: motion-blurred blades, drawn separately so it can spin.
    R = 180
    rot = f'''<circle cx="{R}" cy="{R}" r="{R - 2}" fill="#0c0c0c" opacity=".16"/>
 <circle cx="{R}" cy="{R}" r="{R - 2}" fill="none" stroke="#0c0c0c" stroke-opacity=".25" stroke-width="2"/>
 <rect x="{R - 4}" y="4" width="8" height="{2 * R - 8}" rx="3" fill="#151515" opacity=".85"/>
 <rect x="4" y="{R - 4}" width="{2 * R - 8}" height="8" rx="3" fill="#151515" opacity=".85"/>
 <circle cx="{R}" cy="{R}" r="9" fill="#222"/>'''
    G.write(name + "_rotor", G.svg(2 * R, 2 * R, rot))


def build():
    lb = lambda W, y: lightbar(W, y)
    # ---------------- police
    W, L = 84, 160
    ws, rf = 0.31 * L, 0.90 * L
    vehicle("pd_suv", 101, W, L, "suv", ("#d9dde2", "#ffffff"), roof="#f4f6f8", livery="#151a24",
            extras=lb(W, ws + 4) + roof_text(W, ws + 60, "4127", "#1d3a7a"))
    W, L = 84, 176
    vehicle("pd_pickup", 102, W, L, "pickup", ("#d9dde2", "#ffffff"), roof="#f4f6f8", livery="#151a24", bed="#2a2c30",
            extras=lb(W, 0.31 * L + 3))
    W, L = 80, 150
    vehicle("pd_unmarked", 103, W, L, "sedan", ("#22262b", "#3c434b"), roof="#30363d",
            extras=f'<rect x="28" y="{0.39 * L + 1:.1f}" width="10" height="4" rx="1" fill="#e8302a"/><rect x="42" y="{0.39 * L + 1:.1f}" width="10" height="4" rx="1" fill="#2b62e8"/>')
    W, L = 80, 142
    vehicle("pd_interceptor", 104, W, L, "coupe", ("#0f1218", "#232a35"), roof="#f2f4f6", livery=None,
            extras=lightbar(W, 0.45 * L + 2, 40) + f'<rect x="7" y="{0.5 * L:.1f}" width="66" height="7" fill="#1d3a7a"/>')
    W, L = 94, 210
    vehicle("pd_transport", 105, W, L, "van", ("#d9dde2", "#ffffff"), roof="#eef0f2",
            extras=lb(W, 0.19 * L + 4) + f'<rect x="7" y="{0.45 * L:.1f}" width="{W - 14}" height="10" fill="#1d3a7a"/>'
                   + roof_text(W, 0.7 * L, "POLICE", "#1d3a7a", 12)
                   + "".join(f'<rect x="{W / 2 - 18 + i * 9:.1f}" y="{L - 26:.1f}" width="3" height="16" fill="#333"/>' for i in range(5)))
    W, L = 100, 196
    vehicle("pd_bearcat", 106, W, L, "armored", ("#15181d", "#2b3038"), roof="#23272e", bumper="#0d0d0e",
            extras=f'<circle cx="{W / 2}" cy="{0.55 * L:.1f}" r="14" fill="#1b1e24" stroke="{OUTLINE}" stroke-width="2"/>'
                   + f'<rect x="{W / 2 - 4}" y="{0.55 * L - 4:.1f}" width="8" height="8" fill="#333"/>'
                   + lightbar(W, 0.27 * L + 3, 44) + roof_text(W, 0.8 * L, "POLICE", "#d8dde5", 13)
                   + f'<rect x="7" y="{0.18 * L:.1f}" width="{W - 14}" height="5" fill="#0d0d0e"/>')
    motorcycle("pd_motorcycle", 107, "#f2f4f6", police=True)
    helicopter("pd_helicopter", 108)

    # ---------------- civilian
    for colour, paint in (("Red", ("#7a1414", "#d0362b")), ("Blue", ("#14306a", "#2f62c4")), ("White", ("#c9ccd0", "#f4f5f6")), ("Green", ("#264a2c", "#4c8a52"))):
        vehicle(f"car_compact_{colour.lower()}", 200 + len(colour), 72, 118, "compact", paint)
    for colour, paint in (("Black", ("#101214", "#2b3036")), ("Silver", ("#80878f", "#c3c9cf")), ("Blue", ("#14254a", "#30508c")), ("White", ("#c9ccd0", "#f4f5f6"))):
        vehicle(f"car_suv_{colour.lower()}", 300 + len(colour), 84, 160, "suv", paint,
                extras='<rect x="22" y="56" width="3" height="80" fill="#222"/><rect x="59" y="56" width="3" height="80" fill="#222"/>')
    for colour, paint, bed in (("White", ("#c9ccd0", "#f4f5f6"), "#3a3c40"), ("Red", ("#6e1512", "#b8322a"), "#2e2a28"), ("Black", ("#101214", "#2b3036"), "#25272a"), ("Tan", ("#8a7350", "#c8ad7f"), "#4a3f30")):
        vehicle(f"car_pickup_{colour.lower()}", 400 + len(colour), 84, 176, "pickup", paint, bed=bed)
    vehicle("van_delivery_white", 501, 90, 186, "van", ("#c9ccd0", "#f4f5f6"), extras=f'<rect x="7" y="110" width="76" height="10" fill="#c0392b"/>')
    vehicle("van_delivery_brown", 502, 90, 186, "van", ("#3e2a17", "#6b4a2b"), extras=f'<rect x="7" y="110" width="76" height="10" fill="#e0b13a"/>')
    box_truck("truck_box_white", 601, 100, 290, "#e9ebee", "#f7f7f5", stripe="#2f62c4", text="HALDEN MOVING")
    box_truck("truck_box_yellow", 602, 100, 270, "#e3e5e8", "#f3c53b", stripe="#262626", text="FREIGHT")


if __name__ == "__main__":
    G.OUT = sys.argv[1] if len(sys.argv) > 1 else "out"
    build()
    print("vehicles written to", G.OUT)
