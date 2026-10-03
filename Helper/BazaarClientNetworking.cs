using RoR2.Networking;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

namespace BazaarIsMyHaven
{
    // Created to facilate proper scale sync for compatible clients that have this mod installed, remains entirely optional.
    // Primarily uses Unity's Network messages for MessageBase. RoR2.Networking for connection lfecycle NetworkManagerSystem, ClientAuthData, and ServerAuthManager.
    // https://docs.unity.cn/2021.1/Documentation/Manual/UNetMessages.html was the best documention I can find to help understand the structure. 
    // https://risk-of-thunder.github.io/R2Wiki/Mod-Creation/C%23-Programming/Networking/UNet/ was also helpful for serialization
    // Unfortunately, took several tries to get this working until the Astra corrected mistakes. I hate networking...
    internal static class BazaarClientNetworking
    {
        // Change the protocol version for incompatible message changes.
        // Optional trailing fields can keep version 1 because older receivers ignore them.
        private const ushort ProtocolVersion = 1;
        private const ulong ProtocolMarker = 0x42494D4853434C31; //Identifer, belongs to our protocol. Hexedecimal Pairs represent BIMHSCLI which is BazaarIsMyHaven Scale Protocol 1
        private const int AdvertisementLength = 12; // Marker (8), version (2), message ID (2).
        private const short FirstMessageId = 30000; //Arbitary search range comfortably below maximum positive short and way from built-in message ids for the game
        private const short LastMessageId = 29000;

        // Missing entry: authentication is pending. Zero: use the vanilla scale fallback.
        private static readonly Dictionary<NetworkConnection, short> clientScaleMessageIds = new Dictionary<NetworkConnection, short>();
        private static short localScaleMessageId;
        private static bool sendingClientAuth;

        public static void Hook()
        {
            On.RoR2.Networking.NetworkManagerSystem.ClientSendAuth += NetworkManagerSystem_ClientSendAuth;
            On.RoR2.Networking.ClientAuthData.Serialize += ClientAuthData_Serialize;
            On.RoR2.Networking.ServerAuthManager.HandleSetClientAuth += ServerAuthManager_HandleSetClientAuth;

            NetworkManagerSystem.onStartServerGlobal += ClearServerClients;
            NetworkManagerSystem.onStopServerGlobal += ClearServerClients;
            NetworkManagerSystem.onServerDisconnectGlobal += ForgetClient;
            NetworkManagerSystem.onStopClientGlobal += ClearLocalClient;
        }

        private static void NetworkManagerSystem_ClientSendAuth(On.RoR2.Networking.NetworkManagerSystem.orig_ClientSendAuth orig, NetworkManagerSystem self, NetworkConnection connection)
        {
            localScaleMessageId = RegisterScaleReceiver(self.client);

            // Only append our advertisement to this outgoing connection handshake.
            sendingClientAuth = localScaleMessageId != 0;
            try
            {
                orig(self, connection);
            }
            finally
            {
                sendingClientAuth = false;
            }
        }

        private static short RegisterScaleReceiver(NetworkClient client)
        {
            if (client == null)
            {
                return 0;
            }

            // Choose a free ID on this client, then tell the host which ID to use. Different clients may choose different IDs respecting different handlers
            for (short messageId = FirstMessageId; messageId >= LastMessageId; messageId--)
            {
                if (client.handlers.TryGetValue(messageId, out var handler))
                {
                    if (handler == ReceiveScale)
                    {
                        return messageId;
                    }

                    continue;
                }

                client.RegisterHandler(messageId, ReceiveScale);
                return messageId;
            }

            Log.LogWarning("No free message ID for lunarshop scale sync. Using the vanilla fallback.");
            return 0;
        }

        private static void ClientAuthData_Serialize(On.RoR2.Networking.ClientAuthData.orig_Serialize orig, ClientAuthData self, NetworkWriter writer)
        {
            orig(self, writer);

            if (sendingClientAuth)
            {
                // The current game reads only its normal auth fields and ignores trailing bytes.
                // An unmodded host therefore receives no unknown message type or changed auth fields.
                writer.Write(ProtocolMarker);
                writer.Write(ProtocolVersion);
                writer.Write(localScaleMessageId);
            }
        }

        private static void ServerAuthManager_HandleSetClientAuth(On.RoR2.Networking.ServerAuthManager.orig_HandleSetClientAuth orig, NetworkMessage message)
        {
            orig(message);

            if (message.conn == null || ServerAuthManager.FindAuthData(message.conn) == null || clientScaleMessageIds.ContainsKey(message.conn))
            {
                return;
            }

            short messageId = ReadScaleAdvertisement(message.reader);
            clientScaleMessageIds.Add(message.conn, messageId);

            if (messageId != 0)
            {
                Log.LogDebug($"Connection {message.conn.connectionId} supports direct lunar shop scale updates (protocol {ProtocolVersion}).");
            }
        }

