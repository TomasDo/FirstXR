# Noto Sans CJK SC

Unmodified `NotoSansCJKsc-Regular.otf`, release **Sans2.004**, from the
[official Noto CJK repository](https://github.com/notofonts/noto-cjk/blob/Sans2.004/Sans/OTF/SimplifiedChinese/NotoSansCJKsc-Regular.otf).
Redistributed under the SIL Open Font License 1.1; see `LICENSE.txt`.
The player also includes the copyright and license in
`Assets/Resources/Fonts/NotoSansCJK-License.txt`.

SHA-256: `2c76254f6fc379fddfce0a7e84fb5385bb135d3e399294f6eeb6680d0365b74b`

The original Liberation Sans TMP atlas has no Chinese glyphs. The project now uses
`Assets/Resources/Fonts/DentalHud SDF.asset` as its TMP default and global fallback.
The asset retains this source font for runtime Chinese names, supports multiple
atlas textures, and preserves the baked UI glyphs during player builds. It does
not depend on the fonts installed on macOS, Beam Pro, or Android.

After changing HUD/status text, use **XREAL > Fonts > Rebuild Chinese HUD Font**
and run `DentalHudFontTests` in Unity's EditMode Test Runner. The generator collects
literal text in the HelloMR runtime source tree and fails on missing glyphs.
The font asset, its material/atlases, this source font, and their `.meta` files
must stay together in version control.
