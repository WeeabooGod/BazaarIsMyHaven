using BepInEx;
using BepInEx.Bootstrap;
using RoR2;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Networking;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.UIElements;
using UnityEngine.Events;
using UnityEngine.SceneManagement;

namespace BazaarIsMyHaven
{
    public class BazaarLunarShop : BazaarBase
    {
        //Give the Shop a name, useful for QoLChest's blacklisting feature, to prevent the shops from dissapearing
        //and interfering with rerolling and instancing. Clients even with QolChest wouldn't have their shops dissapear anyways.
        private const string LunarShopObjectName = "LunarShopTerminal_WeebsCustom";

        AsyncOperationHandle<GameObject> lunarShopBud;
        AsyncOperationHandle<GameObject> lunarShopTerminal;
        AsyncOperationHandle<GameObject> LunarRerollEffect;

        Dictionary<int, SpawnCardStruct> DicLunarShopTerminals = new Dictionary<int, SpawnCardStruct>();
        int generateNewPickupIndex = 0;
        int lunarRecyclerRerolledCount = 0;
        List<GameObject> ObjectLunarShopTerminals_Spawn = new List<GameObject>();
        PlayerCharacterMasterController currentActivator = null;
        Dictionary<PurchaseInteraction, List<PlayerCharacterMasterController>> whichStallsHaveBeenBoughtOnce = new Dictionary<PurchaseInteraction, List<PlayerCharacterMasterController>>();

        public override void Preload()
        {
            // lunarShopTerminal = Addressables.LoadAssetAsync<GameObject>("RoR2/Base/LunarShopTerminal/LunarShopTerminal.prefab");
            // lunarShopTerminal = Addressables.LoadAssetAsync<GameObject>("RoR2/DLC1/FreeChestTerminal/FreeChestTerminal.prefab");
            // lunarShopTerminal = Addressables.LoadAssetAsync<GameObject>("RoR2/Base/MultiShopTerminal/ShopTerminal.prefab");
            // lunarShopTerminal = Addressables.LoadAssetAsync<GameObject>("RoR2/Base/bazaar/SeerStation.prefab");
   
            //Gameobjects for Shops
            lunarShopBud = Addressables.LoadAssetAsync<GameObject>("RoR2/Base/LunarShopTerminal/LunarShopTerminal.prefab");
            lunarShopTerminal = Addressables.LoadAssetAsync<GameObject>("RoR2/DLC1/FreeChestTerminalShippingDrone/FreeChestTerminalShippingDrone.prefab");
            LunarRerollEffect = Addressables.LoadAssetAsync<GameObject>("RoR2/Base/LunarRecycler/LunarRerollEffect.prefab");

            LunarShopScaleSync.Preload();
        }

        public override void Hook()
        {
            On.RoR2.PurchaseInteraction.Awake += PurchaseInteraction_Awake;
            On.RoR2.PurchaseInteraction.OnInteractionBegin += PurchaseInteraction_OnInteractionBegin;
            //On.RoR2.PurchaseInteraction.OnInteractionBegin += PurchaseInteraction_OnInteractionEnd;
            On.RoR2.PurchaseInteraction.ScaleCost += PurchaseInteraction_ScaleCost;
            On.RoR2.PurchaseInteraction.SetAvailable += PurchaseInteraction_SetAvailable;
            On.RoR2.ShopTerminalBehavior.DropPickup += ShopTerminalBehavior_DropPickup;
            On.RoR2.ShopTerminalBehavior.GenerateNewPickupServer_bool += ShopTerminalBehavior_GenerateNewPickupServer_bool;
            SceneManager.sceneUnloaded += SceneUnloaded;
        }
        public override void RunStart()
        {
            RunEnd();
        }

        public override void RunEnd()
        {
            ObjectLunarShopTerminals_Spawn.Clear();
            whichStallsHaveBeenBoughtOnce.Clear();
            currentActivator = null;
        }

