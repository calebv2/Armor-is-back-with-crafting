# :shield: Player Armor 0.10.24 for A Township Tale

Eight full armor sets, standalone helmets, 35 crafting recipes, and a dedicated Armor Workshop. Each set has seven pieces, and you can mix pieces from different sets.

## Download and install

Download the ZIP for your setup from the [0.10.24 release](https://github.com/calebv2/Armor-is-back-with-crafting/releases/tag/v0.10.24).

| Package | DLL | Where to install |
| --- | --- | --- |
| Combined (`PlayerArmor-0.10.24.zip`) | `PlayerArmor.dll` | Shared `Mods` folder, server, or PC client |
| Server (`PlayerArmor-0.10.24-Server.zip`) | `PlayerArmor.Server.dll` | Server `Mods` folder |
| Client (`PlayerArmor-0.10.24-Client.zip`) | `PlayerArmor.Client.dll` | PC client `Mods` folder |

1. Install MelonLoader and SyncLib on the server and each PC client. This release was built with SyncLib 1.1.1.
2. Choose one build per `Mods` folder. When switching builds, remove the previous Player Armor DLL from that folder.
3. Copy the DLL from the ZIP's `Mods` folder into the corresponding `Mods` folder. The Combined build detects the process type automatically.
4. Restart the server and clients. Keep your existing world save, `armor.json`, and `recipe-history.json`.

Version 0.10.24 tucks protruding cosmetic vertices under equipped armor. Exposed material keeps its original shape, and the full garment returns to its original fit when the armor comes off. This is a client-only visual upgrade for existing 0.10.22 and 0.10.23 servers.

The armor-wheel dummy shows a durability percentage and condition bar for each equipped piece. The display refreshes with server armor state and disappears when the dummy closes.

## :hammer_pick: Craftable at the Armor Workshop

- **Forgeguard** — 65% melee damage reduction · 75% projectile damage reduction · 500 base durability per piece · 24 base weight per set. Heavy forged plates, a raised collar, layered shoulder and waist plates, and articulated gauntlets. A full set reduces walking and running speed by 30%. Each equipped piece contributes to that penalty; taking it off removes its contribution. Uses medium square metal plates and buckles.

- **Iron** — 35% damage reduction · 250 base durability per piece · 14 base weight per set. Uses forged medium square metal plates and buckles.
- **Hebios** — 35% damage reduction · 250 base durability per piece · 12 base weight per set. Uses forged medium square metal plates and leather straps. Dyed straps keep their color.
- **Leather** — 18% damage reduction · 125 durability per piece · 6 weight per set. The rolls set the panel colors, and the strips set the strap colors.
- **Woodland** — 8 weight per set. Uses woodcut wedges and rope. Each panel takes on the color of its wood. Mixed woods keep their individual colors, and the piece averages the durability and protection of its wedges.

The Woodland Helmet uses three woodcut wedges and one rope clump. Each group of bucket staves takes on the color of its wedge. The iron bands are part of the helmet model.

### Woodland stats per piece, using one wood throughout

| Wood | Default durability per piece | Default damage reduction |
| --- | ---: | ---: |
| Birch | 100 | 14.4% |
| Oak | 125 | 18% |
| Walnut | 150 | 20.7% |
| Redwood | 175 | 23.4% |
| Ash | 200 | 26.1% |

## :evergreen_tree: Found in the world

- **Mushroom Warden** — 18% damage reduction · 125 durability per piece · 5 weight per set. World loot chests have a 10% chance to add one random piece when stocked or restocked, if a compatible slot is free.
- **Wyrm** — 18% damage reduction · 125 durability per piece · 9 weight per set. Each regular Wyrm kill has a **5% chance (1 in 20)** to drop one random Wyrm piece.
- **Crystal Wyrm** — 30% damage reduction · 250 durability per piece · 9 weight per set. Each Crystal Wyrm kill has a **5% chance (1 in 20)** to drop one random Crystal Wyrm piece. Trial Wyrms roll from the pool for their matching variant.
- **Newsboy Cap** — Cosmetic headwear · 0.5 item weight. World loot chests have a **1% chance (1 in 100)** to add a cap when stocked or restocked if a compatible slot is free.
- **Bucket Helmet** — 18% damage reduction · 125 durability · 1.5 item weight. World loot chests have a separate **1% chance (1 in 100)** to add a helmet when stocked or restocked if a compatible slot is free.
- **Spriggull Helmet** — 18% damage reduction · 125 durability · 1.5 item weight. Each Spriggull kill has a **1% chance (1 in 100)** to drop one helmet.
- **Cauldron Helmet** — 18% damage reduction · 125 durability · 1.5 item weight. It has its own **1% chance (1 in 100)** on world chest stock or restock when a compatible slot is free.

## :crossed_swords: Protection, wear, and weight

The piece covering the area hit reduces eligible slashing, piercing, blunt, and explosive damage. Protection applies per hit without stacking across your outfit. Falls, hunger, poison, and fire bypass armor.

By default, each point of damage absorbed costs one point of durability. At zero durability, the piece stops protecting until repaired. Held, stored, and worn armor count toward ATT’s normal load, which can affect climbing. Chest pieces weigh the most.

**Iron and Hebios** can use mixed metals. Each plate keeps its metal’s color. The plate weight and durability multipliers are averaged separately, and protection stays at 35%.

**Forgeguard** also inherits plate colors, material weight, and durability multipliers. Its protection remains 65% melee / 75% projectile regardless of plate metal. Its movement penalty is separate from the native item-weight system.

Older pieces without a saved material palette keep their original appearance.

### Full-set weight by metal

Each total covers all seven pieces with every plate made from the same standard ATT metal. The table runs from lightest to heaviest.

| Plate metal | Iron set weight | Hebios set weight |
| --- | ---: | ---: |
| Mythril | 10.50 | 9.00 |
| Copper | 11.90 | 10.20 |
| Iron | 14.00 | 12.00 |
| Red Iron | 16.80 | 14.40 |
| Palladium | 17.50 | 15.00 |
| Silver | 21.00 | 18.00 |
| Electrum | 22.40 | 19.20 |
| Viridium | 24.50 | 21.00 |
| Gold | 42.00 | 36.00 |
| Valyan | 42.00 | 36.00 |

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

Three armor workbenches, four display stands, a table with a lockbox, and a dedicated Armor Recipe Book on the central lectern. The book opens to the first learned page and groups recipes in Iron, Leather, Woodland, Hebios, and Forgeguard order. Each set runs from helmet to tassets. An administrator can place a workshop with `ATT_ArmorWorkshop`.

## Server settings

Server settings live in `UserData/PlayerArmor/armor.json`. The release ZIP includes [ExampleArmor.json](ExampleArmor.json), which shows how to configure armor stats, wood multipliers, recipes, and Mushroom Warden chest odds.

If you are upgrading from before 0.10.11, the server halves maximum durability values in schema 1 or 2 once and backs up the previous settings as `armor.json.pre-durability-halving.bak`. Existing pieces keep their current condition percentage.

## Build from source

Use Windows with a local A Township Tale PC installation, MelonLoader with its `net35` assemblies, SyncLib 1.1.1, and Unity 2020.3.23f1’s Roslyn C# compiler. The repository includes the runtime source and prebuilt Unity art bundles used by the mod.

From the repository root in PowerShell, run the build with your game directory.

```powershell
.\src\Build-Windows.ps1 -GamePath 'D:\Games\A Township Tale'
```

If Unity is installed outside the default Unity Hub path, pass the path to `csc.exe` with `-CompilerPath`. The default build writes the combined `PlayerArmor.dll` to `src/bin/`. Pass `-Package Client` or `-Package Server` for a role-specific DLL.

## Admin spawn commands

Replace `PLAYER` with the in-game username of a connected player. Run one command at a time in the server console.

### Iron armor

```text
spawn PLAYER ATT_IronArmor_Helmet
spawn PLAYER ATT_IronArmor_Cuirass
spawn PLAYER ATT_IronArmor_Pauldron_Left
spawn PLAYER ATT_IronArmor_Pauldron_Right
spawn PLAYER ATT_IronArmor_Bracer_Left
spawn PLAYER ATT_IronArmor_Bracer_Right
spawn PLAYER ATT_IronArmor_Tassets
```

### Leather armor

```text
spawn PLAYER ATT_LeatherArmor_Helmet
spawn PLAYER ATT_LeatherArmor_Cuirass
spawn PLAYER ATT_LeatherArmor_Pauldron_Left
spawn PLAYER ATT_LeatherArmor_Pauldron_Right
spawn PLAYER ATT_LeatherArmor_Bracer_Left
spawn PLAYER ATT_LeatherArmor_Bracer_Right
spawn PLAYER ATT_LeatherArmor_Tassets
```

### Woodland armor

```text
spawn PLAYER ATT_WoodlandArmor_Helmet
spawn PLAYER ATT_WoodlandArmor_Cuirass
spawn PLAYER ATT_WoodlandArmor_Pauldron_Left
spawn PLAYER ATT_WoodlandArmor_Pauldron_Right
spawn PLAYER ATT_WoodlandArmor_Bracer_Left
spawn PLAYER ATT_WoodlandArmor_Bracer_Right
spawn PLAYER ATT_WoodlandArmor_Tassets
```

### Hebios armor

```text
spawn PLAYER ATT_HebiosArmor_Helmet
spawn PLAYER ATT_HebiosArmor_Cuirass
spawn PLAYER ATT_HebiosArmor_Pauldron_Left
spawn PLAYER ATT_HebiosArmor_Pauldron_Right
spawn PLAYER ATT_HebiosArmor_Bracer_Left
spawn PLAYER ATT_HebiosArmor_Bracer_Right
spawn PLAYER ATT_HebiosArmor_Tassets
```

### Wyrm armor

```text
spawn PLAYER ATT_WyrmArmor_Helmet
spawn PLAYER ATT_WyrmArmor_Cuirass
spawn PLAYER ATT_WyrmArmor_Pauldron_Left
spawn PLAYER ATT_WyrmArmor_Pauldron_Right
spawn PLAYER ATT_WyrmArmor_Bracer_Left
spawn PLAYER ATT_WyrmArmor_Bracer_Right
spawn PLAYER ATT_WyrmArmor_Tassets
```

### Crystal Wyrm armor

```text
spawn PLAYER ATT_CrystalWyrmArmor_Helmet
spawn PLAYER ATT_CrystalWyrmArmor_Cuirass
spawn PLAYER ATT_CrystalWyrmArmor_Pauldron_Left
spawn PLAYER ATT_CrystalWyrmArmor_Pauldron_Right
spawn PLAYER ATT_CrystalWyrmArmor_Bracer_Left
spawn PLAYER ATT_CrystalWyrmArmor_Bracer_Right
spawn PLAYER ATT_CrystalWyrmArmor_Tassets
```

### Standalone helmets

These helmets use the normal head slot. The Bucket Helmet, Newsboy Cap, and Cauldron Helmet can be found in world loot chests. Spriggulls can drop the Spriggull Helmet.

```text
spawn PLAYER ATT_Helmet_Newsboy
spawn PLAYER ATT_Helmet_Bucket
spawn PLAYER ATT_Helmet_SpriggullFull
spawn PLAYER ATT_Helmet_Cauldron
```

### Iron recipe pages

```text
spawn PLAYER ATT_ArmorRecipe_Iron_Helmet
spawn PLAYER ATT_ArmorRecipe_Iron_Cuirass
spawn PLAYER ATT_ArmorRecipe_Iron_Pauldron_Left
spawn PLAYER ATT_ArmorRecipe_Iron_Pauldron_Right
spawn PLAYER ATT_ArmorRecipe_Iron_Bracer_Left
spawn PLAYER ATT_ArmorRecipe_Iron_Bracer_Right
spawn PLAYER ATT_ArmorRecipe_Iron_Tassets
```

### Leather recipe pages

```text
spawn PLAYER ATT_ArmorRecipe_Leather_Helmet
spawn PLAYER ATT_ArmorRecipe_Leather_Cuirass
spawn PLAYER ATT_ArmorRecipe_Leather_Pauldron_Left
spawn PLAYER ATT_ArmorRecipe_Leather_Pauldron_Right
spawn PLAYER ATT_ArmorRecipe_Leather_Bracer_Left
spawn PLAYER ATT_ArmorRecipe_Leather_Bracer_Right
spawn PLAYER ATT_ArmorRecipe_Leather_Tassets
```

### Woodland recipe pages

```text
spawn PLAYER ATT_ArmorRecipe_Woodland_Helmet
spawn PLAYER ATT_ArmorRecipe_Woodland_Cuirass
spawn PLAYER ATT_ArmorRecipe_Woodland_Pauldron_Left
spawn PLAYER ATT_ArmorRecipe_Woodland_Pauldron_Right
spawn PLAYER ATT_ArmorRecipe_Woodland_Bracer_Left
spawn PLAYER ATT_ArmorRecipe_Woodland_Bracer_Right
spawn PLAYER ATT_ArmorRecipe_Woodland_Tassets
```

### Hebios recipe pages

```text
spawn PLAYER ATT_ArmorRecipe_Hebios_Helmet
spawn PLAYER ATT_ArmorRecipe_Hebios_Cuirass
spawn PLAYER ATT_ArmorRecipe_Hebios_Pauldron_Left
spawn PLAYER ATT_ArmorRecipe_Hebios_Pauldron_Right
spawn PLAYER ATT_ArmorRecipe_Hebios_Bracer_Left
spawn PLAYER ATT_ArmorRecipe_Hebios_Bracer_Right
spawn PLAYER ATT_ArmorRecipe_Hebios_Tassets
```

### Workshop and display

```text
spawn PLAYER ATT_ArmorWorkshop
spawn PLAYER ATT_ArmorCraftingTable
spawn PLAYER ATT_ArmorRecipeBook
spawn PLAYER ATT_ArmorRecipeLectern
spawn PLAYER ATT_ArmorStand
```

Mushroom Warden gear comes from world loot chests. Regular Wyrms drop Wyrm gear, and Crystal Wyrms drop Crystal Wyrm gear.

## Credits

Original A Township Tale game assets by Alta. The Wyrm armor restoration uses assets from [Nyx12012’s ArmorRestored](https://github.com/Nyx12012/ArmorRestored/tree/4dfa5783a09ca4113f9173275bce74cc10584dd2).
