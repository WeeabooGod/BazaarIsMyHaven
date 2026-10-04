using RoR2;
using RoR2.Hologram;
using RoR2.Networking;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Networking;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.SceneManagement;

namespace BazaarIsMyHaven
{
    // One shared price sign for clients without this mod. The Seer Station supplies all client-side code.
    public class LunarShopFallbackHologram : NetworkBehaviour
    {
        // Desired WORLD position and rotation of the hologram, not the station root. Rotation is in X/Y/Z degrees.
        private static readonly Vector3 HologramWorldPosition = new Vector3(-80.32f, -24.8f, -36.8f);
        private static readonly Vector3 HologramWorldEulerAngles = new Vector3(0f, 180f, 0f);
        private static readonly Vector3 SetupPosition = new Vector3(0f, 10000f, 0f);
        private static AsyncOperationHandle<GameObject> seerStationPrefab;

        private readonly HashSet<NetworkConnection> initializedClients = new HashSet<NetworkConnection>();
        private PurchaseInteraction sourcePurchase;
        private PurchaseInteraction signPurchase;
        private Vector3 clientRootPosition;
        private Quaternion clientRootRotation;

        internal static LunarShopFallbackHologram Create(GameObject sourceShop)
        {
            if (!seerStationPrefab.IsValid())
            {
                seerStationPrefab = Addressables.LoadAssetAsync<GameObject>("RoR2/Base/bazaar/SeerStation.prefab");
            }

            var station = Instantiate(seerStationPrefab.WaitForCompletion(), SetupPosition, Quaternion.identity);
            SceneManager.MoveGameObjectToScene(station, sourceShop.scene);

            try
            {
                var model = station.GetComponent<ModelLocator>();
                var projector = station.GetComponent<HologramProjector>();
                var networker = station.GetComponent<NetworkStateMachine>();
                var stateMachine = station.GetComponent<EntityStateMachine>();

                // The vanilla station has separate Model and HologramPivot children, with billboarding already disabled.
                // Its modelBaseTransform is empty; Wisp death removes modelTransform instead.
                if (!model || !model.modelTransform || model.modelBaseTransform
                    || model.modelTransform.parent != station.transform
                    || !projector || !projector.hologramPivot || projector.hologramPivot.parent != station.transform
                    || !projector.disableHologramRotation
                    || !networker || !stateMachine || networker.stateMachines.Length != 1 || networker.stateMachines[0] != stateMachine)
                {
                    throw new InvalidOperationException("SeerStation no longer has the expected fixed price-sign hierarchy.");
                }

                if (EntityStateCatalog.GetStateIndex(typeof(EntityStates.Wisp1Monster.DeathState)) == EntityStateIndex.Invalid
                    || EntityStateCatalog.GetStateIndex(typeof(EntityStates.Idle)) == EntityStateIndex.Invalid)
                {
                    throw new InvalidOperationException("The vanilla states needed by the price sign are unavailable.");
                }

                // Add the observer filter before anything can cache the station's network behaviours.
                var sign = station.AddComponent<LunarShopFallbackHologram>();

                // Never run Wisp death on the host: its server path destroys the whole object.
                stateMachine.SetState(new EntityStates.Idle());
                projector.enabled = false;

                // Keep the host's model out of sight at SetupPosition. The Seer controller still needs its
                // renderer during client initialization on the host; remote clients remove theirs after spawning.
                station.name = "BazaarFallbackCostHologram";

                sign.sourcePurchase = sourceShop.GetComponent<PurchaseInteraction>();
                sign.signPurchase = station.GetComponent<PurchaseInteraction>();
                sign.signPurchase.automaticallyScaleCostWithDifficulty = false;
                sign.signPurchase.Networkavailable = true;
                sign.CopyPrice();

                // Compensate for the native pivot's rotation and offset so the settings describe the visible price.
                // The station was instantiated with identity rotation, so these offsets are relative to that root.
                sign.clientRootRotation = Quaternion.Euler(HologramWorldEulerAngles) * Quaternion.Inverse(projector.hologramPivot.rotation);
                Vector3 pivotOffset = projector.hologramPivot.position - station.transform.position;
                sign.clientRootPosition = HologramWorldPosition - sign.clientRootRotation * pivotOffset;

                NetworkServer.Spawn(station);
                sign.StartCoroutine(sign.SynchronizeClients());
                return sign;
            }
            catch
            {
                NetworkServer.Destroy(station);
                throw;
            }
        }

