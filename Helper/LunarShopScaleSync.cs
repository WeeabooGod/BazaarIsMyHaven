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
    // RoR2 Gameobject creation uses original prefab scale, this causes scale to be set host-wide, but not client wide. I am trying to fix this.
    // Host-only bookkeeping. Clients receive existing RoR2 messages, not this component.
    public class LunarShopScaleSync : MonoBehaviour
    {
        private static AsyncOperationHandle<GameObject> helperPrefab;
        private readonly List<NetworkIdentity> shops = new List<NetworkIdentity>();
        private readonly HashSet<NetworkConnection> synchronizedClients = new HashSet<NetworkConnection>();

        public static void Preload()
        {
            //This prefab as a built in prefrab scale of 1. We are attempting to use it as a helper to set the scale of the shops for clients.
            //It has no attachment point or pickup offset, so it will unparent the shops and set their scale to exactly 1.
            //RPCParentToMuzzle function from ItemShareController will help facilitate this
            helperPrefab = Addressables.LoadAssetAsync<GameObject>("RoR2/DLC3/Drifter/DrifterHoard.prefab");
        }

        public static void Apply(List<GameObject> shopObjects)
        {
            if (!NetworkServer.active || shopObjects.Count == 0)
            {
                return;
            }

            var syncObject = new GameObject("LunarShopScaleSync");
            SceneManager.MoveGameObjectToScene(syncObject, shopObjects[0].scene);
            var sync = syncObject.AddComponent<LunarShopScaleSync>();

            foreach (var shop in shopObjects)
            {
                shop.transform.SetParent(null, true);
                shop.transform.localScale = Vector3.one;
                sync.shops.Add(shop.GetComponent<NetworkIdentity>());
            }

            sync.StartCoroutine(sync.SynchronizeClients());
        }

        private IEnumerator SynchronizeClients()
        {
            var retryDelay = new WaitForSecondsRealtime(1f);

            // Scene loading and late joins can make clients ready after the shops are created.
            // Observers have already been sent the spawn messages needed by our RPC.
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

                if (shops.All(shop => shop.observers != null && shop.observers.Contains(connection)))
                {
                    result.Add(connection);
                }
            }

            return result;
        }

        private bool SendScaleUpdates(List<NetworkConnection> clients)
        {
            var prefab = helperPrefab.WaitForCompletion();
            var itemShare = prefab ? prefab.GetComponent<ItemShareController>() : null;

            // This prefab has no attachment point or pickup offset. It is said to unparent the target and sets its root scale to exactly 1.
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
                var messages = shops.Select(shop => CreateScaleMessages(helperIdentity.netId, shop)).ToArray();

                foreach (var connection in clients)
                {
                    bool sent = true;
                    foreach (var message in messages)
                    {
                        sent = connection.SendBytes(message, message.Length, QosChannelIndex.defaultReliable.intVal) && sent;
                    }

                    if (sent)
                    {
                        synchronizedClients.Add(connection);
                        Log.LogDebug($"Set {shops.Count} lunar shops to scale 1 for connection {connection.connectionId}.");
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

        private static byte[] CreateScaleMessages(NetworkInstanceId helperId, NetworkIdentity shop)
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

            // Send both framed messages as one small buffer so a packet boundary cannot leave a shop at origin.
            var scaleBytes = scaleWriter.ToArray();
            var teleportBytes = teleportWriter.ToArray();
            var messages = new byte[scaleBytes.Length + teleportBytes.Length];
            Buffer.BlockCopy(scaleBytes, 0, messages, 0, scaleBytes.Length);
            Buffer.BlockCopy(teleportBytes, 0, messages, scaleBytes.Length, teleportBytes.Length);
            return messages;
        }
    }
}
