using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using R2API.Utils;
using RoR2;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Networking;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace BazaarIsMyHaven
{
    [BepInDependency(R2API.R2API.PluginGUID)]
    [BepInDependency(R2API.LanguageAPI.PluginGUID)]
    [BepInDependency("Faust.QoLChests", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency("com.KingEnderBrine.InLobbyConfig", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency("com.funkfrog_sipondo.sharesuite", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency(ItemStringParser.ItemStringParser.PluginGUID)]
    [BepInIncompatibility("com.Lunzir.BazaarLunarForEveryone")]
    [BepInPlugin(PluginGUID, PluginName, PluginVersion)]
    [NetworkCompatibility(CompatibilityLevel.NoNeedForSync, VersionStrictness.DifferentModVersionsAreOk)]
    public class Main : BaseUnityPlugin
    {
        public static PluginInfo PluginInfo;
        public static Main instance;

        public const string PluginGUID = PluginAuthor + "." + PluginName;
        public const string PluginAuthor = "Def";
        public const string PluginName = "BazaarIsMyHaven";
        public const string PluginVersion = "4.2.0";

        private static System.Random Random = new System.Random();
        private static readonly Dictionary<(Type type, string address), AsyncOperationHandle> debugAssets = new Dictionary<(Type, string), AsyncOperationHandle>();
        List<BazaarBase> bazaarMods = new List<BazaarBase>();
        private readonly Dictionary<PlayerCharacterMasterController, PlayerStruct> playerStructs_ = new Dictionary<PlayerCharacterMasterController, PlayerStruct>();
        public AsyncOperationHandle<GameObject> pickupTakenOrbPrefab;

        public PlayerStruct GetPlayerStruct(PlayerCharacterMasterController master)
        {
            if(!playerStructs_.ContainsKey(master)) {
                playerStructs_.Add(master, new PlayerStruct(master));
            }
            return playerStructs_[master];
         }

        public void Awake()
        {
            PluginInfo = Info;
            instance = this;
            Log.Init(Logger);

            bazaarMods.Add(new BazaarCauldron());
            bazaarMods.Add(new BazaarPrinter());
            bazaarMods.Add(new BazaarRestack());
            bazaarMods.Add(new BazaarDonate());
            bazaarMods.Add(new BazaarScrapper());
            bazaarMods.Add(new BazaarEquipment());
            bazaarMods.Add(new BazaarLunarShop());
            bazaarMods.Add(new BazaarCleansingPool());
            bazaarMods.Add(new BazaarDecorate());
            bazaarMods.Add(new BazaarWanderingChef());

            pickupTakenOrbPrefab = Addressables.LoadAssetAsync<GameObject>("RoR2/DLC3/MealPrep/PickupTakenOrbEffect.prefab");
            foreach (var bazaarMod in bazaarMods)
            {
                bazaarMod.Preload();
            }
            RoR2Application.onLoad += () =>
            {
                ModConfig.InitConfig(Config);
                Hook();
            };
        }

        private void Hook()
        {
            foreach (var bazaarMod in bazaarMods)
            {
                bazaarMod.Hook();
            }

            // Hook optional client support regardless of client settings
            // Host is authoritive on the creation of lunar shops and if networked updates are required
            BazaarClientNetworking.Hook();
            InstancedPurchases.Hook();

            On.RoR2.Run.Start += Run_Start;
            Run.onRunDestroyGlobal += Run_Destroy;
            On.RoR2.BazaarController.SetUpSeerStations += BazaarController_SetUpSeerStations;
            RoR2.SceneDirector.onPrePopulateSceneServer += SceneDirector_onPrePopulateSceneServer;
            On.RoR2.TeleporterInteraction.Start += TeleporterInteraction_Start;
            On.EntityStates.NewtMonster.KickFromShop.FixedUpdate += KickFromShop_FixedUpdate;
            On.RoR2.GlobalEventManager.OnHitAll += GlobalEventManager_OnHitAll;
            On.RoR2.CharacterMaster.OnBodyDeath += CharacterMaster_OnBodyDeath;
            On.EntityStates.NewtMonster.SpawnState.OnEnter += SpawnState_OnEnter;
        }


        private void Run_Start(On.RoR2.Run.orig_Start orig, Run self)
        {

            ShopKeeper.DiedAtLeastOnce = false;
            ShopKeeper.Body = null;
            playerStructs_.Clear();
            orig(self);
            if (NetworkServer.active)
            {
                //Allows the Config to be-reread without having to restart game on a new run without any other aditional mods
                ModConfig.ReloadConfig(Config);
                
                foreach (var bazaarMod in bazaarMods)
                {
                    bazaarMod.RunStart();
                }
            }
        }

        private void Run_Destroy(Run run)
        {
            playerStructs_.Clear();
            ShopKeeper.Body = null;
            InstancedPurchases.currentInteractor = null;
            foreach (var bazaarMod in bazaarMods)
            {
                bazaarMod.RunEnd();
            }
        }


        private void BazaarController_SetUpSeerStations(On.RoR2.BazaarController.orig_SetUpSeerStations orig, BazaarController self)
        {
            if (ModConfig.EnableMod.Value && NetworkServer.active)
            {
                if (ModConfig.LunarSeerSectionEnabled.Value || (ModConfig.EquipmentSectionEnabled.Value && ModConfig.EquipmentReplaceLunarSeersWithEquipment.Value))
                {
                    foreach (SeerStationController seerStationController in self.seerStations)
                    {
                        var seerStationAvailable = ModConfig.LunarSeerAvailable.Value;
                        if (ModConfig.EquipmentSectionEnabled.Value && ModConfig.EquipmentReplaceLunarSeersWithEquipment.Value)
                        {
                            seerStationAvailable = false;
                        }
                        seerStationController.GetComponent<PurchaseInteraction>().available = seerStationAvailable;
                        seerStationController.GetComponent<PurchaseInteraction>().Networkavailable = seerStationAvailable;
                        if (ModConfig.LunarSeerSectionEnabled.Value && seerStationAvailable)
                        {
                            seerStationController.GetComponent<PurchaseInteraction>().cost = ModConfig.LunarSeerCost.Value;
                            seerStationController.GetComponent<PurchaseInteraction>().Networkcost = ModConfig.LunarSeerCost.Value; 
                        }
                    }
                }
            }
            orig(self);
        }

        private void SceneDirector_onPrePopulateSceneServer(SceneDirector obj)
        {
            if (ModConfig.EnableMod.Value && IsCurrentMapInBazaar() && NetworkServer.active)
            {
                ShopKeeper.Body = null;
                ShopKeeper.DeathCount = 0;
                playerStructs_.Clear();

                ArtifactDef artifactDef = ArtifactCatalog.FindArtifactDef("Sacrifice");
                bool isEnableSacrifice = false;
                if (RunArtifactManager.instance.IsArtifactEnabled(artifactDef))
                {
                    isEnableSacrifice = true;
                    RunArtifactManager.instance.SetArtifactEnabledServer(artifactDef, false);
                }
                try
                {
                    foreach (var bazaarMod in bazaarMods)
                    {
                        try
                        {
                            bazaarMod.SetupBazaar();
                        }
                        catch (Exception exception)
                        {
                            Log.LogError($"Could not set up {bazaarMod.GetType().Name}: {exception}");
                        }
                    }
                }
                finally
                {
                    // Restore the run's artifact even if one section fails to spawn.
                    if (isEnableSacrifice) RunArtifactManager.instance.SetArtifactEnabledServer(artifactDef, true);
                }
            }
        }

        private void TeleporterInteraction_Start(On.RoR2.TeleporterInteraction.orig_Start orig, TeleporterInteraction self)
        {
            orig(self);
            if (ModConfig.EnableMod.Value && ModConfig.AlwaysSpawnShopPortal.Value && NetworkServer.active)
            {
                self.shouldAttemptToSpawnShopPortal = true;
            }
        }
        private void KickFromShop_FixedUpdate(On.EntityStates.NewtMonster.KickFromShop.orig_FixedUpdate orig, EntityStates.NewtMonster.KickFromShop self)
        {
            if (ModConfig.EnableMod.Value && ModConfig.NewtSectionEnabled.Value && ModConfig.NewtNoKickFromShop.Value && IsCurrentMapInBazaar() && NetworkServer.active)
            {
                if(!ShopKeeper.DiedAtLeastOnce)
                {
                    ChatHelper.HitWord();
                }
                if (ModConfig.NewtDeathBehavior.Value == ShopKeeper.DeathState.Hostile)
                {
                    // TODO
                }
                self.outer.SetNextStateToMain();
            }
            else
            {
                orig(self);
            }
        }
        private void GlobalEventManager_OnHitAll(On.RoR2.GlobalEventManager.orig_OnHitAll orig, GlobalEventManager self, DamageInfo damageInfo, GameObject hitObject)
        {
            if (ModConfig.EnableMod.Value && ModConfig.NewtSectionEnabled.Value && ModConfig.NewtTrashTalk.Value && IsCurrentMapInBazaar() && NetworkServer.active && hitObject != null)
            {
                if (hitObject.name.StartsWith("ShopkeeperBody") && damageInfo.attacker != null)
                {
                    float damageAsPercentageOfHealth = damageInfo.damage / hitObject.GetComponent<HealthComponent>().fullCombinedHealth;
                    if(Random.NextDouble() < damageAsPercentageOfHealth / 2f) {
                        ChatHelper.HitWord();
                    }
                }
            }
            orig(self, damageInfo, hitObject);

        }
        private void CharacterMaster_OnBodyDeath(On.RoR2.CharacterMaster.orig_OnBodyDeath orig, CharacterMaster self, CharacterBody body)
        {
            orig(self, body);
            if (ModConfig.EnableMod.Value && ModConfig.NewtSectionEnabled.Value && IsCurrentMapInBazaar() && NetworkServer.active)
            {
                //var attack = self.name;
                var victim = body.name;
                if (victim.Contains("ShopkeeperBody"))
                {
                    ShopKeeper.DiedAtLeastOnce = true;
                    ShopKeeper.DeathCount++;
                    if (ModConfig.NewtDeathBehavior.Value == ShopKeeper.DeathState.Default)
                    {
                        if(ModConfig.NewtTrashTalk.Value)
                        {
                            ChatHelper.ShowNewtDeath();
                        }
                        //string card = "Spawncards/InteractableSpawncard/iscScavLunarbackpack";
                        //DoSpawnCard(card, new Vector3(-122.7888f, -22.3505f, -45.7878f));
                        //DoSpawnCard(card, new Vector3(-125.9958f, -22.4272f, -42.6213f));
                        //Vector3 vector3 = body.footPosition;
                    }
                    else
                    {
                        AddItemToShopKeeper(body);
                    }
                }
            }
        }

        private void SpawnState_OnEnter(On.EntityStates.NewtMonster.SpawnState.orig_OnEnter orig, EntityStates.NewtMonster.SpawnState self)
        {
            if (IsCurrentMapInBazaar()) //Apperently SPEX is a Newt this should prevent the welcome message from playing in the computational exchange
            {
                if (ModConfig.EnableMod.Value && ModConfig.NewtSectionEnabled.Value && NetworkServer.active && NetworkServer.active)
                {
                    if (ModConfig.NewtGreeting.Value)
                    {
                        StartCoroutine(ShopWelcomeWord());
                    }

                    if (!ShopKeeper.Body) FindShopkeeper();

                    if (ShopKeeper.Body && ShopKeeper.Body.inventory && ModConfig.NewtDeathBehavior.Value != ShopKeeper.DeathState.Default)
                    {
                        ShopKeeper.Body.inventory.GiveItemPermanent(ItemCatalog.GetItemDef(ItemCatalog.FindItemIndex("ExtraLife")), 1000);
                        ShopKeeper.Body.inventory.GiveItemPermanent(ItemCatalog.GetItemDef(ItemCatalog.FindItemIndex("CutHp")), 200);
                    }
                }
            }
            
            orig(self);
        }

        private void AddItemToShopKeeper(CharacterBody body)
        {
            switch (ModConfig.NewtDeathBehavior.Value)
            {
                case ShopKeeper.DeathState.Ghost:
                    ShopKeeper.Body.inventory.GiveItemPermanent(ItemCatalog.GetItemDef(ItemCatalog.FindItemIndex("Ghost")), 1);
                    ShopKeeper.Body.AddBuff(RoR2Content.Buffs.HiddenInvincibility);
                    break;
                case ShopKeeper.DeathState.Tank:
                    var healthBoost = 10 * (int)Math.Pow(2, ShopKeeper.DeathCount) - 10 * (int)Math.Pow(2, ShopKeeper.DeathCount - 1);
                    body.inventory.GiveItemPermanent(ItemCatalog.GetItemDef(ItemCatalog.FindItemIndex("BoostHp")), healthBoost);
                    break;
                case ShopKeeper.DeathState.Hostile:
                    body.inventory.GiveItemPermanent(ItemCatalog.GetItemDef(ItemCatalog.FindItemIndex("Thorns")), 1);
                    break;
                default:
                    break;
            }
        }

        IEnumerator ShopWelcomeWord()
        {
            yield return new WaitForSeconds(0.5f);
            ChatHelper.WelcomeWord();
        }
        private void FindShopkeeper()
        {
            TeamIndex team = TeamIndex.Neutral;
            foreach (CharacterMaster cm in UnityEngine.Object.FindObjectsOfType<CharacterMaster>())
            {
                if (cm.teamIndex == team)
                {
                    CharacterBody cb = cm.GetBody();
                    if (cb && cb.name.StartsWith("ShopkeeperBody"))
                    {
                        ShopKeeper.Body = cb;
                        break;
                    }
                }
            }
        }

        private bool IsCurrentMapInBazaar()
        {
            return SceneManager.GetActiveScene().name == "bazaar";
        }

        [ConCommand(commandName = "next_chef_item", flags = ConVarFlags.ExecuteOnServer | ConVarFlags.SenderMustBeServer, helpText = "生成特效")]
        private static void Command_NextChefItem(ConCommandArgs args)
        {
            foreach (var bazaarMod in Main.instance.bazaarMods)
            {
                if (bazaarMod is BazaarWanderingChef bazaarWanderingChef)
                {
                    bazaarWanderingChef.NextRecipe();
                }
            }
            
        }
    }
}
