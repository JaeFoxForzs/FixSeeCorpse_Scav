# FixSeeCorpse_Scav

Overview

This mod completely reworks corpse detection mechanics. Instead of vanilla instant detection within a 7-tile radius (ignoring walls and lighting), players must now actually see the corpse

Realistic Visibility Checks

    Field of view check (120° cone in look direction)
    Line of sight check (corpses behind walls are not detected)
    Camera bounds check (corpse must be on screen)
    Lighting check (corpses in complete darkness are not noticed)
    
Gradual Detection

    Corpses must be looked at for a period of time before being noticed
    Close corpses (≤10m) are spotted quickly (~0.15s)
    Distant corpses (≥45m) take longer to notice (~1.5s)
    Low consciousness adds up to +3.5s detection delay

Multiplayer Support

    Full compatibility with KrokoshaCasualtiesMP mod
    Works correctly for both host and clients

Installation

    Install BepInEx 5.x
    Place it in BepInEx/plugins/

Configuration

Currently no configuration file.
