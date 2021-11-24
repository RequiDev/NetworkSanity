using System;
using System.Reflection;
using System.Runtime.InteropServices;
using ExitGames.Client.Photon;
using MelonLoader;
using NetworkSanity.Core;
using Photon.Realtime;
using UnhollowerBaseLib;
using UnityEngine;
using VRC;
using VRC.Networking;
using Il2CppMethodInfo = NetworkSanity.Core.Il2CppMethodInfo;

namespace NetworkSanity.Sanitizers
{
    internal class FlatBufferSanitizer : ISanitizer
    {
        private static IntPtr _ourAvatarPlayableDecode;
        private static IntPtr _ourAvatarPlayableDecodeMethodInfo;

        private static IntPtr _ourSyncPhysicsDecode;
        private static IntPtr _ourSyncPhysicsDecodeMethodInfo;

        private static IntPtr _ourPoseRecorderDecode;
        private static IntPtr _ourPoseRecorderDecodeMethodInfo;

        private static IntPtr _ourPoseRecorderDispatchedUpdate;
        private static IntPtr _ourPoseRecorderDispatchedUpdateMethodInfo;

        private static IntPtr _ourSyncPhysicsDispatchedUpdate;
        private static IntPtr _ourSyncPhysicsDispatchedUpdateMethodInfo;

        private static readonly RateLimiter RateLimiter = new RateLimiter();

        public FlatBufferSanitizer()
        {
            unsafe
            {
                var originalMethod = (Il2CppMethodInfo*)(IntPtr)UnhollowerUtils.GetIl2CppMethodInfoPointerFieldForGeneratedMethod(typeof(AvatarPlayableController).GetMethod(nameof(AvatarPlayableController.Method_Public_Virtual_Final_New_Void_ValueTypePublicSealedObInObVoIn71Vo1NuInUnique_Int32_Single_0))).GetValue(null);
                var originalMethodPtr = *(IntPtr*)originalMethod;

                MelonUtils.NativeHookAttach((IntPtr)(&originalMethodPtr), typeof(FlatBufferSanitizer).GetMethod(nameof(AvatarPlayableControllerDecodePatch), BindingFlags.Static | BindingFlags.NonPublic)!.MethodHandle.GetFunctionPointer());

                var methodInfoCopy = (Il2CppMethodInfo*)Marshal.AllocHGlobal(Marshal.SizeOf<Il2CppMethodInfo>());
                *methodInfoCopy = *originalMethod;

                _ourAvatarPlayableDecodeMethodInfo = (IntPtr)methodInfoCopy;
                _ourAvatarPlayableDecode = originalMethodPtr;
            }

            unsafe
            {
                var originalMethod = (Il2CppMethodInfo*)(IntPtr)UnhollowerUtils.GetIl2CppMethodInfoPointerFieldForGeneratedMethod(typeof(SyncPhysics).GetMethod(nameof(SyncPhysics.Method_Public_Virtual_Final_New_Void_ValueTypePublicSealedObInObVoIn71Vo1NuInUnique_Int32_Single_0))).GetValue(null);
                var originalMethodPtr = *(IntPtr*)originalMethod;

                MelonUtils.NativeHookAttach((IntPtr)(&originalMethodPtr), typeof(FlatBufferSanitizer).GetMethod(nameof(SyncPhysicsDecodePatch), BindingFlags.Static | BindingFlags.NonPublic)!.MethodHandle.GetFunctionPointer());

                var methodInfoCopy = (Il2CppMethodInfo*)Marshal.AllocHGlobal(Marshal.SizeOf<Il2CppMethodInfo>());
                *methodInfoCopy = *originalMethod;

                _ourSyncPhysicsDecodeMethodInfo = (IntPtr)methodInfoCopy;
                _ourSyncPhysicsDecode = originalMethodPtr;
            }

            unsafe
            {
                var originalMethod = (Il2CppMethodInfo*)(IntPtr)UnhollowerUtils.GetIl2CppMethodInfoPointerFieldForGeneratedMethod(typeof(PoseRecorder).GetMethod(nameof(PoseRecorder.Method_Public_Virtual_Final_New_Void_ValueTypePublicSealedObInObVoIn71Vo1NuInUnique_Int32_Single_0))).GetValue(null);
                var originalMethodPtr = *(IntPtr*)originalMethod;

                MelonUtils.NativeHookAttach((IntPtr)(&originalMethodPtr), typeof(FlatBufferSanitizer).GetMethod(nameof(PoseRecorderDecodePatch), BindingFlags.Static | BindingFlags.NonPublic)!.MethodHandle.GetFunctionPointer());

                var methodInfoCopy = (Il2CppMethodInfo*)Marshal.AllocHGlobal(Marshal.SizeOf<Il2CppMethodInfo>());
                *methodInfoCopy = *originalMethod;

                _ourPoseRecorderDecodeMethodInfo = (IntPtr)methodInfoCopy;
                _ourPoseRecorderDecode = originalMethodPtr;
            }

            // -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

            unsafe
            {
                var originalMethod = (Il2CppMethodInfo*)(IntPtr)UnhollowerUtils.GetIl2CppMethodInfoPointerFieldForGeneratedMethod(typeof(PoseRecorder).GetMethod(nameof(PoseRecorder.Method_Public_Virtual_Final_New_Void_Single_Single_0))).GetValue(null);
                var originalMethodPtr = *(IntPtr*)originalMethod;

                MelonUtils.NativeHookAttach((IntPtr)(&originalMethodPtr), typeof(FlatBufferSanitizer).GetMethod(nameof(PoseRecorderDispatchedUpdatePatch), BindingFlags.Static | BindingFlags.NonPublic)!.MethodHandle.GetFunctionPointer());

                var methodInfoCopy = (Il2CppMethodInfo*)Marshal.AllocHGlobal(Marshal.SizeOf<Il2CppMethodInfo>());
                *methodInfoCopy = *originalMethod;

                _ourPoseRecorderDispatchedUpdateMethodInfo = (IntPtr)methodInfoCopy;
                _ourPoseRecorderDispatchedUpdate = originalMethodPtr;
            }

            unsafe
            {
                var originalMethod = (Il2CppMethodInfo*)(IntPtr)UnhollowerUtils.GetIl2CppMethodInfoPointerFieldForGeneratedMethod(typeof(SyncPhysics).GetMethod(nameof(SyncPhysics.Method_Public_Virtual_Final_New_Void_Single_Single_0))).GetValue(null);
                var originalMethodPtr = *(IntPtr*)originalMethod;

                MelonUtils.NativeHookAttach((IntPtr)(&originalMethodPtr), typeof(FlatBufferSanitizer).GetMethod(nameof(SyncPhysicsDispatchedUpdatePatch), BindingFlags.Static | BindingFlags.NonPublic)!.MethodHandle.GetFunctionPointer());

                var methodInfoCopy = (Il2CppMethodInfo*)Marshal.AllocHGlobal(Marshal.SizeOf<Il2CppMethodInfo>());
                *methodInfoCopy = *originalMethod;

                _ourSyncPhysicsDispatchedUpdateMethodInfo = (IntPtr)methodInfoCopy;
                _ourSyncPhysicsDispatchedUpdate = originalMethodPtr;
            }

            NetworkSanity.Harmony.Patch(
                typeof(FlatBufferNetworkSerializer).GetMethod(nameof(FlatBufferNetworkSerializer
                    .Method_Public_Void_EventData_0)), typeof(FlatBufferSanitizer).GetMethod(nameof(FlatBufferNetworkSerializeReceivePatch), BindingFlags.NonPublic | BindingFlags.Static).ToNewHarmonyMethod());
        }

