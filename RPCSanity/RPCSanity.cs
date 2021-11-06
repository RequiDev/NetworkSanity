using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using ExitGames.Client.Photon;
using MelonLoader;
using Photon.Realtime;
using UnhollowerBaseLib;
using UnhollowerBaseLib.Runtime;
using UnityEngine;
using UnityEngine.SceneManagement;
using VRC.Core;
using VRC.Networking;
using VRC.SDKBase;
using Il2CppException = UnhollowerBaseLib.Il2CppException;

namespace RPCSanity
{
    public static class BuildInfo
    {
        public const string Name = "RPCSanity";
        public const string Author = "Requi";
        public const string Company = null;
        public const string Version = "1.0.2";
        public const string DownloadLink = "https://github.com/RequiDev/RPCSanity/";
    }

    public class RPCSanity : MelonMod
    {
        private static RateLimiter _rateLimiter;
        private static RateLimiter _otherRateLimiter;

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

        [StructLayout(LayoutKind.Sequential)]
        public unsafe struct Il2CppMethodInfo
        {
            public IntPtr methodPointer;
            public IntPtr invoker_method;
            public IntPtr name; // const char*
            public Il2CppClass* klass;
            public Il2CppTypeStruct* return_type;
            public Il2CppParameterInfo* parameters;

            public IntPtr someRtData;
            /*union
            {
                const Il2CppRGCTXData* rgctx_data; /* is_inflated is true and is_generic is false, i.e. a generic instance method #1#
                const Il2CppMethodDefinition* methodDefinition;
            };*/

            public IntPtr someGenericData;
            /*/* note, when is_generic == true and is_inflated == true the method represents an uninflated generic method on an inflated type. #1#
            union
            {
                const Il2CppGenericMethod* genericMethod; /* is_inflated is true #1#
                const Il2CppGenericContainer* genericContainer; /* is_inflated is false and is_generic is true #1#
            };*/

            public int customAttributeIndex;
            public uint token;
            public Il2CppMethodFlags flags;
            public Il2CppMethodImplFlags iflags;
            public ushort slot;
            public byte parameters_count;
            public MethodInfoExtraFlags extra_flags;
            /*uint8_t is_generic : 1; /* true if method is a generic method definition #1#
            uint8_t is_inflated : 1; /* true if declaring_type is a generic instance or if method is a generic instance#1#
            uint8_t wrapper_type : 1; /* always zero (MONO_WRAPPER_NONE) needed for the debugger #1#
            uint8_t is_marshaled_from_native : 1*/
        }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void EventDelegate(IntPtr thisPtr, IntPtr eventDataPtr, IntPtr nativeMethodInfo);
        private static readonly List<object> OurPinnedDelegates = new();

        private static IntPtr _ourAvatarPlayableDecode;
        private static IntPtr _ourAvatarPlayableDecodeMethodInfo;

        private static IntPtr _ourSyncPhysicsDecode;
        private static IntPtr _ourSyncPhysicsDecodeMethodInfo;
        public override void OnApplicationStart()
        {
            _rateLimiter = new RateLimiter();
            _otherRateLimiter = new RateLimiter();

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

            HarmonyInstance.Patch(
                typeof(FlatBufferNetworkSerializer).GetMethod(nameof(FlatBufferNetworkSerializer.Method_Public_Void_EventData_0)), typeof(RPCSanity)
                    .GetMethod(nameof(FlatBufferNetworkSerializeReceivePatch), BindingFlags.NonPublic | BindingFlags.Static)
                    .ToNewHarmonyMethod());

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
                    
                    if (!ShouldBlockEvent(eventData))
                        originalDelegate(thisPtr, eventDataPtr, nativeMethodInfo);
                }

                var patchDelegate = new EventDelegate(OnEventDelegate);
                OurPinnedDelegates.Add(patchDelegate);

                MelonUtils.NativeHookAttach((IntPtr)(&originalMethodPtr), Marshal.GetFunctionPointerForDelegate(patchDelegate));
                originalDelegate = Marshal.GetDelegateForFunctionPointer<EventDelegate>(originalMethodPtr);
            }

            unsafe
            {
                var originalMethod = (Il2CppMethodInfo*)(IntPtr)UnhollowerUtils.GetIl2CppMethodInfoPointerFieldForGeneratedMethod(typeof(AvatarPlayableController).GetMethods().FirstOrDefault(m => m.Name.StartsWith("Method_Public_Virtual_Final_New_Void_ValueTypePublicSealedObInObVoIn71"))).GetValue(null);
                var originalMethodPtr = *(IntPtr*)originalMethod;

                MelonUtils.NativeHookAttach((IntPtr)(&originalMethodPtr), typeof(RPCSanity).GetMethod(nameof(AvatarPlayableControllerDecodePatch), BindingFlags.Static | BindingFlags.NonPublic)!.MethodHandle.GetFunctionPointer());

                var methodInfoCopy = (Il2CppMethodInfo*)Marshal.AllocHGlobal(Marshal.SizeOf<Il2CppMethodInfo>());
                *methodInfoCopy = *originalMethod;

                _ourAvatarPlayableDecodeMethodInfo = (IntPtr)methodInfoCopy;
                _ourAvatarPlayableDecode = originalMethodPtr;
            }

