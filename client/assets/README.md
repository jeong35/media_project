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
