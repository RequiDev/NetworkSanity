using System;
using System.Linq;
using ExitGames.Client.Photon;
using MoPhoGames.USpeak.Core;
using NetworkSanity.Core;
using Photon.Realtime;
using UnhollowerBaseLib;

namespace NetworkSanity.Sanitizers
{
    internal class USpeakSanity : ISanitizer
    {
        private readonly RateLimiter _rateLimiter = new RateLimiter();

        public bool OnPhotonEvent(LoadBalancingClient loadBalancingClient, EventData eventData)
        {
            return eventData.Code == 1 && IsVoicePacketBad(eventData);
        }
        public bool VRCNetworkingClientOnPhotonEvent(EventData eventData)
        {
            return eventData.Code == 1 && _rateLimiter.IsRateLimited(eventData.Sender);
        }

        private bool IsVoicePacketBad(EventData eventData)
        {
            if (_rateLimiter.IsRateLimited(eventData.Sender))
                return true;

            byte[] bytes = Il2CppArrayBase<byte>.WrapNativeGenericArrayPointer(eventData.CustomData.Pointer);
            if (bytes.Length <= 8)
            {
                _rateLimiter.BlacklistUser(eventData.Sender);
                return true;
            }

            var sender = BitConverter.ToInt32(bytes, 0);
            if (sender != eventData.Sender)
            {
                _rateLimiter.BlacklistUser(eventData.Sender);
                return true;
            }

            var sourceOffset = 4;
            var source = bytes.Skip(4).ToArray();
            while (sourceOffset < source.Length)
            {
                var container = new USpeakFrameContainer();
                var offset = container.Method_Public_Int32_ArrayOf_Byte_Int32_1(source, sourceOffset);
                if (offset == -1)
                {
                    _rateLimiter.BlacklistUser(eventData.Sender);
                    return true;
                }

                container.Method_Public_Void_0();
                sourceOffset += offset;
            }

            return false;
        }
    }
}