        private void SceneUnloaded(Scene scene)
        {
            if (scene.name == "bazaar")
            {
                RunEnd();
            }
        }
        public override void SetupBazaar()
        {
            if (ModConfig.LunarShopSectionEnabled.Value)
            {
                lunarRecyclerRerolledCount = 0;
                generateNewPickupIndex = 0;
                whichStallsHaveBeenBoughtOnce.Clear();
                SpawnLunarShopTerminal();
            }
        }

        public void PurchaseInteraction_Awake(On.RoR2.PurchaseInteraction.orig_Awake orig, PurchaseInteraction self)
        {
            orig(self);
            if (ModConfig.EnableMod.Value && IsCurrentMapInBazaar() && NetworkServer.active)
            {
                if (ModConfig.LunarShopSectionEnabled.Value && ModConfig.LunarShopCost.Value >= 0) 
                {
                    if (self.name.StartsWith("LunarShopTerminal"))
                    {
                        self.cost = ModConfig.LunarShopCost.Value;
                        self.Networkcost = ModConfig.LunarShopCost.Value;
                        self.costType = CostTypeIndex.LunarCoin;
                    }
                }
                if (ModConfig.LunarShopSectionEnabled.Value)
                {
                    if (self.name.StartsWith("LunarRecycler"))
                    {
                        if (ModConfig.LunarRecyclerAvailable.Value && ModConfig.LunarRecyclerCost.Value >= 0)
                        {
                            self.cost = ModConfig.LunarRecyclerCost.Value;
                            self.Networkcost = ModConfig.LunarRecyclerCost.Value;
                        }
                        else
                        {
                            NetworkServer.Destroy(self.gameObject);
                        }
                    }
                }
            }
        }

        private bool TryCompletePurchase(On.RoR2.PurchaseInteraction.orig_OnInteractionBegin orig, PurchaseInteraction self, Interactor activator, PlayerCharacterMasterController player)
        {
            // onDetailedPurchaseServer runs after the game accepts and pays for the purchase.
            // Merely entering OnInteractionBegin does not prove that a purchase succeeded.
            bool purchaseSucceeded = false;
            UnityEngine.Events.UnityAction<CostTypeDef.PayCostContext, CostTypeDef.PayCostResults> purchaseListener =
                (context, results) =>
                {
                    if (context.activator == activator)
                    {
                        purchaseSucceeded = true;
                    }
                };

            // Keep the buyer available to DropPickup while the game's purchase code runs.
            var previousActivator = currentActivator;
            self.onDetailedPurchaseServer.AddListener(purchaseListener);
            try
            {
                currentActivator = player;
                orig(self, activator);
            }
            finally
            {
                currentActivator = previousActivator;
                self.onDetailedPurchaseServer.RemoveListener(purchaseListener);
            }

            return purchaseSucceeded;
        }

