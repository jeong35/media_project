# Visual asset contract

The dialogue screen loads final art by content ID without changing scene logic.

```text
backgrounds/{backgroundId}.png
characters/{npcId}/{emotion}.png
```

- Background images should use a 16:9 canvas. The current viewport is 960×540.
- Character images should be transparent PNG files with the same canvas and character alignment for every expression.
- If an exact expression file is missing, the client tries `neutral.png`.
- If no character image exists yet, the visual-novel layout displays a temporary placeholder.

## Story v0.2 asset manifest

```text
backgrounds/modern_library.png
backgrounds/study_night.png
backgrounds/mage_tower_interior.png
backgrounds/community_barley_field.png
backgrounds/cracked_sky_castle_wall.png

characters/steward_edwin/neutral.png
characters/steward_edwin/surprised.png
characters/steward_edwin/urgent.png

characters/mage_lia/neutral.png
characters/mage_lia/nervous.png
characters/mage_lia/determined.png

characters/farmer_marta/neutral.png
characters/farmer_marta/smiling.png
characters/farmer_marta/worried.png

ui/dialogue_panel.png
ui/name_plate.png
ui/basic_button.png
```

UI textures are used as scalable 9-slice styles for dialogue/story panels,
speaker name plates, and shared action/choice buttons.

The current story flow works without these files and replaces them with fallback colors and character initials.
