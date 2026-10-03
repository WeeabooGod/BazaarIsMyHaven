# BazaarIsMyHaven

**server-side mod** - Majority of features remain functional with optional client side syncing for networked features that require it.

This is an independently maintained derivative of [BazaarIsMyHaven](https://thunderstore.io/c/riskofrain2/p/Def/BazaarIsMyHaven/) by [Deflaktor](https://github.com/Deflaktor/BazaarIsMyHaven), itself based on [BazaarIsMyHome](https://thunderstore.io/c/riskofrain2/p/Lunzir2/BazaarIsMyHome/) by [Lunzir](https://github.com/Lunzir-0325/RoR2-BazaarIsMyHome). This mod will retain the configurable features of BazaarIsMyHaven while developing its own changes for multiplayer behavior and presentation.

The project aims to fix the issues of BazaarIsMyHaven while potentially introducing requested features as well.

- **Fixed Features**
  - Shop Terminals not retaining scale for clients
      - As a server only mod, this was done by using the Drifterhoard prefab which includes ItemShareController.RpcParentToMuzzle procedure. This RPC function re-scales game-objects even for clients. I do not like this myself, but I wanted to keep this mod server-sided as best as I can
          - #TO-DO: Optional client side networking to properly sync scale without having to use work around.
  - Re-rolling breaking in newest version
  - Purchases being free
  - Instancing not working correctly under specific circumstances
      - Instancing also did not mesh well with re-rolling
  - Buds make a return, now you can have more than 5 buds with Shop features.

- **Reworked Features**
  - Instancing was reworked to include extra states, a queue, and additional bookkeeping for use for re-rolling.
      - When instance purchases was on, various buggy interactions could happen. Host could buy an item, and clients would receive nothing on the same item. Rer-oll would re-roll items already purchased from clients. Host was largely authoritative over tracking purchases.
  ~Added Changes
    - Correct buyer states; purchases temporarily use the buyer's shop data then restores the host's view.
        - Instances also track for states to protect re-roll, and swapped equipment
              - Old re-roll path called normal shop-generation. We can now skip purchased shops and have various states to ensure each instance client gets only what should be re-rolled. 
    - broadcast no longer overwrite personal fields; Original already sent targeted updates, but the game had normal synchronization. Broadcast should not overwrite everyone's individual stock or availability,.
    - Client updates now arrive in order needed for animation, this is most useful for the buds which wouldn't open on purchases.
    - Queued update system allows slow clients to process messages without causing mismatches.

This was my own summarization of what I understand. Me, the human. I am a goblin with no experience in creating git commits. I usually never share my work, my commits will be as horrible as can be, Apologies. I aimed to understand what I was doing, despite the assistance I was receiving... speaking of assistance...

---
**AI WAS USED TO HELP CREATE THIS** - Specifically GPT-6 Astra at Extra High

The death-mark a project. Primarily used to help probe solutions for complex problems, like scale not being synced, and networked re-rolling. I already know I am going to hell for this, but I suppose I shall share my carefully curated and tested slop to the masses. 
I did not use code wholesale. Any assistance that was given was in pieces, I still had to put it together and ensure it works. Please feel free to curate my commits and provide reasons why my code is bad, I'll do my best to fix them. Consequently, Astra was used to help initialize the project
I believe I put the necessary work to ensure none of what people usually mean by "AI Slop" is reflected in this project. This project wont break your game settings, it wont cause memory leaks, it wont blue screen your computer. I did my best to test it, with MultiplayerTestMod, UnityExplorer and all that jazz.

---

## Building 
Project was edited and made with Visual Studio Community 2026 with the **.NET desktop development** workload and .NET SDK **10.0.401 or a later stable 10.0 feature band**. the supplied .csproj should still work for other IDE's though.

## Features ##
Please reffer to Deflaktor's [full documentation](https://github.com/Deflaktor/BazaarIsMyHaven) for the complete list of features. I shall only go over what I added and changed

# Known Issues

With a heavy heart I have to admit that I still could not figure out a way to sync client scale for terminals. Oriignally, with terminals, the scale in the prefab was set to .75. This isnt quite noticable, but was when I reimplemented lunar buds to the equation, which had a default scale of .5. 
This cannot be fixed with a host only mod. However, will be fixed if the client shares the same mod, this is still completely optional.