        public void PurchaseInteraction_OnInteractionBegin(On.RoR2.PurchaseInteraction.orig_OnInteractionBegin orig, PurchaseInteraction self, Interactor activator)
        {
            // The fallback station supplies a price label only; it must never accept a purchase.
            if (self.GetComponent<LunarShopFallbackHologram>())
            {
                //The reason this is important is the seers sign for non-clients however the host still owns its network object. Even though only the client which is unmodded
                //Can see, and even potentially interact with the seers sign, a request is sent to the host
                //This was proposed as a defensive guard... all this just to give non-clients a price tag reference ...
                return;
            }

            if (ModConfig.EnableMod.Value && ModConfig.LunarShopSectionEnabled.Value && IsCurrentMapInBazaar() && NetworkServer.active)
            {
                var body = activator ? activator.GetComponent<CharacterBody>() : null;
                var player = body && body.master ? body.master.playerCharacterMasterController : null;
                if (!player)
                {
                    orig(self, activator);
                    return;
                }

                // Shops/Terminals are already in the dictionary before anyone purchases them. Each sart with an empty buyer list
                // TryGetValue identifies tracked shops buyers.contains(player) determins whether that player bought it.
                if (whichStallsHaveBeenBoughtOnce.TryGetValue(self, out var buyers))
                {
                    var playerState = Main.instance.GetPlayerStruct(player);
                    bool firstPurchase = !buyers.Contains(player);
                    int usesLeft = ModConfig.LunarShopBuyLimit.Value - playerState.LunarShopUseCount;

                    if (firstPurchase && ModConfig.LunarShopBuyLimit.Value >= 0 && usesLeft <= 0)
                    {
                        ChatHelper.LunarShopTerminalUsesLeft(player, usesLeft);
                        return;
                    }

                    if (!TryCompletePurchase(orig, self, activator, player))
                    {
                        return;
                    }

                    // Swapping equipment at a previously purchased slot does not use another purchase.
                    if (firstPurchase)
                    {
                        buyers.Add(player);
                        playerState.LunarShopUseCount++;
                        if (self.TryGetComponent(out InstancedPurchase instance))
                        {
                            instance.GetOrCreate(player).hasBeenPurchasedOnce = true;
                        }

                        if (ModConfig.LunarShopBuyLimit.Value >= 0)
                        {
                            ChatHelper.LunarShopTerminalUsesLeft(
                                player, ModConfig.LunarShopBuyLimit.Value - playerState.LunarShopUseCount);
                        }
                    }

                    return;
                }

                if (self.name.StartsWith("LunarRecycler"))
                {
                    if (!self.available || (ModConfig.LunarRecyclerRerollLimit.Value >= 0 && lunarRecyclerRerolledCount >= ModConfig.LunarRecyclerRerollLimit.Value))
                    {
                        return;
                    }

                    if (!TryCompletePurchase(orig, self, activator, player))
                    {
                        return;
                    }

                    lunarRecyclerRerolledCount++;
                    if (ModConfig.LunarRecyclerRerollLimit.Value >= 0)
                    {
                        int rerollsLeft = ModConfig.LunarRecyclerRerollLimit.Value - lunarRecyclerRerolledCount;
                        ChatHelper.LunarRecyclerUsesLeft(rerollsLeft);
                        if (rerollsLeft <= 0)
                        {
                            self.SetAvailable(false);
                        }
                    }

                    float delay = 0f;
                    foreach (var shop in ObjectLunarShopTerminals_Spawn)
                    {
                        Main.instance.StartCoroutine(DelayRerollEffect(shop, delay));
                        delay += 0.1f;
                    }

                    return;
                }
            }

            orig(self, activator);
        }

        private void PurchaseInteraction_ScaleCost(On.RoR2.PurchaseInteraction.orig_ScaleCost orig, PurchaseInteraction self, float scalar)
        {
            if (ModConfig.EnableMod.Value && ModConfig.LunarShopSectionEnabled.Value && ModConfig.LunarRecyclerAvailable.Value && IsCurrentMapInBazaar() && NetworkServer.active)
            {
                if (self.name.StartsWith("LunarRecycler"))
                {
                    scalar = (float)ModConfig.LunarRecyclerCostMultiplier.Value;
                }
            }
            orig(self, scalar);
        }
        private void PurchaseInteraction_SetAvailable(On.RoR2.PurchaseInteraction.orig_SetAvailable orig, PurchaseInteraction self, bool newAvailable)
        {
            if (ModConfig.EnableMod.Value && ModConfig.LunarShopSectionEnabled.Value && ModConfig.LunarRecyclerAvailable.Value && IsCurrentMapInBazaar() && NetworkServer.active)
            {
                if (self.name.StartsWith("LunarRecycler"))
                {
                    if(ModConfig.LunarRecyclerRerollLimit.Value >= 0) 
                    {
                        //Preserve game's cooldown, only veto a request to become available
                        newAvailable = newAvailable && lunarRecyclerRerolledCount < ModConfig.LunarRecyclerRerollLimit.Value;
                    }
                }
            }
            orig(self, newAvailable);
        }

