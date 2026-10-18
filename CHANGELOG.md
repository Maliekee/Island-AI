# Changelog

## 0.2.4

- Fixed: walking with WASD was steered like an NPC's walk - pressing towards a wall or a chest turned you along it, as if the game were pathfinding for you. The character you play is never steered, and never let out of a house by Door Assist.

## 0.2.3

- Steering now covers every walk outdoors, not only chasing and following: wandering, walking to a point, running away, and a villager's walk to a house at night. That walk used to end against the first chest or planter on the straight line to the door, all night (found with a friend pressed into a chest in front of the farm).
- A villager walking to a house at night walks into the house, as the game takes her in on touching it; it is never steered around.

## 0.2.2

- Fixed: your people could get stuck inside the Native House, standing at the front wall beside the door. The house's exit sits mostly inside its wall, and a friend walking out stopped just short of it. A friend who reaches the door now gets out, as the door would have let them.
- New setting `[Houses] Door Assist` (default on): the fix above; off leaves houses as in the unmodded game. Applies live.

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