        private static short ReadScaleAdvertisement(NetworkReader reader)
        {
            // Look only at data after the game's auth fields. Restore the reader for other hooks.
            int originalPosition = (int)reader.Position;
            int remaining = reader.Length - originalPosition;
            if (remaining < AdvertisementLength)
            {
                return 0;
            }

            byte[] extraData = reader.ReadBytes(remaining);
            reader.SeekZero();
            reader.ReadBytes(originalPosition);

            // Another mod may append its own data before or after ours.
            var markerWriter = new NetworkWriter();
            markerWriter.Write(ProtocolMarker);
            byte[] marker = markerWriter.ToArray();

            for (int offset = 0; offset <= extraData.Length - AdvertisementLength; offset++)
            {
                bool matches = true;
                for (int index = 0; index < marker.Length; index++)
                {
                    if (extraData[offset + index] != marker[index])
                    {
                        matches = false;
                        break;
                    }
                }

                if (!matches)
                {
                    continue;
                }

                var advertisement = new byte[AdvertisementLength];
                Array.Copy(extraData, offset, advertisement, 0, advertisement.Length);
                var advertisementReader = new NetworkReader(advertisement);
                advertisementReader.ReadUInt64();
                ushort version = advertisementReader.ReadUInt16();
                short messageId = advertisementReader.ReadInt16();

                if (version == ProtocolVersion && messageId >= LastMessageId && messageId <= FirstMessageId)
                {
                    return messageId;
                }
            }

            return 0;
        }

        public static bool TryGetScaleMessageId(NetworkConnection connection, out short messageId)
        {
            return clientScaleMessageIds.TryGetValue(connection, out messageId);
        }

        public static bool SendScale(NetworkConnection connection, short messageId, NetworkIdentity shop)
        {
            var message = new ScaleMessage
            {
                shopId = shop.netId,
                // Receivers apply scale before parenting, so transmit world size, including to older clients.
                localScale = shop.transform.lossyScale,
                addCostHologram = shop.GetComponent<LunarShopHologram>() != null,
                shopName = shop.name,
                parentToLunarShop = shop.transform.parent && shop.transform.parent == LunarShopScaleSync.FindLunarShopParent(shop.gameObject.scene)
            };

            // Send to this compatible connection only, never broadcast to all clients.
            return connection.SendByChannel(messageId, message, QosChannelIndex.defaultReliable.intVal);
        }

        private static void ReceiveScale(NetworkMessage message)
        {
            // This is a server-to-client visual update. Clients cannot change the host's shops.
            if (NetworkServer.active || message.conn != ClientScene.readyConnection)
            {
                return;
            }

            try
            {
                var scaleMessage = message.ReadMessage<ScaleMessage>();
                if (scaleMessage.compatible && IsValidScale(scaleMessage.localScale))
                {
                    LunarShopScaleSync.ReceiveScale(scaleMessage.shopId, scaleMessage.localScale, message.conn, scaleMessage.addCostHologram, scaleMessage.shopName, scaleMessage.parentToLunarShop);
                }
            }
            catch (Exception exception)
            {
                Log.LogWarning($"Ignored an invalid lunar shop scale message: {exception.Message}");
            }
        }

        private static bool IsValidScale(Vector3 scale)
        {
            return scale.x > 0f && !float.IsInfinity(scale.x) && scale.y > 0f && !float.IsInfinity(scale.y) && scale.z > 0f && !float.IsInfinity(scale.z);
        }

        private static void ForgetClient(NetworkConnection connection)
        {
            clientScaleMessageIds.Remove(connection);
        }

        private static void ClearServerClients()
        {
            clientScaleMessageIds.Clear();
        }

        private static void ClearLocalClient()
        {
            localScaleMessageId = 0;
            sendingClientAuth = false;
        }

        private class ScaleMessage : MessageBase
        {
            public NetworkInstanceId shopId;
            public Vector3 localScale;
            public bool addCostHologram;
            public string shopName;
            public bool parentToLunarShop;
            public bool compatible;

            public override void Serialize(NetworkWriter writer)
            {
                writer.Write(ProtocolMarker);
                writer.Write(ProtocolVersion);
                writer.Write(shopId);
                writer.Write(localScale);

                // Optional extensions: older clients ignore fields after the ones they understand.
                writer.Write(addCostHologram);
                writer.Write(shopName ?? string.Empty);
                writer.Write(parentToLunarShop);
            }

            public override void Deserialize(NetworkReader reader)
            {
                ulong marker = reader.ReadUInt64();
                ushort version = reader.ReadUInt16();
                compatible = marker == ProtocolMarker && version == ProtocolVersion;
                addCostHologram = false;
                shopName = string.Empty;
                parentToLunarShop = false;

                if (compatible)
                {
                    shopId = reader.ReadNetworkId();
                    localScale = reader.ReadVector3();

                    // Older hosts send only the scale fields, so absence means no added hologram.
                    if (reader.Position < reader.Length)
                    {
                        addCostHologram = reader.ReadBoolean();
                    }

                    // Hologram-only hosts have no name or parent instruction.
                    if (reader.Position < reader.Length)
                    {
                        shopName = reader.ReadString();
                        parentToLunarShop = reader.ReadBoolean();
                    }
                }
            }
        }
    }
}