        private void ShopTerminalBehavior_DropPickup(On.RoR2.ShopTerminalBehavior.orig_DropPickup orig, ShopTerminalBehavior self)
        {
            if (ModConfig.EnableMod.Value && ModConfig.LunarShopSectionEnabled.Value && IsCurrentMapInBazaar() && NetworkServer.active && self.name.StartsWith("LunarShopTerminal"))
            {
                if (ModConfig.LunarShopBuyToInventory.Value)
                {
                    var body = currentActivator && currentActivator.master ? currentActivator.master.GetBody() : null;
                    if (!body || !body.inventory)
                    {
                        orig(self);
                        SendPurchasedFlagToClients(self);
                        return;
                    }

                    var droppedEquipment = Helper.GivePickup(body, self.CurrentPickup().pickupIndex, self.transform.position, false);
                    if (droppedEquipment != EquipmentIndex.None)
                    {
                        self.SetPickup(new UniquePickup(PickupCatalog.FindPickupIndex(droppedEquipment)));
                        var purchaseInteraction = self.GetComponent<PurchaseInteraction>();
                        purchaseInteraction.SetAvailable(true);
                    }
                    else
                    {
                        self.SetHasBeenPurchased(newHasBeenPurchased: true);
                        SendPurchasedFlagToClients(self);
                        self.SetNoPickup();
                    }
                }
                else
                {
                    orig(self);
                    SendPurchasedFlagToClients(self);
                    self.SetNoPickup();
                }
            }
            else
            {
                orig(self);
            }
        }

        //Workaround for client lunar shop "BUDS" for not having an opening animation upon purchase
        //Only useful for non-instanced shops
        private void SendPurchasedFlagToClients(ShopTerminalBehavior shop)
        {
            if (!NetworkServer.active || shop.GetComponent<InstancedPurchase>() || !shop.hasBeenPurchased)
            {
                return;
            }

            var identity = shop.GetComponent<NetworkIdentity>();
            if (!identity || identity.observers == null)
            {
                return;
            }

            int channel = shop.GetNetworkChannel();
            var writer = new NetworkWriter();

            writer.StartMessage(MsgType.UpdateVars);
            writer.Write(identity.netId);

            // Preserve the component order expected by the client's network reader.
            foreach (var behaviour in identity.GetBehavioursOfSameChannel(channel, false))
            {
                uint originalDirtyBits = behaviour.m_SyncVarDirtyBits;

                try
                {
                    // 4 is ShopTerminalBehavior's hasBeenPurchased field.
                    // Exclude its pickup field so this message cannot trigger the
                    // animation before the client has received the purchased flag.
                    behaviour.m_SyncVarDirtyBits = behaviour == shop ? 4u : 0u;
                    behaviour.OnSerialize(writer, false);
                }
                finally
                {
                    // Leave all pending changes available for the normal update.
                    behaviour.m_SyncVarDirtyBits = originalDirtyBits;
                }
            }

            writer.FinishMessage();

            foreach (var connection in identity.observers)
            {
                if (connection.isReady && !Util.ConnectionIsLocal(connection))
                {
                    if (!connection.SendWriter(writer, channel))
                    {
                        Log.LogWarning("Could not send the lunar bud purchased flag.");
                    }
                }
            }
        }

        //Reusable function as its used in multiple places.
        private bool TryGenerateLunarShopPickup(out UniquePickup pickup)
        {
            int itemIndex = ModConfig.LunarShopSequentialItems.Value ? generateNewPickupIndex : -1;

            var resolvedItems = new Dictionary<PickupIndex, int>();
            ItemStringParser.ItemStringParser.ParseItemString(ModConfig.LunarShopItemList.Value, resolvedItems, Log.GetSource(), false, itemIndex);

            foreach (var entry in resolvedItems)
            {
                if (entry.Value > 0 && PickupCatalog.GetPickupDef(entry.Key) != null)
                {
                    pickup = new UniquePickup(entry.Key);
                    generateNewPickupIndex++;
                    return true;
                }
            }

            pickup = UniquePickup.none;
            Log.LogError($"Could not generate a lunar shop pickup from: {ModConfig.LunarShopItemList.Value}");
            return false;
        }


