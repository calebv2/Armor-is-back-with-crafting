# Player Armor for A Township Tale

Player Armor adds six wearable sets, a dedicated Armor Workshop, and 28 crafting recipes. Players can mix armor pieces from different sets.

## Download and install

Download `PlayerArmor-0.9.4.zip` from the [0.9.4 release](https://github.com/calebv2/Armor-is-back-with-crafting/releases/tag/v0.9.4).

1. Install MelonLoader and SyncLib on the server and each PC client. This release was built with SyncLib 1.1.1.
2. Copy `Server/Mods/PlayerArmor.Server.dll` into the server’s `Mods` folder.
3. Copy `Client/Mods/PlayerArmor.Client.dll` into each PC client’s `Mods` folder.
4. Restart the server and clients. When upgrading, replace the previous DLL for each role and keep your existing world save, `armor.json`, and `recipe-history.json`.

## Finding and using armor

- **Crafting.** Craft Iron, Hebios, Leather, and Woodland at the Armor Workshop. The central Armor Recipe Book displays learned recipes. An administrator can place a workshop with `ATT_ArmorWorkshop`. It comes with three workbenches and four display stands.
- **Loot.** A world loot chest has a 10% default chance to add one random Mushroom Warden piece when stocked or restocked if a compatible slot is free. Each Wyrm kill has a 5% chance to drop one random Wyrm piece.
- **Equipping.** Open **Equipment** from the quick wheel. Hold the armor piece at the matching point on the dummy and release when the fitting ring turns green. To remove it, grip it on the dummy with an empty hand and pull it away.
- **Disassembly.** Release one loose piece over an empty Armor Workshop bench with its recipe page removed. The outermost ingredient comes loose first. Use a chisel and hammer to recover the remaining parts. Items with saved recipe records return their original materials.
- **Repair.** Servers with a compatible Repair Shop mod can restore durability at that shop. A piece stops protecting at zero durability.

## Materials in 0.9.4

Woodland panels inherit the colors of their wedges. If you mix woods, the piece averages their durability and protection while keeping each panel’s color.

| Wood | Default durability per piece | Default damage reduction |
| --- | ---: | ---: |
| Birch | 200 | 14.4% |
| Oak | 250 | 18% |
| Walnut | 300 | 20.7% |
| Redwood | 350 | 23.4% |
| Ash | 400 | 26.1% |

Leather panels inherit their roll colors. Leather and Hebios straps inherit their strip colors. Iron and Hebios plates keep their metal colors and average their weight and durability multipliers. Older pieces without a saved material palette keep their original appearance.

Server settings live in `UserData/PlayerArmor/armor.json`. The ZIP includes `ExampleArmor.json` as a reference for armor stats, wood multipliers, recipes, and Mushroom Warden chest odds.

## Credits

Original A Township Tale game assets by Alta. The Wyrm armor restoration uses assets from [Nyx12012’s ArmorRestored](https://github.com/Nyx12012/ArmorRestored/tree/4dfa5783a09ca4113f9173275bce74cc10584dd2).
