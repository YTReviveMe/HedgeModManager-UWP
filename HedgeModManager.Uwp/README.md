# HedgeModManager UWP

This is the first Xbox dev mode UWP skeleton for HedgeModManager.

Current scope:

- Pick an Unleashed game folder or its `Mods` folder.
- Detect child folders containing `mod.ini`.
- Read title, author, version, and existing enabled state from `ModsDB.ini`.
- Toggle mods on/off.
- Save a compatible `ModsDB.ini` with `[Main] ActiveMod=...` and `[Mods] guid=...\mod.ini` entries.

Intentionally not included in this first slice:

- Add game / game launcher flows.
- Save and play.
- Mod downloading, GameBanana, updates, profiles, code compilation, and PC dependency checks.

Notes for Xbox:

- The app uses `FolderPicker` and stores access in `FutureAccessList`.
- The manifest includes `removableStorage` and `broadFileSystemAccess` so dev mode builds can reach game/mod folders where the platform allows it.
- Saving regenerates the transient GUIDs the same way desktop HMM does, so the important stable data remains the actual `mod.ini` path written into `ModsDB.ini`.