        private void ShopTerminalBehavior_GenerateNewPickupServer_bool(On.RoR2.ShopTerminalBehavior.orig_GenerateNewPickupServer_bool orig, ShopTerminalBehavior self, bool newHidden)
        {
            if (ModConfig.EnableMod.Value && ModConfig.LunarShopSectionEnabled.Value && IsCurrentMapInBazaar() && NetworkServer.active && self.name.StartsWith("LunarShopTerminal"))
            {
                if (TryGenerateLunarShopPickup(out var pickup))
                {
                    self.SetPickup(pickup, newHidden);
                }
                return;
            }

            orig(self, newHidden);
        }

        private IEnumerator DelayRerollEffect(GameObject shop, float delay)
        {
            yield return new WaitForSeconds(delay);

            //Sanity Checks
            if (!NetworkServer.active || !shop)
            {
                yield break;
            }

            //Sanity checks again woooo
            var terminal = shop.GetComponent<ShopTerminalBehavior>();
            var purchase = shop.GetComponent<PurchaseInteraction>();
            if (!terminal || !purchase)
            {
                yield break;
            }

            if (shop.TryGetComponent(out InstancedPurchase instance))
            {
                // Evaluate after the delay - a player may have purchased this slot meanwhile.
                bool anyEligiblePlayer = PlayerCharacterMasterController.instances.Any(pc => pc && instance.GetOrOriginal(pc).CanReroll);
                if (!anyEligiblePlayer || !TryGenerateLunarShopPickup(out var pickup))
                {
                    yield break;
                }

                // Roll once per slot. Players who have not used it receive the same new offer.
                // The default also supplies players without a personal record yet.
                if (instance.original.CanReroll)
                {
                    instance.original.pickup = pickup;
                    instance.original.hidden = false;
                }

                foreach (var state in instance.purchases.Values)
                {
                    if (state.CanReroll)
                    {
                        state.pickup = pickup;
                        state.hidden = false;
                    }
                }

                // This sends each client their own state, including consumed/disabled slots.
                InstancedPurchases.UpdateAll(shop);
            }
            else //Non-instanced shops 
            {
                //Preserve remaining equipment if a purchase has been made and swapped
                if (whichStallsHaveBeenBoughtOnce.TryGetValue(purchase, out var buyers) && buyers.Count > 0)
                {
                    yield break;
                }

                if (!purchase.available || terminal.hasBeenPurchased || terminal.pickup.Equals(UniquePickup.none) || !TryGenerateLunarShopPickup(out var pickup))
                {
                    yield break;
                }

                terminal.SetPickup(pickup, false);
            }

            //Account for Bud and Terminal Differences
            float height = ModConfig.LunarShopReplaceLunarBudsWithTerminals.Value ? -2.5f : 2.5f;
            SpawnEffect(LunarRerollEffect, shop.transform.position + Vector3.up * height, new Color32(255, 255, 255, 255), 2f);
        }

        public static List<Vector2> GenerateCirclePoints(float radius, float startAngle, float endAngle, float orientation, int numberOfPoints)
        {
            List<Vector2> points = new List<Vector2>();
            float angleStep = (endAngle - startAngle) / (numberOfPoints - 1);
            if (numberOfPoints <= 1)
            {
                angleStep = 0;
            }

            for (int i = 0; i < numberOfPoints; i++)
            {
                float angleInDegrees = startAngle + i * angleStep + orientation;
                float angleInRadians = angleInDegrees * Mathf.Deg2Rad;
                float x = radius * Mathf.Cos(angleInRadians);
                float y = radius * Mathf.Sin(angleInRadians);
                points.Add(new Vector2(x, y));
            }
            return points;
        }

