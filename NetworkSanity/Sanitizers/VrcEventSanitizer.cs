using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using ExitGames.Client.Photon;
using MelonLoader;
using NetworkSanity.Core;
using Photon.Realtime;
using UnhollowerBaseLib;
using VRC.Core;
using VRC.SDKBase;

namespace NetworkSanity.Sanitizers
{
    internal class VrcEventSanitizer : ISanitizer
    {
        private readonly RateLimiter _rateLimiter = new RateLimiter();

        private readonly Dictionary<string, (int, int)> _ratelimitValues = new Dictionary<string, (int, int)>()
        {
            { "Generic", (500, 500) },
            { "ReceiveVoiceStatsSyncRPC", (348, 64) },
            { "InformOfBadConnection", (64, 6) },
            { "initUSpeakSenderRPC", (256, 6) },
            { "InteractWithStationRPC", (128, 32) },
            { "SpawnEmojiRPC", (128, 6) },
            { "SanityCheck", (256, 32) },
            { "PlayEmoteRPC", (256, 6) },
            { "TeleportRPC", (256, 16) },
            { "CancelRPC", (256, 32) },
            { "SetTimerRPC", (256, 64) },
            { "_DestroyObject", (512, 128) },
            { "_InstantiateObject", (512, 128) },
            { "_SendOnSpawn", (512, 128) },
            { "ConfigurePortal", (512, 128) },
            { "UdonSyncRunProgramAsRPC", (512, 128) }, // <--- Udon is gay
            { "ChangeVisibility", (128, 12) },
            { "PhotoCapture", (128, 32) },
            { "TimerBloop", (128, 16) },
            { "ReloadAvatarNetworkedRPC", (128, 12) },
            { "InternalApplyOverrideRPC", (512, 128) },
            { "AddURL", (64, 6) },
            { "Play", (64, 6) },
            { "Pause", (64, 6) },
            { "SendVoiceSetupToPlayerRPC", (512, 6) },
            { "SendStrokeRPC", (512, 32) }
        };


        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void EventDelegate(IntPtr thisPtr, IntPtr eventDataPtr, IntPtr nativeMethodInfo);
        private readonly List<object> OurPinnedDelegates = new();

