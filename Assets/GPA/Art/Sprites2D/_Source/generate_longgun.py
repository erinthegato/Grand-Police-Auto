#!/usr/bin/env python3
"""Officer holding a rifle/shotgun braced against the right shoulder (faces UP, 64x64 units).
The gun itself is drawn by the game (a bar along x = 38 from the shoulder forward), so this
sprite has only the arms: the right hand on the grip just ahead of the shoulder, the left arm
reaching across to the fore-end. Same style and scale as generate_sprites.py."""
import importlib.util, os, sys
OUT = sys.argv[1] if len(sys.argv) > 1 else "out"
spec = importlib.util.spec_from_file_location("gs", sys.argv[2])
gs = importlib.util.module_from_spec(spec); sys.argv = [sys.argv[0], OUT]; spec.loader.exec_module(gs)
O = gs.OUTLINE

def longgun_arms(uniform, skin):
    # Right arm: shoulder (44,31) -> grip hand (39.5,23). Left arm: shoulder (20,31) -> fore-end hand (37.5,9).
    right = (f'<path d="M47 33 Q47 26 42 21.5 L38.5 24.5 Q41.5 28 41.5 33 Z" fill="{uniform}" stroke="{O}" stroke-width="1.5"/>'
             f'<circle cx="39.6" cy="22.6" r="3.1" fill="{skin}" stroke="{O}" stroke-width="1"/>')
    left = (f'<path d="M18 31 Q20 18 34.5 8.2 L37 11.6 Q25.5 20 23.5 32 Z" fill="{uniform}" stroke="{O}" stroke-width="1.5"/>'
            f'<circle cx="37.4" cy="9.4" r="3.1" fill="{skin}" stroke="{O}" stroke-width="1"/>')
    return left + right

def officer(name, step):
    uniform, dark, skin, head_top = "#24345c", "#12192e", "#c98f6b", "#1a1f2b"
    defs = gs.grit_defs(61, fine=1.2, coarse=0.08, fine_alpha=1.7, coarse_alpha=1.2)
    feet = ""
    if step:
        fl, fr = (22, 47) if step == 1 else (47, 22)
        feet = (f'<ellipse cx="26" cy="{fl}" rx="3.4" ry="4.6" fill="#1b1b1b" stroke="{O}" stroke-width="1"/>'
                f'<ellipse cx="38" cy="{fr}" rx="3.4" ry="4.6" fill="#1b1b1b" stroke="{O}" stroke-width="1"/>')
    # Bladed stance: shoulders turned a little (right shoulder back, left forward).
    body = f'''
<g>
 {feet}
 <g transform="rotate(-12 32 35)">
  <g filter="url(#grit61)">
   <ellipse cx="32" cy="35" rx="16" ry="9.5" fill="{uniform}"/>
   <rect x="17" y="33" width="30" height="3" fill="{dark}"/>
  </g>
  <ellipse cx="32" cy="35" rx="16" ry="9.5" fill="none" stroke="{O}" stroke-width="2"/>
 </g>
 {longgun_arms(uniform, skin)}
 <circle cx="33" cy="33.5" r="7.6" fill="{head_top}" stroke="{O}" stroke-width="1.8"/>
 <path d="M26 30 Q33 25 40 30 L39 32.5 Q33 29 27 32.5 Z" fill="#0b0d12"/>
 <circle cx="33" cy="35" r="1.8" fill="#8f7a3a"/>
</g>'''
    gs.write(name, gs.svg(64, 64, body, defs))

for step, suffix in ((0, ""), (1, "_walk_a"), (2, "_walk_b")):
    officer("officer_longgun" + suffix, step)
