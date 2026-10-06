"""Reviewed production prompts; this writes text, never fabricates artwork.

Send each prompt to the built-in image generator with transparent_background
enabled. Save the actual selected outputs and generation date in provenance.
"""
import argparse
import json
from pathlib import Path

TRAITS = {
    "chiikawa": "Chiikawa: a very small pure-white round bear-like creature, two tiny round ears, no hair cap, tiny white round tail, dark brown oval eyes with white highlights, slightly worried gentle eyebrows, three short blush lines on each pink cheek, little W mouth and tiny white paws. No clothing.",
    "usagi": "Usagi: a pale warm-yellow rabbit with two LONG upright ears with pink inner ears, very round head and little body, tiny round white pompom tail, dark brown oval eyes with white highlights, high arched mischievous eyebrows, pink cheeks and little smiling W mouth. No clothing. Keep the complete long ears inside every cell.",
    "kurimanju": "Kurimanju: a round cream-colored chestnut-like little creature, two tiny rounded ears, a solid chestnut-brown SEMICIRCULAR hair cap across the top of the head and upper back of the head, brown oval muzzle around the small mouth, dark brown round eyes, relaxed straight eyebrows, pink cheeks, tiny cream paws and small attached cream tail. No clothing, food, drink or bottles.",
    "rakko": "Rakko: a round pale-ivory little sea otter with small BLACK rounded ears, BLACK paws and feet, a small tan X-shaped scar on the upper left forehead, serious angled eyebrows, dark brown oval eyes, pink cheeks, a little W mouth, and a plain WHITE CAPE with a white collar wrapped around the neck. The cape is attached and visible on front and back poses. No sword, weapon, scabbard or belt on the body; the game supplies the sword separately.",
    "shisa": "Shisa: a small cream lion-dog with little pointed cream ears, a distinctive orange-gold CURLY MANE surrounding the sides and lower edge of the head, two curled orange-gold eyebrows, dark brown oval eyes, pink cheeks and tiny cream paws. Back view has a cream-white rear head framed by the orange-gold curls and a tiny attached tail with an orange tip. No blue, green, clothes, food or accessories.",
    "furuhonya": "Furuhonya (Kani, the used-bookshop character): a tiny cream-white round creature with a soft pastel-pink hair cap on the upper head, TWO pink crab-CLAW-shaped ears rising from that cap, two tiny attached brown hair tufts at the cap's top center, four short dark brown eyebrow marks in TWO stacked pairs directly above the two eyes, dark brown oval eyes with white highlights, pink cheeks, little W mouth and tiny cream-white paws. No ribbon, bow, barrette, actual crab limbs, book, clothes or large tail. Back view retains the pink cap, attached tufts and two claw-shaped ears and has no visible face or eyebrow marks.",
}
THEMES = {
    "chiikawa": ("soft pink, white and pale peach", "dark plum", "tiny attached round-bear-ear corners", "delicate rounded ribbon crescents and small airy petal-shaped traces"),
    "usagi": ("warm yellow, cream and soft orange", "dark chocolate", "tiny attached long-rabbit-ear corners", "quick angular golden streaks and energetic slim zigzag crescents"),
    "kurimanju": ("chestnut brown, cream and muted amber", "dark espresso", "tiny attached chestnut-cap curves", "rounded amber arcs and soft low-opacity chestnut-colored wisps"),
    "rakko": ("ivory, cool silver and muted gold", "dark charcoal", "tiny attached white-cape folds at the outer border", "precise slim silver blade trails with restrained small gold-edged arcs"),
    "shisa": ("orange-gold, cream and soft apricot", "dark burnt umber", "tiny attached orange curled-mane corners", "spiral-edged orange wind ribbons and small curled golden traces"),
    "furuhonya": ("pastel pink, ivory and pale coral", "dark burgundy", "tiny attached pink claw-ear corners", "soft pink double-curved crescents and pale coral ribbon traces"),
}
WEAPONS = {
    "chiikawa": "A single complete PINK SASUMATA: one long straight slim pink shaft with a symmetric open U-shaped two-prong fork at its top, rounded pale-white bead-like caps on the two prong tips. No extra prongs. Upright, shaft bottom down, fork up, whole object centered. Clean dark-cocoa outline, flat pink fill and pale-white tip caps.",
    "usagi": "A single complete straight YELLOW TORUBOU: a long slim yellow cylindrical baton with one rounded WHITE cap at EACH end. It is a straight rod, with no fork, blade, handle guard, ears, ribbon or ornamental branches. Upright and fully centered. Clean dark-cocoa outline, warm yellow fill and white caps.",
    "rakko": "A single complete Rakko-inspired STRAIGHT SWORD: slim straight silver blade with a pointed tip, a simple white cross guard, short white handle with three black grip bands and a small white pommel. Upright with blade tip up and handle down. The silver exposed blade is a fan-art interpretation of the sword whose official merchandise shows a white-and-black grip and brown sheath. Do not draw the sheath, body, cape or character. Clean dark-charcoal outline and restrained flat silver/white/black fills.",
}


