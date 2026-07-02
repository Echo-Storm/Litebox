# Building the LiteBox releases (4 artifacts)

`build-release.ps1` produces **four** artifacts — two runtimes × two forms — under `release\`
(git-ignored). One command:

```powershell
powershell -ExecutionPolicy Bypass -File build-release.ps1
```

---

## Why four

LaunchBox moved its runtime from **.NET 9** (13.x stable) to **.NET 10** (newer alpha). LiteBox borrows
LaunchBox's SDK (`Unbroken.LaunchBox.Plugins.dll`) at runtime and **must run on the same runtime family**,
so we ship one set per runtime:

| Runtime | For a LaunchBox where `Core\LaunchBox.runtimeconfig.json` says… |
|---------|----------------------------------------------------------------|
| **net9**  | `"tfm": "net9.0"`  |
| **net10** | `"tfm": "net10.0"` |

Each runtime ships two **forms** — **both self-contained** (see next box), differing in how they carry the runtime:

| Form | Runtime | ~Size | Install |
|------|---------|-------|---------|
| **standalone** | bundles its own .NET runtime (single-file)        | ~85 MB | self-installing, runs anywhere |
| **light "zip"** | **borrows** the runtime already in `LaunchBox\Core` | ~13 MB | extract into `<LaunchBox>\Core`; runs only from there |

> **How the light build shares LaunchBox's runtime.** It is published **self-contained** (so its
> `runtimeconfig.json` has `includedFrameworks`, exactly like `LaunchBox.exe`), then **stripped** of every
> runtime DLL — only `LiteBox.exe`, `LiteBox.dll`, `LiteBox.deps.json`, `LiteBox.runtimeconfig.json` ship. In
> `Core`, the host loads `coreclr.dll` and all framework assemblies from `Core` itself (they're right there,
> because LaunchBox is a self-contained flat deployment). This is the ONLY way a small build can run in Core:
> a plain *framework-dependent* build fails there — Core's flat runtime hijacks its host resolution and it
> can't find an installed shared framework ("You must install .NET"). The light build runs **only from Core**
> (that's where the runtime it borrows lives).
>
> The **net10 standalone** also runs on a .NET 9 LaunchBox (it carries its own .NET 10 runtime; the net9 SDK
> loads fine on a net10 runtime). A net10 **light** build needs a .NET 10 `Core` — it's tied to the runtime
> major (net9/net10), like everything else. That's why all four exist.

---

## Prerequisites

- **.NET SDK 10.x** — it targets **both** `net9.0-windows` and `net10.0-windows`. Check with `dotnet --list-sdks`.
- A **.NET 9 LaunchBox** available on disk, used *only* as the net9 compile reference (see next section). Default `G:\LB`.
- Windows + PowerShell (5.1 is fine).

### The net9 SDK reference (the one non-obvious bit)

A **net9** project **cannot** reference LaunchBox's **net10** build of `Unbroken.LaunchBox.Plugins.dll`
— it fails to compile with **CS1705** because that DLL pulls in `System.Runtime, Version=10.0.0.0`. So:

- the **net9** target compiles its SDK reference against a **.NET 9** LaunchBox's copy, and
- the **net10** target compiles against the primary `..\..\..\LB\Core` (net10).

This split lives in `LiteBox.csproj` via `$(SdkRefDll)` / `$(Lb9Root)` / `$(Lb10Root)`:

- default net9 root = **`G:\LB`** → `G:\LB\Core\Unbroken.LaunchBox.Plugins.dll`;
- default net10 root = the primary LB beside the repo (`..\..\..\LB`);
- override either: `build-release.ps1 -Lb9Root "D:\LB-net9" -Lb10Root "D:\LB-net10"` (or `dotnet … -p:Lb9Root=… -p:Lb10Root=…`).
- the script also reads each root's `Core\LaunchBox.exe` **version** to name the outputs (`<ver>_net9`, `LiteBox-<ver>.exe`, …).

`Magick.NET*` and `Microsoft.Data.Sqlite` / `SQLitePCLRaw*` are **net8**, so they satisfy *both* targets —
only the LaunchBox SDK reference is split. All of these are `Private=false` (compile-time only); at runtime
LiteBox resolves whatever version LaunchBox ships, by simple name.

---

## Output layout

```
release\
  <ver>_net9\          (e.g. 13.27_net9)
    standalone\   LiteBox-<ver>.exe    README.txt
    zip\          LiteBox-<ver>.zip    README.txt
  <ver>_net10\         (e.g. 13.28_net10)
    standalone\   LiteBox-<ver>.exe    README.txt
    zip\          LiteBox-<ver>.zip    README.txt
```

`<ver>` = Major.Minor of the LaunchBox each build compiled against (net9 → Lb9Root's LaunchBox, net10 →
Lb10Root's); the script reads it from `<LbRoot>\Core\LaunchBox.exe`. It's just a label — a net9 build runs
on ANY .NET 9 LaunchBox, a net10 build on any .NET 10 one; the runtime is the real requirement.

- **standalone** = the self-contained single-file exe (native payload embedded). The distributed file is
  versioned; on install it self-copies to `Core\LiteBox.exe`. Ship the `.exe` (+ README).
- **light "zip"** = extract into `<LaunchBox>\Core` → `Core\LiteBox.exe` + `LiteBox.dll` + `LiteBox.deps.json`
  + `LiteBox.runtimeconfig.json` + `Core\litebox\thirdparty\<8 native files>`. The `.zip` is versioned but
  **the exe inside stays `LiteBox.exe`** — it lands in Core as-is, and the uninstaller + ExtendDB
  host-detection key on that name, so it must NOT be renamed. The `README.txt` sits *beside* the zip, not
  inside it. (`LiteBox.dll` + the two `.json` are the light build's app + its self-contained runtimeconfig;
  the runtime DLLs themselves are stripped — Core provides them.)

---

## What the script runs (per TFM = `net9.0-windows` or `net10.0-windows`)

Both forms are **self-contained** (`-p:SelfContained=true`); `-p:LiteBoxDist=` picks the shape.

```powershell
# A) standalone — self-contained SINGLE-FILE; payload embedded, self-installing (defines FULL_INSTALLER)
dotnet publish LiteBox.csproj -c Release -r win-x64 -f <tfm> -p:SelfContained=true -p:PublishSingleFile=true  -p:LiteBoxDist=standalone -p:Lb9Root=G:\LB -o <dir>
#    then copy <dir>\LiteBox.exe -> release\<ver>_<label>\standalone\LiteBox-<ver>.exe

