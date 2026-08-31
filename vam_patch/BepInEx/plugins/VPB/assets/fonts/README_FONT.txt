Bundled UI fonts (optional, for Chinese/CJK in Unity Text)

Place one of these in this folder (next to VPB.dll after build):
  - NotoSansSC-VariableFont_wght.ttf  (common Google Fonts download name)
  - NotoSansSC-Regular.ttf            (static instance from Google Fonts)

VPB loads the first existing file from the list in VPBUiFont.BundledFontFileNames.

Licensing: Noto Sans is under the SIL Open Font License (OFL). OFL.txt in this folder is a copy
for redistribution; if you replace the font, update the copyright line at the top of OFL.txt to
match the font package you ship (or use the OFL.txt from that font’s ZIP).

You may delete this readme; the build copies all files in vpb_fonts/.