def prompts(character):
    trait = TRAITS[character]
    palette, dark, motif, energy = THEMES[character]
    body = f"""Use case: stylized-concept. Asset type: production 2D body sprite atlas.
Create brand-new Chiikawa-series fan art of {trait}
Composition: wide 2048x1024 PNG atlas, exactly 8 columns and 4 rows, 32 equal 256x256 cells. One complete connected UNARMED character per cell, horizontally centered feet, identical character proportions across poses, standing height about 165 pixels (include ears and cape). Leave at least 32 pixels genuinely empty transparent padding around the whole silhouette. Feet baseline about y=210 in each cell except the lying pose. Every ear, tail, paw and cape belongs to the connected body silhouette. No grid lines.
Row 1 FRONT left to right: standing idle eyes open; same idle blinking; walk left foot forward; walk right foot forward; low leaning dash paws tucked; braced charging paws near chest; flinching hurt eyes shut; lying defeated on side eyes closed, intact ears and tail/cape.
Row 2 BACK facing away: these same eight poses from behind, ear twitch instead of blinking; absolutely no eyes, nose, mouth or face on any back pose. Preserve the actual back markings and attached tail/cape described above.
Row 3 FRONT unarmed attack phases: neutral ready; paws draw back to wind up; paws reach forward; broad paw swing; peak follow-through leaning forward; recover paws; return stance; neutral ready. Eight slightly distinct complete character poses, no held object.
Row 4 BACK unarmed attack phases: those same eight phases from behind with no face. Keep all ears, tail/cape and rear markings intact.
Style: clean flat kawaii 2D sprite illustration, thick consistent dark cocoa outline legible at tiny game size, solid original character colors, minimal antialiasing, no gradients, grain or glow. REAL transparent alpha. No weapon, magic, effect, sparks, dust, shadow, ground, floating fragment, text, label, logo, watermark or extra character. Body only; the game supplies separate weapons and effects."""
    effects = f"""Use case: stylized-concept. Asset type: production game effect-phase atlas.
Create brand-new {character} fan-art game effects using {palette}, with {energy}. Exactly 4 columns and 6 rows on a 1024x1536 PNG atlas: 24 equal cells, truly transparent alpha background, at least 36px empty gutters around each cell's ink. Each row uses the same cell center and maximum extent across four phases, changing the amount of ink rather than the center. No character, body part, weapon, text or grid. Keep every ending cell faint but visibly nonempty.
Each row left to right: BEGINNING, PEAK, RELEASE, FAINT ENDING.
Row 1 slim open curved attack slash, larger open crescent peak, shortening release, faint short crescent trace.
Row 2 short horizontal dash streak, broader two-streak peak, shortening release, faint horizontal trace.
Row 3 small rounded projectile pointing RIGHT, slightly larger with short leftward trail, small fading projectile, faint tiny oval trace.
Row 4 small impact puff, medium airy impact puff, breaking fading puff, tiny faint puff.
Row 5 small open circular guard ring, larger HOLLOW ring, thin fading ring, faint short ring arc.
Row 6 small horizontal revive ellipse with tiny glint above, two restrained light ellipses and tiny glint, fading ellipses, faint short ellipse trace.
Clean flat airy game effects, low-opacity fading phases, restrained thin edges, transparent interiors. Preserve attack direction and enemy visibility. No giant stars, opaque flash disks, bloom, speckles, random pixels, backgrounds or shadows."""
    ui = f"""Use case: stylized-concept. Asset type: production game UI sprite atlas, not a screenshot.
Create a new {character} fan-art UI atlas: EXACTLY 4 columns and 2 rows on a wide 2048x1024 PNG, eight separate centered elements, at least 40px truly empty transparent gutters. Thin repeatable borders and simple corners for nine-slicing. Palette {palette}, with QUIET {dark} interiors on every panel/button to keep existing white multilingual text readable. Decorative {motif} stay attached to the outer border. Draw no text or numbers.
Top row: dark dialogue panel; dark inventory panel; dark normal button; dark selected button with a restrained double border.
Bottom row: a dedicated large FRONT FACE PORTRAIT in a simple round medallion, faithfully recognizable from these traits: {trait}; dark horizontal HUD bar with an uncluttered center and tiny attached outer-left motif; dark tooltip panel; dark header panel with thin accent top edge.
Clean flat 2D illustration with consistent dark outlines and transparent alpha outside each sprite. No gradients, grain, glow, central large ornaments, background, grid, watermark, extra portrait outside its cell, full interface, text, number or logo. Portrait is the only character illustration; leave panel centers free for native text."""
    result = {"body.png": body, "effects.png": effects, "ui.png": ui}
    if character in WEAPONS:
        result["weapon.png"] = "Use case: stylized-concept. Asset type: production isolated 2D game weapon sprite.\n" + WEAPONS[character] + "\n1024x1024 PNG, real transparent alpha, one centered complete weapon inside a single cell with 100px empty padding. Flat kawaii game art with dark outline. No hand, character, shadow, ground, light flare, magic, text, logo or watermark. Do not add the weapon to a body atlas."
    return result


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("character", choices=TRAITS)
    parser.add_argument("--output", help="New text record; no image generation is performed")
    args = parser.parse_args()
    result = prompts(args.character)
    if args.output:
        path = Path(args.output)
        if path.exists():
            raise ValueError("Prompt output already exists")
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text("Production prompts prepared 2026-10-06. Generation not yet run.\n\n" +
                        "\n\n".join(name + "\n" + prompt for name, prompt in result.items()) + "\n", encoding="utf-8")
    print(json.dumps(result, ensure_ascii=False, indent=2))