        private void SetLunarShopTerminal(bool isLunarBuds = true)
        {
            Vector3 lunarTablePosition = new Vector3(-76.6438f, -24.0468f, -41.6449f);
            float orientation = 280f;
            Vector3 lunarTableDroneShopPosition = new Vector3(-139.8156f, -21.8568f, 2.9263f);

            const float tableRadiusInner = 3.0f;
            const float tableRadiusMiddle = 4.0f;
            const float tableRadiusOuter = 5.0f;
            float tableStartAngleInner = 140f;
            float tableStartAngleMiddle = 135f;
            float tableStartAngleOuter = 123f;
            float tableEndAngleInner = 330f;
            float tableEndAngleMiddle = 325f;
            float tableEndAngleOuter = 339f;
            
            const float middleCapacity = 10;
            const float maxCapacity = 20;

            List<Vector2> points = new List<Vector2>();

            int count = 0;
            if (ModConfig.SpawnCountByStage.Value)
                count = SetCountbyGameStage(ModConfig.LunarShopAmount.Value, ModConfig.SpawnCountOffset.Value);
            else
                count = ModConfig.LunarShopAmount.Value;

            if (count <= middleCapacity)
            {
                if (count < middleCapacity)
                {
                    // place them closer together --Edit: useful for more than 5 lunar shops, but tightens lunar shops when there is less terminals.
                    float angleDiff = tableEndAngleMiddle - tableStartAngleMiddle;
                    tableStartAngleMiddle += angleDiff / (float)(count + 1f);
                    tableEndAngleMiddle -= angleDiff / (float)(count + 1f);
                }

                //Attempts to correct the arch of how the lunar shops are placed on the table between a range
                //In turn, it makes 5 lunar buds be approximately in where the original 5 where, this can potentially be completely removed
                if (count >= 3 && count < 10)
                {
                    const float maximumExtraArc = 90f;
                    const float maximumCorrectedArc = 180f;

                    // Full correction at 3 shops, fading to zero at 10.
                    float correctionWeight = (10f - count) / 7f;
                    float middleAngle = (tableStartAngleMiddle + tableEndAngleMiddle) / 2f;
                    float currentArc = tableEndAngleMiddle - tableStartAngleMiddle;
                    float correctedArc = Mathf.Min(currentArc + maximumExtraArc * correctionWeight, maximumCorrectedArc);

                    tableStartAngleMiddle = middleAngle - correctedArc / 2f;
                    tableEndAngleMiddle = middleAngle + correctedArc / 2f;
                }



                points = GenerateCirclePoints(tableRadiusMiddle, tableStartAngleMiddle, tableEndAngleMiddle, orientation, count);
                points.Reverse();
            }
            else
            {
                List<Vector2> samples = new List<Vector2>();
                var innerCountIdeal = count * tableRadiusInner / (tableRadiusInner + tableRadiusOuter);
                var outerCountIdeal = count * tableRadiusOuter / (tableRadiusInner + tableRadiusOuter);
                int innerCount = Mathf.RoundToInt(innerCountIdeal);
                int outerCount = Mathf.RoundToInt(outerCountIdeal);
                if(innerCount + outerCount != count)
                {
                    // possibility 1: round inner
                    var innerDistanceInnerRounded = 2f * Mathf.PI + tableRadiusInner / innerCount;
                    var outerDistanceInnerRounded = 2f * Mathf.PI + tableRadiusOuter / (count - innerCount);
                    // possibility 2: round outer
                    var innerDistanceOuterRounded = 2f * Mathf.PI + tableRadiusInner / outerCount;
                    var outerDistanceOuterRounded = 2f * Mathf.PI + tableRadiusOuter / (count - outerCount);
                    // choose the one where the distance is more equal
                    if(Math.Abs(innerDistanceInnerRounded - outerDistanceInnerRounded) < Math.Abs(innerDistanceOuterRounded - outerDistanceOuterRounded))
                    {
                        outerCount = count - innerCount;
                    }
                    else
                    {
                        innerCount = count - outerCount;
                    }
                }
                if (count < maxCapacity)
                {
                    // place them closer together
                    float angleDiff = tableEndAngleOuter - tableStartAngleOuter;
                    tableStartAngleOuter += angleDiff / (float)(outerCount + 1f);
                    tableEndAngleOuter -= angleDiff / (float)(outerCount + 1f);
                    angleDiff = tableEndAngleInner - tableStartAngleInner;
                    tableStartAngleInner += angleDiff / (float)(innerCount + 1f);
                    tableEndAngleInner -= angleDiff / (float)(innerCount + 1f);
                }

                var innerSamples = GenerateCirclePoints(tableRadiusInner, tableStartAngleInner, tableEndAngleInner, orientation, innerCount);
                var outerSamples = GenerateCirclePoints(tableRadiusOuter, tableStartAngleOuter, tableEndAngleOuter, orientation, outerCount);
                outerSamples.Reverse();
                points.AddRange(outerSamples);
                points.AddRange(innerSamples);
                //List<Vector2> centroids = Lloyd.Centroids(samples, count);
                //points = Lloyd.MapSamplesOrderToCentroids(samples, centroids);
            }

            for (int i = 0; i < points.Count; i++) {
                Quaternion rotation = Quaternion.LookRotation(new Vector3(-points[i].x, 0, -points[i].y));
                if (count > middleCapacity && points[i].magnitude < tableRadiusMiddle)
                {
                    // we are on the inner row
                    rotation = Quaternion.LookRotation(new Vector3(points[i].x, 0, points[i].y));
                }

                //Shop Terminals are oriented in a specific way, causing buds to be upside down
                Quaternion rotationUpsideDown = Quaternion.Euler(180, 0, 0);
                if (isLunarBuds)
                {
                    rotationUpsideDown = Quaternion.Euler(0, 180, 0); //Also, Y axis needs to be flipped so the price hologram is oriented outwards
                }
                rotation = rotation * rotationUpsideDown;

                //Shop Terminals require a height offset, but buds do not
                var position = new Vector3(lunarTablePosition.x + points[i].x, lunarTablePosition.y + 4.0f , lunarTablePosition.z + points[i].y);
                if (isLunarBuds)
                {
                    position = new Vector3(lunarTablePosition.x + points[i].x, lunarTablePosition.y, lunarTablePosition.z + points[i].y);
                }

                DicLunarShopTerminals.Add(i, new SpawnCardStruct(position, rotation.eulerAngles));
            }
        }

