<img src="icon.png" width="128" align="right" alt="Island AI">

# Island AI

A BepInEx mod for **Mad Island** (Steam) that teaches NPCs and enemies to walk around
things.

The game has no pathfinding. Anything that chases or follows — an enemy after you, a
follower behind you, a villager after an enemy — runs in a straight line at its target and
pushes into whatever is in the way: a tree, a rock, the corner of your house. Island AI
makes them look ahead and steer around it.

## What it changes

- **Enemies** chasing a target walk around obstacles instead of pushing into them.
- **Followers and villagers** do the same when following you or chasing an enemy.
- The mod's icon in the bottom-left of the title screen shows that it is loaded. Hover it for the
  version; click it to switch the mod off or on, which takes effect immediately (a grey icon is a
  mod that is switched off).
- Everything else about how they move is the game's own: give-up distance, water, attacks
  and speed are untouched.

This is a gameplay change, not a cosmetic one: **walls are less of a defence** once enemies
can walk around them. That is why it is its own mod — remove the DLL and the game is
vanilla again.

What it does not do: it is local steering, not route planning. NPCs get around trees,
rocks and corners and will follow along a wall, but they do not solve a closed trap — a U
of walls or a sealed base still stops them.

## Install

Pick one download from the [Releases](../../releases) page:

| Download | For whom |
|---|---|
| `Island-AI-vX.Y.Z-installer.zip` | **Easiest.** Extract it anywhere and double-click `install.bat`. It finds the game through Steam, adds BepInEx only if the game does not have it yet, and copies the mod in. It does not touch other mods or settings, and downloads nothing. `uninstall.bat` removes the mod again. |
| `Island-AI-vX.Y.Z-with-BepInEx.zip` | New to mods, no installer wanted: extract it into the game folder (next to `Mad Island.exe`). Brings BepInEx 5.4.23.5 along. |
| `Island-AI-vX.Y.Z.zip` | You already have BepInEx 5: extract it into the game folder. |

All three put the same thing in the same place:

```
BepInEx/plugins/IslandAI/IslandAI.dll   (plus LICENSE.txt and README.txt)
```

To uninstall by hand, delete the `BepInEx/plugins/IslandAI` folder.

The game folder is usually
`C:\Program Files (x86)\Steam\steamapps\common\Mad Island` — in Steam: right-click the
game, *Manage*, *Browse local files*. You have BepInEx already if that folder contains a
`BepInEx` folder and a `winhttp.dll`. Windows may warn about running a downloaded script;
the installer is plain text and lives in [`installer/`](installer) if you want to read it
first.

## Settings

Created on first launch at `BepInEx/config/madisland.islandai.cfg`:

| Setting | Default | |
|---|---|---|
| `[General] Enabled` | `true` | Master switch, live. Same as clicking the icon on the title screen. |
| `[Steering] Enemies` | `true` | Enemies steer around obstacles. |
| `[Steering] Friends` | `true` | Followers and villagers steer around obstacles. |
| `[Steering] Probe Distance` | `2` | How far ahead, in metres, an NPC looks (0.5 – 6). |
| `[Steering] Method` | `AlongWalls` | How a blocked NPC picks its way, live. `AlongWalls` also tries the direction of the wall it met and takes the heading with the most room nearest its target; it finds narrow passages. `Fan` is the first version's rule: the nearest of eleven fixed headings that is completely clear. |
| `[Steering] Clearance` | `1.1` | How wide the look-ahead is, as a share of the NPC's body (0.8 – 1.3), live. Above 1 rounds corners with room to spare and refuses tight gaps; below 1 squeezes into gaps barely wider than the body and brushes corners. |

## What is in this repository

Each commit here is one release; the version tags match the release zips.

```
src/IslandAI/    the mod: Plugin.cs (settings, startup), Steering.cs (the steering itself)
src/Shared/      PatchCensus.cs — applies the Harmony patches one class at a time and logs
                 any that no longer bind, which is the first thing to check after a game update
installer/       the install.bat / install.ps1 shipped in the installer download
CHANGELOG.md     what changed in each release
icon.png         the mod icon (src/IslandAI/icon.png is the copy embedded in the DLL for the title screen)
LICENSE          GNU GPL v3
```

## Build from source

Needs the .NET SDK and the game installed (the project references the game's and
BepInEx's DLLs from the game folder).

```
dotnet build src/IslandAI/IslandAI.csproj -c Release
```

A build copies `IslandAI.dll` into the game's `BepInEx/plugins`. If the game is somewhere
other than the default Steam path, add `-p:GameDir="D:\...\Mad Island"`; to build without
copying, add `-p:NoDeploy=true`.

## Compatibility

Tested on the Steam `beta_open` branch, build 25095856, with BepInEx 5.4.23.5. After a
game update, look in `BepInEx/LogOutput.log` for the line
`Patch census [IslandAI]: 2 methods across 2 classes, all bound.` — if it says anything
else, the update moved something and the mod needs a new release.

## License

Copyright (c) 2026 Maliekee.

Island AI is free software under the **GNU General Public License, version 3** — see
[LICENSE](LICENSE), which is what counts. In short: you may use it, change it, share it and
put it in a modpack, as long as whatever you distribute stays under the same license, keeps
this notice, and comes with its source. It may not become part of closed-source software.
Installing and playing with it is always fine; the license only concerns distribution.

Want to use it under other terms? [Open an issue](../../issues).