        public VrcEventSanitizer()
        {
            foreach (var kv in _ratelimitValues)
            {
                var rpcKey = kv.Key;
                var (globalLimit, individualLimit) = kv.Value;

                _rateLimiter.OnlyAllowPerSecond($"G_{rpcKey}", globalLimit);
                _rateLimiter.OnlyAllowPerSecond(rpcKey, individualLimit);
            }

            foreach (var nestedType in typeof(VRC_EventLog).GetNestedTypes())
            {
                foreach (var methodInfo in nestedType.GetMethods(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (!methodInfo.Name.StartsWith("Method_Public_Virtual_Final_New_Void_EventData_")) continue;

                    unsafe
                    {
                        var originalMethodPtr = *(IntPtr*)(IntPtr)UnhollowerUtils.GetIl2CppMethodInfoPointerFieldForGeneratedMethod(methodInfo).GetValue(null);

                        EventDelegate originalDelegate = null;

                        void OnEventDelegate(IntPtr thisPtr, IntPtr eventDataPtr, IntPtr nativeMethodInfo)
                        {
                            if (eventDataPtr == IntPtr.Zero)
                            {
                                originalDelegate(thisPtr, eventDataPtr, nativeMethodInfo);
                                return;
                            }

                            var eventData = new EventData(eventDataPtr);
                            try
                            {
                                if (!VRC_EventLogOnPhotonEvent(eventData))
                                    originalDelegate(thisPtr, eventDataPtr, nativeMethodInfo);
                            }
                            catch (Exception ex)
                            {
                                originalDelegate(thisPtr, eventDataPtr, nativeMethodInfo);
                                MelonLogger.Error(ex.Message);
                            }
                        }

                        var patchDelegate = new EventDelegate(OnEventDelegate);
                        OurPinnedDelegates.Add(patchDelegate);

                        MelonUtils.NativeHookAttach((IntPtr)(&originalMethodPtr), Marshal.GetFunctionPointerForDelegate(patchDelegate));
                        originalDelegate = Marshal.GetDelegateForFunctionPointer<EventDelegate>(originalMethodPtr);
                    }
                }
            }
        }

        private readonly Dictionary<string, int> _rpcParameterCount = new Dictionary<string, int>
        {
            { "ReceiveVoiceStatsSyncRPC", 3 },
            { "InformOfBadConnection", 2 },
            { "initUSpeakSenderRPC", 1 },
            { "InteractWithStationRPC", 1 },
            { "SpawnEmojiRPC", 1 },
            { "SanityCheck", 3 },
            { "PlayEmoteRPC", 1 },
            { "TeleportRPC", 4 }, // has 2 overloads. don't bother for now until it becomes a problem
            { "CancelRPC", 0 },
            { "SetTimerRPC", 1 },
            { "_DestroyObject", 1 },
            { "_InstantiateObject", 4 },
            { "_SendOnSpawn", 1 },
            { "ConfigurePortal", 3 },
            { "UdonSyncRunProgramAsRPC", 1 },
            { "ChangeVisibility", 1 },
            { "PhotoCapture", 0 },
            { "TimerBloop", 0 },
            { "ReloadAvatarNetworkedRPC", 0 },
            { "InternalApplyOverrideRPC", 1 },
            { "AddURL", 1 },
            { "Play", 0 },
            { "Pause", 0 },
            { "SendVoiceSetupToPlayerRPC", 0 },
            { "SendStrokeRPC", 1 }
        };

        public bool OnPhotonEvent(LoadBalancingClient loadBalancingClient, EventData eventData)
        {
            return eventData.Code == 6 && IsRpcBad(eventData);
        }

        public bool VRCNetworkingClientOnPhotonEvent(EventData eventData)
        {
            return eventData.Code == 6 && _rateLimiter.IsRateLimited(eventData.Sender);
        }
        private bool VRC_EventLogOnPhotonEvent(EventData eventData)
        {
            return eventData.Code == 6 && _rateLimiter.IsRateLimited(eventData.Sender);
        }

        private bool IsRpcBad(EventData eventData)
        {
            if (_rateLimiter.IsRateLimited(eventData.Sender))
                return true;

            if (!_rateLimiter.IsSafeToRun("Generic", 0))
                return true; // Failsafe to prevent extremely high amounts of RPCs passing through

            Il2CppSystem.Object obj;
            try
            {
                var bytes = Il2CppArrayBase<byte>.WrapNativeGenericArrayPointer(eventData.CustomData.Pointer);
                if (!BinarySerializer.Method_Public_Static_Boolean_ArrayOf_Byte_byref_Object_0(bytes.ToArray(), out obj))
                    return true; // we can't parse this. neither can vrchat. drop it now.
            }
            catch (Il2CppException)
            {
                _rateLimiter.BlacklistUser(eventData.Sender);
                return true;
            }

            var evtLogEntry = obj.TryCast<VRC_EventLog.EventLogEntry>();

            if (evtLogEntry.field_Private_Int32_1 != eventData.Sender)
            {
                _rateLimiter.BlacklistUser(eventData.Sender);
                return true;
            }

            var vrcEvent = evtLogEntry.field_Private_VrcEvent_0;

            if (vrcEvent.EventType > VRC_EventHandler.VrcEventType.CallUdonMethod)
            {
                _rateLimiter.BlacklistUser(eventData.Sender);
                return true;
            }

            if (vrcEvent.EventType != VRC_EventHandler.VrcEventType.SendRPC)
                return false;

            if (!evtLogEntry.prop_String_0.All(c => char.IsLetterOrDigit(c) || c == ':' || c == '/' || char.IsWhiteSpace(c) || c == ' '))
            {
                _rateLimiter.BlacklistUser(eventData.Sender);
                return true;
            }

            if (!_rateLimiter.IsSafeToRun($"G_{vrcEvent.ParameterString}", 0)
                || !_rateLimiter.IsSafeToRun(vrcEvent.ParameterString, eventData.Sender))
                return true;

            if (!_rpcParameterCount.ContainsKey(vrcEvent.ParameterString))
            {
                return false; // we don't have any information about this RPC. Let it slide.
            }

            var paramCount = _rpcParameterCount[vrcEvent.ParameterString];
            if (paramCount == 0 && vrcEvent.ParameterBytes.Length > 0)
            {
                _rateLimiter.BlacklistUser(eventData.Sender);
                return true;
            }

            if (paramCount > 0 && vrcEvent.ParameterBytes.Length == 0)
            {
                _rateLimiter.BlacklistUser(eventData.Sender);
                return true;
            }

            Il2CppReferenceArray<Il2CppSystem.Object> parameters;
            try
            {
                parameters = ParameterSerialization.Method_Public_Static_ArrayOf_Object_ArrayOf_Byte_0(vrcEvent.ParameterBytes);
            }
            catch (Il2CppException)
            {
                _rateLimiter.BlacklistUser(eventData.Sender);
                return true;
            }

            if (parameters == null)
            {
                _rateLimiter.BlacklistUser(eventData.Sender);
                return true;
            }

            if (parameters.Length != paramCount)
            {
                _rateLimiter.BlacklistUser(eventData.Sender);
                return true;
            }

            return false;
        }
    }
}
