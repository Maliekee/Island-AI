# Changelog

## 0.2.1

- Fixed: an NPC steered round an obstacle faced its target while walking another way ("moonwalking"). It now faces the way it walks.

## 0.2.0

- Narrow passages: a walker that meets a wall now also tries the direction of that wall, and takes the heading with the most room nearest its target. It finds corridors and gaps the first version walked past, and no longer turns back when its target crosses a corridor's line.
- New setting `[Steering] Method`: `AlongWalls` (the new default) or `Fan` (the first version's rule), so the two can be compared. Applies live.
- New setting `[Steering] Clearance` (0.8–1.3, default 1.1): how wide a walker looks ahead, against its own body. Below 1 squeezes into gaps barely wider than the body and brushes corners. Applies live.
- The mod's icon on the title screen shows that it loaded; hover for the version, click to switch the mod off or on.
- New setting `[General] Enabled`: the same switch as the title-screen icon. Applies live.
- Still not solved: very tight gaps are sometimes passed, and a U of walls or a closed base still traps.

## 0.1.0

First release.

- Enemies chasing a target steer around obstacles instead of pushing into them.
- Followers and villagers steer around obstacles when following you or chasing an enemy.
- Settings: `[Steering] Enemies`, `Friends`, `Probe Distance`.