# B) light "zip" — self-contained NON-single-file, then STRIP the runtime (Core provides it)
dotnet publish LiteBox.csproj -c Release -r win-x64 -f <tfm> -p:SelfContained=true -p:PublishSingleFile=false -p:LiteBoxDist=light      -p:Lb9Root=G:\LB -o <dir>
#    keep ONLY  <dir>\{LiteBox.exe, LiteBox.dll, LiteBox.deps.json, LiteBox.runtimeconfig.json}  (delete the rest = the .NET runtime)
#    then stage those 4 at the zip root  +  <stage>\litebox\thirdparty\<8 files from .\thirdparty\>   (exe KEEPS the name LiteBox.exe)
#    then Compress-Archive <stage>\*  ->  release\<ver>_<label>\zip\LiteBox-<ver>.zip
#    (<ver> = Major.Minor of <LbRoot>\Core\LaunchBox.exe ; <label> = net9 / net10)
```

The **8 payload files** (source of truth) live in `.\thirdparty\`. That list is duplicated in three places
— keep them in sync:

1. `.\thirdparty\` (the files themselves),
2. the `EmbeddedResource` block in `LiteBox.csproj` (embedded into the standalone build),
3. `NativeInstaller.Payload` in `Host\Install\NativeInstaller.cs` (the src→ThirdParty mapping).

---

## Verify (so it comes out right every time)

After `build-release.ps1`, confirm:

1. **Both TFM compiled** — the script throws on any publish failure. For a manual clean check:
   `dotnet build -f net9.0-windows --no-incremental` and `-f net10.0-windows` → `Build succeeded`, `0 Error(s)`.
2. **Four artifacts** exist: `release\<ver>_net9\{standalone,zip}\` and `release\<ver>_net10\{standalone,zip}\`,
   named `LiteBox-<ver>.exe` and `LiteBox-<ver>.zip`.
3. **Light zip contents** (list it) = exactly `LiteBox.exe` + `LiteBox.dll` + `LiteBox.deps.json` +
   `LiteBox.runtimeconfig.json` + `litebox\thirdparty\<8 files>`; `README.txt` is *beside* the zip, not inside;
   the inner exe stays `LiteBox.exe`.
4. **Light runtimeconfig matches the runtime** — `LiteBox.runtimeconfig.json` in the net9 zip lists
   `includedFrameworks` 9.0.x; in the net10 zip, 10.0.x. (Plain readable JSON — no binary grep needed.)
5. **The light build runs on Core's runtime** (the decisive one) — copy the 4 app files **plus every `*.dll`
   from `<LB>\Core`** into a temp folder, then run `LiteBox.exe --migrate`: it must exit `0` and print
   `[migrate…]`, with **no** "You must install .NET" on stderr. Do it once per TFM (net9 → DLLs from a .NET 9
   Core such as `G:\LB\Core`; net10 → from the .NET 10 `Core`).
6. **Each build references its own runtime's SDK** — `GetReferencedAssemblies()` on
   `bin\Release\<tfm>\LiteBox.dll` shows `Unbroken.LaunchBox.Plugins` at the net9 vs net10 version (currently
   13.27 vs 13.28). It locks nothing — at runtime the real Core DLL is loaded by simple name.
7. **Sizes** ≈ standalone 84 MB, zip 13 MB (app ~2.3 MB, payload ~29 MB raw → ~13 MB zipped).

The definitive end-to-end check is human: extract the matching light zip into a **real** `<LaunchBox>\Core`
and launch `Core\LiteBox.exe` — the UI should boot.

---

## When a future LaunchBox SDK update breaks the build (CS0535)

LiteBox's data objects extend generated dummies — `Generated\Dummies.g.cs` has one `Dummy<Iface>` class per
SDK interface, and `HostGame : DummyGame`, `HostEmulator : DummyEmulator`, etc. rely on them to auto-implement
the ~86 members of `IGame` and friends. When LaunchBox **adds** interface members, the build fails with
**CS0535** ("`Dummy…` does not implement interface member …").

Fix:

1. Get the exact member types (reflect the new SDK — e.g. `MetadataLoadContext` over
   `LB\Core\Unbroken.LaunchBox.Plugins.dll`), then add them to the matching `Dummy…` class in
   `Dummies.g.cs` (style: `public virtual global::System.<Type> <Name> { get; set; }`), **or**
2. delete the file and regenerate once the project compiles: `dotnet run --project . -- --gen-stubs`.

Extra members are harmless on an older SDK, so the **net10 superset satisfies the net9 build too** — you only
need to patch the file once, against the newest SDK.

> This already happened for the net10 SDK (13.28): it added `StartupScreenPostLaunchDisplayTime` (int) +
> `MonitorStartupShutdownWithProcess` (bool) to both `IGame` and `IEmulator`, plus `ForceFrontendFocusOnShutdown`
> (bool) to `IEmulator`. Those five are patched into `Dummies.g.cs`.
