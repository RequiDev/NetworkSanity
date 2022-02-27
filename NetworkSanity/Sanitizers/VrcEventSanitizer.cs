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

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void EventDelegate(IntPtr thisPtr, IntPtr eventDataPtr, IntPtr nativeMethodInfo);
        private readonly List<object> OurPinnedDelegates = new();

        public VrcEventSanitizer()
        {
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

            Il2CppSystem.Object obj;
            try
            {
                var bytes = Il2CppArrayBase<byte>.WrapNativeGenericArrayPointer(eventData.CustomData.Pointer);
                BinarySerializer.Method_Public_Static_Boolean_ArrayOf_Byte_byref_Object_0(bytes.ToArray(), out obj);
            }
            catch (Il2CppException)
            {
                _rateLimiter.BlacklistUser(eventData.Sender);
                return true;
            }

            if (obj == null)
                return false;
            

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

            if (!evtLogEntry.prop_String_0.All(c => char.IsLetterOrDigit(c) || char.IsWhiteSpace(c) || c == ':' || c == '/' || c is ' ' or '(' || c == ')' || c == '-' || c == '_'))
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

            return false;
        }
    }
}
