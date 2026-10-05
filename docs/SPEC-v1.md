# skin.json schema version 1

UTF-8 JSON with skin.json at the pack root. The schema version is independent of the plugin's prerelease version. Unknown properties and author executable payloads are rejected.

| Field | Requirement |
|---|---|
| schemaVersion | 1 |
| id | 3–64 lower-case letters/digits/dot/underscore/hyphen; first character alphanumeric |
| version/name/author | Numeric version, nonempty name (max 128), nonempty author |
| compatibleCatalogs | Must contain the current catalog ID |
| preview | Optional sprite resource ID |
| resources | ID → file/kind/optional asset/bundle/rect/border/pivot/pixelsPerUnit |
| body/weapons/effects | Catalog animation key → frames + exact frameIndices, optional material/particle/fitOriginal |
| visuals | Catalog static renderer key → optional sprite/material/fitOriginal/hide |
| ui | Catalog UI key → optional sprite/material/font/existingFont/color/anchoredPosition/sizeDelta/fontSize/imageType |
| audio | FMOD GUID key → resource/scope/channel/loop/volume |

Sections are optional. Omitted bindings use the game original. Null sections are invalid. Resource kind is sprite/audio/font/material/particle. Sprites support PNG. Audio always uses loose WAV/OGG, including Unity exports, and is decoded by FMOD. Font/material/particle require a bundle. Rect is x,y,width,height in bottom-left Unity coordinates. Border is left,bottom,right,top PNG pixels; opposing borders cannot exceed the sprite dimension. ImageType is simple/sliced for Image only. Pivot is normalized 0–1; pixelsPerUnit is positive and at most 4096. UI colors are RGBA 0–1; numeric vectors must be finite. ExistingFont refers to a loaded game TMP font name. A font choice only applies to TMP text.

`fitOriginal: true` fits a replacement into the source frame's world dimensions and normalized pivot, preserving transforms and physics. It defaults to false. `hide: true` suppresses only that static cosmetic sprite and restores it with the theme; it cannot be combined with sprite/material/fitOriginal. Use this for weapon add-on layers when the primary sprite already contains the whole new weapon. Character packs without a weapon should omit weapons/weapon visuals to retain native weapons. Runtime world sprites are isolated from atlas neighbours; the client pixel setting changes their sampling without changing frame events or world bounds. UI uses its independent client switch.

Bundles require UnityFS with actual revision 6000.3.21f1, StandaloneWindows64, nonzero Unity CRC and matching lower-case SHA256. Asset is a bundle path or path#spriteName for a subasset. Runtime checks CRC/platform and asset type. New fonts are cloned and use original game fonts as fallback. Identical bundles use counted leases so a replacement can be prepared while the current pack remains active. Particle prefabs support only Transform/ParticleSystem/ParticleSystemRenderer, with collision/trigger modules disabled.

Animation identity includes role, set name, state and a SHA256-derived signature of FPS, repeat, sparse frame indices and source sprite names. Array order and frame counts must match the catalog, including unsorted keyframes. An original null sprite remains null. UI identity is hierarchy with duplicate sibling indices and component type. Static renderer identity is source prefab hierarchy and role; detached weapon parts retain their original identity. Display names alone are insufficient.

Audio keys use guid:{xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx}. Catalog path is the corresponding readable event:/ path. Scope is local/client. Channel is sfx/music/ambience; music and ambience require client scope. Local events without a verified owner use the original. Volume is 0–1, loop is boolean. Original Studio events continue to control lifetime while their volume is suppressed. Replacement Core channels track game master/channel volumes, pause and pitch. Music uses a short DSP fade; native stopping/release ends replacement playback.

Limits: 8192 files/directories, maximum folder depth 32, 64 MiB per file, 256 MiB per pack, 2 MiB manifest. PNG side ≤8192 and total ≤16 megapixels. Decoded audio ≤128 MiB per resource and ≤8 channels. Paths use forward slashes; absolute paths, traversal, drive prefixes, Windows special/control/reserved names, trailing dot/space and symlink/junctions are forbidden. ZIP entries are read into bounded memory without extraction. Case-insensitive duplicate filenames are rejected. Duplicate skin IDs exclude every duplicate candidate.

Selection/reload prepares a complete file snapshot and loads every declared resource before commit. Failure retains the valid current theme. Restore reverses only properties still holding the applied values. The selector reuses game UI components and is excluded from skin bindings. Skin bindings do not alter native transitions, hitboxes, network fields or gameplay events.
