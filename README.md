# BazaarIsMyHaven

**server-side mod** - Majority of features remain functional with optional client side syncing for networked features that require it.

This is an independently maintained derivative of [BazaarIsMyHaven](https://thunderstore.io/c/riskofrain2/p/Def/BazaarIsMyHaven/) by [Deflaktor](https://github.com/Deflaktor/BazaarIsMyHaven), itself based on [BazaarIsMyHome](https://thunderstore.io/c/riskofrain2/p/Lunzir2/BazaarIsMyHome/) by [Lunzir](https://github.com/Lunzir-0325/RoR2-BazaarIsMyHome). This mod will retain the configurable features of BazaarIsMyHaven while developing its own changes for multiplayer behavior and presentation.

The project aims to fix the issues of BazaarIsMyHaven while potentially introducing requested features as well.

- **New/Fixed Features**
  - Buds make a return, now you can have more than 5 buds with Shop features up to 20. They have all the traditional functionality and animations and can even swap lunar equipment.
        - This had a consequence of revealing limitations of the RoR2 Networking, scale was not synced.
            - Terminals where originally used to mask this problem.
  - Shop Terminals not retaining scale for clients when using lunar buds, now they can
        - As a server sided mod, clients will use a fallback method that spawns a prefab that has a network controller on it to use a RPC teleport command. This sets the scale to 1, 1, 1 for the client, allowing the client to see lunar buds at their original scale when you didn't have the mod, rather than the .5 scale their prefab is at
  - as a consequence to the above, lunar buds can also most of the features that where limited to shipping terminals. 1-20 can exist, they can grow in number as the rounds progress, etc.
  - Re-rolling was fixed, it was broken for what ever reason but now it works with extra features
      - It also has additional checks and bookkeeping to ensure it works for both clients and host.
  - Purchases being free was a deliberate choice due to the lack of a cost hologram when using terminals, but with the ability to actually use lunar buds makes it less of an issue, so they now actually have a price on them.
      - Lunar buds have the cost hologram, and it appears to be networked.
  - Both shipping terminals and traditional lunar buds can also hold equipment for swapping, but beware, it will continue to cost you to swap
  - As stated above, the terminal replacement will show cost on the shipping terminals, synced between clients who have the mod installed
        - Those that do not have a mod uses the fallback discussed before, it will display the set cost to reduce any ambiguity of price. The work around is similar to syncing the scale for lunar buds. Host spawns a seers terminal then uses a network trick to kill the model of the terminal keeping only the price hologram, then teleporting it using built in network teleport to where it needs to be on the table.

- **Networked Features and Instancing**
  - Proper bookkeeping for re-roll eligibility, stock and availability when using Instanced Purchases. Should be much more reliable. 
      - When instance purchases was on, various buggy interactions could happen. Host could buy an item, and clients would receive nothing on the same item. Re-roll would re-roll items already purchased from clients. Host was largely authoritative over tracking purchases.
      - The proper bookkeeping allows reroll to respect host and client shop states, and wont reroll unavailable shops.
  - Client updates now arrive in order needed for animation, this is most useful for the buds which wouldn't open on purchases.
      - There is also a vanilla bug where buds wont open when purchased which should be fixed with the mod enabled

- **Misc Additions**
  - Configs will reload on a new run, so you can edit the config without having to restart. InLobbyConfig would make this even easier but continues to be an optional mod
  - SPEX was concidered a newt for some reason, and would display a welcome message, the AI no longer hallucinates being a big blue glowing slug creature.
  - Attempted to refine the stability of the mod as a whole, adding sanity checks, and cleaning up run bookkeeping earlier. Most problems where never verifiably a problem, but it never hurts being more careful.
      - These sanity checks should help things not breaking so dramatically if and when they do break. There was even a risk of a negative value making cost be the upper limit of a uint, causing players to lose all their lunar coins not that ever happened to me though. If something was to break during init, creation, or a loop, it could break the entire function which can also interrupt other mods, not that I notice it ever happening.

- **ToDo's**
  - Consider an optional rare Drone Recycler & Combiner to be added to the bazaar

Please refer to Deflaktor's [full documentation](https://github.com/Deflaktor/BazaarIsMyHaven) for the complete list of features. I only went over what I changed and fixed.

This was my own summarization of what I understand. Me, the human. I am a goblin with no experience in creating git commits. I usually never share my work, my commits will be as horrible as can be, Apologies. I aimed to understand what I was doing, despite the assistance I was receiving... speaking of assistance...

---
**AI WAS USED** - Specifically GPT-6 Astra at Extra High

The death-mark a project. Primarily used to help probe solutions for complex problems, like scale not being synced, and networked re-rolling and unity networking in general. I already know I am going to hell for this, but I suppose I shall share my carefully curated verified and tested slop to the masses. 

I did not use code wholesale. I would be given a list based on a read-only review of the project of what I COULD do, then I look through that list and implement where I see fit. I would copy comments where useful, and try to write my own on how I understand it. While I am no expert at programming, I do have actual experience with programming. I only used AI to help me where I struggled, and even then I only ever wanted to see examples and goals of what I could do, not have it done for me.

My main goal is to fix bugs that existed. Through my own testing, as much as I can do alone and with UnityExplorer and MultiplayerTesting mod have confirmed some form of stability. Please, if any problems where found, report and I'll try to fix.

Original mod remains completely functional, please use that if you still do not trust me.

---

## Building
Project was edited and made with Visual Studio Community 2026 with the **.NET desktop development** workload and .NET SDK **10.0.401 or a later stable 10.0 feature band**. the supplied .csproj should still work for other IDE's though.

## Known Issues / Multiplayer Considerations

Most host to clients-without-mods functions offer workarounds to provide solutions to problems. 
1. Create a Drifterhoard to hook its networked object to properly scale lunarbuds to their proper size through its buildtin RPC commands. This means a drifterhoard will exist in scene
2. Create a copy of a seer terminal to have a common sale price on the table for lunar terminals. terminals do not have a cost hologram, leaving what it price could be ambiguous. It has a similar philosophy to the above.

Host will not generate these work around's alone, or with other clients that have the mod installed. Host can choose to even have these fallbacks used, though if so, I'd recommend using replacelunarbuds as they wont have a scale problem.

Its also currently possible slow clients could experience issues with delays in model size corrections, or have it not work entirely, but this should be rare.
