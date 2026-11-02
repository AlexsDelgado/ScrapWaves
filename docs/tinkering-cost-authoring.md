# Tinkering cost authoring

1. Stop Play Mode. In the Project window, select **Assets > ScriptableObjects > Economy > MaterialUsageBalance.asset** (the production asset, not `MaterialUsageBalance_Test`). The player's **Weapon Crafting Service > Material Balance** references this asset.
2. **Basic tinkering: each common material** contains **Basic Second Weapon Cost** and **Basic Third Weapon Cost**. Each number is the cost of each of Sheet Metal, Metal Pipe and Gears when adding that equipped-weapon slot. The defaults remain 5 and 15.
3. **Advanced tinkering: each rare material** contains **Advanced First Weapon Cost**, **Advanced Second Weapon Cost** and **Advanced Third Weapon Cost**, defaulting to 5, 15 and 30. Each number is charged for each of Jellified Fuel, Plastic Explosive and Wiring. The tier counts equipped weapons already on an advanced path plus one; it is not the selected weapon's inventory slot. Level 5 to 6 uses this price.
4. **After rejecting an advanced offer: each rare material** contains the corresponding three prices, defaulting to 8, 22 and 45. Buying an advanced offer consumes the current attempt's price before rolling or revealing its path. Accepting applies level 6 and the path without another payment. Declining keeps level 5 with no path and no refund. The next attempt on that weapon uses this independently authored tier and guarantees the other path. The price is no longer described as a fixed percentage increase.
5. Save the asset with **Ctrl+S**, then start a new test run. Costs, affordability/pulse checks, UI readouts and payments all read the same balance through `WeaponCraftingService`. A paid basic-tinkering choice keeps its already reserved cost until completed or refunded; editing a price does not change an existing paid reservation.

The settings are global shared amounts per material. This change does not add per-weapon recipes, change material types, or move offer selection, slot limits, unlocks, rejection, or counting behavior into the asset. Zero intentionally makes a tier free. The Inspector prevents negative values; negative serialized data is read as zero without rewriting the asset. Invalid tier indices produce no cost entries, as before.

**Ordinary upgrades: material roles and totals** retains the existing **Role Assignments** (`Column`, `Material`, `Role`) and **Role Totals** (`Role`, destination `Level`, `Total`). These rows still determine ordinary upgrade materials and amounts. Editing tinkering fields does not alter them or weapon stats.

**CSV import:** `Assets/Data/Balance/balance_material_usage.csv` continues to author ordinary upgrade roles/totals. **ScrapWaves > Balance > Import Material Usage CSV** replaces and saves those rows, while retaining all eight tinkering values, including zero. There is no automatic CSV refresh of populated assets. Newly created and legacy assets missing the new fields start at the previous prices; a missing balance reference uses the same defaults without writing an asset. No migration or Apply button is required, and no balance-sheet numbers are synchronized.

Serialized fields: `_basicSecondWeaponCost`, `_basicThirdWeaponCost`, `_advancedFirstWeaponCost`, `_advancedSecondWeaponCost`, `_advancedThirdWeaponCost`, `_rejectedFirstWeaponCost`, `_rejectedSecondWeaponCost`, `_rejectedThirdWeaponCost`.

## Advanced offer popup

`CraftingMenu/Window/Details/AdvancedTinkering` shows the purchase button and material cost before buying. The paid offer is revealed in `CraftingMenu/AdvancedTinkeringChoice/Window`, with editable path/effects, paid-cost and consequences labels, and `Actions/AcceptButton` / `Actions/DeclineButton`.

Edit these objects in `Assets/Prefabs/UI/RunMenus/CraftingMenu.prefab` outside Play Mode; GameplayScene inherits that prefab. SampleScene and the four legacy testing scene menus have their own saved copies. Runtime only binds text and toggles those authored objects. The explicit editor migration is **ScrapWaves > UI > Author Advanced Tinkering Popup**, with a separate **In Scene Menus** command for the legacy copies.

A paid offer blocks station close, tutorial close and other crafting until resolved. Forced presentation disable/recreation retains the paid offer in the player service and reopening neither pays nor rolls again. Decline is available once, only when the other path is unlocked; the guaranteed retry cannot be declined. The paid cost is captured at purchase, so later balance or inventory changes cannot charge it again.
