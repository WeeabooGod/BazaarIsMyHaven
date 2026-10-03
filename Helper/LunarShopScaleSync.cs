using RoR2;
using RoR2.Networking;
using RoR2.Scripts.GameBehaviors;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Networking;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.SceneManagement;

namespace BazaarIsMyHaven
{
    // Host bookkeeping for lunar shops. Updated clients also receive their name, parent, and optional hologram.
    // Clients without the mod use the vanilla RPC fallback for scale only.
    public class LunarShopScaleSync : MonoBehaviour
    {
        private static AsyncOperationHandle<GameObject> helperPrefab;
        private readonly List<NetworkIdentity> shops = new List<NetworkIdentity>();
        private readonly HashSet<NetworkConnection> synchronizedClients = new HashSet<NetworkConnection>();
        private bool fallbackUnavailable;
        private bool fallbackHologramUnavailable;
        private LunarShopFallbackHologram fallbackHologram;

        public static void Preload()
        {
            // Only the fallback uses this prefab. Its RPC unparents a shop and sets scale to 1.
            // RPCParentToMuzzle function from ItemShareController will help facilitate this
            // No helper is spawned when every remote client supports direct scale messages.
            helperPrefab = Addressables.LoadAssetAsync<GameObject>("RoR2/DLC3/Drifter/DrifterHoard.prefab");
        }

        public static void Apply(List<GameObject> shopObjects)
        {
            if (!NetworkServer.active || !ModConfig.EnableMod.Value || !ModConfig.LunarShopSectionEnabled.Value || shopObjects.Count == 0)
            {
                return;
            }

            var syncObject = new GameObject("LunarShopScaleSync");
            SceneManager.MoveGameObjectToScene(syncObject, shopObjects[0].scene);
            var sync = syncObject.AddComponent<LunarShopScaleSync>();

            var parent = FindLunarShopParent(shopObjects[0].scene);
            if (!parent)
            {
                Log.LogWarning("Could not find HOLDER: Store/LunarShop; custom lunar shops will remain at the scene root.");
            }

            foreach (var shop in shopObjects)
            {
                // Set the intended world size first, then preserve the world transform when parenting.
                shop.transform.SetParent(null, true);
                shop.transform.localScale = Vector3.one;
                shop.transform.SetParent(parent, true);
                RefreshPickupDisplay(shop);
                sync.shops.Add(shop.GetComponent<NetworkIdentity>());
            }

            sync.StartCoroutine(sync.SynchronizeClients());
        }

        internal static Transform FindLunarShopParent(Scene scene)
        {
            // Search this shop's scene, including inactive holders, rather than objects in other loaded scenes.
            foreach (var root in scene.GetRootGameObjects())
            {
                if (root.name == "HOLDER: Store")
                {
                    return root.transform.Find("LunarShop");
                }
            }

            return null;
        }

        private static void RefreshPickupDisplay(GameObject shop)
        {
            var terminal = shop.GetComponent<ShopTerminalBehavior>();
            if (terminal && terminal.pickupDisplay)
            {
                // The item model caches its size when created. Invalidate the prefab cache so even
                // an unchanged item is rebuilt at the corrected scale, without rerolling stock.
                var display = terminal.pickupDisplay;
                display.modelPrefab = null;
                display.RebuildModel(null);
            }
        }

        private IEnumerator SynchronizeClients()
        {
            var retryDelay = new WaitForSecondsRealtime(1f);

            // Scene loading and late joins can make clients ready after the shops are created.
            // Observers have already been sent the spawn messages needed by either scale path.
            while (NetworkServer.active)
            {
                shops.RemoveAll(shop => !shop);
                if (shops.Count == 0)
                {
                    break;
                }

                synchronizedClients.RemoveWhere(connection => !connection.isReady || !NetworkServer.connections.Contains(connection));

                var pendingClients = GetPendingClients();
                bool sent = true;
                if (pendingClients.Count > 0)
                {
                    try
                    {
                        sent = SendScaleUpdates(pendingClients);
                    }
                    catch (Exception exception)
                    {
                        Log.LogError($"Lunar shop scale synchronization failed: {exception}");
                        break;
                    }
                }

                // A full send queue should not create another helper and warning every frame.
                yield return sent ? null : retryDelay;
            }

            Destroy(gameObject);
        }

