using RoR2;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

namespace BazaarIsMyHaven
{
    public class InstancedPurchase : MonoBehaviour
    {
        public readonly InstancedPurchaseStruct original = new InstancedPurchaseStruct();
        public Dictionary<PlayerCharacterMasterController, InstancedPurchaseStruct> purchases = new Dictionary<PlayerCharacterMasterController, InstancedPurchaseStruct>();
        public PlayerCharacterMasterController pcClient;

        private readonly HashSet<PlayerCharacterMasterController> pendingUpdates = new HashSet<PlayerCharacterMasterController>();
        private readonly Dictionary<PlayerCharacterMasterController, NetworkConnection> initializedClients = new Dictionary<PlayerCharacterMasterController, NetworkConnection>();
        private float nextRetryTime;

        public InstancedPurchaseStruct GetOrCreate(PlayerCharacterMasterController pc)
        {
            if (!pc)
            {
                return original;
            }

            if (!purchases.TryGetValue(pc, out var state))
            {
                state = new InstancedPurchaseStruct
                {
                    available = original.available,
                    pickup = original.pickup,
                    hasBeenPurchased = original.hasBeenPurchased,
                    hidden = original.hidden,
                    hasBeenPurchasedOnce = original.hasBeenPurchasedOnce
                };
                purchases.Add(pc, state);
            }

            return state;
        }

        //Jebus this is a condensed function, its cool you can do this, but I want to learn this and stop having to google it all the time, so how does it work?
        //pc is evaluated with an && which only evaluates the right side if the left side is true therefore preventing a dictionary lookup
        //TryGetValue asks dictionary to find entry assosiated with PC, if found returns true and puts it into "state", otherwise false
        //Finally the ternary operator ( : ) means if pc is valid and purchase state was found, return state, otherwise return original
        public InstancedPurchaseStruct GetOrOriginal(PlayerCharacterMasterController pc)
        {
            return pc && purchases.TryGetValue(pc, out var state) ? state : original;
        }

        public void QueueUpdate(PlayerCharacterMasterController pc)
        {
            if (pc)
            {
                pendingUpdates.Add(pc);
            }
        }

        private void LateUpdate()
        {
            if (!NetworkServer.active || Time.unscaledTime < nextRetryTime)
            {
                return;
            }

            var identity = GetComponent<NetworkIdentity>();
            foreach (var pc in PlayerCharacterMasterController.instances)
            {
                if (!pc || pc.hasAuthority)
                {
                    continue;
                }

                var connection = pc.networkUser ? pc.networkUser.connectionToClient : null;
                if (connection == null ||!connection.isReady || !identity || !identity.observers.Contains(connection))
                {
                    initializedClients.Remove(pc);
                    continue;
                }

                //Initial updates must follow spawning, also covers slow clients.
                bool initialized = initializedClients.TryGetValue(pc, out var previousConnection) && previousConnection == connection;
                if (initialized && !pendingUpdates.Contains(pc))
                {
                    continue;
                }

                if (InstancedPurchases.SendUpdateToClient(this, pc))
                {
                    initializedClients[pc] = connection;
                    pendingUpdates.Remove(pc);
                }
                else
                {
                    nextRetryTime = Time.unscaledTime + 1f;
                    break;
                }
            }
        }
    }

    public class InstancedPurchaseStruct
    {
        public bool available = false;
        public UniquePickup pickup = UniquePickup.none;
        public bool hasBeenPurchased = false;
        public bool hidden = false;
        public bool hasBeenPurchasedOnce = false;

        // Consumed shops and terminals disabled for a character must stay unavailable.
        public bool CanReroll => available && !hasBeenPurchased && !hasBeenPurchasedOnce && !pickup.Equals(UniquePickup.none);
    }
}
