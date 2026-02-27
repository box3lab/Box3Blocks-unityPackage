# Box3 BlockWorld MVP (UPM)

This package generates a block world from JSON data.

## Setup
1. Create block prefabs (example: `grass`, `stone`).
2. Create catalog: `Create -> Block World MVP -> Block Catalog`
3. Create JSON template: `Assets -> Create -> Block World MVP -> Blocks JSON Template`
4. Add `BlockWorldGenerator` to a GameObject.
5. Assign `Catalog` and `Blocks Json`.
6. Click `Build From JSON` in inspector.

## Visual Builder (recommended)
1. Open `Tools -> Block World MVP -> World Builder`
2. Assign/create `Root`
3. Search + choose category tabs (including `Recent`)
4. Pick block cards in a 4-column grid
5. In Scene view:
   - `Place` mode: left click to place
   - `Erase` mode: left click to remove
6. Click `Clean Materials` to remove unused generated materials

Notes:
- `Recent` only updates when a block is actually placed in Scene.
- Block textures are auto-scanned from `Packages/com.box3.blockworld-mvp/Assets/block`.
- Block metadata is read from `Packages/com.box3.blockworld-mvp/Assets/block-spec.json` (fallback: `block-id.json`).
- `transparent: true` uses transparent material, so PNG alpha will render correctly.
- If texture has `.png.mcmeta`, block face animation is played (supports multi-frame strips such as 4-frame textures).
- Glow blocks show a color strip on the card bottom.
- Generated mesh/material assets are stored in `Assets/BlockWorldGenerated`.
- World Builder now uses atlas UV + one shared transparent material (`WorldBuilderAtlas_Transparent`), and animated faces use property blocks (no per-block material clone).
- World Builder non-animated blocks now use single-submesh baked-UV mesh + single shared material (further draw-call reduction).
- Block menu cards now render true 3D cube previews (top/side faces) instead of flat single-texture thumbnails.
- Added `Tools -> Block World MVP -> Voxel GZ Importer` for MC-style `.gz` voxel import (`shape/dir/indices/data/rot`) into chunk-merged meshes.
- `BlockWorldOcclusionCuller` can cull chunked renderers by distance + frustum + runtime occlusion (for large scenes with many hidden blocks).

## JSON format
```json
{
  "blocks": [
    { "id": "grass", "x": 0, "y": 0, "z": 0 },
    { "id": "stone", "x": 1, "y": 0, "z": 0 }
  ]
}
```