        private List<NetworkConnection> GetPendingClients()
        {
            var result = new List<NetworkConnection>();

            foreach (var connection in NetworkServer.connections)
            {
                // The host already has scale 1 and must not receive the RPC's position reset.
                if (connection == null || !connection.isReady || Util.ConnectionIsLocal(connection) || synchronizedClients.Contains(connection))
                {
                    continue;
                }

                // Wait for the connection handshake before deciding which path this client needs.
                if (!BazaarClientNetworking.TryGetScaleMessageId(connection, out short messageId))
                {
                    continue;
                }

                if (messageId == 0 && fallbackUnavailable)
                {
                    continue;
                }

                if (shops.All(shop => shop.observers != null && shop.observers.Contains(connection)))
                {
                    result.Add(connection);
                }
            }

            return result;
        }

        private bool SendScaleUpdates(List<NetworkConnection> clients)
        {
            var fallbackClients = new List<NetworkConnection>();
            bool allSent = true;

            foreach (var connection in clients)
            {
                if (!BazaarClientNetworking.TryGetScaleMessageId(connection, out short messageId))
                {
                    allSent = false;
                    continue;
                }

                if (messageId == 0)
                {
                    fallbackClients.Add(connection);
                    continue;
                }

                bool sent = true;
                foreach (var shop in shops)
                {
                    if (!BazaarClientNetworking.SendScale(connection, messageId, shop))
                    {
                        sent = false;
                    }
                }

                if (sent)
                {
                    synchronizedClients.Add(connection);
                    Log.LogDebug($"Sent direct scale updates for {shops.Count} lunar shops to connection {connection.connectionId}.");
                }
                else
                {
                    allSent = false;
                    Log.LogWarning($"Could not send direct lunar shop scales to connection {connection.connectionId}; retrying.");
                }
            }

            if (fallbackClients.Count > 0) //Fallback using workaround for syncing scale without client having the mod
            {
                EnsureFallbackHologram();

                try
                {
                    if (!SendFallbackScaleUpdates(fallbackClients))
                    {
                        allSent = false;
                    }
                }
                catch (Exception exception)
                {
                    // A changed or missing fallback prefab must not stop direct updates for later joins.
                    fallbackUnavailable = true;
                    Log.LogError($"Lunar shop scale fallback failed; direct client support remains active: {exception}");
                }
            }

            return allSent;
        }

        private void EnsureFallbackHologram()
        {
            // Only replacement terminals need a shared fallback sign. Buds already have their own prices.
            if (fallbackHologram || fallbackHologramUnavailable || !shops[0].GetComponent<LunarShopHologram>())
            {
                return;
            }

            try
            {
                fallbackHologram = LunarShopFallbackHologram.Create(shops[0].gameObject);
            }
            catch (Exception exception)
            {
                fallbackHologramUnavailable = true;
                Log.LogError($"Could not create the shared lunar price sign: {exception}");
            }
        }

        private void OnDestroy()
        {
            if (fallbackHologram)
            {
                NetworkServer.Destroy(fallbackHologram.gameObject);
            }
        }

        internal static void ReceiveScale(NetworkInstanceId shopId, Vector3 scale, NetworkConnection connection, bool addCostHologram, string shopName, bool parentToLunarShop)
        {
            Main.instance.StartCoroutine(ApplyScaleWhenSpawned(shopId, scale, connection, addCostHologram, shopName, parentToLunarShop));
        }

        private static IEnumerator ApplyScaleWhenSpawned(NetworkInstanceId shopId, Vector3 scale, NetworkConnection connection, bool addCostHologram, string shopName, bool parentToLunarShop)
        {
            int sceneHandle = SceneManager.GetActiveScene().handle;
            float deadline = Time.realtimeSinceStartup + 10f;

            // A scale message can arrive before Unity has finished creating its target object.
            // Do not retain it after a scene change, disconnect, or prolonged missing spawn.
            while (NetworkClient.active && ClientScene.readyConnection == connection && SceneManager.GetActiveScene().handle == sceneHandle && Time.realtimeSinceStartup < deadline)
            {
                var shop = ClientScene.FindLocalObject(shopId);
                if (shop)
                {
                    if (shop.GetComponent<ShopTerminalBehavior>() && shop.GetComponent<PurchaseInteraction>())
                    {
                        shop.transform.SetParent(null, true);
                        shop.transform.localScale = scale;

                        // Names identify the objects in debug tools; the message itself still uses the network ID.
                        if (!string.IsNullOrEmpty(shopName))
                        {
                            shop.name = shopName;
                        }

                        if (parentToLunarShop)
                        {
                            var parent = FindLunarShopParent(shop.scene);
                            if (parent)
                            {
                                // Keep the shop and its hologram at their existing world position, rotation, and size.
                                shop.transform.SetParent(parent, true);
                            }
                            else
                            {
                                Log.LogWarning($"Could not find HOLDER: Store/LunarShop for object {shopId}; keeping it at the scene root.");
                            }
                        }

                        // The host marks replacement terminals; client config does not decide this.
                        if (addCostHologram)
                        {
                            LunarShopHologram.AddTo(shop);
                        }

                        RefreshPickupDisplay(shop);
                        Log.LogDebug($"Applied direct lunar shop scale {scale} to object {shopId}.");
                    }

                    yield break;
                }

                yield return null;
            }

            Log.LogDebug($"Discarded lunar shop scale update for missing object {shopId}.");
        }

