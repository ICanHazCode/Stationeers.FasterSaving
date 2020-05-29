# Faster world saving for Stationeers
This is a QOL improvement

Saving a game in Stationeers takes a long time if you've been running your world for some time.

This mod fixes an issue in how saving is processed.

## Building from source

You need to set your Reference Paths for the project to your Stationeers install
 - \<steam install path\>/steamapps/common/Stationeers/rocketstation_Data/Managed
 - \<steam install path\>/steamapps/common/Stationeers/BepInEx/core
 
 You also need the [ICanHazCode.ModUtils](https://github.com/ICanHazCode/ModUtils) dll.
 Put this in the `BepInEx/core` folder so it can be found and loaded with the fastersaving patch.


## Releases

To download, get it from the [Releases](https://github.com/ICanHazCode/Stationeers.FasterSaving/releases) page.


## Requirements
This is made with a modified version of [BepInEx 5.0.1](https://github.com/BepInEx/BepInEx/releases) that uses the HarmonyX code at [HarmonyX](https://github.com/BepInEx/HarmonyX).
The reason is that later versions of Unity (around 2017.x to now) cut out many of the code building functions needed by transpiler mods.
I've tried with the original BepInEx with Harmony and couldn't get it to work.

Also needed is [ICanHazCode.ModUtils](https://github.com/ICanHazCode/ModUtils).

This version is compatible with BepInEx 5.0.1 so you can just drop it in place of the original.
[BepInEx with HarmonyX](https://github.com/ICanHazCode/BepInEx/releases)

## Installation
1. Install BepInEx in the Stationeers steam folder.
2. Install the [ICanHazCode.ModUtils.dll](https://github.com/ICanHazCode/ModUtils) into BepInEx/core
3. Launch the game, reach the main menu, then quit back out.
4. In the steam folder, there should now be a folder BepInEx/Plugins
5. Copy the [stationeers.fastsaves]() folder from this mod into BepInEx/Plugins


