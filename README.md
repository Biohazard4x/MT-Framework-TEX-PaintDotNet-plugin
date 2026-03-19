RE5 PC TEX paint.net FileType plugin (DXT1 / DXT3 / DXT5)

This build is intentionally scoped to Resident Evil 5 PC TEX files that use the RE5-style TEX\0 header layout...for now.

Supported load/save compression tags:
- DXT1
- DXT3
- DXT5

Save UI:
- Profile presets: RE5 Generic / BM / MM / NM
- Manual override for compression, mipmaps, and alpha handling
- Profile selection fills in defaults but does not lock the controls

Known limits:
- Header flavor is still a safe generic RE5 PC header
- No RE6 or other MT games support in this branch
- No cubemap/special-data texture support in this branch
