LiteBox - lightweight LaunchBox plugin host (web/kiosk)
https://github.com/nixxou/Litebox

============================================================
WHICH BUILD?  Pick two things: your LaunchBox .NET version, then the form.
============================================================

1) Runtime - must match your LaunchBox.
   Folders are named  <LaunchBox version>_<runtime>, e.g.  13.27_net9  and  13.28_net10.
   LaunchBox moved from .NET 9 (13.27 and earlier) to .NET 10 (13.28 alpha).
   Pick by EITHER:
     - your LaunchBox version (LaunchBox > Help > About, or right-click
       Core\LaunchBox.exe > Properties > Details), or
     - its .NET, in <LaunchBox>\Core\LaunchBox.runtimeconfig.json -> "tfm"
       ("net9.0" -> the net9 folder, "net10.0" -> the net10 folder).
   The version is just what each build was compiled against: a net9 build runs
   on ANY .NET 9 LaunchBox, a net10 build on ANY .NET 10 LaunchBox - the runtime
   is the real requirement, not the exact number.
   (The net10 STANDALONE also runs on a .NET 9 LaunchBox - it carries its own
    .NET 10 runtime - but the net10 ZIP does not. When unsure, the folder whose
    runtime matches yours always works.)

2) Form - two ways to install, pick one. (<ver> = the version in the folder name.)

   A) standalone\LiteBox-<ver>.exe   (self-installing, ~85 MB)
      Carries its own .NET runtime, so it needs nothing installed. Drop it at
      your LaunchBox ROOT (next to LaunchBox.exe), into <LaunchBox>\Core, or
      anywhere: it copies itself into Core (as LiteBox.exe) and starts. Run it
      from a random folder and it asks you to point at your LaunchBox.exe first.

   B) zip\LiteBox-<ver>.zip          (lightweight, ~13 MB)
      BORROWS the .NET runtime that already lives in LaunchBox's Core (exactly like
      LaunchBox.exe does), so it MUST be extracted there and runs ONLY from there.
      EXTRACT IT INTO <LaunchBox>\Core: you get Core\LiteBox.exe (+ LiteBox.dll and
      two small .json) plus Core\litebox\thirdparty\ (the native tools). Run
      Core\LiteBox.exe. Extracted anywhere else it won't start (no runtime there) -
      that's what the standalone is for.
      (The .zip is versioned but the LiteBox.exe inside is not - normal, it must
       keep that name once in Core. Match the zip to your LaunchBox: a 13.28 zip is
       built for a .NET 10 LaunchBox, a 13.27 zip for a .NET 9 one.)

============================================================
Then, on first run (either form) ...
============================================================
LiteBox unpacks the native tools it uses into <LaunchBox>\ThirdParty\ -
RetroAchievements (RAHasher), Everything, ImageMagick, Steamworks - shared with
the ExtendDB plugin and NEVER overwriting a copy already there. Core stays clean:
just LiteBox.exe plus one "litebox\" folder that holds everything LiteBox creates
(config, write-back journal, RA/store achievement caches, logs, and - for the
zip - the native payload source under litebox\thirdparty\).

The UI is 100% native WinForms: a source TREE, a virtual game LIST (sortable,
searchable, show/hide + reorder columns), a POSTER grid view, and a details pane
(logo + box/screenshot carousel + meta card + VNDB tags + notes + RetroAchievements
and store-achievements cards). Pane widths, columns, sort and list/poster mode
are saved.

Options (gear / litebox\LiteBox.ini):
  - ReadOnly (DEFAULT TRUE): never write to the LaunchBox XMLs; favorites/ratings/
    play changes stay in memory for the session. Set false to persist them (kept in
    a journal and written only while LaunchBox/BigBox are NOT running).
  - Game-running screen, unload list while a game runs, image cache, game cache
    (Everything-backed, when ExtendDB isn't providing one), 16:9 main media.

RetroAchievements / store achievements:
  With RA creds (LaunchBox Settings.xml) the details pane shows a RetroAchievements
  card; with GOG/Steam it shows a store-achievements card. When the ExtendDB plugin
  isn't resolving RA, LiteBox computes the hash itself with the bundled RAHasher.
  Odd platforms are mappable in Options > LB Integrations > RetroAchievements.

Uninstall: Options > Uninstall LiteBox (red button). Removes LiteBox's own files;
leaves the ExtendDB plugin and the shared ThirdParty tools intact. Two opt-in
checkboxes let you also delete the shared thumbnail cache and/or the shared
ThirdParty tools if you want a full wipe.
