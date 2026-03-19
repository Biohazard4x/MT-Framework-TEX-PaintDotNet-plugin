MT Framework TEX paint.net FileType plugin

Current scope of this source package:
- RE5 PC: load + save
  - DXT1
  - DXT3
  - DXT5
- RE6 PC: load-only (2D textures)
  - BC1-family: 0x011401 / 0x011901
  - BC3-family: 0x011801 / 0x012501 / 0x012F01
  - BC5: 0x011F01
  - RGBA8-family: 0x012801 / 0x012701 / 0x010E01

Current save path:
- Save/export is still RE5-only
- Save UI remains the RE5 profile workflow (Generic / BM / MM / NM)

Current known limits:
- RE6 cubemaps are detected but not loaded yet
- RE6 volume textures are detected but not loaded yet
- RE6 save/export is not implemented yet
- Some rare RE6 special textures may still need per-format handling later
