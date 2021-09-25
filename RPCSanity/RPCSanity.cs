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
        public const string Name = "RPCSanityNative";
        public const string Author = "Requi (Native By OptoCloud)";
        public const string Company = null;
        public const string Version = "1.0.1";
        public const string DownloadLink = "https://github.com/OptoCloud/RPCSanityNative";
    }

    public class RPCSanity : MelonMod
    {
        [DllImport("RPCSanityNative.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern void Native_Initialize();
        [DllImport("RPCSanityNative.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern void Native_CleanupAfterDeparture();
        [DllImport("RPCSanityNative.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern bool Native_IsRateLimited(Int32 senderID);
        [DllImport("RPCSanityNative.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern bool Native_IsSafeSender(Int32 senderID);
        [DllImport("RPCSanityNative.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern void Native_BlackListSender(Int32 senderID);
        [DllImport("RPCSanityNative.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        private static extern Int32 Native_IsSafeToRun(Int32 senderId, string parameterStringPtr, Int32 parameterStringLength, Int32 paramBytesLength);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void EventDelegate(IntPtr thisPtr, IntPtr eventDataPtr, IntPtr nativeMethodInfo);
        private static readonly List<object> OurPinnedDelegates = new();

        public override void OnApplicationStart()
        {
            Native_Initialize();

            HarmonyInstance.Patch(typeof(LoadBalancingClient).GetMethod(nameof(LoadBalancingClient.OnEvent)),
                typeof(RPCSanity)
                    .GetMethod(nameof(OnEventPatch), BindingFlags.NonPublic | BindingFlags.Static)
                    .ToNewHarmonyMethod());

            SceneManager.add_sceneUnloaded(new Action<Scene>(s => Native_CleanupAfterDeparture()));

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

                    if (!Native_IsRateLimited(eventData.Sender))
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
                return !Native_IsRateLimited(__0.Sender);
            }

            return true;
        }

        private bool IsRPCBad(EventData eventData)
        {
            if (!Native_IsSafeSender(eventData.Sender))
                return true;

            var bytes = eventData.CustomData.Cast<Il2CppArrayBase<byte>>();
            if (!BinarySerializer.Method_Public_Static_Boolean_ArrayOf_Byte_byref_Object_0(bytes.ToArray(), out var obj)) // BinarySerializer.Deserialize(byte[] bytes, out object result)
                return true; // we can't parse this. neither can vrchat. drop it now.

            var evtLogEntry = obj.TryCast<MonoBehaviour2PublicSiInBoSiObLiOb1PrDoUnique.ObjectNPublicInVrInStSiInObSiByVrUnique>();
            var vrcEvent = evtLogEntry.field_Private_VrcEvent_0;

            if (vrcEvent.EventType > VRC_EventHandler.VrcEventType.CallUdonMethod) // EventType can't be higher than the enum. That's bullshit.
            {
                Native_BlackListSender(eventData.Sender);
                return true;
            }

            if (vrcEvent.EventType != VRC_EventHandler.VrcEventType.SendRPC)
                return false;

            Int32 rc = Native_IsSafeToRun(eventData.Sender, vrcEvent.ParameterString, vrcEvent.ParameterString.Length, vrcEvent.ParameterBytes.Length);
            if (rc < 0)
                return rc == -1 ? true : false;

            var parameters = ParameterSerialization.Method_Public_Static_ArrayOf_Object_ArrayOf_Byte_0(vrcEvent.ParameterBytes); // ParameterSerialization.Decode(byte[] data). Technically just calls BinarySerializer under the hood, but checks a few more things.
            if (parameters == null)
            {
                Native_BlackListSender(eventData.Sender);
                return true;
            }

            if (parameters.Length != rc)
            {
                Native_BlackListSender(eventData.Sender);
                return true;
            }

            var go = ObjectPublicAbstractSealedSeVRObGaDaSiInObBoDoUnique.Method_Public_Static_GameObject_String_Boolean_0(evtLogEntry.prop_String_0, true); // Network.FindGameObject(string path, bool suppressErrors)

            if (go == null && vrcEvent.ParameterString != "ConfigurePortal") // ConfigurePortal might be sent before VRChat processed InstantiateObject resulting in the portal being deleted.
            {
                Native_BlackListSender(eventData.Sender);
                return true;
            }

            return false;
        }
    }
}
