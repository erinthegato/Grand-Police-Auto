#!/usr/bin/env python3
"""
Grand Police Auto - "lying down" body sprites for every person look (same style/scale as
generate_sprites.py: 64x64 units = 2 m, faces UP). Seen from above, the body is stretched out
full length, arms and legs splayed. Used when someone is knocked down, tased or killed.
Usage: python3 generate_downed.py <out_dir>
"""
import sys
import generate_sprites as G

O = G.OUTLINE

def limb(x1, y1, x2, y2, w, fill):
    return f'<line x1="{x1}" y1="{y1}" x2="{x2}" y2="{y2}" stroke="{O}" stroke-width="{w + 3}" stroke-linecap="round"/>' \
           f'<line x1="{x1}" y1="{y1}" x2="{x2}" y2="{y2}" stroke="{fill}" stroke-width="{w}" stroke-linecap="round"/>'

def lying(name, seed, top, dark, skin, hair, cap=False, tail=None, legs="#2c3240", shoes="#1b1b1b", dress=False):
    defs = G.grit_defs(seed, fine=1.2, coarse=0.08, fine_alpha=1.7, coarse_alpha=1.2)
    leg_col = skin if dress else legs
    body = "<g>"
    # legs splayed in a V, shoes at the ends
    body += limb(29, 40, 21, 57, 5.5, leg_col) + limb(35, 40, 44, 56, 5.5, leg_col)
    body += f'<ellipse cx="20" cy="58.5" rx="3" ry="2.6" fill="{shoes}" stroke="{O}" stroke-width="1"/>'
    body += f'<ellipse cx="45" cy="57.5" rx="3" ry="2.6" fill="{shoes}" stroke="{O}" stroke-width="1"/>'
    # arms flung out from the shoulders, one higher than the other
    body += limb(26, 21, 11, 29, 4.6, top) + limb(38, 21, 53, 15, 4.6, top)
    body += f'<circle cx="9.5" cy="30" r="2.7" fill="{skin}" stroke="{O}" stroke-width="1"/>'
    body += f'<circle cx="54.5" cy="14" r="2.7" fill="{skin}" stroke="{O}" stroke-width="1"/>'
    # torso (a skirt flares for the dress look)
    torso = f'<path d="M24 19 Q32 15 40 19 L41 40 Q32 43 23 40 Z" fill="{top}"/>' if not dress else \
            f'<path d="M24 19 Q32 15 40 19 L44 44 Q32 48 20 44 Z" fill="{top}"/>'
    body += f'<g filter="url(#grit{seed})">{torso}<rect x="23.5" y="33" width="17" height="2.6" fill="{dark}"/></g>'
    body += torso.replace(f'fill="{top}"', f'fill="none" stroke="{O}" stroke-width="2"')
    # head, turned slightly; hair / cap on top
    if tail: body += f'<ellipse cx="34" cy="5.5" rx="4.5" ry="4" fill="{tail}" stroke="{O}" stroke-width="1.2"/>'
    body += f'<circle cx="33" cy="11.5" r="6.3" fill="{skin}" stroke="{O}" stroke-width="1.8"/>'
    body += f'<path d="M26.8 10.5 Q33 3 39.2 10.5 Q33 7.5 26.8 10.5 Z" fill="{hair}" stroke="{O}" stroke-width="1"/>'
    if cap: body += f'<path d="M26.5 11 Q33 2.5 39.5 11 L38 12.5 Q33 9 28 12.5 Z" fill="{hair}" stroke="{O}" stroke-width="1.2"/>'
    body += "</g>"
    G.write(name, G.svg(64, 64, body, defs))

def build():
    for name, seed, top, dark, skin, head, weapon, cap, tail in G.PEOPLE:
        if name == "officer_armed": continue
        base = "officer" if name.startswith("officer") else name
        lying(base + "_down", seed + 100, top, dark, skin, head, cap=cap, tail=tail,
              legs="#1f2638" if base == "officer" else "#2c3240", dress=(name == "civ_dress_purple"))

if __name__ == "__main__":
    G.OUT = sys.argv[1] if len(sys.argv) > 1 else "out"
    build()
    print("downed bodies written to", G.OUT)
