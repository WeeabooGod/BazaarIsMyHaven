# 5.0.0
- Lunar buds are created when shop terminals are not enabled, allowing to modify the cost and amount of default buds, maintaining original functionality.
- Fixed Re-rolling not functioning under specific circumstances and not meshing with re-rolling functions
- Added proper instancing to protect re-roll keep track shop buyers for networking, preventing client bought shops from re-rolling
- Added proper networked syncing for scale, cost and re-rolling that works under Unity's default newtworking message, offering a clean way to sync scale without using fallbacks for host and clients with the same mod installed
- Added an optionally disable-able fallback for syncing gameobjects of the lunarshopterminals and buds when lunarshopsection is enabled for clients without the mod to maintian server-sidedness
- Added an optionally disable-able fallback for adding a cost hologram for lunarshopterminals for clients without the mod, maintaining server-sidedness
  
# 4.2.0

- Add LunarShop AmountDependingOnCharacter
- Add the two new surivors to the default DonateRewardListCharacters

# 4.1.1

- Fix various issues with the Wandering Chef
- Fix equipments not working when buy to inventory is set

# 4.1.0

- Add Wandering Chef to the bazaar
- Fix Cleansing Pool not giving Lunar Coins as intended
- Fix equipment duplication when having Functional Coupler

# 4.0.0

- Update for Alloyed Collective
- Add proper R2API dependencies
- Use ItemStringParser for all item lists
- Fix donation of unused items
- Limit amount of donations per visit to the Bazaar and per run
- Fix equipments in bazaar not getting replaced with the elite list

# 3.0.0

- Donation Shrine no longer requires to donate 10 times to get reward

# 2.1.2

- Add item colors in donate chat message
- Better placement of lunar shop terminals

# 2.1.1

- Fix sequential items in lunar shop starting with wrong index

# 2.1.0

- Add feature for custom donation reward lists depending on the character
- Add feature for sequential rewards at donation altar

# 2.0.1

- Fix readme

# 2.0.0

- Rename RewardNormalList->RewardList1
- Rename RewardEliteList->RewardList2
- Rename RewardPeculiarList->RewardList3
- Add feature to replace equipment with elite aspects
- Fix link in readme to the original mod for real

# 1.0.1

- Fix link in readme to the original mod
- Fix language file

# 1.0.0

- Initial Release
