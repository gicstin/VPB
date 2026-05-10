VPB embeds Noto Sans SC Regular (SIL OFL) into VPB.dll at compile time.

Build fetches fonts/VPB.Cjk.ttf automatically if missing (needs network).

MSBuild: /p:SkipVPBCjkFontFetch=true skips download; supply fonts/VPB.Cjk.ttf yourself (offline CI).

I18N.CJK.dll handles .NET globalization only; glyphs still come from the embedded TrueType outline data in VPB.dll, not from that assembly.

License: see vam_patch/BepInEx/plugins/vpb_fonts/OFL.txt (Noto project).