        private bool SendFallbackScaleUpdates(List<NetworkConnection> clients)
        {
            var prefab = helperPrefab.WaitForCompletion();
            var itemShare = prefab ? prefab.GetComponent<ItemShareController>() : null;

            // An attachment point or offset would change the position assumed by our fallback.
            if (!itemShare || itemShare.pickupMuzzle || !string.IsNullOrEmpty(itemShare.pickupMuzzleChildName) || itemShare.subsequentPickupLocalOffset != Vector3.zero)
            {
                throw new InvalidOperationException("DrifterHoard no longer has the expected scale-helper settings.");
            }

            var helper = Instantiate(prefab, new Vector3(0f, 10000f, 0f), Quaternion.identity);
            bool allSent = true;

            try
            {
                // Keep the helper inert. Its serialized state also prevents the normal hoard timer on clients.
                helper.GetComponent<EntityStateMachine>().SetState(new EntityStates.Idle());
                helper.GetComponent<BuffWard>().Networkradius = 0f;
                NetworkServer.Spawn(helper);

                var helperIdentity = helper.GetComponent<NetworkIdentity>();

                foreach (var connection in clients)
                {
                    bool sent = true;
                    foreach (var shop in shops)
                    {
                        // Each client may own different stock. Append its display refresh after the
                        // scale RPC and teleport, in the same buffer, so the order is preserved.
                        // All this just to fuckin FIX SCALING FUCK YOU UNITY FUCK NETWORKING
                        var refresh = InstancedPurchases.CreatePickupDisplayRefresh(shop, connection, QosChannelIndex.defaultReliable.intVal);
                        if (refresh == null)
                        {
                            sent = false;
                            continue;
                        }

                        var message = CreateFallbackScaleMessages(helperIdentity.netId, shop, refresh);
                        sent = connection.SendBytes(message, message.Length, QosChannelIndex.defaultReliable.intVal) && sent;
                    }

                    if (sent)
                    {
                        synchronizedClients.Add(connection);
                        Log.LogDebug($"Used the vanilla RPC fallback to set {shops.Count} lunar shops to scale 1 for connection {connection.connectionId}.");
                    }
                    else
                    {
                        allSent = false;
                        Log.LogWarning($"Could not send lunar shop scale updates to connection {connection.connectionId}; retrying.");
                    }
                }
            }
            finally
            {
                // The RPC leaves shops unparented, so removing the helper does not remove them.
                NetworkServer.Destroy(helper);
            }

            return allSent;
        }

        private static byte[] CreateFallbackScaleMessages(NetworkInstanceId helperId, NetworkIdentity shop, byte[] displayRefresh)
        {
            var scaleWriter = new NetworkWriter();
            scaleWriter.StartMessage(MsgType.Rpc);
            scaleWriter.WritePackedUInt32(unchecked((uint)ItemShareController.kRpcRpcParentToMuzzle)); //The Scale Fix is here
            scaleWriter.Write(helperId);
            scaleWriter.Write(shop.netId);
            scaleWriter.FinishMessage();

            // The vanilla RPC also resets position/rotation. Restore them using RoR2's own teleport serializer.
            var teleportWriter = new NetworkWriter();
            teleportWriter.StartMessage(UmsgType.Teleport);
            new TeleportHelper.TeleportMessage
            {
                gameObject = shop.gameObject,
                newPosition = shop.transform.position,
                newRotation = shop.transform.rotation,
                useRotation = true,
                delta = shop.transform.position // The RPC moves the client object to world origin first.
            }.Serialize(teleportWriter);
            teleportWriter.FinishMessage();

            // Keep scale, position restoration, and the display rebuild together and in that order.
            var scaleBytes = scaleWriter.ToArray();
            var teleportBytes = teleportWriter.ToArray();
            var messages = new byte[scaleBytes.Length + teleportBytes.Length + displayRefresh.Length];
            Buffer.BlockCopy(scaleBytes, 0, messages, 0, scaleBytes.Length);
            Buffer.BlockCopy(teleportBytes, 0, messages, scaleBytes.Length, teleportBytes.Length);
            Buffer.BlockCopy(displayRefresh, 0, messages, scaleBytes.Length + teleportBytes.Length, displayRefresh.Length);
            return messages;
        }
    }
}