            unsafe
            {
                var originalMethod = (Il2CppMethodInfo*)(IntPtr)UnhollowerUtils.GetIl2CppMethodInfoPointerFieldForGeneratedMethod(typeof(SyncPhysics).GetMethods().FirstOrDefault(m => m.Name.StartsWith("Method_Public_Virtual_Final_New_Void_ValueTypePublicSealedObInObVoIn71"))).GetValue(null);
                var originalMethodPtr = *(IntPtr*)originalMethod;

                MelonUtils.NativeHookAttach((IntPtr)(&originalMethodPtr), typeof(RPCSanity).GetMethod(nameof(SyncPhysicsDecodePatch), BindingFlags.Static | BindingFlags.NonPublic)!.MethodHandle.GetFunctionPointer());

                var methodInfoCopy = (Il2CppMethodInfo*)Marshal.AllocHGlobal(Marshal.SizeOf<Il2CppMethodInfo>());
                *methodInfoCopy = *originalMethod;

                _ourSyncPhysicsDecodeMethodInfo = (IntPtr)methodInfoCopy;
                _ourSyncPhysicsDecode = originalMethodPtr;
            }
        }

        private static unsafe bool SafeInvokeDecode(IntPtr ourMethodInfoPtr, IntPtr ourMethodPtr, IntPtr thisPtr, IntPtr objectsPtr, int objectIndex, float sendTime)
        {
            void** args = stackalloc void*[3];
            ((Il2CppMethodInfo*)ourMethodInfoPtr)->methodPointer = ourMethodPtr;
            args[0] = objectsPtr.ToPointer();
            args[1] = &objectIndex;
            args[2] = &sendTime;
            var exc = IntPtr.Zero;
            IL2CPP.il2cpp_runtime_invoke(ourMethodInfoPtr, thisPtr, args, ref exc);

            return exc == IntPtr.Zero;
        }

        private static void SyncPhysicsDecodePatch(IntPtr thisPtr, IntPtr objectsPtr, int objectIndex,
            float sendTime, IntPtr nativeMethodInfo)
        {
            SafeDecode(_ourSyncPhysicsDecodeMethodInfo, _ourSyncPhysicsDecode, thisPtr, objectsPtr, objectIndex, sendTime);
        }

        private static void AvatarPlayableControllerDecodePatch(IntPtr thisPtr, IntPtr objectsPtr, int objectIndex,
            float sendTime, IntPtr nativeMethodInfo)
        {
            SafeDecode(_ourAvatarPlayableDecodeMethodInfo, _ourAvatarPlayableDecode, thisPtr, objectsPtr, objectIndex, sendTime);
        }

        private static void SafeDecode(IntPtr ourMethodInfoPtr, IntPtr ourMethodPtr, IntPtr thisPtr, IntPtr objectsPtr, int objectIndex, float sendTime)
        {
            if (SafeInvokeDecode(ourMethodInfoPtr, ourMethodPtr, thisPtr, objectsPtr, objectIndex, sendTime))
                return;

            var component = new Component(thisPtr);
            var vrcPlayer = component.GetComponentInParent<VRCPlayer>();
            if (vrcPlayer == null)
                return;

            var player = vrcPlayer._player;
            if (player == null)
                return;

            _otherRateLimiter.BlacklistUser(player.prop_Int32_0);
        }

        private static bool ShouldBlockEvent(EventData eventData)
        {
            switch (eventData.Code)
            {
                case 6:
                    return _rateLimiter.IsRateLimited(eventData.Sender);
                case 7:
                case 9:
                    return _otherRateLimiter.IsRateLimited(eventData.Sender);
                default:
                    return false;
            }
        }

        private static bool FlatBufferNetworkSerializeReceivePatch(EventData __0)
        {
            if ((__0.Code == 7 || __0.Code == 9) && _otherRateLimiter.IsRateLimited(__0.Sender))
                return false;

            return true;
        }

        private static bool OnEventPatch(EventData __0)
        {
            switch (__0.Code)
            {
                case 6:
                    return _rateLimiter.IsRateLimited(__0.Sender);
                case 7:
                case 9:
                    return _otherRateLimiter.IsRateLimited(__0.Sender);
                default:
                    return false;
            }
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

            var evtLogEntry = obj.TryCast<VRC_EventLog.ObjectNPublicInVrInStSiInObSiByVrUnique>();
            if (evtLogEntry.field_Private_Int32_1 != eventData.Sender)
            {
                _rateLimiter.BlacklistUser(eventData.Sender);
                return true;
            }

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

            Il2CppReferenceArray<Il2CppSystem.Object> parameters;
            try
            {
                parameters = ParameterSerialization.Method_Public_Static_ArrayOf_Object_ArrayOf_Byte_0(vrcEvent.ParameterBytes);
            }
            catch (Il2CppException e)
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
