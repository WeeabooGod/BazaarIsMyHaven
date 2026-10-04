# BazaarIsMyHaven

**server-side mod** - Majority of features remain functional with optional client side syncing for networked features that require it.

This is an independently maintained derivative of [BazaarIsMyHaven](https://thunderstore.io/c/riskofrain2/p/Def/BazaarIsMyHaven/) by [Deflaktor](https://github.com/Deflaktor/BazaarIsMyHaven), itself based on [BazaarIsMyHome](https://thunderstore.io/c/riskofrain2/p/Lunzir2/BazaarIsMyHome/) by [Lunzir](https://github.com/Lunzir-0325/RoR2-BazaarIsMyHome). This mod will retain the configurable features of BazaarIsMyHaven while developing its own changes for multiplayer behavior and presentation.

The project aims to fix the issues of BazaarIsMyHaven while potentially introducing requested features as well.

- **Fixed Features**
  - Shop Terminals not retaining scale for clients, now they do
      - As a server only mod, this was done by using the Drifterhoard prefab which includes ItemShareController.RpcParentToMuzzle procedure. This RPC function re-scales game-objects even for clients. I do not like this myself, but I wanted to keep this mod server-sided as best as I can
          - The floating item display also had wonky scaling inconsistencies which are also fixed for clients
          - This problem may have not been noticed by much, only really becomes noticeable with lunar buds. Terminals, which the original mod recommends have a scale of .75 so did not really appear that much smaller. Overall you also had to have the lunar shop section enabled all together.
  - Re-rolling did not appear to work (when you had terminals and lunar shop modifications), now it does.
      - It also has additional checks and bookkeeping to ensure it works for both clients and host.
  - Purchases being free was a deliberate choice due to the lack of a cost hologram, but with the ability to actually use lunar buds makes it less of an issue, so they now actually have a price on them.
      - Lunar buds have the cost hologram, and it appears to be networked. Terminals do not for clients, but a solution was to create a single cost hologram based off a seers terminal clone for un-mooded clients. 
  - Instancing not working correctly under specific circumstances
      - Instancing also did not mesh well with re-rolling
  - Buds make a return, now you can have more than 5 buds with Shop features up to 20. They have all the traditional functionality and animations and can even swap lunar equipment. 

- **Reworked Features and Added Changes**
  - Instancing was reworked to include extra states, a queue, and additional bookkeeping for use for re-rolling.
      - When instance purchases was on, various buggy interactions could happen. Host could buy an item, and clients would receive nothing on the same item. Re-roll would re-roll items already purchased from clients. Host was largely authoritative over tracking purchases.
  - Correct buyer states; purchases temporarily use the buyer's shop data then restores the host's view.
      - Instances also track for states to protect re-roll, and swapped equipment
          - Old re-roll path called normal shop-generation. We can now skip purchased shops and have various states to ensure each instance client gets only what should be re-rolled. 
  - Networking broadcast no longer overwrite personal fields; Original already sent targeted updates, but the game had normal synchronization. Broadcast should not overwrite everyone's individual stock or availability which often resulted in mismatching re-rolling.
  - Client updates now arrive in order needed for animation, this is most useful for the buds which wouldn't open on purchases.
  - Queued shop update system allows slow clients to process messages without causing mismatches.

- **New Features**
  - ReplaceLunarShopsWithTerminals now have their cost hologram for host and for clients with the mod also installed. Un-modded clients will only have a single cost on the side of the table. No more price ambiguity.
  - Technically discussed earlier, but a proper client side syncing is available for clients who have the mod installed. Offers a much more reliable, and cleaner way to adjust model size and corrections without having to resort to workarounds.

- **ToDo's**
  - Consider an optional rare Drone Recycler & Combiner to be added to the bazaar

Please refer to Deflaktor's [full documentation](https://github.com/Deflaktor/BazaarIsMyHaven) for the complete list of features. I only went over what I changed and fixed.

This was my own summarization of what I understand. Me, the human. I am a goblin with no experience in creating git commits. I usually never share my work, my commits will be as horrible as can be, Apologies. I aimed to understand what I was doing, despite the assistance I was receiving... speaking of assistance...

---
**AI WAS USED** - Specifically GPT-6 Astra at Extra High

The death-mark a project. Primarily used to help probe solutions for complex problems, like scale not being synced, and networked re-rolling and unity networking in general. I already know I am going to hell for this, but I suppose I shall share my carefully curated verified and tested slop to the masses. 

I did not use code wholesale. I would be given a list based on a read-only review of the project of what I COULD do, then I look through that list and implement where I see fit. I would copy comments where useful, and try to write my own on how I understand it. I am not godly at programming myself, so my approach was Astra being a helper, not a do-it-for-me-all-the-way with no review.

My main goal is to fix bugs that existed. Through my own testing, as much as I can do alone and with UnityExplorer and MultiplayerTesting mod have confirmed some form of stability. Please, if any problems where found, report and I'll try to fix.

Original mod remains completely functional, please use that if you still do not trust me.

---

## Building
Project was edited and made with Visual Studio Community 2026 with the **.NET desktop development** workload and .NET SDK **10.0.401 or a later stable 10.0 feature band**. the supplied .csproj should still work for other IDE's though.

## Known Issues / Multiplayer Considerations

Most host to clients-without-mods functions offer workarounds to provide solutions to problems. 
1. Create a Drifterhoard to hook its networked object to properly scale lunarbuds and lunarshopterminals to their proper size isint its build in RPC commands. This means a drifterhoard will exist in scene
2. Create a copy of a seer terminal to have a common sale price on the table for lunar terminals. terminals do not have a cost hologram, leaving what it price could be ambiguous. It has a similar philosophy to the above.

Host will not generate these work around's alone, or with other clients that have the mod installed.

Its also currently possible slow clients could experience issues with delays in model size corrections, or have it not work entirely, but this should be rare.
