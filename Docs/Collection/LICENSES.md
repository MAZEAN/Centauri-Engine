# Common Game Asset Licenses Explained

## Introduction

Game assets (art, sprites, 3D models, textures, audio, music, fonts, animations, VFX, UI elements, and code) are distributed under many different licenses. Understanding these licenses is critical because they determine how assets can be used, modified, shared, sold, or included in commercial games.

---

# 1. Public Domain (PD)

## What it means
The creator has waived copyright rights, or the copyright has expired.

## What you can do
- Use commercially
- Modify
- Redistribute
- Sell as part of a game
- Use without attribution (usually)

## Requirements
- Generally none

## Pros
- Maximum freedom
- Lowest legal friction

## Cons
- Authenticity can sometimes be difficult to verify

## Examples
- Public domain artwork
- Some OpenGameArt assets

---

# 2. CC0 (Creative Commons Zero)

## What it means
A creator intentionally places a work into the public domain as much as legally possible.

## What you can do
- Commercial use
- Modification
- Redistribution
- No attribution required

## Requirements
- None

## Pros
- One of the safest and most flexible licenses

## Common in
- OpenGameArt
- Kenney assets
- Free sound libraries

---

# 3. Creative Commons Attribution (CC BY)

## What it means
You may use the asset if you credit the creator.

## What you can do
- Commercial use
- Modification
- Redistribution

## Requirements
- Attribution required

## Example attribution
"Character sprite by Jane Doe, licensed under CC BY 4.0."

## Pros
- Highly permissive

## Cons
- Requires tracking and maintaining credits

---

# 4. Creative Commons Attribution-ShareAlike (CC BY-SA)

## What it means
You may modify and use the asset, but derivative works must use the same license.

## What you can do
- Commercial use
- Modification
- Redistribution

## Requirements
- Attribution
- Share derivative works under the same license

## Pros
- Encourages open ecosystems

## Cons
- Can complicate commercial projects

---

# 5. Creative Commons Attribution-NoDerivatives (CC BY-ND)

## What it means
Redistribution is allowed, but modifications are not.

## What you can do
- Commercial use
- Redistribution

## Restrictions
- Cannot distribute modified versions

## Game Development Impact
Often unsuitable because games frequently require editing assets.

---

# 6. Creative Commons Attribution-NonCommercial (CC BY-NC)

## What it means
Asset may only be used in non-commercial projects.

## What you can do
- Personal projects
- Educational projects
- Hobby games

## Restrictions
- No commercial use

## Risk
The definition of "commercial" can be broader than expected.

---

# 7. CC BY-NC-SA

## What it means
Non-commercial use only, attribution required, and derivatives must remain under the same license.

## Requirements
- Attribution
- Non-commercial usage
- ShareAlike

---

# 8. CC BY-NC-ND

## What it means
One of the most restrictive Creative Commons licenses.

## Requirements
- Attribution
- No commercial use
- No derivative works

## Game Development Impact
Rarely practical for commercial game development.

---

# 9. GNU General Public License (GPL)

## What it means
A strong copyleft license originally designed for software.

## What you can do
- Use
- Modify
- Redistribute

## Requirements
- Source code disclosure
- Derivative works must remain GPL

## Game Asset Considerations
Generally used for code rather than art assets.

## Risk
Can create licensing obligations incompatible with proprietary games.

---

# 10. GNU Lesser GPL (LGPL)

## What it means
A less restrictive version of GPL.

## What you can do
- Use in proprietary projects under certain conditions

## Requirements
- Modifications to LGPL components must remain LGPL

## Common Use
Libraries and middleware.

---

# 11. MIT License

## What it means
A very permissive open-source license.

## What you can do
- Commercial use
- Modification
- Redistribution
- Private use

## Requirements
- Include license notice

## Common Use
Code assets, tools, frameworks, shaders.

---

# 12. BSD Licenses

## What it means
Permissive licenses similar to MIT.

## What you can do
- Commercial use
- Modification
- Redistribution

## Requirements
- Retain copyright notice

---

# 13. Apache License 2.0

## What it means
A permissive open-source license with patent protections.

## What you can do
- Commercial use
- Modification
- Redistribution

## Requirements
- Include notices
- Preserve license text

## Common Use
Tools, engines, plugins, and code.

---

# 14. Proprietary Commercial License

## What it means
A paid or custom license from an asset creator.

## What you can do
Depends entirely on the license agreement.

## Typical Permissions
- Use in commercial games
- Modify assets
- Distribute within games

## Common Restrictions
- Cannot resell assets directly
- Cannot redistribute source files

## Examples
- Unity Asset Store assets
- Unreal Marketplace assets

---

# 15. Royalty-Free License

## What it means
Pay once (or obtain free access) and use according to license terms without ongoing royalties.

## Typical Permissions
- Use in games
- Commercial release
- Modification

## Restrictions
- Usually cannot redistribute standalone assets

## Common Use
Music, sound effects, stock art.

---

# 16. Editorial-Only License

## What it means
Assets may only be used for commentary, news, education, or documentary purposes.

## Restrictions
- No commercial game use
- No promotional use

## Common Examples
Celebrity photos
News photography

---

# 17. Marketplace Standard Licenses

## Unity Asset Store

Typical permissions:
- Commercial use
- Modification
- Use in shipped games

Typical restrictions:
- No redistribution of raw assets
- No asset-pack reselling

## Unreal Engine Marketplace / Fab

Typical permissions:
- Commercial use
- Modification
- Distribution within games

Typical restrictions:
- Cannot redistribute source assets separately

## Itch.io Asset Packs

Varies by creator:
- Commercial licenses
- CC licenses
- Custom licenses

Always read the specific package terms.

---

# 18. Custom Licenses

## What it means
A creator writes their own terms.

## Examples
- Personal-use only
- Single-project license
- Studio license
- Revenue-limited license

## Recommendation
Read every clause carefully.

---

# Quick Comparison Table

| License | Commercial Use | Modification | Attribution | Share Alike |
|----------|----------|----------|----------|----------|
| Public Domain | Yes | Yes | No | No |
| CC0 | Yes | Yes | No | No |
| CC BY | Yes | Yes | Yes | No |
| CC BY-SA | Yes | Yes | Yes | Yes |
| CC BY-ND | Yes | No | Yes | No |
| CC BY-NC | No | Yes | Yes | No |
| CC BY-NC-SA | No | Yes | Yes | Yes |
| CC BY-NC-ND | No | No | Yes | No |
| GPL | Yes | Yes | Yes | Yes |
| LGPL | Yes | Yes | Yes | Partial |
| MIT | Yes | Yes | License Notice | No |
| BSD | Yes | Yes | License Notice | No |
| Apache 2.0 | Yes | Yes | License Notice | No |
| Proprietary | Usually | Usually | Depends | Depends |
| Royalty-Free | Usually | Usually | Depends | No |

---

# Best Practices for Game Developers

1. Keep copies of all license files.
2. Store purchase receipts and invoices.
3. Maintain a credits document.
4. Track asset origins.
5. Verify commercial rights before release.
6. Avoid mixing incompatible licenses.
7. Read marketplace-specific terms.
8. Consult legal counsel for large commercial projects.
9. Do not assume "free" means unrestricted.
10. Re-check licenses when assets are updated.

---

# General Recommendation

For most commercial game projects, the easiest licenses to work with are:

- CC0
- Public Domain
- MIT
- BSD
- Apache 2.0
- Commercial marketplace licenses

Licenses requiring ShareAlike, NonCommercial, or NoDerivatives clauses should be reviewed carefully before inclusion in a commercial game.