        private static bool FlatBufferNetworkSerializeReceivePatch(EventData __0)
        {
            if (__0.Code == 9 && RateLimiter.IsRateLimited(__0.Sender))
                return false;

            return true;
        }

        private static void SyncPhysicsDispatchedUpdatePatch(IntPtr thisPtr, float param1, float param2, IntPtr nativeMethodInfo)
        {
            SafeDispatchedUpdate(_ourSyncPhysicsDispatchedUpdateMethodInfo, _ourSyncPhysicsDispatchedUpdate, thisPtr,
                param1, param2);
        }

        private static void PoseRecorderDispatchedUpdatePatch(IntPtr thisPtr, float param1, float param2, IntPtr nativeMethodInfo)
        {
            SafeDispatchedUpdate(_ourPoseRecorderDispatchedUpdateMethodInfo, _ourPoseRecorderDispatchedUpdate, thisPtr,
                param1, param2);
        }

        private static unsafe void SafeDispatchedUpdate(IntPtr ourMethodInfoPtr, IntPtr ourMethodPtr, IntPtr thisPtr, float param1,
            float param2)
        {
            void** args = stackalloc void*[2];
            ((Il2CppMethodInfo*)ourMethodInfoPtr)->methodPointer = ourMethodPtr;
            args[0] = &param1;
            args[1] = &param2;
            var exc = IntPtr.Zero;
            IL2CPP.il2cpp_runtime_invoke(ourMethodInfoPtr, thisPtr, args, ref exc);
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

        private static void PoseRecorderDecodePatch(IntPtr thisPtr, IntPtr objectsPtr, int objectIndex,
            float sendTime, IntPtr nativeMethodInfo)
        {
            SafeDecode(_ourPoseRecorderDecodeMethodInfo, _ourPoseRecorderDecode, thisPtr, objectsPtr, objectIndex, sendTime);
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

            RateLimiter.BlacklistUser(player.prop_Int32_0);
        }

        public bool OnPhotonEvent(LoadBalancingClient loadBalancingClient, EventData eventData)
        {
            return eventData.Code == 9 && IsReliableBad(eventData);
        }

        public bool VRCNetworkingClientOnPhotonEvent(EventData eventData)
        {
            return eventData.Code == 9 && RateLimiter.IsRateLimited(eventData.Sender);
        }

        private static bool IsReliableBad(EventData eventData)
        {
            if (RateLimiter.IsRateLimited(eventData.Sender))
                return true;

            var bytes = Il2CppArrayBase<byte>.WrapNativeGenericArrayPointer(eventData.CustomData.Pointer);
            if (bytes.Length <= 10)
            {
                RateLimiter.BlacklistUser(eventData.Sender);
                return true;
            }

            var serverTime = BitConverter.ToInt32(bytes, 4);
            if (serverTime == 0)
            {
                RateLimiter.BlacklistUser(eventData.Sender);
                return true;
            }

            return false;
        }
    }
}