        private void CopyPrice()
        {
            // This shared label stays visible after purchases. It represents the common price, not personal stock.
            signPurchase.Networkcost = sourcePurchase.cost;
            signPurchase.NetworkcostType = sourcePurchase.costType;
        }

        private IEnumerator SynchronizeClients()
        {
            var retryDelay = new WaitForSecondsRealtime(1f);

            while (NetworkServer.active && sourcePurchase)
            {
                CopyPrice();
                netIdentity.RebuildObservers(false);
                initializedClients.RemoveWhere(connection => !netIdentity.observers.Contains(connection));

                foreach (var connection in netIdentity.observers)
                {
                    if (!initializedClients.Contains(connection) && SendClientSetup(connection))
                    {
                        initializedClients.Add(connection);
                        Log.LogDebug($"Created shared lunar price sign {netId} for connection {connection.connectionId}; hologram position {HologramWorldPosition}.");
                    }
                }

                yield return retryDelay;
            }

            NetworkServer.Destroy(gameObject);
        }

        private bool SendClientSetup(NetworkConnection connection)
        {
            var writer = new NetworkWriter();

            // The spawn arrives first on the same reliable channel, at SetupPosition.
            // Wisp death removes the model and colliders on remote clients, then Idle ends that state.
            // Its brief death effect stays out of sight at SetupPosition.
            WriteState(writer, netId, new EntityStates.Wisp1Monster.DeathState());
            WriteState(writer, netId, new EntityStates.Idle());

            var teleportWriter = new NetworkWriter();
            teleportWriter.StartMessage(UmsgType.Teleport);
            new TeleportHelper.TeleportMessage
            {
                gameObject = gameObject,
                newPosition = clientRootPosition,
                newRotation = clientRootRotation,
                useRotation = true,
                delta = clientRootPosition - SetupPosition
            }.Serialize(teleportWriter);
            teleportWriter.FinishMessage();
            byte[] teleport = teleportWriter.ToArray();
            writer.Write(teleport, teleport.Length);

            byte[] messages = writer.ToArray();
            return connection.SendBytes(messages, messages.Length, QosChannelIndex.defaultReliable.intVal);
        }

        private static void WriteState(NetworkWriter writer, NetworkInstanceId signId, EntityStates.EntityState state)
        {
            // StartMessage resets a writer, so frame each message separately before appending it.
            var stateWriter = new NetworkWriter();
            stateWriter.StartMessage(UmsgType.SetEntityState);
            stateWriter.Write(signId);
            stateWriter.Write((byte)0); // The station has one networked state machine.
            stateWriter.Write(EntityStateCatalog.GetStateIndex(state.GetType()));
            state.OnSerialize(stateWriter);
            stateWriter.FinishMessage();
            byte[] message = stateWriter.ToArray();
            writer.Write(message, message.Length);
        }

        public override bool OnCheckObserver(NetworkConnection connection)
        {
            // Wait for authentication; neither the host nor compatible clients need this extra sign.
            // Rebuilding observers also hides an existing sign if fallbacks are disabled during a visit.
            return ModConfig.LunarShopUseFallbackMethods.Value && connection != null && connection.isReady && !Util.ConnectionIsLocal(connection)
                && BazaarClientNetworking.TryGetScaleMessageId(connection, out short messageId) && messageId == 0;
        }

        public override bool OnRebuildObservers(HashSet<NetworkConnection> observers, bool initialize)
        {
            foreach (var connection in NetworkServer.connections)
            {
                if (OnCheckObserver(connection))
                {
                    observers.Add(connection);
                }
            }

            return true;
        }

        public override bool OnSerialize(NetworkWriter writer, bool initialState)
        {
            // Host-only observer bookkeeping: add NO bytes to the vanilla station's network layout.
            // Unmodded clients deserialize their existing NetworkStateMachine, PurchaseInteraction and SeerStationController.
            return false;
        }
    }
}
