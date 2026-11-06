# :shield: Player Armor 0.9.4 for A Township Tale

Six armor sets, 42 pieces, 28 crafting recipes, and a dedicated Armor Workshop. Each set has seven pieces, and you can mix pieces from different sets.

## Download and install

Download `PlayerArmor-0.9.4.zip` from the [0.9.4 release](https://github.com/calebv2/Armor-is-back-with-crafting/releases/tag/v0.9.4).

1. Install MelonLoader and SyncLib on the server and each PC client. This release was built with SyncLib 1.1.1.
2. Copy `Server/Mods/PlayerArmor.Server.dll` into the server’s `Mods` folder.
3. Copy `Client/Mods/PlayerArmor.Client.dll` into each PC client’s `Mods` folder.
4. Restart the server and clients. When upgrading, replace the previous DLL for each role and keep your existing world save, `armor.json`, and `recipe-history.json`.

## :hammer_pick: Craftable at the Armor Workshop

- **Iron** — 35% damage reduction · 500 base durability per piece · 14 base weight per set. Uses forged medium square metal plates and buckles.
- **Hebios** — 35% damage reduction · 500 base durability per piece · 12 base weight per set. Uses forged medium square metal plates and leather straps. Dyed straps keep their color.
- **Leather** — 18% damage reduction · 250 durability per piece · 6 weight per set. The rolls set the panel colors, and the strips set the strap colors.
- **Woodland** — 8 weight per set. Uses woodcut wedges and rope. Each panel takes on the color of its wood. Mixed woods keep their individual colors, and the piece averages the durability and protection of its wedges.

### Woodland stats per piece, using one wood throughout

| Wood | Default durability per piece | Default damage reduction |
| --- | ---: | ---: |
| Birch | 200 | 14.4% |
| Oak | 250 | 18% |
| Walnut | 300 | 20.7% |
| Redwood | 350 | 23.4% |
| Ash | 400 | 26.1% |

## :evergreen_tree: Found in the world

- **Mushroom Warden** — 18% damage reduction · 250 durability per piece · 5 weight per set. World loot chests have a 10% chance to add one random piece when stocked or restocked, if a compatible slot is free.
- **Wyrm** — 18% damage reduction · 250 durability per piece · 9 weight per set. Each Wyrm kill has a **5% chance (1 in 20)** to drop one random piece.

## :crossed_swords: Protection, wear, and weight

The piece covering the area hit reduces eligible slashing, piercing, blunt, and explosive damage. Protection applies per hit without stacking across your outfit. Falls, hunger, poison, and fire bypass armor.

By default, each point of damage absorbed costs one point of durability. At zero durability, the piece stops protecting until repaired. Held, stored, and worn armor count toward ATT’s normal load, which can affect climbing. Chest pieces weigh the most.

**Iron and Hebios** can use mixed metals. Each plate keeps its metal’s color. The plate weight and durability multipliers are averaged separately, and protection stays at 35%.

Older pieces without a saved material palette keep their original appearance.

## :hammer: Taking armor apart

1. Remove the recipe page and clear any active project from an Armor Workshop bench.
2. Hold one loose armor piece over the empty bench and release it.
3. The outermost ingredient comes loose first. Use a chisel and hammer to free the rest, working inward.

Pieces with a saved recipe record return their original ingredients, including mixed woods, mixed metals, and dyed leather. Older pieces may use recipe defaults where material details are missing.

## :person_standing: Equipping, displaying, and repairing

- **Equip** through the **Equipment** dummy in your quick wheel or at the matching point on your body. Wait for the ring to turn green, then release.
- **Remove** armor by gripping the equipped piece on the dummy with an empty hand and pulling away.
- **Display** armor on an armor stand. Release each piece against its matching support and grip it to take it back.
- **Repair** damaged armor at the Repair Shop on servers with the compatible mod.

Equipped armor stows with your belongings during the Climbing Tower’s Spirit mode and returns when you leave.

## :book: The Armor Workshop

Three armor workbenches, four display stands, a table with a lockbox, and a dedicated Armor Recipe Book on the central lectern. Pick up the book to browse recipes learned on your server. An administrator can place a workshop with `ATT_ArmorWorkshop`.

## Server settings

Server settings live in `UserData/PlayerArmor/armor.json`. The ZIP includes `ExampleArmor.json` as a reference for armor stats, wood multipliers, recipes, and Mushroom Warden chest odds.

PLAYER ARMOR 0.9.4 - ADMIN SPAWN COMMANDS

Iron armor
spawn PLAYER ATT_IronArmor_Helmet
spawn PLAYER ATT_IronArmor_Cuirass
spawn PLAYER ATT_IronArmor_Pauldron_Left
spawn PLAYER ATT_IronArmor_Pauldron_Right
spawn PLAYER ATT_IronArmor_Bracer_Left
spawn PLAYER ATT_IronArmor_Bracer_Right
spawn PLAYER ATT_IronArmor_Tassets

Leather armor
spawn PLAYER ATT_LeatherArmor_Helmet
spawn PLAYER ATT_LeatherArmor_Cuirass
spawn PLAYER ATT_LeatherArmor_Pauldron_Left
spawn PLAYER ATT_LeatherArmor_Pauldron_Right
spawn PLAYER ATT_LeatherArmor_Bracer_Left
spawn PLAYER ATT_LeatherArmor_Bracer_Right
spawn PLAYER ATT_LeatherArmor_Tassets

Woodland armor
spawn PLAYER ATT_WoodlandArmor_Helmet
spawn PLAYER ATT_WoodlandArmor_Cuirass
spawn PLAYER ATT_WoodlandArmor_Pauldron_Left
spawn PLAYER ATT_WoodlandArmor_Pauldron_Right
spawn PLAYER ATT_WoodlandArmor_Bracer_Left
spawn PLAYER ATT_WoodlandArmor_Bracer_Right
spawn PLAYER ATT_WoodlandArmor_Tassets

Hebios armor
spawn PLAYER ATT_HebiosArmor_Helmet
spawn PLAYER ATT_HebiosArmor_Cuirass
spawn PLAYER ATT_HebiosArmor_Pauldron_Left
spawn PLAYER ATT_HebiosArmor_Pauldron_Right
spawn PLAYER ATT_HebiosArmor_Bracer_Left
spawn PLAYER ATT_HebiosArmor_Bracer_Right
spawn PLAYER ATT_HebiosArmor_Tassets

Wyrm armor
spawn PLAYER ATT_WyrmArmor_Helmet
spawn PLAYER ATT_WyrmArmor_Cuirass
spawn PLAYER ATT_WyrmArmor_Pauldron_Left
spawn PLAYER ATT_WyrmArmor_Pauldron_Right
spawn PLAYER ATT_WyrmArmor_Bracer_Left
spawn PLAYER ATT_WyrmArmor_Bracer_Right
spawn PLAYER ATT_WyrmArmor_Tassets

Iron recipe pages
spawn PLAYER ATT_ArmorRecipe_Iron_Helmet
spawn PLAYER ATT_ArmorRecipe_Iron_Cuirass
spawn PLAYER ATT_ArmorRecipe_Iron_Pauldron_Left
spawn PLAYER ATT_ArmorRecipe_Iron_Pauldron_Right
spawn PLAYER ATT_ArmorRecipe_Iron_Bracer_Left
spawn PLAYER ATT_ArmorRecipe_Iron_Bracer_Right
spawn PLAYER ATT_ArmorRecipe_Iron_Tassets

Leather recipe pages
spawn PLAYER ATT_ArmorRecipe_Leather_Helmet
spawn PLAYER ATT_ArmorRecipe_Leather_Cuirass
spawn PLAYER ATT_ArmorRecipe_Leather_Pauldron_Left
spawn PLAYER ATT_ArmorRecipe_Leather_Pauldron_Right
spawn PLAYER ATT_ArmorRecipe_Leather_Bracer_Left
spawn PLAYER ATT_ArmorRecipe_Leather_Bracer_Right
spawn PLAYER ATT_ArmorRecipe_Leather_Tassets

Woodland recipe pages
spawn PLAYER ATT_ArmorRecipe_Woodland_Helmet
spawn PLAYER ATT_ArmorRecipe_Woodland_Cuirass
spawn PLAYER ATT_ArmorRecipe_Woodland_Pauldron_Left
spawn PLAYER ATT_ArmorRecipe_Woodland_Pauldron_Right
spawn PLAYER ATT_ArmorRecipe_Woodland_Bracer_Left
spawn PLAYER ATT_ArmorRecipe_Woodland_Bracer_Right
spawn PLAYER ATT_ArmorRecipe_Woodland_Tassets

Hebios recipe pages
spawn PLAYER ATT_ArmorRecipe_Hebios_Helmet
spawn PLAYER ATT_ArmorRecipe_Hebios_Cuirass
spawn PLAYER ATT_ArmorRecipe_Hebios_Pauldron_Left
spawn PLAYER ATT_ArmorRecipe_Hebios_Pauldron_Right
spawn PLAYER ATT_ArmorRecipe_Hebios_Bracer_Left
spawn PLAYER ATT_ArmorRecipe_Hebios_Bracer_Right
spawn PLAYER ATT_ArmorRecipe_Hebios_Tassets

Workshop and display
spawn PLAYER ATT_ArmorWorkshop
spawn PLAYER ATT_ArmorCraftingTable
spawn PLAYER ATT_ArmorRecipeBook
spawn PLAYER ATT_ArmorRecipeLectern
spawn PLAYER ATT_ArmorStand

Mushroom Warden gear comes from world loot chests. Wyrm gear can also drop from Wyrms.

## Credits

Original A Township Tale game assets by Alta. The Wyrm armor restoration uses assets from [Nyx12012’s ArmorRestored](https://github.com/Nyx12012/ArmorRestored/tree/4dfa5783a09ca4113f9173275bce74cc10584dd2).
