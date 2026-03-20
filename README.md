# FixSeeCorpse_Scav

Overview

This mod completely reworks how characters notice corpses. Instead of the original instant detection when a corpse is rendered on screen, players now need to actually see the corpse under realistic conditions.
Features

Gradual Detection

    Corpses must be looked at for a period of time before being noticed
    Close corpses (≤10m) are spotted quickly (~0.15s)
    Distant corpses (≥45m) take longer to notice (~1.5s)
    Low consciousness adds up to +3.5s detection delay

Realistic Visibility Checks

    Field of view check (120° cone in look direction)
    Line of sight check (corpses behind walls are not detected)
    Camera bounds check (corpse must be on screen)
    Lighting check (corpses in complete darkness are not noticed)

Multiplayer Support

    Full compatibility with KrokoshaCasualtiesMP mod
    Works correctly for both host and clients

Installation

    Install BepInEx 5.x
    Place it in BepInEx/plugins/

Configuration

Currently no configuration file.