        //Helper function that aims to disable the defualt "SetNoPickup()" behavior in lunar buds so they can "swap" equipment only when the lunar shop section is enabled
        private void DisableLunarBudPickupClearing(PurchaseInteraction purchaseInteraction, ShopTerminalBehavior shopTerminalBehavior)
        {
            // Lunar buds still store their SetNoPickup listener on this legacy event.
            #pragma warning disable CS0618
            var purchaseEvent = purchaseInteraction.onPurchase;
            #pragma warning restore CS0618

            if (purchaseEvent == null)
            {
                Log.LogWarning("The Lunar Bud's Purchase Event is Missing");
                return;
            }

            for (int index = 0; index < purchaseEvent.GetPersistentEventCount(); index++)
            {
                if (purchaseEvent.GetPersistentTarget(index) == shopTerminalBehavior && purchaseEvent.GetPersistentMethodName(index) == nameof(shopTerminalBehavior.SetNoPickup))
                {
                    //Turn off built in NoPickup, our DropPickup hook already does this
                    purchaseEvent.SetPersistentListenerState(index, UnityEventCallState.Off);
                }
            }
        }

        private void SpawnLunarShopTerminal()
        {
            //Soft compatibility with QolChest to prevent the removal of the terminals.
            if (ModCompatibilityQoLChests.enabled)
            {
                ModCompatibilityQoLChests.RegisterQoLChestsBlacklist(LunarShopObjectName);
                //Todo: Check for other mods that clear chests, make it universal rule without having to resort to modcompatbility
            }

            ObjectLunarShopTerminals_Spawn.Clear();
            generateNewPickupIndex = 0;
            DicLunarShopTerminals.Clear();
            SetLunarShopTerminal(!ModConfig.LunarShopReplaceLunarBudsWithTerminals.Value); //To take care of Lunar Buds and Terminal Distinction

            // find original lunar buds
            var gameObjects = new List<GameObject>();
            foreach (GameObject obj in UnityEngine.Object.FindObjectsOfType<GameObject>())
            {
                if (obj.name.StartsWith("LunarShopTerminal"))
                {
                    gameObjects.Add(obj);
                }
            }

            // Remove original Lunar Buds
            gameObjects.ForEach(NetworkServer.Destroy);

            if (ModConfig.LunarShopReplaceLunarBudsWithTerminals.Value)
            {
                // Spawn Shop Terminals
                gameObjects = DoSpawnGameObject(DicLunarShopTerminals, lunarShopTerminal, ModConfig.LunarShopAmount.Value);
                ObjectLunarShopTerminals_Spawn.AddRange(gameObjects);
            }
            else
            {
                // Spawn Lunar Buds
                gameObjects = DoSpawnGameObject(DicLunarShopTerminals, lunarShopBud, ModConfig.LunarShopAmount.Value);
                ObjectLunarShopTerminals_Spawn.AddRange(gameObjects);
            }

            //Go through each object and give them their behaviors and interactions
            for (int i = 0; i < gameObjects.Count; i++)
            {
                GameObject gameObject = gameObjects[i];
                gameObject.name = LunarShopObjectName;
                var purchaseInteraction = gameObject.GetComponent<PurchaseInteraction>();
                var shopTerminalBehavior = gameObject.GetComponent<ShopTerminalBehavior>();

                if (ModConfig.LunarShopReplaceLunarBudsWithTerminals.Value)
                {
                    //When the lunar shop terminals are created, they are actually called "FreeChestTerminalShippingDrone", which causes them to miss 
                    //price sets during the awake function. WolfoFixes supresses holograms with costtype of none making the new cost display hidden and gives the shipping containers a cost again.
                    if (ModConfig.LunarShopCost.Value >= 0)
                    {
                        purchaseInteraction.Networkcost = ModConfig.LunarShopCost.Value;
                        purchaseInteraction.NetworkcostType = CostTypeIndex.LunarCoin;
                    }

                    // Buds already have a price display; add one only to replacement terminals.
                    LunarShopHologram.AddTo(gameObject);
                }
                else //Clear Lunar Buds of their defualt SetNoPickup Behavior
                {
                    DisableLunarBudPickupClearing(purchaseInteraction, shopTerminalBehavior);
                }

                if (ModConfig.LunarShopInstancedPurchases.Value)
                {
                    var instancedPurchase = gameObject.AddComponent<InstancedPurchase>();
                    instancedPurchase.lunarShopIndex = i;
                    instancedPurchase.original.available = purchaseInteraction.available;
                    instancedPurchase.original.pickup = shopTerminalBehavior.pickup;
                    instancedPurchase.original.hasBeenPurchased = shopTerminalBehavior.hasBeenPurchased;
                    instancedPurchase.original.hidden = shopTerminalBehavior.hidden;

                }

                // purchaseInteraction.onPurchase.AddListener((interactor) => shopTerminalBehavior.SetNoPickup());
                whichStallsHaveBeenBoughtOnce.Add(purchaseInteraction, new List<PlayerCharacterMasterController>());
                //Main.instance.StartCoroutine(DelayRerollEffect(shopTerminalBehavior, 0.1f, false));
            }

            // Compatible clients will recieve scale updates directly, clients without mod will use RPC fallback.
            LunarShopScaleSync.Apply(gameObjects);
        }
    }
}
