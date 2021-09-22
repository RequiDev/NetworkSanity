using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using ExitGames.Client.Photon;
using MelonLoader;
using Photon.Realtime;
using UnhollowerBaseLib;
using UnityEngine.SceneManagement;
using VRC.Core;
using VRC.SDKBase;

namespace RPCSanity
{
    public static class BuildInfo
    {
        public const string Name = "RPCSanity";
        public const string Author = "Requi";
        public const string Company = null;
        public const string Version = "1.0.1";
        public const string DownloadLink = "https://github.com/RequiDev/RPCSanity/";
    }

    public class RPCSanity : MelonMod
    {
        private static RateLimiter _rateLimiter;

        private Dictionary<string, (int, int)> _ratelimitValues = new()
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

        private static readonly Dictionary<string, int> _rpcParameterCount = new()
        {
            { "ReceiveVoiceStatsSyncRPC", 3 },
            { "InformOfBadConnection", 2 },
            { "initUSpeakSenderRPC", 1 },
            { "InteractWithStationRPC", 1 },
            { "SpawnEmojiRPC", 1 },
            { "SanityCheck", 3 },
            { "PlayEmoteRPC", 1 },
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

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void EventDelegate(IntPtr thisPtr, IntPtr eventDataPtr, IntPtr nativeMethodInfo);
        private static readonly List<object> OurPinnedDelegates = new();

        public override void OnApplicationStart()
        {
            _rateLimiter = new RateLimiter();

            _ratelimitValues.ToList().ForEach(kv =>
            {
                var rpcKey = kv.Key;
                var (globalLimit, individualLimit) = kv.Value;

                _rateLimiter.OnlyAllowPerSecond($"G_{rpcKey}", globalLimit);
                _rateLimiter.OnlyAllowPerSecond(rpcKey, individualLimit);
            });

            HarmonyInstance.Patch(typeof(LoadBalancingClient).GetMethod(nameof(LoadBalancingClient.OnEvent)),
                typeof(RPCSanity)
                    .GetMethod(nameof(OnEventPatch), BindingFlags.NonPublic | BindingFlags.Static)
                    .ToNewHarmonyMethod());

            SceneManager.add_sceneUnloaded(new Action<Scene>(s => _rateLimiter.CleanupAfterDeparture()));

            foreach (var nestedType in typeof(MonoBehaviour2PublicSiInBoSiObLiOb1PrDoUnique).GetNestedTypes())
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

                            if (eventData.Code != 6)
                            {
                                originalDelegate(thisPtr, eventDataPtr, nativeMethodInfo);
                                return;
                            }

                            if (!IsRPCBad(eventData))
                                originalDelegate(thisPtr, eventDataPtr, nativeMethodInfo);
                        }

                        var patchDelegate = new EventDelegate(OnEventDelegate);
                        OurPinnedDelegates.Add(patchDelegate);

                        MelonUtils.NativeHookAttach((IntPtr)(&originalMethodPtr), Marshal.GetFunctionPointerForDelegate(patchDelegate));
                        originalDelegate = Marshal.GetDelegateForFunctionPointer<EventDelegate>(originalMethodPtr);
                    }
                }
            }

            unsafe
            {
                var originalMethodPtr = *(IntPtr*)(IntPtr)UnhollowerUtils.GetIl2CppMethodInfoPointerFieldForGeneratedMethod(typeof(VRCNetworkingClient).GetMethod(nameof(VRCNetworkingClient.OnEvent))).GetValue(null);

                EventDelegate originalDelegate = null;

                void OnEventDelegate(IntPtr thisPtr, IntPtr eventDataPtr, IntPtr nativeMethodInfo)
                {
                    if (eventDataPtr == IntPtr.Zero)
                    {
                        originalDelegate(thisPtr, eventDataPtr, nativeMethodInfo);
                        return;
                    }

                    var eventData = new EventData(eventDataPtr);

                    if (eventData.Code != 6)
                    {
                        originalDelegate(thisPtr, eventDataPtr, nativeMethodInfo);
                        return;
                    }

                    if (!_rateLimiter.IsRateLimited(eventData.Sender))
                        originalDelegate(thisPtr, eventDataPtr, nativeMethodInfo);
                }

                var patchDelegate = new EventDelegate(OnEventDelegate);
                OurPinnedDelegates.Add(patchDelegate);

                MelonUtils.NativeHookAttach((IntPtr)(&originalMethodPtr), Marshal.GetFunctionPointerForDelegate(patchDelegate));
                originalDelegate = Marshal.GetDelegateForFunctionPointer<EventDelegate>(originalMethodPtr);
            }
        }

        private static bool OnEventPatch(EventData __0)
        {
            if (__0.Code == 6)
            {
                return !_rateLimiter.IsRateLimited(__0.Sender);
            }

            return true;
        }

        private bool IsRPCBad(EventData eventData)
        {
            if (_rateLimiter.IsRateLimited(eventData.Sender))
                return true;

            if (!_rateLimiter.IsSafeToRun("Generic", 0))
                return true; // Failsafe to prevent extremely high amounts of RPCs passing through

            var bytes = eventData.CustomData.Cast<Il2CppArrayBase<byte>>();
            if (!BinarySerializer.Method_Public_Static_Boolean_ArrayOf_Byte_byref_Object_0(bytes.ToArray(), out var obj)) // BinarySerializer.Deserialize(byte[] bytes, out object result)
                return true; // we can't parse this. neither can vrchat. drop it now.

            var evtLogEntry = obj.TryCast<MonoBehaviour2PublicSiInBoSiObLiOb1PrDoUnique.ObjectNPublicInVrInStSiInObSiByVrUnique>();
            var vrcEvent = evtLogEntry.field_Private_VrcEvent_0;

            if (vrcEvent.EventType > VRC_EventHandler.VrcEventType.CallUdonMethod) // EventType can't be higher than the enum. That's bullshit.
            {
                _rateLimiter.BlacklistUser(eventData.Sender);

                return true;
            }

            if (vrcEvent.EventType != VRC_EventHandler.VrcEventType.SendRPC)
                return false;

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

            var parameters = ParameterSerialization.Method_Public_Static_ArrayOf_Object_ArrayOf_Byte_0(vrcEvent.ParameterBytes); // ParameterSerialization.Decode(byte[] data). Technically just calls BinarySerializer under the hood, but checks a few more things.
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

            var go = ObjectPublicAbstractSealedSeVRObGaDaSiInObBoDoUnique.Method_Public_Static_GameObject_String_Boolean_0(evtLogEntry.prop_String_0, true); // Network.FindGameObject(string path, bool suppressErrors)

            if (go == null && vrcEvent.ParameterString != "ConfigurePortal") // ConfigurePortal might be sent before VRChat processed InstantiateObject resulting in the portal being deleted.
            {
                _rateLimiter.BlacklistUser(eventData.Sender);
                return true;
            }

            return false;
        }
    }
}
